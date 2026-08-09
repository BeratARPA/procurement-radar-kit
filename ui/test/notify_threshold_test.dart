import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/filter/keyword_draft.dart';
import 'package:procurement_radar/src/strings.dart';

void main() {
  group('the notification threshold chip', () {
    test('says the number the setting actually holds', () {
      // The bug: the chip read "Bildirim eşiği (30+)" while the stored setting
      // said 55, because 30 was written into the label. It named itself after
      // the threshold and did not know it.
      expect(Strings.scoreNotified(55), contains('55'));
      expect(Strings.scoreNotified(55), isNot(contains('30')));
      expect(Strings.scoreNotified(30), contains('30'));
    });

    test('the number comes out of the stored keyword document', () {
      const String stored = '{"include":[],"exclude":[],"notify_threshold":55}';

      expect(KeywordDraft.parse(stored).notifyThreshold, 55);
    });

    test('a document with no threshold falls back rather than throwing', () {
      // The api validates before storing, so this should not happen - but a
      // filter chip is the wrong place to find out that it did.
      expect(
        KeywordDraft.parse('{"include":[],"exclude":[]}').notifyThreshold,
        30,
      );
    });
  });
}
