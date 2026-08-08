import 'dart:convert';
import 'dart:io';

import 'package:flutter/foundation.dart';

import 'system_control.dart';

/// Talks to the machine, not to the api.
///
/// Compiled only where `dart:io` exists, so this file is absent from the web
/// build entirely - it is reached through the conditional import in
/// `system_control.dart`.
class NativeSystemControl implements SystemControl {
  NativeSystemControl({String? projectRoot}) : _root = projectRoot ?? _findRoot();

  /// The task names `deploy/install-windows.ps1` registers.
  static const String _workerTask = 'ProcurementRadar';
  static const String _apiTask = 'ProcurementRadarApi';

  static const String _container = 'procurement-radar-db';
  static const int _apiPort = 5080;

  String? _root;

  @override
  bool get isSupported => Platform.isWindows;

  @override
  String? get projectRoot => _root;

  @override
  Future<bool> useProjectRoot(String path) async {
    if (!_looksLikeCheckout(path)) return false;

    _root = path;

    return true;
  }

  // --- finding the checkout ---------------------------------------------------

  /// A directory is the project if it holds the two files nothing else does.
  ///
  /// Checked rather than assumed because the answer differs by how the app was
  /// started: from `flutter run` the working directory is `ui/`, from a built
  /// exe it is wherever the exe sits. Guessing wrong means every button on the
  /// screen silently does nothing.
  static bool _looksLikeCheckout(String path) =>
      File('$path${Platform.pathSeparator}docker-compose.yml').existsSync() &&
      File('$path${Platform.pathSeparator}ProcurementRadar.sln').existsSync();

  static String? _findRoot() {
    for (final String start in <String>[
      Directory.current.path,
      File(Platform.resolvedExecutable).parent.path,
    ]) {
      Directory? directory = Directory(start);

      // Ten is well past any real nesting; the loop is bounded so a symlink
      // cycle cannot hang the first frame.
      for (int depth = 0; depth < 10; depth++) {
        if (_looksLikeCheckout(directory!.path)) return directory.path;

        final Directory parent = directory.parent;

        if (parent.path == directory.path) break;

        directory = parent;
      }
    }

    return null;
  }

  String _in(String relative) => '$_root${Platform.pathSeparator}$relative';

  // --- status -----------------------------------------------------------------

  @override
  Future<PartStatus> statusOf(Part part) async {
    if (_root == null) {
      return PartStatus.unknown(part, 'proje klasörü bulunamadı');
    }

    return switch (part) {
      Part.database => _databaseStatus(),
      Part.worker => _taskStatus(Part.worker, _workerTask),
      Part.api => _apiStatus(),
    };
  }

  Future<PartStatus> _databaseStatus() async {
    final CommandOutcome outcome = await _run('docker', <String>[
      'ps',
      '--all',
      '--filter',
      'name=$_container',
      '--format',
      '{{.State}}|{{.Status}}',
    ]);

    if (!outcome.succeeded) {
      return const PartStatus(
        part: Part.database,
        health: Health.unknown,
        detail: 'docker yanıt vermiyor — Docker Desktop kapalı olabilir',
        canStart: false,
        canStop: false,
      );
    }

    final String line = outcome.output.trim();

    if (line.isEmpty) {
      return const PartStatus(
        part: Part.database,
        health: Health.down,
        detail: 'kapsayıcı yok',
        canStart: true,
      );
    }

    final List<String> parts = line.split('|');
    final String state = parts.first.trim();
    final String status = parts.length > 1 ? parts[1].trim() : state;

    if (state != 'running') {
      return PartStatus(
        part: Part.database,
        health: Health.down,
        detail: status,
        canStart: true,
      );
    }

    // A container can be up and not healthy. docker spells that in the status
    // text, and it is the difference between "postgres is listening" and
    // "postgres is still replaying its write-ahead log".
    final bool healthy = !status.contains('unhealthy');

    return PartStatus(
      part: Part.database,
      health: healthy ? Health.up : Health.degraded,
      detail: status,
      canStop: true,
    );
  }

  Future<PartStatus> _apiStatus() async {
    final PartStatus task = await _taskStatus(Part.api, _apiTask);
    final bool listening = await _isListening(_apiPort);

    // The task and the port can disagree, and which way round matters. A task
    // that says running while nothing is listening means the api died on
    // startup - the case a panel exists to catch.
    if (listening) {
      return PartStatus(
        part: Part.api,
        health: Health.up,
        detail: '$_apiPort dinliyor',
        startsAtLogon: task.startsAtLogon,
        canStop: true,
      );
    }

    return PartStatus(
      part: Part.api,
      health: task.health == Health.up ? Health.degraded : Health.down,
      detail: task.health == Health.up
          ? 'görev çalışıyor ama $_apiPort sessiz'
          : 'çalışmıyor',
      startsAtLogon: task.startsAtLogon,
      canStart: true,
    );
  }

  static Future<bool> _isListening(int port) async {
    try {
      final Socket socket = await Socket.connect(
        InternetAddress.loopbackIPv4,
        port,
        timeout: const Duration(milliseconds: 700),
      );

      socket.destroy();

      return true;
    } on Object {
      return false;
    }
  }

  /// Reads a scheduled task through PowerShell rather than `schtasks`.
  ///
  /// `schtasks /query` prints its status in the display language - on this
  /// machine "Çalışıyor", not "Running" - so parsing it would work here and fail
  /// on an English install, or the other way round. `(Get-ScheduledTask).State`
  /// is an enum whose name is the same in every locale.
  Future<PartStatus> _taskStatus(Part part, String taskName) async {
    final CommandOutcome outcome = await _powerShell(
      '\$t = Get-ScheduledTask -TaskName "$taskName" -ErrorAction SilentlyContinue; '
      'if (-not \$t) { "MISSING" } else { '
      '\$logon = @(\$t.Triggers | Where-Object { \$_.CimClass.CimClassName -eq "MSFT_TaskLogonTrigger" }); '
      '"\$(\$t.State)|\$(if (\$logon.Count -gt 0 -and \$t.State -ne \'Disabled\') { \'yes\' } else { \'no\' })" }',
    );

    final String answer = outcome.output.trim();

    if (!outcome.succeeded || answer.isEmpty || answer == 'MISSING') {
      return PartStatus.unknown(part, 'görev kayıtlı değil');
    }

    final List<String> fields = answer.split('|');
    final String state = fields.first.trim();
    final bool? atLogon = fields.length > 1 ? fields[1].trim() == 'yes' : null;

    return switch (state) {
      'Running' => PartStatus(
          part: part,
          health: Health.up,
          detail: 'çalışıyor',
          startsAtLogon: atLogon,
          canStop: true,
        ),
      'Ready' => PartStatus(
          part: part,
          health: Health.down,
          detail: 'hazır, çalışmıyor',
          startsAtLogon: atLogon,
          canStart: true,
        ),
      'Disabled' => PartStatus(
          part: part,
          health: Health.down,
          detail: 'devre dışı',
          startsAtLogon: false,
          canStart: true,
        ),
      _ => PartStatus(part: part, health: Health.unknown, detail: state),
    };
  }

  // --- start and stop ---------------------------------------------------------

  @override
  Future<CommandOutcome> start(Part part) => switch (part) {
        Part.database => _compose(<String>['up', '-d']),
        Part.worker => _powerShell('Start-ScheduledTask -TaskName "$_workerTask"'),
        Part.api => _powerShell('Start-ScheduledTask -TaskName "$_apiTask"'),
      };

  @override
  Future<CommandOutcome> stop(Part part) => switch (part) {
        // stop, not down. `down` removes the container, and while the volume
        // survives (D-05 keeps it outside), removing a container to pause it is
        // a bigger thing than the button says.
        Part.database => _compose(<String>['stop']),
        Part.worker => _powerShell('Stop-ScheduledTask -TaskName "$_workerTask"'),
        Part.api => _powerShell('Stop-ScheduledTask -TaskName "$_apiTask"'),
      };

  @override
  Future<CommandOutcome> setStartsAtLogon(Part part, bool enabled) {
    final String? task = switch (part) {
      Part.worker => _workerTask,
      Part.api => _apiTask,
      Part.database => null,
    };

    if (task == null) {
      // Docker Desktop's own "start when you sign in" is a checkbox inside that
      // application's settings, not something this can reach. docs/deployment.md
      // says so; the screen repeats it rather than offering a switch that lies.
      return Future<CommandOutcome>.value(const CommandOutcome(
        command: '',
        exitCode: -1,
        output: 'Docker Desktop → Settings → General içinden açılır.',
      ));
    }

    return _powerShell(
      '${enabled ? 'Enable' : 'Disable'}-ScheduledTask -TaskName "$task"',
    );
  }

  @override
  Future<String?> sleepWarning() async {
    // The index is seconds until sleep on mains power; zero means never.
    final CommandOutcome outcome = await _run(
      'powercfg',
      <String>['/query', 'SCHEME_CURRENT', 'SUB_SLEEP', 'STANDBYIDLE'],
    );

    if (!outcome.succeeded) return null;

    final RegExp acIndex = RegExp(r'AC[^\n]*:\s*0x([0-9a-fA-F]+)');
    final Match? match = acIndex.firstMatch(outcome.output);

    if (match == null) return null;

    final int seconds = int.parse(match.group(1)!, radix: 16);

    if (seconds == 0) return null;

    return '${seconds ~/ 60} dakika sonra uyuyor — uyurken hiçbir şey toplanmaz';
  }

  // --- the cli ----------------------------------------------------------------

  @override
  Future<CommandOutcome> runCli(CliCommand command) => _run(
        'dotnet',
        <String>[
          'run',
          '--project',
          _in('src${Platform.pathSeparator}ProcurementRadar.Cli'),
          '--',
          command.name,
          ...command.arguments,
        ],
        timeout: const Duration(minutes: 3),
      );

  // --- logs -------------------------------------------------------------------

  @override
  Future<List<LogLine>> readLog({int lines = 200, bool problemsOnly = false}) async {
    final File? file = _newestLog();

    if (file == null) return const <LogLine>[];

    return readLogFile(file, lines: lines, problemsOnly: problemsOnly);
  }

  /// The same reading, against a file the caller names.
  @visibleForTesting
  Future<List<LogLine>> readLogFile(
    File file, {
    int lines = 200,
    bool problemsOnly = false,
  }) async {
    final List<String> raw = await _tail(file);
    final List<LogLine> parsed = <LogLine>[];

    for (final String line in raw.reversed) {
      if (parsed.length >= lines) break;
      if (line.trim().isEmpty) continue;

      final LogLine? entry = parseLogLine(line);

      if (entry == null) continue;
      if (problemsOnly && !entry.isProblem) continue;

      parsed.add(entry);
    }

    return parsed.reversed.toList();
  }

  /// The last stretch of a file, without reading the rest of it.
  ///
  /// These grow: one of the files in `publish/logs` is already 4 MB and they
  /// roll daily, so `readAsLines` would pull the whole day into memory to show
  /// the last two hundred lines. A quarter of a megabyte is far more than that
  /// many lines of json and a fixed cost whatever the log does.
  static Future<List<String>> _tail(File file, {int bytes = 256 * 1024}) async {
    final RandomAccessFile handle = await file.open();

    try {
      final int length = await handle.length();
      final int start = length > bytes ? length - bytes : 0;

      await handle.setPosition(start);

      final Uint8List read = await handle.read(length - start);

      // allowMalformed because the cut lands wherever it lands, possibly in the
      // middle of a Turkish character.
      String text = utf8.decode(read, allowMalformed: true);

      if (start > 0) {
        // The first line is a fragment of one. Dropping it is right: half a
        // json object parses as nothing and would be discarded anyway, but
        // silently rather than deliberately.
        final int firstBreak = text.indexOf('\n');

        text = firstBreak < 0 ? '' : text.substring(firstBreak + 1);
      }

      return const LineSplitter().convert(text);
    } finally {
      await handle.close();
    }
  }

  File? _newestLog() {
    if (_root == null) return null;

    for (final String directory in <String>[_in('publish/logs'), _in('logs')]) {
      final Directory candidate = Directory(directory);

      if (!candidate.existsSync()) continue;

      final List<File> logs = candidate
          .listSync()
          .whereType<File>()
          .where((File file) => file.path.endsWith('.log'))
          .toList()
        ..sort((File a, File b) =>
            a.statSync().modified.compareTo(b.statSync().modified));

      if (logs.isNotEmpty) return logs.last;
    }

    return null;
  }

  /// Renders a Serilog json line the way a person would write it.
  ///
  /// The file holds the template and the values apart - `"{Rows} archive row(s)"`
  /// beside `Rows: 3` - which is right for grepping and unreadable on a screen.
  /// Putting them back together is the whole difference between a log panel and
  /// a wall of braces.
  @visibleForTesting
  static LogLine? parseLogLine(String line) {
    final Map<String, Object?> row;

    try {
      row = jsonDecode(line) as Map<String, Object?>;
    } on FormatException {
      return null;
    }

    final String template = row['@mt'] as String? ?? row['@m'] as String? ?? '';

    final String text = template.replaceAllMapped(
      RegExp(r'\{@?(\w+)(?::[^}]*)?\}'),
      (Match match) {
        final Object? value = row[match.group(1)];

        return value == null ? match.group(0)! : '$value';
      },
    );

    return LogLine(
      at: DateTime.tryParse(row['@t'] as String? ?? '')?.toLocal(),
      // Serilog leaves the level out when it is Information, because that is
      // most of them.
      level: row['@l'] as String? ?? 'Information',
      text: row['@x'] != null ? '$text\n${row['@x']}' : text,
    );
  }

  // --- .env -------------------------------------------------------------------

  @override
  Future<String?> readEnv(String key) async {
    if (_root == null) return null;

    final File file = File(_in('.env'));

    if (!file.existsSync()) return null;

    for (final String line in await file.readAsLines()) {
      final String text = line.trim();

      if (text.isEmpty || text.startsWith('#')) continue;

      final int separator = text.indexOf('=');

      if (separator < 1 || text.substring(0, separator).trim() != key) continue;

      String value = text.substring(separator + 1).trim();

      if (value.length > 1 &&
          ((value.startsWith('"') && value.endsWith('"')) ||
              (value.startsWith("'") && value.endsWith("'")))) {
        value = value.substring(1, value.length - 1);
      }

      return value.isEmpty ? null : value;
    }

    return null;
  }

  // --- is what is running what was built --------------------------------------

  @override
  Future<String?> staleDeployment() async {
    if (_root == null) return null;

    final File published = File(_in('publish/ProcurementRadar.Api.dll'));

    if (!published.existsSync()) return null;

    final DateTime built = published.statSync().modified;
    DateTime newestSource = built;

    final Directory source = Directory(_in('src'));

    if (!source.existsSync()) return null;

    await for (final FileSystemEntity entity
        in source.list(recursive: true, followLinks: false)) {
      if (entity is! File || !entity.path.endsWith('.cs')) continue;

      // bin and obj are build output; they are newer than the publish by
      // definition and would make this warn forever.
      if (entity.path.contains('${Platform.pathSeparator}bin${Platform.pathSeparator}') ||
          entity.path.contains('${Platform.pathSeparator}obj${Platform.pathSeparator}')) {
        continue;
      }

      final DateTime modified = entity.statSync().modified;

      if (modified.isAfter(newestSource)) newestSource = modified;
    }

    if (!newestSource.isAfter(built)) return null;

    final Duration behind = newestSource.difference(built);

    return 'publish klasörü kaynaktan ${_roughly(behind)} eski — '
        'çalışan sürüm derlediğiniz sürüm değil';
  }

  static String _roughly(Duration duration) {
    if (duration.inHours < 1) return '${duration.inMinutes} dakika';
    if (duration.inDays < 1) return '${duration.inHours} saat';

    return '${duration.inDays} gün';
  }

  // --- running things ---------------------------------------------------------

  Future<CommandOutcome> _compose(List<String> arguments) =>
      _run('docker', <String>['compose', ...arguments]);

  /// The process runner, reachable from a test.
  ///
  /// Exposed because the thing worth testing here is not what any one command
  /// returns but that a hung one is actually killed - and that cannot be
  /// observed through any of the public methods.
  @visibleForTesting
  Future<CommandOutcome> runRaw(
    String executable,
    List<String> arguments, {
    Duration timeout = const Duration(seconds: 25),
  }) =>
      _run(executable, arguments, timeout: timeout);

  Future<CommandOutcome> _powerShell(String script) => _run(
        'powershell',
        <String>['-NoProfile', '-NonInteractive', '-Command', script],
      );

  /// Arguments go as a list, never as one string.
  ///
  /// `runInShell` is off for the same reason: nothing here is assembled from
  /// anything a person typed, and keeping it that way is cheaper than checking.
  ///
  /// Started rather than run, so that a timeout can actually end it.
  /// `Process.run(...).timeout(...)` reads correctly and does the wrong thing:
  /// the timeout completes the future and the child goes on living. This screen
  /// re-reads every twelve seconds, so a `docker` that hangs while Docker
  /// Desktop is starting would leave three orphans behind per tick, forever.
  Future<CommandOutcome> _run(
    String executable,
    List<String> arguments, {
    Duration timeout = const Duration(seconds: 25),
  }) async {
    final String printed = '$executable ${arguments.join(' ')}';

    try {
      final Process process = await Process.start(
        executable,
        arguments,
        workingDirectory: _root,
        runInShell: false,
      );

      // Drained straight away. A child whose output fills a pipe nobody is
      // reading blocks on the write and never exits, which would turn every
      // call into a timeout.
      final Future<String> stdout =
          process.stdout.transform(systemEncoding.decoder).join();
      final Future<String> stderr =
          process.stderr.transform(systemEncoding.decoder).join();

      bool timedOut = false;

      final int exitCode = await process.exitCode.timeout(
        timeout,
        onTimeout: () {
          timedOut = true;
          process.kill();

          return -1;
        },
      );

      // Both complete once the pipes close, which killing does.
      final String out = (await stdout).trim();
      final String err = (await stderr).trim();

      return CommandOutcome(
        command: printed,
        exitCode: exitCode,
        output: timedOut
            ? '${timeout.inSeconds} saniyede yanıt vermedi, durduruldu.'
            : <String>[out, err].where((String s) => s.isNotEmpty).join('\n'),
      );
    } on Object catch (error) {
      // A missing executable lands here. It should not take the screen down.
      return CommandOutcome(command: printed, exitCode: -1, output: '$error');
    }
  }
}

SystemControl createSystemControl() => NativeSystemControl();
