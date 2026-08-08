import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/api/export.dart';

void main() {
  group('authority type', () {
    test('reads the names the schema stores, not numbers', () {
      // This is the bug the source editor showed as an empty dropdown on every
      // source. The api described the field as an integer 0-5, so the generated
      // enum was value0..value5 - meaningless to a screen, and matching nothing
      // in a list of names. spec Bolum 4 stores this column as text.
      expect(
        AuthorityType.fromJson('provincial_directorate'),
        AuthorityType.provincialDirectorate,
      );
      expect(AuthorityType.fromJson('soe_subsidiary'), AuthorityType.soeSubsidiary);
      expect(AuthorityType.fromJson('ekap'), AuthorityType.ekap);
    });

    test('every value the schema lists has a name to show', () {
      // The dropdown is built from this list, so a value with no name would be
      // a blank row somebody could select.
      for (final AuthorityType type in AuthorityType.$valuesDefined) {
        expect(type.json, isNotNull, reason: '$type');
        expect(type.json, isNotEmpty, reason: '$type');
        expect(type.json, isNot(startsWith('value')), reason: '$type');
      }
    });

    test('covers the six the schema names', () {
      expect(
        AuthorityType.$valuesDefined.map((AuthorityType t) => t.json),
        containsAll(<String>[
          'university',
          'municipality',
          'provincial_directorate',
          'hospital',
          'soe_subsidiary',
          'ekap',
        ]),
      );
    });

    test('something the backend adds later does not throw', () {
      expect(AuthorityType.fromJson('something_new'), AuthorityType.$unknown);
    });

    test('a source deserialises with its type readable', () {
      final Source source = Source.fromJson(<String, Object?>{
        'id': 1013,
        'authorityName': 'Kocaeli Büyükşehir Belediyesi',
        'authorityType': 'municipality',
        'province': 'Kocaeli',
        'url': 'https://www.kocaeli.bel.tr/ihaleler',
        'adapterKey': 'generic_table',
        'isActive': true,
        'consecutiveFailures': 0,
        'pollIntervalMinutes': 15,
      });

      expect(source.authorityType, AuthorityType.municipality);
      expect(source.authorityType?.json, 'municipality');
    });
  });
}
