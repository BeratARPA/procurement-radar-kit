
import 'system_control.dart';

/// What the web gets.
///
/// Every answer is "cannot". A browser cannot see a container, a scheduled task
/// or a file on disk, and no amount of arranging changes that. The screen reads
/// `isSupported` and does not appear, so none of the rest is ever called - but
/// it is written honestly rather than throwing, because a stub that throws turns
/// a hidden feature into a crash the first time somebody links to it.
class _UnsupportedSystemControl implements SystemControl {
  const _UnsupportedSystemControl();

  @override
  bool get isSupported => false;

  @override
  String? get projectRoot => null;

  @override
  Future<bool> useProjectRoot(String path) async => false;

  @override
  Future<PartStatus> statusOf(Part part) async =>
      PartStatus.unknown(part, 'tarayıcıdan görülemez');

  @override
  Future<CommandOutcome> start(Part part) async => _refused;

  @override
  Future<CommandOutcome> stop(Part part) async => _refused;

  @override
  Future<CommandOutcome> setStartsAtLogon(Part part, bool enabled) async => _refused;

  @override
  Future<String?> sleepWarning() async => null;

  @override
  Future<CommandOutcome> runCli(CliCommand command) async => _refused;

  @override
  Future<List<LogLine>> readLog({int lines = 200, bool problemsOnly = false}) async =>
      const <LogLine>[];

  @override
  Future<String?> readEnv(String key) async => null;

  @override
  Future<String?> staleDeployment() async => null;

  static const CommandOutcome _refused = CommandOutcome(
    command: '',
    exitCode: -1,
    output: 'Bu tarayıcıdan yapılamaz. Masaüstü sürümünü kullanın.',
  );
}

SystemControl createSystemControl() => const _UnsupportedSystemControl();
