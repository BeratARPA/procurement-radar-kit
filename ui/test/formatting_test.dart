import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:intl/intl.dart';
import 'package:procurement_radar/src/formatting.dart';
import 'package:procurement_radar/src/strings.dart';

void main() {
  setUpAll(() async {
    await initializeDateFormatting('tr_TR');
    Intl.defaultLocale = 'tr_TR';
  });

  group('deadline', () {
    test('is rendered in the local zone, not the one it arrives in', () {
      // D-08's whole point. The api serialises UTC, so parsing gives a UTC
      // DateTime, and formatting that directly prints UTC - a deadline of
      // 14:00 in Istanbul shown as 11:00, with nothing about it looking wrong.
      final DateTime utc = DateTime.utc(2026, 3, 14, 11);
      final DateFormat pattern = DateFormat('d MMMM y HH:mm', 'tr_TR');

      expect(formatDeadline(utc), pattern.format(utc.toLocal()));

      if (utc.toLocal().timeZoneOffset == Duration.zero) {
        // The machine is on UTC, so the mistake and the fix print the same
        // thing and there is nothing here to catch. Said out loud rather than
        // passing quietly, because a green test that cannot fail is worse than
        // no test.
        markTestSkipped('machine runs on UTC; the drift is invisible here');
        return;
      }

      expect(formatDeadline(utc), isNot(pattern.format(utc)));
    });

    test('says so when there is none rather than showing a blank', () {
      // spec Bolum 7: a notice whose deadline could not be read is still
      // recorded, and the reader has to be told which one it is.
      expect(formatDeadline(null), Strings.deadlineUnknown);
    });
  });

  group('remaining', () {
    final DateTime now = DateTime.utc(2026, 3, 14, 10);

    test('counts hours the way the notification does', () {
      expect(remaining(now.add(const Duration(hours: 31)), now), 'kalan 31 saat');
    });

    test('counts minutes under the hour', () {
      expect(remaining(now.add(const Duration(minutes: 40)), now), 'kalan 40 dakika');
    });

    test('counts days past two', () {
      expect(remaining(now.add(const Duration(days: 5)), now), 'kalan 5 gün');
    });

    test('does not report a negative amount of time left', () {
      expect(remaining(now.subtract(const Duration(hours: 1)), now),
          Strings.deadlinePassed);
    });

    test('crossing into summer time does not gain or lose an hour', () {
      // Turkey has not changed clocks since 2016, but the archive holds dates
      // either side of the last change and the arithmetic is done in UTC on
      // purpose so it cannot matter.
      final DateTime beforeChange = DateTime.utc(2016, 3, 26, 12);
      final DateTime afterChange = DateTime.utc(2016, 3, 28, 12);

      expect(remaining(afterChange, beforeChange), 'kalan 2 gün');
    });
  });

  group('status', () {
    test('every value the schema stores has a word for it', () {
      for (final String status in <String>[
        'new',
        'reviewed',
        'bid_submitted',
        'missed',
        'not_relevant',
      ]) {
        expect(statusLabel(status), isNot(status), reason: status);
      }
    });

    test('an unknown value is shown rather than swallowed', () {
      expect(statusLabel('something_new'), 'something_new');
    });
  });
}
