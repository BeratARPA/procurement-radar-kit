/// What the Sistem screen knows, with no platform in it.
///
/// Kept free of `dart:io` so the web build can import it. The web has no way to
/// look at a process or a container, and the screen is hidden there - but the
/// types still have to compile, because they are named in shared code.
library;

/// One moving part of the system.
enum Part {
  /// PostgreSQL in Docker (D-05).
  database,

  /// The collector, registered as a task that starts at logon (D-10).
  worker,

  /// The local api the interface talks to (D-16).
  api,
}

enum Health {
  /// Doing what it should.
  up,

  /// Not running. Not necessarily wrong - the api is only needed when something
  /// is looking at it.
  down,

  /// Running and unhappy: a container that is up but not healthy, a task in a
  /// state that is neither running nor ready.
  degraded,

  /// The question could not be asked. A missing docker, a task that was never
  /// registered, a project directory pointing at nothing.
  unknown,
}

class PartStatus {
  const PartStatus({
    required this.part,
    required this.health,
    required this.detail,
    this.startsAtLogon,
    this.canStart = false,
    this.canStop = false,
  });

  const PartStatus.unknown(this.part, this.detail)
      : health = Health.unknown,
        startsAtLogon = null,
        canStart = false,
        canStop = false;

  final Part part;
  final Health health;

  /// One line a person can read. "healthy, 3 gün", "60080 dinliyor".
  final String detail;

  /// Null when the part has no such notion, or it could not be read.
  final bool? startsAtLogon;

  final bool canStart;
  final bool canStop;
}

/// The result of running something, kept whole.
///
/// Both streams and the exit code, because the interesting output is not always
/// on the one you expect: `preflight` writes its findings to stdout and its
/// verdict to the log, and `docker` puts most of its complaints on stderr.
class CommandOutcome {
  const CommandOutcome({
    required this.command,
    required this.exitCode,
    required this.output,
  });

  final String command;
  final int exitCode;
  final String output;

  bool get succeeded => exitCode == 0;
}

/// One line out of a Serilog file (D-07 writes them as json).
class LogLine {
  const LogLine({
    required this.at,
    required this.level,
    required this.text,
  });

  final DateTime? at;

  /// Information, Warning, Error, Fatal - Serilog's own spellings.
  final String level;
  final String text;

  bool get isProblem => level == 'Warning' || level == 'Error' || level == 'Fatal';
}

/// A command the Sistem screen offers.
class CliCommand {
  const CliCommand({
    required this.name,
    required this.description,
    this.arguments = const <String>[],
    this.isSafe = true,
  });

  /// The verb as the CLI spells it.
  final String name;

  /// Why somebody would press it.
  final String description;

  final List<String> arguments;

  /// False for anything that writes. Those get a confirmation first.
  final bool isSafe;
}
