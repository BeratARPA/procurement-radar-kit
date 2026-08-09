import '../../api/export.dart';

/// How the notice list is narrowed.
///
/// The score half is sent to the api, which already takes `minScore` and had
/// been ignored: the screen asked for everything and showed everything, so one
/// notice worth reading arrived under nine pages of toner and hard disks.
///
/// The time and sort halves are done here, on the rows already fetched. Two
/// hundred rows is nothing to filter in memory and the api has no parameter for
/// either - inventing two endpoints to avoid one `where` would be the wrong
/// trade.
class NoticeFilter {
  const NoticeFilter({
    this.term = '',
    this.scope = NoticeScope.open,
    this.minScore = 0,
    this.withinHours,
    this.sort = NoticeSort.deadline,
    this.hideUndated = false,
  });

  final String term;

  /// Still open, or everything ever collected.
  ///
  /// Open is the default because an expired notice cannot be bid on. But the
  /// archive is the thing this project is accumulating (spec Bolum 16) and
  /// Telegram already applies the filter, so the screen has to be able to hand
  /// over all of it.
  final NoticeScope scope;

  /// 0 shows everything. 30 is the notification threshold (D-14).
  final int minScore;

  /// Null means any deadline. 48 means closing within two days.
  final int? withinHours;

  final NoticeSort sort;

  /// Notices whose deadline could not be read.
  ///
  /// Kept visible by default: spec Bolum 7 records them deliberately and hiding
  /// them by default would quietly drop a third of the archive.
  final bool hideUndated;

  bool get isNarrowed =>
      minScore > 0 || withinHours != null || hideUndated || sort != NoticeSort.deadline;

  /// How many choices have been moved off their default.
  ///
  /// On a narrow screen the controls live behind a button, and a button that
  /// hides its state is a filter somebody forgets is on. Scope counts too: it is
  /// the one that changes how many notices exist at all.
  int get activeCount => <bool>[
        scope != NoticeScope.open,
        minScore > 0,
        withinHours != null,
        sort != NoticeSort.deadline,
        hideUndated,
      ].where((bool changed) => changed).length;

  NoticeFilter copyWith({
    String? term,
    NoticeScope? scope,
    int? minScore,
    int? withinHours,
    bool clearWithinHours = false,
    NoticeSort? sort,
    bool? hideUndated,
  }) =>
      NoticeFilter(
        term: term ?? this.term,
        scope: scope ?? this.scope,
        minScore: minScore ?? this.minScore,
        withinHours: clearWithinHours ? null : (withinHours ?? this.withinHours),
        sort: sort ?? this.sort,
        hideUndated: hideUndated ?? this.hideUndated,
      );

  /// Everything the api can answer. Two filters with the same key are one
  /// request, which is what makes the family cache useful.
  String get requestKey => '$term|${scope.name}|$minScore';

  @override
  bool operator ==(Object other) =>
      other is NoticeFilter &&
      other.term == term &&
      other.scope == scope &&
      other.minScore == minScore &&
      other.withinHours == withinHours &&
      other.sort == sort &&
      other.hideUndated == hideUndated;

  @override
  int get hashCode =>
      Object.hash(term, scope, minScore, withinHours, sort, hideUndated);
}

enum NoticeScope {
  /// Still biddable: status new, deadline not passed.
  open,

  /// Everything ever collected, newest first.
  all,
}

enum NoticeSort {
  /// Soonest first. The default, because time is the thing that runs out.
  deadline,

  /// Highest first, for deciding what to read rather than what is urgent.
  score,
}

/// Applies the half the api cannot.
List<CommandNotice> applyFilter(
  List<CommandNotice> rows,
  NoticeFilter filter,
  DateTime nowUtc,
) {
  final List<CommandNotice> kept = <CommandNotice>[
    for (final CommandNotice notice in rows)
      if (_keep(notice, filter, nowUtc)) notice,
  ];

  kept.sort((CommandNotice a, CommandNotice b) {
    if (filter.sort == NoticeSort.score) {
      final int byScore = b.score.compareTo(a.score);

      if (byScore != 0) return byScore;
    }

    // A notice with no deadline sorts last either way. It is not urgent because
    // nothing is known about when it closes, not because it closes late.
    final DateTime? x = a.bidDeadlineAt;
    final DateTime? y = b.bidDeadlineAt;

    if (x == null && y == null) return b.score.compareTo(a.score);
    if (x == null) return 1;
    if (y == null) return -1;

    return x.compareTo(y);
  });

  return kept;
}

bool _keep(CommandNotice notice, NoticeFilter filter, DateTime nowUtc) {
  if (notice.score < filter.minScore) return false;

  final DateTime? deadline = notice.bidDeadlineAt;

  if (deadline == null) return !filter.hideUndated && filter.withinHours == null;

  if (filter.withinHours case final int hours) {
    final Duration left = deadline.difference(nowUtc);

    if (left.isNegative || left.inHours > hours) return false;
  }

  return true;
}
