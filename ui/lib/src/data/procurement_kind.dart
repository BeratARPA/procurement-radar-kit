/// Turkish names for the procurement type and procedure code a notice carries.
///
/// The codes are English because the schema is (CLAUDE.md); the words a person
/// reads are Turkish because the reader is. The mapping lives here rather than
/// in the api: the api answers with codes on purpose, and a sentence crossing
/// that boundary would put Turkish in a `.cs` file.
///
/// Most of the archive has neither field. Only EKAP states them - an authority's
/// own listing page carries a title and a link, and nothing on it says whether a
/// purchase under the monetary limit or an open tender is behind it. So the
/// absent case is the common one and it is labelled, not hidden: a row with no
/// badge would read as "ordinary" rather than as "not known".
library;

/// What a notice is, coarsely. Matches `procurement_notice.procurement_type`.
String? procurementTypeLabel(String? code) => switch (code) {
      'direct_procurement' => 'Doğrudan temin',
      'tender' => 'İhale',
      'exempt' => 'İstisna',
      _ => null,
    };

/// Which article or procedure it was run under.
///
/// `art22d` is the monetary-limit purchase and the one this project was built
/// around; `art21e` is a negotiated tender. Anything unrecognised comes back as
/// the raw code rather than as null - a code we have not named yet is still more
/// than nothing, and it shows up on screen where somebody will notice it.
String? procedureLabel(String? code) {
  if (code == null || code.isEmpty) return null;

  return switch (code) {
    'open' => 'Açık ihale',
    'restricted' => 'Belli istekliler',
    'negotiated' => 'Pazarlık',
    'exempt_procedure' => 'İstisna',
    'art22' => 'Doğrudan temin (madde 22)',
    _ => _articleLabel(code) ?? code,
  };
}

/// `art22d` reads as `22/d`, which is how every notice and every official page
/// writes it.
String? _articleLabel(String code) {
  final RegExpMatch? match =
      RegExp(r'^art(\d{1,2})([a-z])$').firstMatch(code);

  if (match == null) return null;

  return 'Madde ${match.group(1)}/${match.group(2)}';
}

/// The one line to put on a row: the type, narrowed by the article when there
/// is one.
///
/// Null when the notice says nothing about either, which the caller shows as
/// "belirtilmemiş" rather than as blank.
String? procurementSummary(String? type, String? procedure) {
  final String? typeText = procurementTypeLabel(type);
  final String? procedureText = procedureLabel(procedure);

  if (typeText == null) return procedureText;
  if (procedureText == null) return typeText;

  // "Doğrudan temin · Madde 22/d" rather than repeating the word twice.
  if (procedureText.startsWith(typeText)) return procedureText;

  return '$typeText · $procedureText';
}
