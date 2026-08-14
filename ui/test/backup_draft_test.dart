import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/filter/backup_draft.dart';

/// The backup setting as the Ayarlar screen edits it (D-24).
void main() {
  /// The real file, not an invented one (D-09).
  String realConfig() => File('../config/backup.json').readAsStringSync();

  group('the keys this screen does not show', () {
    test('survive a round trip', () {
      // Every explanation in the document is a `_note` key the screen reads and
      // never writes. Serialising only the known fields would delete the
      // reasoning the next person needs - the same trap KeywordDraft was
      // written to avoid.
      final Map<String, Object?> before =
          jsonDecode(realConfig()) as Map<String, Object?>;

      final Map<String, Object?> after =
          jsonDecode(BackupDraft.parse(realConfig()).toJson())
              as Map<String, Object?>;

      expect(after.keys, containsAll(before.keys));

      for (final String key in before.keys.where((String k) => k.startsWith('_'))) {
        expect(after[key], before[key], reason: '$key was lost');
      }
    });

    test('and editing one field leaves the others alone', () {
      final BackupDraft edited =
          BackupDraft.parse(realConfig()).withValue('destination', 'E:\\Yedek');

      final Map<String, Object?> after =
          jsonDecode(edited.toJson()) as Map<String, Object?>;

      expect(after['destination'], 'E:\\Yedek');
      expect(after['keep_dumps'], 14);
      expect(after['_comment'], isNotNull);
    });
  });

  group('what the screen refuses before the api has to', () {
    test('keeping zero dumps', () {
      // It would delete the dump it had just written. The server refuses this
      // too; the screen only saves a round trip and puts the sentence beside
      // the field.
      final BackupDraft draft =
          BackupDraft.parse(realConfig()).withValue('keep_dumps', 0);

      expect(draft.refusal, isNotNull);
    });

    test('a zero-day archive interval', () {
      final BackupDraft draft =
          BackupDraft.parse(realConfig()).withValue('archive_every_days', 0);

      expect(draft.refusal, isNotNull);
    });

    test('but not an empty destination', () {
      // No USB disk plugged in is a normal Tuesday, not a mistake. The screen
      // says backups are off and the save button stays live.
      final BackupDraft draft =
          BackupDraft.parse(realConfig()).withValue('destination', '');

      expect(draft.isOff, isTrue);
      expect(draft.refusal, isNull);
    });

    test('and a valid document has nothing to refuse', () {
      expect(BackupDraft.parse(realConfig()).refusal, isNull);
    });
  });

  test('the shipped default points somewhere', () {
    // If this ever ships empty, backups are off out of the box and the only
    // sign is a line in a log.
    expect(BackupDraft.parse(realConfig()).isOff, isFalse);
  });
}
