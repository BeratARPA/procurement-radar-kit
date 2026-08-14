import 'dart:convert';

/// The backup setting, opened for editing without losing the rest of it.
///
/// Same rule as [PolitenessDraft]: the document carries notes this screen does
/// not edit, and serialising only the fields it knows about would delete them.
/// Here the notes are the reasoning for each number, written in Turkish by
/// whoever set it, and the screen shows them beside the field rather than
/// inventing its own explanation.
class BackupDraft {
  const BackupDraft._(this._raw);

  factory BackupDraft.parse(String json) =>
      BackupDraft._(jsonDecode(json) as Map<String, Object?>);

  final Map<String, Object?> _raw;

  String get destination => (_raw['destination'] as String?) ?? '';
  bool get database => _raw['database'] as bool? ?? true;
  bool get archive => _raw['archive'] as bool? ?? true;
  int get keepDumps => _int('keep_dumps', 14);
  int get archiveEveryDays => _int('archive_every_days', 7);

  int _int(String key, int fallback) => (_raw[key] as num?)?.toInt() ?? fallback;

  /// The note the document carries for a field, if it carries one.
  String? noteFor(String key) {
    final Object? note = _raw[key];

    return note is String && note.trim().isNotEmpty ? note : null;
  }

  BackupDraft withValue(String key, Object? value) {
    final Map<String, Object?> next = Map<String, Object?>.of(_raw)..[key] = value;

    return BackupDraft._(next);
  }

  /// Why this cannot be saved, before the api says the same thing in English.
  ///
  /// Not a second validator - `BackupSettings.Load` on the server decides, and
  /// its answer is the one that counts. This saves a round trip and puts the
  /// message beside the field that caused it.
  String? get refusal {
    if (keepDumps < 1) {
      return 'Saklanacak döküm sayısı 1\'in altına inemez — sıfır, az önce '
          'yazılan dökümü de siler.';
    }

    if (archiveEveryDays < 1) {
      return 'Arşiv aralığı 1 günün altına inemez — her çalıştırmada 400 MB\'ı '
          'yeniden yazar.';
    }

    return null;
  }

  /// An empty destination is not an error: it means backups are off, and the
  /// command says so instead of failing every night.
  bool get isOff => destination.trim().isEmpty;

  String toJson() => const JsonEncoder.withIndent('  ').convert(_raw);
}
