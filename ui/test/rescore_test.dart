import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/filter/keyword_draft.dart';
import 'package:procurement_radar/src/screens/filter_screen.dart';

void main() {
  String realConfig() => File('../config/keywords.json').readAsStringSync();

  /// The same document as the api hands it back.
  ///
  /// `setting.value` is jsonb (D-16), so Postgres returns it on one line, in its
  /// own key order, with the whitespace gone. This is what the screen compares
  /// against - not the indented file, which is only what an empty table is
  /// seeded from.
  String asStored() {
    final Map<String, Object?> parsed =
        jsonDecode(realConfig()) as Map<String, Object?>;

    // jsonb orders keys by length and then alphabetically. Reproduced here so
    // the test is not quietly relying on the file's order surviving.
    final List<String> keys = parsed.keys.toList()
      ..sort((String a, String b) =>
          a.length != b.length ? a.length - b.length : a.compareTo(b));

    return jsonEncode(<String, Object?>{
      for (final String key in keys) key: parsed[key],
    });
  }

  group('the rescore button', () {
    test('is available when nothing has been edited', () {
      // The bug this exists for. The check was `stored.value != draft.toJson()`
      // - text against text - and the stored value is jsonb, so it comes back on
      // one line while the draft serialises indented. They are never the same
      // string, so the button was permanently disabled, telling somebody who had
      // typed nothing to save first.
      final String stored = asStored();

      expect(stored, isNot(contains('\n')), reason: 'not the stored shape');
      expect(
        sameKeywordDocument(stored, KeywordDraft.parse(stored)),
        isTrue,
        reason: 'formatting alone must not read as an edit',
      );
    });

    test('and still available when the file is what came back instead', () {
      // A machine seeded from the file rather than from the api. Same answer.
      final String stored = realConfig();

      expect(
        sameKeywordDocument(stored, KeywordDraft.parse(stored)),
        isTrue,
      );
    });

    test('is locked once a real edit is made', () {
      final String stored = asStored();
      final KeywordDraft edited =
          KeywordDraft.parse(stored).copyWith(notifyThreshold: 55);

      expect(sameKeywordDocument(stored, edited), isFalse);
    });

    test('is locked when a term is added, not just when a number moves', () {
      final String stored = asStored();
      final KeywordDraft draft = KeywordDraft.parse(stored);
      final KeywordDraft edited = draft.copyWith(
        include: <IncludeTerm>[
          ...draft.include,
          const IncludeTerm(term: 'bir şey', weight: 30),
        ],
      );

      expect(sameKeywordDocument(stored, edited), isFalse);
    });

    test('an unreadable stored value keeps it locked rather than throwing', () {
      // The api reports a setting its loader cannot read. A button is the wrong
      // place to find that out, and the wrong place to crash.
      expect(
        sameKeywordDocument('not json', KeywordDraft.parse(realConfig())),
        isFalse,
      );
    });
  });
}
