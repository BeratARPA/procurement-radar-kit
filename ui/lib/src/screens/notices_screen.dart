import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../../api/export.dart';
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
      TextEditingController(text: ref.read(searchTermProvider));

  @override
  void dispose() {
    _search.dispose();
    super.dispose();
  }

  @override
  Widget build(BuildContext context) {
    final String term = ref.watch(searchTermProvider);
    final AsyncValue<List<CommandNotice>> notices = ref.watch(noticesProvider(term));

    return Scaffold(
      appBar: AppBar(
        title: Text(term.isEmpty ? Strings.openNotices : Strings.notices),
        actions: <Widget>[
          IconButton(
            tooltip: Strings.refresh,
            onPressed: () => ref.invalidate(noticesProvider(term)),
            icon: const Icon(Icons.refresh),
          ),
        ],
        bottom: PreferredSize(
          preferredSize: const Size.fromHeight(64),
          child: Padding(
            padding: const EdgeInsets.fromLTRB(12, 0, 12, 12),
            child: SearchBar(
              controller: _search,
              hintText: Strings.searchHint,
              leading: const Icon(Icons.search),
              // Searching goes to the api rather than filtering what is on
              // screen. Dart's toLowerCase is locale-independent and gets
              // Turkish wrong - 'I' folds to 'i' and not 'ı' - so a local
              // filter would quietly disagree with the normalisation in
              // spec Bolum 8 on exactly the words this project is about.
              onSubmitted: (String value) =>
                  ref.read(searchTermProvider.notifier).set(value),
              trailing: <Widget>[
                if (term.isNotEmpty)
                  IconButton(
                    tooltip: Strings.clearSearch,
                    onPressed: () {
                      _search.clear();
                      ref.read(searchTermProvider.notifier).set('');
                    },
                    icon: const Icon(Icons.close),
                  ),
              ],
            ),
          ),
        ),
      ),
      body: notices.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) => FailureView(
          error: error,
          onRetry: () => ref.invalidate(noticesProvider(term)),
        ),
        data: (List<CommandNotice> rows) {
          if (rows.isEmpty) {
            return EmptyView(
              message: term.isEmpty ? Strings.noNotices : Strings.noSearchResults,
            );
          }

          return RefreshIndicator(
            onRefresh: () async => ref.invalidate(noticesProvider(term)),
            child: ListView.separated(
              // A list that cannot be dragged cannot be pull-to-refreshed, and
              // a short list on a wide screen is the common case here.
              physics: const AlwaysScrollableScrollPhysics(),
              itemCount: rows.length,
              separatorBuilder: (BuildContext context, int index) =>
                  const Divider(height: 1),
              itemBuilder: (BuildContext context, int index) =>
                  _NoticeRow(notice: rows[index]),
            ),
          );
        },
      ),
    );
  }
}

class _NoticeRow extends StatelessWidget {
  const _NoticeRow({required this.notice});

  final CommandNotice notice;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final DateTime nowUtc = DateTime.now().toUtc();
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
