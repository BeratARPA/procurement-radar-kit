import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/export.dart';
import '../data/notice_filter.dart';
import '../data/providers.dart';
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
        title: Text(filter.term.isEmpty ? Strings.openNotices : Strings.notices),
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
          SingleChildScrollView(
            scrollDirection: Axis.horizontal,
            child: Row(
              children: <Widget>[
                // Score first, because it is the one that turns nine pages of
                // toner into the four notices worth reading. 30 is the
                // notification threshold (D-14): at or above it, the system
                // would have told you about this one.
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
                  _Choice(
                    label: Strings.scoreNotified,
                    selected: filter.minScore == 30,
                    onSelected: () => notifier.setMinScore(30),
                  ),
                ]),
                const SizedBox(width: 16),
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
                const SizedBox(width: 16),
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
                const SizedBox(width: 16),
                FilterChip(
                  label: const Text(Strings.hideUndated),
                  selected: filter.hideUndated,
                  onSelected: notifier.setHideUndated,
                ),
              ],
            ),
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
        Row(
          children: <Widget>[
            for (int i = 0; i < children.length; i++) ...<Widget>[
              if (i > 0) const SizedBox(width: 6),
              children[i],
            ],
          ],
        ),
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
