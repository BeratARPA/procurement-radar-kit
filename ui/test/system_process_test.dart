@TestOn('windows')
library;

import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/system/system_control.dart';
import 'package:procurement_radar/src/system/system_control_native.dart';

void main() {
  late NativeSystemControl control;

  setUp(() => control = NativeSystemControl());

  group('running something', () {
    test('a command that answers gives back what it said', () async {
      final CommandOutcome outcome = await control.runRaw(
        'cmd',
        <String>['/c', 'echo', 'merhaba'],
      );

      expect(outcome.succeeded, isTrue);
      expect(outcome.output, contains('merhaba'));
    });

    test('a command that does not exist fails instead of throwing', () async {
      final CommandOutcome outcome = await control.runRaw(
        'this-executable-does-not-exist',
        <String>[],
      );

      expect(outcome.succeeded, isFalse);
      expect(outcome.output, isNotEmpty);
    });

    test('a command that hangs is killed, and really killed', () async {
      // The whole point of moving off Process.run. `.timeout()` on that future
      // stops the waiting and leaves the child alive; this screen re-reads every
      // twelve seconds, so a hanging docker would pile up orphans all day.
      final int before = _sleepersRunning();

      final Stopwatch clock = Stopwatch()..start();

      final CommandOutcome outcome = await control.runRaw(
        'powershell',
        <String>['-NoProfile', '-Command', 'Start-Sleep -Seconds 60'],
        timeout: const Duration(seconds: 2),
      );

      clock.stop();

      expect(outcome.succeeded, isFalse);
      expect(outcome.output, contains('durduruldu'));
      expect(clock.elapsed, lessThan(const Duration(seconds: 20)));

      // Give the OS a moment to reap it, then check nothing was left behind.
      await Future<void>.delayed(const Duration(seconds: 2));

      expect(_sleepersRunning(), lessThanOrEqualTo(before));
    });
  });

  group('reading the tail of a log', () {
    test('a file bigger than the window is not read whole', () async {
      final Directory temporary = await Directory.systemTemp.createTemp('radar');

      addTearDown(() => temporary.delete(recursive: true));

      final File file = File('${temporary.path}${Platform.pathSeparator}big.log');
      final StringBuffer buffer = StringBuffer();

      // ~1.5 MB, comfortably past the 256 KB window, with Turkish in it so a
      // cut through a multi-byte character is actually exercised.
      for (int i = 0; i < 12000; i++) {
        buffer.writeln(
          '{"@t":"2026-08-08T17:42:04.0000000Z","@mt":"Satır $i — çğıöşü"}',
        );
      }

      await file.writeAsString(buffer.toString());

      final NativeSystemControl scoped =
          NativeSystemControl(projectRoot: temporary.path);

      final List<LogLine> lines = await scoped.readLogFile(file, lines: 50);

      expect(lines, hasLength(50));
      // The last line written is the last line read: the tail is the end of the
      // file, not the beginning of it.
      expect(lines.last.text, contains('Satır 11999'));
      // And nothing came back mangled by the byte-boundary cut.
      expect(lines.first.text, contains('çğıöşü'));
    });

    test('a file smaller than the window is read entirely', () async {
      final Directory temporary = await Directory.systemTemp.createTemp('radar');

      addTearDown(() => temporary.delete(recursive: true));

      final File file = File('${temporary.path}${Platform.pathSeparator}small.log');

      await file.writeAsString(
        '{"@t":"2026-08-08T17:42:04.0000000Z","@mt":"bir"}\n'
        '{"@t":"2026-08-08T17:42:05.0000000Z","@mt":"iki"}\n',
      );

      final NativeSystemControl scoped =
          NativeSystemControl(projectRoot: temporary.path);

      final List<LogLine> lines = await scoped.readLogFile(file);

      expect(lines.map((LogLine l) => l.text), <String>['bir', 'iki']);
    });
  });
}

/// How many sleeping powershells are about, so the test can prove it did not
/// add one that outlived it.
int _sleepersRunning() {
  final ProcessResult result = Process.runSync(
    'powershell',
    <String>[
      '-NoProfile',
      '-Command',
      '@(Get-CimInstance Win32_Process -Filter "Name=\'powershell.exe\'" | '
          'Where-Object { \$_.CommandLine -like "*Start-Sleep -Seconds 60*" }).Count',
    ],
  );

  return int.tryParse('${result.stdout}'.trim()) ?? 0;
}
