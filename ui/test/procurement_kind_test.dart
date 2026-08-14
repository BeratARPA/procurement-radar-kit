import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/data/procurement_kind.dart';

/// The codes the api answers with, and the words a person reads.
///
/// The api sends `art22d` on purpose - the schema is English (CLAUDE.md) and a
/// Turkish sentence crossing that boundary would put Turkish in a `.cs` file.
/// So the translation lives here, and so does the risk that it silently stops
/// covering a code the parser started producing.
void main() {
  group('what the notice is', () {
    test('the three types read as Turkish', () {
      expect(procurementTypeLabel('direct_procurement'), 'Doğrudan temin');
      expect(procurementTypeLabel('tender'), 'İhale');
      expect(procurementTypeLabel('exempt'), 'İstisna');
    });

    test('an absent type is absent rather than guessed', () {
      // Nine notices in ten. The screen has to be able to tell "we do not know"
      // from "ordinary tender", so this may not fall back to a type.
      expect(procurementTypeLabel(null), isNull);
      expect(procurementTypeLabel(''), isNull);
    });
  });

  group('which article', () {
    test('an article reads the way every official page writes it', () {
      expect(procedureLabel('art22d'), 'Madde 22/d');
      expect(procedureLabel('art21e'), 'Madde 21/e');
    });

    test('the named procedures read as words', () {
      expect(procedureLabel('open'), 'Açık ihale');
      expect(procedureLabel('restricted'), 'Belli istekliler');
      expect(procedureLabel('negotiated'), 'Pazarlık');
    });

    test('a code nobody has named yet is shown, not swallowed', () {
      // The failure this forecloses: the parser learns a new procedure, the
      // interface has no name for it, and the row goes blank. A blank row reads
      // as "not stated" and would be a lie - it is stated, and we did not
      // translate it. Showing the raw code puts it in front of somebody.
      expect(procedureLabel('framework_agreement'), 'framework_agreement');
    });
  });

  group('the one line a row shows', () {
    test('type and article together', () {
      expect(
        procurementSummary('direct_procurement', 'art22d'),
        'Doğrudan temin · Madde 22/d',
      );
      expect(procurementSummary('tender', 'open'), 'İhale · Açık ihale');
    });

    test('the word is not said twice', () {
      // 'art22' expands to a phrase that already begins with the type.
      expect(
        procurementSummary('direct_procurement', 'art22'),
        'Doğrudan temin (madde 22)',
      );
    });

    test('either half alone still says something', () {
      expect(procurementSummary('tender', null), 'İhale');
      expect(procurementSummary(null, 'art22d'), 'Madde 22/d');
    });

    test('neither half means null, and the caller decides what to print', () {
      expect(procurementSummary(null, null), isNull);
    });
  });
}
