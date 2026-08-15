/// Puts back the line breaks that flattened an announcement into one paragraph.
///
/// What is stored in `notice_document.extracted_text` is the announcement as it
/// was extracted - and extraction loses the layout, not the structure. The
/// numbering survives:
///
///     1- İdarenin 1.1. Adı : ... 1.2. Adresi : ... 2- İhalenin 2.1. ...
///
/// So this is not reformatting text, it is reading an outline that is still
/// there and was only made invisible. D-15 already solved the same problem for
/// the result announcement, where "a value ends where the next letter starts".
///
/// **Why not markdown.** It was the obvious idea and it is actively wrong here:
/// 236 of the 267 stored texts contain `***`, because `Parse` masks contact
/// names and phone numbers before anything is written (CLAUDE.md). A markdown
/// renderer reads `***` as bold-italic and eats the rest of the document
/// looking for the closing run. The source has no markup in it; adding a
/// renderer would only add a way to misread it.
///
/// **Why here and not in the api.** D-11's rule: store the raw bytes, interpret
/// them at read time. Rewriting `extracted_text` in the database would make the
/// stored copy depend on today's idea of how it should look, and a later fix
/// could not reach what was already stored. Nothing here changes any data.
///
/// **Degrades to what it does today.** 16 of the 267 are not EKAP
/// announcements at all - a municipality's scanned notice run through the pdf
/// extractor has no outline in it. Fewer than two section markers and this
/// hands back the text untouched, which is exactly what the screen showed
/// before.
library;

/// One piece of a read announcement.
sealed class AnnouncementBlock {
  const AnnouncementBlock();
}

/// Text with no number in front of it: the opening sentences, or the whole
/// document when no outline was found.
class AnnouncementProse extends AnnouncementBlock {
  const AnnouncementProse(this.text);

  final String text;
}

/// A top-level heading, `1- İdarenin`.
class AnnouncementSection extends AnnouncementBlock {
  const AnnouncementSection({required this.number, required this.title});

  /// Without the dash, so `1`.
  final String number;

  final String title;
}

/// A numbered item, `1.2. Adresi : ZÜBEYDE HANIM MAHALLESİ ...`.
class AnnouncementItem extends AnnouncementBlock {
  const AnnouncementItem({
    required this.number,
    required this.label,
    required this.value,
  });

  /// Without the trailing dot, so `1.2`.
  final String number;

  /// Null where the item is a bare sentence rather than a label and a value -
  /// `4.1.1. Teklif mektubu.` is one of those, and inventing a label for it
  /// would be worse than leaving it as a line.
  final String? label;

  final String value;

  /// How deep to indent it: `4.1.2.1` is three levels under its section.
  int get depth => '.'.allMatches(number).length;
}

/// A section marker (`3- `) or an item marker (`3.2. `), at a word boundary and
/// followed by a capital.
///
/// Every part of this is holding something out:
///
/// * the item branch needs at least one `.digit` group, so `45. maddesi` is a
///   sentence and not an item;
/// * `\d{1,2}` on each part keeps `04.09.2026` from reading as item 4.9;
/// * requiring an uppercase letter after the space keeps `No:12 Altındağ` and
///   `10(On) gündür` out;
/// * the lookbehind stops it matching inside a longer number.
///
/// Turkish uppercase is covered by the unicode flag - `İ` is U+0130 and starts
/// the very first section title in most of these documents.
final RegExp _marker = RegExp(
  r'(?<=^|\s)(?:(?<section>\d{1,2})-|(?<item>\d{1,2}(?:\.\d{1,2}){1,3}))\.?\s+(?=\p{Lu})',
  unicode: true,
);

/// A label runs to the first colon, and only if that colon turns up early.
///
/// `1.3. Telefon: ***` has no space before the colon and `2.2. Yapılacağı
/// (e-tekliflerin açılacağı) adres : Gelir ...` has parentheses inside the
/// label, so neither can be found by splitting on a fixed shape. The distance
/// limit is what keeps a whole paragraph from becoming a label because it
/// happened to contain `saat 10:00` two lines down.
const int _longestLabel = 70;

/// Below this many sections it is not an outline, whatever the numbers say.
const int _enoughSections = 2;

List<AnnouncementBlock> readAnnouncement(String? text) {
  final String source = (text ?? '').trim();

  if (source.isEmpty) return const <AnnouncementBlock>[];

  final List<RegExpMatch> markers = _marker.allMatches(source).toList();

  final int sections = markers
      .where((RegExpMatch m) => m.namedGroup('section') != null)
      .length;

  if (sections < _enoughSections) {
    return <AnnouncementBlock>[AnnouncementProse(source)];
  }

  final List<AnnouncementBlock> blocks = <AnnouncementBlock>[];

  final String intro = source.substring(0, markers.first.start).trim();
  if (intro.isNotEmpty) blocks.add(AnnouncementProse(intro));

  for (int i = 0; i < markers.length; i++) {
    final RegExpMatch marker = markers[i];
    final int end = i + 1 < markers.length ? markers[i + 1].start : source.length;
    final String body = source.substring(marker.end, end).trim();

    if (body.isEmpty) continue;

    if (marker.namedGroup('section') case final String number) {
      // "4- Katılım ve yeterlik kriterleri:" - the document ends the heading
      // with the colon that introduced the items under it. As a heading on its
      // own line that colon dangles.
      final String title =
          body.endsWith(':') ? body.substring(0, body.length - 1).trimRight() : body;

      blocks.add(AnnouncementSection(number: number, title: title));
      continue;
    }

    blocks.add(_item(marker.namedGroup('item')!, body));
  }

  return blocks;
}

AnnouncementItem _item(String number, String body) {
  final int colon = body.indexOf(':');

  if (colon < 0 || colon > _longestLabel) {
    return AnnouncementItem(number: number, label: null, value: body);
  }

  final String label = body.substring(0, colon).trim();
  final String value = body.substring(colon + 1).trim();

  // A label with nothing after it is a heading, not a pair. Keeping it as a
  // label with an empty value would render as a dangling colon.
  if (label.isEmpty || value.isEmpty) {
    return AnnouncementItem(number: number, label: null, value: body);
  }

  return AnnouncementItem(number: number, label: label, value: value);
}
