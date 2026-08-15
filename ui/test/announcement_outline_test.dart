import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/data/announcement_outline.dart';

/// Read against a real announcement, not a hand-written one.
///
/// D-09's rule for parsers is that a fixture has to come off the live page,
/// because a hand-written one tests the assumption rather than the source. The
/// same applies here: what makes this hard is exactly the things nobody would
/// think to invent - `3.1 Adı` with no trailing dot beside `3.2.` with one,
/// `Telefon:` with no space before the colon, `NO:12` inside a value, and the
/// `***` our own masking leaves behind.
void main() {
  final String announcement = File(
    'test/fixtures/ekap-announcement-2026-1340114.txt',
  ).readAsStringSync();

  group('a real EKAP announcement', () {
    late List<AnnouncementBlock> blocks;

    setUp(() => blocks = readAnnouncement(announcement));

    test('opens with the sentences that come before any number', () {
      final AnnouncementProse intro = blocks.first as AnnouncementProse;

      expect(intro.text, startsWith('YAZILIM VE DESTEK HİZMETİ ALINACAKTIR'));
      expect(intro.text, contains('açık ihale usulü ile ihale edilecektir'));
    });

    test('the sections are found', () {
      final List<String> titles = blocks
          .whereType<AnnouncementSection>()
          .map((AnnouncementSection s) => '${s.number}- ${s.title}')
          .toList();

      expect(titles, contains('1- İdarenin'));
      expect(titles, contains('2- İhalenin'));
      expect(titles, contains('3- İhale konusu hizmet alımının'));
      expect(titles, contains('4- Katılım ve yeterlik kriterleri'));
    });

    test('a label and its value are separated', () {
      final AnnouncementItem address = blocks
          .whereType<AnnouncementItem>()
          .firstWhere((AnnouncementItem i) => i.number == '1.2');

      expect(address.label, 'Adresi');
      expect(address.value, startsWith('ZÜBEYDE HANIM MAHALLESİ'));
      // The value carries a colon of its own. Splitting on the last one, or on
      // every one, would have cut the address in half.
      expect(address.value, contains('NO:12'));
    });

    test('a colon with no space before it is still a label', () {
      final AnnouncementItem phone = blocks
          .whereType<AnnouncementItem>()
          .firstWhere((AnnouncementItem i) => i.number == '1.3');

      expect(phone.label, 'Telefon');
    });

    test('an item numbered without a trailing dot is still an item', () {
      // The document writes `3.1 Adı` and `3.2. Niteliği` on the same page.
      // Requiring the dot would have swallowed 3.1 into the section heading.
      final AnnouncementItem name = blocks
          .whereType<AnnouncementItem>()
          .firstWhere((AnnouncementItem i) => i.number == '3.1');

      expect(name.label, 'Adı');
      expect(name.value, contains('YAZILIM LİSANS ALIMI'));
    });

    test('a date in a value is not read as an item number', () {
      // `2.1. Tarih ve Saati : 04.09.2026 - 10:00` - the value looks exactly
      // like a numbering scheme and is not one.
      final AnnouncementItem when = blocks
          .whereType<AnnouncementItem>()
          .firstWhere((AnnouncementItem i) => i.number == '2.1');

      expect(when.value, '04.09.2026 - 10:00');
      expect(
        blocks.whereType<AnnouncementItem>().map((AnnouncementItem i) => i.number),
        isNot(contains('04.09')),
      );
    });

    test('depth follows the numbering', () {
      final Map<String, int> depths = <String, int>{
        for (final AnnouncementItem i in blocks.whereType<AnnouncementItem>())
          i.number: i.depth,
      };

      expect(depths['1.1'], 1);
      expect(depths['4.1.1'], 2);
      expect(depths['4.1.2.2'], 3);
    });

    test('an item whose number our own masking ate folds into the one above', () {
      // Real damage, found by reading this fixture rather than by reasoning.
      // PersonalDataMasker replaces everything from a contact label to the end
      // of the sentence, and the sentence ends at the first full stop - which
      // is the one inside the *next* item's number. So
      //
      //   1.3. Telefon: 0312 ... 1.4. İhale dokümanının ...
      //
      // is stored as `1.3. Telefon: ***.4. İhale dokümanının ...` and item 1.4
      // has no number left to be found by. Twice in this document: 1.4 and
      // 4.1.2.1.
      //
      // Nothing here tries to reconstruct it. A leading digit that is not in
      // the text cannot be recovered, and guessing one would put a heading on
      // content that might not belong under it. The orphan stays inside the
      // masked item's value, which is where the characters actually are.
      final List<String?> numbers = blocks
          .whereType<AnnouncementItem>()
          .map((AnnouncementItem i) => i.number)
          .toList();

      expect(numbers, isNot(contains('1.4')));
      expect(numbers, isNot(contains('4.1.2.1')));

      final AnnouncementItem phone = blocks
          .whereType<AnnouncementItem>()
          .firstWhere((AnnouncementItem i) => i.number == '1.3');

      expect(phone.value, startsWith('***'));
      expect(phone.value, contains('İhale dokümanının'));
    });

    test('nothing is dropped on the floor', () {
      // The whole point is to add line breaks, not to filter. If a rewrite ever
      // starts discarding items this fails even when every case above passes.
      final int words = announcement.split(RegExp(r'\s+')).length;

      final int kept = blocks
          .map((AnnouncementBlock b) => switch (b) {
                AnnouncementProse(:final String text) => text,
                AnnouncementSection(:final String title) => title,
                AnnouncementItem(:final String? label, :final String value) =>
                  '${label ?? ''} $value',
              })
          .join(' ')
          .split(RegExp(r'\s+'))
          .length;

      // Only the numbers themselves and the punctuation around them are gone.
      expect(kept, greaterThan((words * 0.9).round()));
    });
  });

  group('what it refuses to touch', () {
    test('a scanned municipal notice comes back whole', () {
      // 16 of the 267 stored texts are like this: a pdf with no outline in it.
      // Before this existed the screen showed the raw text, and for these it
      // still has to - a wrong outline is worse than none.
      const String notice =
          'L İLAN EVREN BELEDİYE BAŞKANLIĞINDAN Mülkiyeti Belediyemize ait '
          'ilçemizin Yusufuşağı Mahallesinde bulunan ve aşağıda özellikleri '
          'belirtilen taşınmazın 2886 sayılı Devlet ihale Kanununun 45. '
          'maddesi gereğince açık artırma suretiyle 28.05.2025 Çarşamba günü '
          'saat 14:00 da ihale edilecektir.';

      final AnnouncementProse only =
          readAnnouncement(notice).single as AnnouncementProse;

      expect(only.text, notice);
    });

    test('`45. maddesi` is a sentence, not item 45', () {
      // The item branch needs a `.digit` group for exactly this reason. An
      // ordinal is the commonest number in a Turkish legal sentence.
      expect(readAnnouncement('2886 sayılı Kanunun 45. maddesi gereğince').single,
          isA<AnnouncementProse>());
    });

    test('empty and null are empty', () {
      expect(readAnnouncement(null), isEmpty);
      expect(readAnnouncement('   '), isEmpty);
    });
  });

  test('the masked contact details survive as written', () {
    // Parse masks names and phone numbers before storage (CLAUDE.md), and what
    // it leaves is `***`. This is the reason the announcement is not run
    // through a markdown renderer: three asterisks open bold-italic and the
    // rest of the document disappears into the run. Here they are just text.
    final String rendered = readAnnouncement(announcement)
        .whereType<AnnouncementItem>()
        .map((AnnouncementItem i) => i.value)
        .join(' ');

    expect(rendered, contains('***'));
  });
}
