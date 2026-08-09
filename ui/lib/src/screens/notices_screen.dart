import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/export.dart';
import '../data/notice_filter.dart';
import '../data/providers.dart';
import '../filter/filter_providers.dart';
import '../formatting.dart';
import '../router.dart';
import '../strings.dart';
import '../theme.dart';
import 'widgets.dart';

class NoticesScreen extends ConsumerStatefulWidget {
  const NoticesScreen({super.key});

  @override
  ConsumerState<NoticesScreen> createState() => _NoticesScreenState();
}

class _NoticesScreenState extends ConsumerState<NoticesScreen> {
  late final TextEditingController _search =
      TextEditingController(text: ref.read(noticeFilterProvider).term);

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final NoticeFilter filter = ref.watch(noticeFilterProvider);
    final AsyncValue<List<CommandNotice>> notices =
        ref.watch(noticesProvider(filter.requestKey));

    return Scaffold(
      appBar: AppBar(
        title: Text(filter.term.isNotEmpty
            ? Strings.notices
            : filter.scope == NoticeScope.all
                ? Strings.scopeAll
                : Strings.openNotices),
        actions: <Widget>[
          IconButton(
            tooltip: Strings.refresh,
            onPressed: () => ref.invalidate(noticesProvider(filter.requestKey)),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: Column(
        children: <Widget>[
          _FilterBar(controller: _search),
          const Divider(height: 1),
          Expanded(
            child: notices.when(
              loading: () => const Center(child: CircularProgressIndicator()),
              error: (Object error, StackTrace stackTrace) => FailureView(
                error: error,
                onRetry: () => ref.invalidate(noticesProvider(filter.requestKey)),
              ),
              data: (List<CommandNotice> rows) {
                final DateTime nowUtc = DateTime.now().toUtc();
                final List<CommandNotice> shown =
                    applyFilter(rows, filter, nowUtc);

                if (shown.isEmpty) {
                  return EmptyView(
                    message: filter.isNarrowed
                        ? Strings.noneMatchFilter
                        : filter.term.isEmpty
                            ? Strings.noNotices
                            : Strings.noSearchResults,
                  );
                }

                return RefreshIndicator(
                  onRefresh: () async =>
                      ref.invalidate(noticesProvider(filter.requestKey)),
                  child: ListView.separated(
                    physics: const AlwaysScrollableScrollPhysics(),
                    itemCount: shown.length + 1,
                    separatorBuilder: (BuildContext context, int index) =>
                        const Divider(height: 1),
                    itemBuilder: (BuildContext context, int index) {
                      if (index == 0) {
                        return _Count(shown: shown.length, total: rows.length);
                      }

                      return _NoticeRow(
                        notice: shown[index - 1],
                        nowUtc: nowUtc,
                      );
                    },
                  ),
                );
              },
            ),
          ),
        ],
      ),
    );
  }
}

/// How many of how many, so a filter that hides everything is visibly a filter.
class _Count extends ConsumerWidget {
  const _Count({required this.shown, required this.total});

  final int shown;
  final int total;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final NoticeFilter filter = ref.watch(noticeFilterProvider);

    return Padding(
      padding: const EdgeInsets.fromLTRB(16, 10, 16, 6),
      child: Row(
        children: <Widget>[
          Text(
            shown == total
                ? Strings.noticeCount(total)
                : Strings.noticeCountFiltered(shown, total),
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          const Spacer(),
          if (filter.isNarrowed)
            TextButton.icon(
              onPressed: () => ref.read(noticeFilterProvider.notifier).clear(),
              icon: const Icon(Icons.filter_alt_off_outlined, size: 16),
              label: const Text(Strings.clearFilter),
            ),
        ],
      ),
    );
  }
}

class _FilterBar extends ConsumerWidget {
  const _FilterBar({required this.controller});

  final TextEditingController controller;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final NoticeFilter filter = ref.watch(noticeFilterProvider);
    final NoticeFilterNotifier notifier = ref.read(noticeFilterProvider.notifier);
    final int? threshold = ref.watch(notifyThresholdProvider);

    return Padding(
      padding: const EdgeInsets.fromLTRB(12, 10, 12, 10),
      child: Column(
        children: <Widget>[
          SearchBar(
            controller: controller,
            hintText: Strings.searchHint,
            leading: const Icon(Icons.search),
            // Searching goes to the api rather than filtering what is on
            // screen. Dart's toLowerCase is locale-independent and gets Turkish
            // wrong - 'I' folds to 'i' and not 'ı' - so a local filter would
            // disagree with the normalisation in spec Bolum 8 on exactly the
            // words this project is about.
            onSubmitted: notifier.setTerm,
            trailing: <Widget>[
              if (filter.term.isNotEmpty)
                IconButton(
                  tooltip: Strings.clearSearch,
                  onPressed: () {
                    controller.clear();
                    notifier.setTerm('');
                  },
                  icon: const Icon(Icons.close),
                ),
            ],
          ),
          const SizedBox(height: 10),
          LayoutBuilder(
            builder: (BuildContext context, BoxConstraints constraints) {
              final List<Widget> groups = _groups(filter, notifier, threshold);

              // Five groups is about 1100 px of chips. Below that they used to
              // sit in a horizontal scroll view, which is why this looked broken
              // on anything narrower: the row was cut at the window edge with no
              // scrollbar and no fade, so three of the five filters were simply
              // invisible and nothing said they were there.
              if (constraints.maxWidth >= _inlineFrom) {
                return Wrap(spacing: 16, runSpacing: 12, children: groups);
              }

              // Narrower than that, wrapping them inline would push the list
              // itself off a phone screen - five labelled groups stack to well
              // over half the viewport. So they move behind one button that
              // says how many are on, and open in a sheet with room to breathe.
              return Align(
                alignment: Alignment.centerLeft,
                child: _FilterButton(filter: filter, groups: _groups),
              );
            },
          ),
        ],
      ),
    );
  }

  /// The controls themselves, laid out the same wherever they are shown.
  static List<Widget> _groups(
    NoticeFilter filter,
    NoticeFilterNotifier notifier,
    int? threshold,
  ) {
    return <Widget>[
      // Scope first: it decides what set the rest narrow. Open is the
      // default because an expired notice cannot be bid on, but the
      // archive is what this project accumulates and Telegram already
      // filters - so all of it has to be reachable in one tap.
      _Group(label: Strings.scopeFilter, children: <Widget>[
        _Choice(
          label: Strings.scopeOpen,
          selected: filter.scope == NoticeScope.open,
          onSelected: () => notifier.setScope(NoticeScope.open),
        ),
        _Choice(
          label: Strings.scopeAll,
          selected: filter.scope == NoticeScope.all,
          onSelected: () => notifier.setScope(NoticeScope.all),
        ),
      ]),
      // The third chip is the threshold the worker is actually
      // running on, read from the setting. It used to say 30 whatever
      // the setting said, which made it wrong the moment anybody moved
      // the slider on the Filtre screen.
      _Group(label: Strings.scoreFilter, children: <Widget>[
        _Choice(
          label: Strings.scoreAny,
          selected: filter.minScore == 0,
          onSelected: () => notifier.setMinScore(0),
        ),
        _Choice(
          label: Strings.scoreSome,
          selected: filter.minScore == 1,
          onSelected: () => notifier.setMinScore(1),
        ),
        if (threshold != null)
          _Choice(
            label: Strings.scoreNotified(threshold),
            selected: filter.minScore == threshold,
            onSelected: () => notifier.setMinScore(threshold),
          ),
      ]),
      _Group(label: Strings.deadlineFilter, children: <Widget>[
        _Choice(
          label: Strings.anyTime,
          selected: filter.withinHours == null,
          onSelected: () => notifier.setWithinHours(null),
        ),
        _Choice(
          label: Strings.within24,
          selected: filter.withinHours == 24,
          onSelected: () => notifier.setWithinHours(24),
        ),
        _Choice(
          label: Strings.within72,
          selected: filter.withinHours == 72,
          onSelected: () => notifier.setWithinHours(72),
        ),
        _Choice(
          label: Strings.withinWeek,
          selected: filter.withinHours == 168,
          onSelected: () => notifier.setWithinHours(168),
        ),
      ]),
      _Group(label: Strings.sortBy, children: <Widget>[
        _Choice(
          label: Strings.sortDeadline,
          selected: filter.sort == NoticeSort.deadline,
          onSelected: () => notifier.setSort(NoticeSort.deadline),
        ),
        _Choice(
          label: Strings.sortScore,
          selected: filter.sort == NoticeSort.score,
          onSelected: () => notifier.setSort(NoticeSort.score),
        ),
      ]),
      // A group like the others rather than a bare chip beside them.
      // Every other control here is a label with choices under it, and
      // one unlabelled chip in the row sat off the shared baseline and
      // read as something that had been bolted on.
      _Group(label: Strings.deadlineKnown, children: <Widget>[
        _Choice(
          label: Strings.deadlineKnownAll,
          selected: !filter.hideUndated,
          onSelected: () => notifier.setHideUndated(false),
        ),
        _Choice(
          label: Strings.deadlineKnownOnly,
          selected: filter.hideUndated,
          onSelected: () => notifier.setHideUndated(true),
        ),
      ]),
    ];
  }
}

/// Below this the five groups go behind a button instead of inline.
///
/// Not one of Material's window-class boundaries on purpose: this is about how
/// wide the controls are, not how wide the device is, and five labelled groups
/// of chips need about 1100 px to sit on two comfortable lines.
const double _inlineFrom = 720;

/// What the filters look like when there is no room to show them.
///
/// The count is on the button because a filter you cannot see is a filter you
/// forget is on - and this list showing 628 of 988 notices with no visible
/// reason is the exact failure the Kapsam chip was added to prevent.
class _FilterButton extends ConsumerWidget {
  const _FilterButton({required this.filter, required this.groups});

  final NoticeFilter filter;
  final List<Widget> Function(NoticeFilter, NoticeFilterNotifier, int?) groups;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final int active = filter.activeCount;

    return Row(
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        FilledButton.tonalIcon(
          onPressed: () => showModalBottomSheet<void>(
            context: context,
            showDragHandle: true,
            // The sheet holds the same five groups and a phone is not tall
            // enough for them plus a keyboard, so it scrolls and can be dragged
            // to full height rather than being clipped at half.
            isScrollControlled: true,
            builder: (BuildContext context) => _FilterSheet(groups: groups),
          ),
          icon: const Icon(Icons.tune),
          label: Text(
            active == 0 ? Strings.filters : Strings.filtersActive(active),
          ),
        ),
        if (active > 0) ...<Widget>[
          const SizedBox(width: 8),
          TextButton(
            onPressed: ref.read(noticeFilterProvider.notifier).clear,
            child: const Text(Strings.clearFilters),
          ),
        ],
      ],
    );
  }
}

class _FilterSheet extends ConsumerWidget {
  const _FilterSheet({required this.groups});

  final List<Widget> Function(NoticeFilter, NoticeFilterNotifier, int?) groups;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final NoticeFilter filter = ref.watch(noticeFilterProvider);
    final NoticeFilterNotifier notifier = ref.read(noticeFilterProvider.notifier);
    final int? threshold = ref.watch(notifyThresholdProvider);

    return DraggableScrollableSheet(
      expand: false,
      initialChildSize: 0.6,
      maxChildSize: 0.95,
      builder: (BuildContext context, ScrollController controller) => ListView(
        controller: controller,
        padding: const EdgeInsets.fromLTRB(20, 0, 20, 32),
        children: <Widget>[
          Text(Strings.filters, style: Theme.of(context).textTheme.titleMedium),
          const SizedBox(height: 16),
          // A column rather than a Wrap: there is one column of width here, and
          // a Wrap would put two short groups side by side at a width where
          // that reads as one group with too many chips.
          for (final Widget group in groups(filter, notifier, threshold))
            Padding(
              padding: const EdgeInsets.only(bottom: 18),
              child: group,
            ),
        ],
      ),
    );
  }
}

class _Group extends StatelessWidget {
  const _Group({required this.label, required this.children});

  final String label;
  final List<Widget> children;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.only(left: 4, bottom: 4),
          child: Text(
            label,
            style: theme.textTheme.labelSmall
                ?.copyWith(color: theme.colorScheme.outline),
          ),
        ),
        // A Wrap, not a Row. "Son teklif" is four chips and on a 320 px phone
        // they do not fit on one line - in a Row that is an overflow, here it
        // is a second line.
        Wrap(spacing: 6, runSpacing: 6, children: children),
      ],
    );
  }
}

class _Choice extends StatelessWidget {
  const _Choice({
    required this.label,
    required this.selected,
    required this.onSelected,
  });

  final String label;
  final bool selected;
  final VoidCallback onSelected;

  @override
  Widget build(BuildContext context) {
    return ChoiceChip(
      label: Text(label),
      selected: selected,
      visualDensity: VisualDensity.compact,
      onSelected: (bool _) => onSelected(),
    );
  }
}

class _NoticeRow extends StatelessWidget {
  const _NoticeRow({required this.notice, required this.nowUtc});

  final CommandNotice notice;
  final DateTime nowUtc;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final Urgency urgency = urgencyOf(notice.bidDeadlineAt, nowUtc);

    return ListTile(
      contentPadding: const EdgeInsets.symmetric(horizontal: 16, vertical: 8),
      leading: ScoreBadge(score: notice.score),
      title: Text(notice.title, maxLines: 3, overflow: TextOverflow.ellipsis),
      subtitle: Padding(
        padding: const EdgeInsets.only(top: 4),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Text(
              notice.authorityName,
              maxLines: 2,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.bodySmall,
            ),
            const SizedBox(height: 4),
            Row(
              children: <Widget>[
                Icon(
                  urgency == Urgency.unknown
                      ? Icons.help_outline
                      : Icons.schedule_outlined,
                  size: 14,
                  color: urgencyColor(urgency, theme.colorScheme),
                ),
                const SizedBox(width: 4),
                Expanded(
                  child: Text(
                    _deadlineLine(notice.bidDeadlineAt, nowUtc),
                    style: theme.textTheme.bodySmall?.copyWith(
                      color: urgencyColor(urgency, theme.colorScheme),
                      fontWeight: urgency == Urgency.hours
                          ? FontWeight.w700
                          : FontWeight.w400,
                    ),
                  ),
                ),
              ],
            ),
          ],
        ),
      ),
      onTap: () => context.go(Routes.notice(notice.noticeId)),
    );
  }

  static String _deadlineLine(DateTime? deadlineUtc, DateTime nowUtc) {
    if (deadlineUtc == null) return Strings.deadlineUnknown;

    return '${formatDeadline(deadlineUtc)} · ${remaining(deadlineUtc, nowUtc)}';
  }
}
