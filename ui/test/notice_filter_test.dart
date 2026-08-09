import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/api/export.dart';
import 'package:procurement_radar/src/data/notice_filter.dart';

void main() {
  final DateTime now = DateTime.utc(2026, 8, 9, 12);

  CommandNotice notice({
    required int id,
    required int score,
    Duration? closesIn,
    String title = 'İlan',
  }) =>
      CommandNotice(
        noticeId: id,
        title: title,
        authorityName: 'İdare',
        score: score,
        bidDeadlineAt: closesIn == null ? null : now.add(closesIn),
      );

  // What the screen actually showed before this existed: one notice worth
  // reading under a page of hardware.
  final List<CommandNotice> rows = <CommandNotice>[
    notice(id: 1, score: 30, closesIn: const Duration(hours: 14), title: 'Arşiv Yönetim Sistemi Yazılımı'),
    notice(id: 2, score: 0, closesIn: const Duration(hours: 14), title: 'HARD DİSK 4 TB ALIMI'),
    notice(id: 3, score: 0, closesIn: const Duration(hours: 27), title: 'VEST CİHAZI İÇİN YELEK'),
    notice(id: 4, score: 25, closesIn: const Duration(days: 6), title: 'Veri Ambarı Çalışması'),
    notice(id: 5, score: 40, closesIn: null, title: 'Tarihi okunamayan yazılım işi'),
    notice(id: 6, score: 10, closesIn: const Duration(hours: -5), title: 'Süresi geçmiş'),
  ];

  group('score', () {
    test('shows everything at zero', () {
      expect(applyFilter(rows, const NoticeFilter(), now), hasLength(rows.length));
    });

    test('drops the unscored ones at one', () {
      final List<int> ids = applyFilter(rows, const NoticeFilter(minScore: 1), now)
          .map((CommandNotice n) => n.noticeId)
          .toList();

      expect(ids, isNot(contains(2)));
      expect(ids, isNot(contains(3)));
      expect(ids, containsAll(<int>[1, 4, 5]));
    });

    test('at the notification threshold, only what would have been announced', () {
      // D-14 put the threshold at 30. This chip answers "what did the system
      // actually tell me about", which is a different question to "what is open".
      final List<int> ids = applyFilter(rows, const NoticeFilter(minScore: 30), now)
          .map((CommandNotice n) => n.noticeId)
          .toList();

      expect(ids, unorderedEquals(<int>[1, 5]));
    });
  });

  group('deadline window', () {
    test('keeps only what closes inside it', () {
      final List<int> ids =
          applyFilter(rows, const NoticeFilter(withinHours: 24), now)
              .map((CommandNotice n) => n.noticeId)
              .toList();

      expect(ids, unorderedEquals(<int>[1, 2]));
    });

    test('never keeps one that has already closed', () {
      for (final int hours in <int>[24, 72, 168]) {
        final List<int> ids =
            applyFilter(rows, NoticeFilter(withinHours: hours), now)
                .map((CommandNotice n) => n.noticeId)
                .toList();

        expect(ids, isNot(contains(6)), reason: '$hours saat');
      }
    });

    test('a notice with no deadline is out of every window', () {
      // Not because it closes late - because nothing is known about when it
      // closes. Putting it in a "closing within 24 hours" list would be a claim
      // the data does not support.
      final List<int> ids =
          applyFilter(rows, const NoticeFilter(withinHours: 168), now)
              .map((CommandNotice n) => n.noticeId)
              .toList();

      expect(ids, isNot(contains(5)));
    });
  });

  group('undated notices', () {
    test('are shown by default', () {
      // spec Bolum 7 records them on purpose. Hiding them by default would drop
      // a third of the archive without saying so.
      expect(
        applyFilter(rows, const NoticeFilter(), now).map((CommandNotice n) => n.noticeId),
        contains(5),
      );
    });

    test('can be hidden deliberately', () {
      expect(
        applyFilter(rows, const NoticeFilter(hideUndated: true), now)
            .map((CommandNotice n) => n.noticeId),
        isNot(contains(5)),
      );
    });
  });

  group('sorting', () {
    test('soonest first by default', () {
      final List<int> ids = applyFilter(rows, const NoticeFilter(), now)
          .map((CommandNotice n) => n.noticeId)
          .toList();

      // 6 closed five hours ago so it sorts before the rest; 5 has no deadline
      // and sorts last whatever else happens.
      expect(ids.first, 6);
      expect(ids.last, 5);
    });

    test('highest score first when asked', () {
      final List<int> ids =
          applyFilter(rows, const NoticeFilter(sort: NoticeSort.score), now)
              .map((CommandNotice n) => n.noticeId)
              .toList();

      expect(ids.first, 5);
      expect(ids[1], 1);
    });

    test('an undated notice still sorts last within its score', () {
      final List<CommandNotice> pair = <CommandNotice>[
        notice(id: 10, score: 30, closesIn: null),
        notice(id: 11, score: 30, closesIn: const Duration(days: 3)),
      ];

      expect(
        applyFilter(pair, const NoticeFilter(sort: NoticeSort.score), now)
            .map((CommandNotice n) => n.noticeId),
        <int>[11, 10],
      );
    });
  });

  group('the request key', () {
    test('changes when the api would answer differently', () {
      expect(const NoticeFilter().requestKey,
          isNot(const NoticeFilter(minScore: 30).requestKey));
      expect(const NoticeFilter().requestKey,
          isNot(const NoticeFilter(term: 'yazılım').requestKey));
      // Scope is the api's question too: open is 632 rows, all is 988.
      expect(const NoticeFilter().requestKey,
          isNot(const NoticeFilter(scope: NoticeScope.all).requestKey));
    });

    test('does not change for filters the screen applies itself', () {
      // Changing the sort order or the deadline window must not re-fetch.
      expect(
        const NoticeFilter(sort: NoticeSort.score).requestKey,
        const NoticeFilter().requestKey,
      );
      expect(
        const NoticeFilter(withinHours: 24).requestKey,
        const NoticeFilter().requestKey,
      );
      expect(
        const NoticeFilter(hideUndated: true).requestKey,
        const NoticeFilter().requestKey,
      );
    });
  });
}
