import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/filter/keyword_draft.dart';

void main() {
  /// The real setting, not an invented one (D-09).
  ///
  /// `config/keywords.json` is the file an empty table is seeded from, so it is
  /// the exact shape this screen has to survive - including the four keys it
  /// does not show.
  String realConfig() =>
      File('../config/keywords.json').readAsStringSync();

  group('the keys this screen does not show', () {
    test('are all still there after a round trip', () {
      final Map<String, Object?> before =
          jsonDecode(realConfig()) as Map<String, Object?>;

      final KeywordDraft draft = KeywordDraft.parse(realConfig());
      final Map<String, Object?> after =
          jsonDecode(draft.toJson()) as Map<String, Object?>;

      expect(after.keys, containsAll(before.keys));
    });

    test('and the okas configuration is byte for byte what it was', () {
      // The one that would hurt. spec Bolum 9 says an okas code is far more
      // reliable than a word match, and a screen that serialised only what it
      // knew about would delete these lists the first time somebody moved the
      // slider - leaving valid json, a scorer that still loads, and a path that
      // silently no longer exists.
      final Map<String, Object?> before =
          jsonDecode(realConfig()) as Map<String, Object?>;

      final Map<String, Object?> after =
          jsonDecode(KeywordDraft.parse(realConfig()).toJson())
              as Map<String, Object?>;

      expect(jsonEncode(after['okas']), jsonEncode(before['okas']));
      expect(after['rule_score_threshold'], before['rule_score_threshold']);
      expect(after['llm_score_threshold'], before['llm_score_threshold']);
      expect(after['_comment'], before['_comment']);
      expect(after['_okas_note'], before['_okas_note']);
    });

    test('and it can say which ones they are', () {
      final KeywordDraft draft = KeywordDraft.parse(realConfig());

      expect(draft.untouchedKeys, contains('okas'));
      expect(draft.untouchedKeys, isNot(contains('include')));
      expect(draft.untouchedKeys, isNot(contains('notify_threshold')));
    });
  });

  group('reading', () {
    test('the real file gives the terms and the live threshold', () {
      final KeywordDraft draft = KeywordDraft.parse(realConfig());

      expect(draft.include, isNotEmpty);
      expect(draft.exclude, isNotEmpty);
      // D-14 moved it to 30 and the test that guards that is on the C# side;
      // this only checks the screen reads the same number the scorer does.
      expect(draft.notifyThreshold, greaterThan(0));
      expect(
        draft.include.any((IncludeTerm t) => t.term == 'yazılım'),
        isTrue,
      );
    });

    test('a term with no weight is read as zero rather than dropped', () {
      // Dropping it would make a term disappear from the screen and then from
      // the saved file - a deletion nobody asked for.
      final KeywordDraft draft = KeywordDraft.parse(
        '{"include":[{"term":"portal"}],"exclude":[],"notify_threshold":30}',
      );

      expect(draft.include.single.term, 'portal');
      expect(draft.include.single.weight, 0);
    });
  });

  group('editing', () {
    test('a change does not touch the draft it came from', () {
      // The draft is passed around and rebuilt while somebody types. A shared
      // list underneath would make an abandoned edit stick.
      final KeywordDraft first = KeywordDraft.parse(realConfig());
      final int before = first.include.length;

      final KeywordDraft second = first.copyWith(
        include: <IncludeTerm>[
          ...first.include,
          const IncludeTerm(term: 'siber güvenlik', weight: 25),
        ],
      );

      expect(first.include, hasLength(before));
      expect(second.include, hasLength(before + 1));
    });

    test('what is saved is what was edited', () {
      final KeywordDraft draft = KeywordDraft.parse(realConfig()).copyWith(
        notifyThreshold: 45,
        exclude: <String>['toner'],
      );

      final Map<String, Object?> saved =
          jsonDecode(draft.toJson()) as Map<String, Object?>;

      expect(saved['notify_threshold'], 45);
      expect(saved['exclude'], <String>['toner']);
    });

    test('the saved json is indented, because it lands in a history', () {
      // setting_history is what an undo reads. One long line is not something a
      // person can compare against the previous value.
      expect(KeywordDraft.parse(realConfig()).toJson(), contains('\n  '));
    });
  });
}
