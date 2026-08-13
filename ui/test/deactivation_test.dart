import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/api/export.dart';
import 'package:procurement_radar/src/sources/deactivation.dart';

/// The reason a source is switched off, and what it costs to get it wrong.
///
/// D-22 splits the six reasons into two groups: three put the source into a
/// queue re-probed once a day, three take it out of circulation. Between 11 and
/// 13 August the screen sent no reason at all, the api read that as `manual`,
/// and `manual` is in the second group - so every source switched off from the
/// interface quietly opted out of the mechanism.
void main() {
  group('what the screen may offer', () {
    test('never includes the one only the monitor can observe', () {
      // health_rule means ten consecutive failures. Offering it would let a
      // person record a diagnosis the system did not make, and the api refuses
      // it with a 400 anyway.
      expect(
        DeactivationChoice.selectable.map((DeactivationChoice c) => c.reason),
        isNot(contains(DeactivationReason.healthRule)),
      );
    });

    test('covers every other reason the api accepts', () {
      // If the server grows a seventh reason, the generated enum gains it and
      // this fails - rather than the screen quietly never offering it.
      final Set<DeactivationReason> offered = DeactivationChoice.selectable
          .map((DeactivationChoice c) => c.reason)
          .toSet();

      final Set<DeactivationReason> expected = DeactivationReason.$valuesDefined
          .where((DeactivationReason r) => r != DeactivationReason.healthRule)
          .toSet();

      expect(offered, expected);
    });

    test('puts the one that comes back at the top', () {
      // The ordering is the advice: if the site might return, saying so is what
      // brings it back.
      expect(
        DeactivationChoice.selectable.first.reason,
        DeactivationReason.siteUnreachable,
      );
      expect(DeactivationChoice.selectable.first.retried, isTrue);
    });

    test('every choice says whether it will be looked at again', () {
      for (final DeactivationChoice choice in DeactivationChoice.selectable) {
        expect(choice.label, isNotEmpty);
        expect(choice.help, isNotEmpty);
      }
    });
  });

  group('which reasons are re-probed', () {
    test('matches the sweeper: site_unreachable and health_rule, nothing else', () {
      // The list the index in 011_source_quarantine.sql is built on. A screen
      // that promised a re-probe the sweeper does not perform would be worse
      // than saying nothing.
      final Set<DeactivationReason> retried = <DeactivationReason>{
        for (final DeactivationReason reason in DeactivationReason.$valuesDefined)
          if (DeactivationChoice.describe(reason)?.retried ?? false) reason,
      };

      expect(retried, <DeactivationReason>{
        DeactivationReason.siteUnreachable,
        DeactivationReason.healthRule,
      });
    });
  });

  group('reading a stored reason back', () {
    test('describes the one nobody can choose', () {
      // Not selectable, but very much displayable: it is the reason the
      // automatic path uses, so most quarantined sources carry it.
      final DeactivationChoice? why =
          DeactivationChoice.describe(DeactivationReason.healthRule);

      expect(why, isNotNull);
      expect(why!.retried, isTrue);
    });

    test('a source switched off before the column existed reads as unknown', () {
      // The 011 backfill left deactivated_at null rather than inventing one,
      // and a row can still arrive with no reason at all. The row has to say
      // "not recorded" rather than guess.
      expect(DeactivationChoice.describe(null), isNull);
      expect(DeactivationChoice.describe(DeactivationReason.$unknown), isNull);
    });
  });
}
