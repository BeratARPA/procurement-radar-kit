import 'model.dart';
import 'system_control_stub.dart'
    if (dart.library.io) 'system_control_native.dart' as platform;

export 'model.dart';

/// What the Sistem screen is allowed to ask the machine.
///
/// The whole point of this interface is that it does **not** go through the http
/// api. The moment you most want a control panel is the moment the api is not
/// answering, and a panel that asks the api whether the api is running is a
/// panel that goes blank exactly when it is needed. So: docker, the task
/// scheduler and the log files, directly.
///
/// `dart:io` lives only in the native implementation, reached through a
/// conditional import. On the web `dart.library.io` is false, the stub is
/// compiled instead, and `isSupported` is false - which is what hides the whole
/// screen there.
abstract interface class SystemControl {
  /// False on the web, where none of this is possible.
  bool get isSupported;

  /// Where the repository is, as this implementation understands it.
  ///
  /// Everything else is relative to it: `docker-compose.yml`, `publish/`,
  /// `config/`, `.env`. Null when it could not be found, which is a state the
  /// screen has to show rather than fail quietly in.
  String? get projectRoot;

  /// Point it somewhere else. Returns false if that directory is not a checkout.
  Future<bool> useProjectRoot(String path);

  Future<PartStatus> statusOf(Part part);

  Future<CommandOutcome> start(Part part);

  Future<CommandOutcome> stop(Part part);

  /// Turn the logon trigger on or off for the parts that have one.
  Future<CommandOutcome> setStartsAtLogon(Part part, bool enabled);

  /// Whether this machine will fall asleep, which stops collection dead.
  ///
  /// D-10 is blunt about it: a sleeping laptop collects nothing, and a notice
  /// that was only listed for four days is gone for good. The installer checks
  /// this and warns; it belongs on a screen too.
  Future<String?> sleepWarning();

  /// Run one CLI verb and hand back everything it said.
  Future<CommandOutcome> runCli(CliCommand command);

  /// The newest log lines, newest last.
  Future<List<LogLine>> readLog({int lines = 200, bool problemsOnly = false});

  /// The value of a key in the project's `.env`.
  ///
  /// The reason the desktop build exists at all, as far as the token is
  /// concerned: a browser cannot read this file and a desktop app can.
  Future<String?> readEnv(String key);

  /// True when `publish/` was built before the sources it was built from.
  ///
  /// This is here because it cost an evening. The api answering on the port was
  /// a copy under `publish/` older than the code, every request returned 200,
  /// and the interface reported that the api could not be reached. Nothing
  /// anywhere said the running binary was stale.
  Future<String?> staleDeployment();
}

/// The implementation for whatever this was compiled for.
SystemControl createSystemControl() => platform.createSystemControl();

/// The commands worth a button.
///
/// Deliberately not all of them. `seed` and `reparse` change data, so they are
/// marked unsafe and asked about first; `try-source` and `check-source` want an
/// argument and belong to T-21's screen, not here.
const List<CliCommand> cliCommands = <CliCommand>[
  CliCommand(
    name: 'preflight',
    description: 'Her okuma yolunu gerçek veritabanına karşı dener (D-17).',
  ),
  CliCommand(
    name: 'telegram-check',
    description: 'Token geçerli mi, bot hangi sohbetleri görüyor.',
  ),
  // Safe in the sense that matters here: it only reads this system. It writes,
  // but somewhere else, and there is nothing it can leave worse than it found.
  CliCommand(
    name: 'backup',
    description:
        'Veritabanı dökümünü ve dosya arşivini ayarlardaki yere kopyalar (D-24). '
        'Gece 03:00\'te kendiliğinden de çalışır.',
  ),
  CliCommand(
    name: 'seed',
    description: 'config/sources.seed.json dosyasını source tablosuna yazar.',
    isSafe: false,
  ),
];
