import 'dart:convert';

/// The politeness budget, opened for editing without losing the rest of it.
///
/// Same rule as [KeywordDraft]: the document carries notes this screen does not
/// edit, and serialising only the known fields would delete them. Here they
/// matter more than usual - the notes are the reasons, written in Turkish by the
/// team, and they are what the screen shows beside each field. T-20 asks for the
/// floors to be visible *and justified*; the justification already exists in the
/// data and does not need inventing.
class PolitenessDraft {
  const PolitenessDraft._(this._raw);

  factory PolitenessDraft.parse(String json) =>
      PolitenessDraft._(jsonDecode(json) as Map<String, Object?>);

  /// Below this the api refuses the setting, and so does the worker.
  ///
  /// CLAUDE.md: one request per host at a time, five seconds apart. Repeated
  /// here only so the screen can stop somebody before they press save; the
  /// enforcement is on the server, where it holds for a curl too (D-16).
  static const int hostIntervalFloorSeconds = 5;

  final Map<String, Object?> _raw;

  int get hostIntervalSeconds => _int('minimum_host_interval_seconds', 5);
  int get jitterSeconds => _int('maximum_jitter_seconds', 3);
  bool get respectRobots => _raw['respect_robots'] as bool? ?? true;
  int get nightStartHour => _int('night_window_start_hour', 2);
  int get nightEndHour => _int('night_window_end_hour', 5);
  int get nightMultiplier => _int('night_interval_multiplier', 3);
  int get requestTimeoutSeconds => _int('request_timeout_seconds', 30);
  int get maximumAttempts => _int('maximum_attempts', 3);

  int _int(String key, int fallback) => (_raw[key] as num?)?.toInt() ?? fallback;

  /// The team's own note for a field, if the document carries one.
  String? noteFor(String key) {
    final Object? note = _raw[key];

    return note is String && note.trim().isNotEmpty ? note : null;
  }

  PolitenessDraft withValue(String key, Object? value) {
    final Map<String, Object?> next = Map<String, Object?>.of(_raw);
    next[key] = value;

    return PolitenessDraft._(next);
  }

  /// Why this cannot be saved, in the reader's language, before the api says it
  /// in English.
  ///
  /// Not a second validator - the server still decides, and its answer is the
  /// one that counts. This only saves a round trip and puts the message next to
  /// the field that caused it.
  String? get refusal {
    if (hostIntervalSeconds < hostIntervalFloorSeconds) {
      return 'Aynı host\'a istekler arası süre $hostIntervalFloorSeconds '
          'saniyenin altına inemez.';
    }

    if (!respectRobots) {
      return 'robots.txt kapatılamaz.';
    }

    if (jitterSeconds < 0) return 'Jitter negatif olamaz.';
    if (nightMultiplier < 1) {
      return 'Gece katsayısı 1\'in altına inemez — altına inerse gece daha '
          'sık taranır, kuralın tersi olur.';
    }

    return null;
  }

  String toJson() => const JsonEncoder.withIndent('  ').convert(_raw);

  /// The note keys, so the screen can show them and nobody wonders where the
  /// explanations went.
  List<String> get noteKeys =>
      <String>[for (final String key in _raw.keys) if (key.startsWith('_')) key];
}
