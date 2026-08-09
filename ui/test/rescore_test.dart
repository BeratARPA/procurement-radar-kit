import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/filter/keyword_draft.dart';

void main() {
  String realConfig() => File('../config/keywords.json').readAsStringSync();

  group('the rescore button', () {
    test('is locked while the draft differs from what is stored', () {
      // The api rescores with the stored list, not the one on screen. Running it
      // over unsaved edits would report numbers for a list nobody chose - so the
      // screen compares the two and this is the comparison it makes.
      final String stored = realConfig();
      final KeywordDraft edited =
          KeywordDraft.parse(stored).copyWith(notifyThreshold: 55);

      expect(edited.toJson() == stored, isFalse);
    });

    test('is available when the draft is what is stored', () {
      // A round trip through the editor must leave the document identical, or
      // the button would never unlock and nobody could tell why.
      final String stored = realConfig();

      expect(KeywordDraft.parse(stored).toJson(), stored.trim());
    });
  });
}
