import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';
import 'package:url_launcher/url_launcher.dart';

import '../../api/export.dart';
import '../data/failures.dart';
import '../data/providers.dart';
import '../formatting.dart';
import '../router.dart';
import '../data/announcement_outline.dart';
import '../data/procurement_kind.dart';
import '../strings.dart';
import '../theme.dart';
import 'widgets.dart';

/// The statuses a person can set, in the order a person would.
///
/// `new` is missing on purpose: it is where every notice starts and setting it
/// back says nothing. The spellings are the schema's (spec Bolum 4) and the api
/// takes them as written.
const List<(String, String, IconData)> _verdicts = <(String, String, IconData)>[
  ('reviewed', Strings.statusReviewed, Icons.visibility_outlined),
  ('bid_submitted', Strings.statusBidSubmitted, Icons.send_outlined),
  ('not_relevant', Strings.statusNotRelevant, Icons.block_outlined),
  ('missed', Strings.statusMissed, Icons.event_busy_outlined),
];

class NoticeDetailScreen extends ConsumerWidget {
  const NoticeDetailScreen({required this.noticeId, super.key});

  /// Null when the url carried something that was not a number.
  final int? noticeId;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final int? id = noticeId;

    if (id == null) {
      return Scaffold(
        appBar: AppBar(title: const Text(Strings.noticeDetail)),
        body: const EmptyView(message: Strings.noticeNotFound),
      );
    }

    final AsyncValue<NoticeDetail> detail = ref.watch(noticeDetailProvider(id));

    return Scaffold(
      appBar: AppBar(
        title: const Text(Strings.noticeDetail),
        leading: IconButton(
          icon: const Icon(Icons.arrow_back),
          onPressed: () => context.go(Routes.notices),
        ),
      ),
      body: detail.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) {
          // A 404 is not a fault - a notice can be reparsed away, and a stale
          // bookmark is an ordinary thing to have.
          final ApiFailure failure = describeFailure(error);

          if (failure is ServerSaidNo && failure.statusCode == 404) {
            return const EmptyView(message: Strings.noticeNotFound);
          }

          return FailureView(
            error: error,
            onRetry: () => ref.invalidate(noticeDetailProvider(id)),
          );
        },
        data: (NoticeDetail notice) => _Detail(notice: notice),
      ),
    );
  }
}

class _Detail extends ConsumerWidget {
  const _Detail({required this.notice});

  final NoticeDetail notice;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final DateTime nowUtc = DateTime.now().toUtc();
    final Urgency urgency = urgencyOf(notice.bidDeadlineAt, nowUtc);

    return ListView(
      padding: const EdgeInsets.fromLTRB(16, 8, 16, 32),
      children: <Widget>[
        // Wide screens get a column rather than a full-width line of text: a
        // notice title running the width of a desktop monitor is unreadable.
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 760),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Row(
                  crossAxisAlignment: CrossAxisAlignment.start,
                  children: <Widget>[
                    ScoreBadge(score: notice.score),
                    const SizedBox(width: 12),
                    Expanded(
                      child: Text(notice.title, style: theme.textTheme.titleLarge),
                    ),
                  ],
                ),
                const SizedBox(height: 16),
                _Field(label: Strings.authority, value: notice.authorityName),
                _DeadlineField(deadlineUtc: notice.bidDeadlineAt, urgency: urgency),
                _Field(
                  label: Strings.procurementKind,
                  value: procurementSummary(
                          notice.procurementType, notice.procedureCode) ??
                      Strings.procurementKindUnknown,
                ),
                if (notice.ikn != null)
                  _Field(label: Strings.iknLabel, value: notice.ikn!, copyable: true),
                if (notice.rationale != null && notice.rationale!.isNotEmpty)
                  _Field(label: Strings.rationale, value: notice.rationale!),
                const SizedBox(height: 8),
                _SourceLink(url: notice.sourceUrl),
                const Divider(height: 32),
                _Verdict(notice: notice),
                const Divider(height: 32),
                Text(Strings.announcement, style: theme.textTheme.labelLarge),
                const SizedBox(height: 8),
                _Announcement(text: notice.announcementText),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

/// The announcement, with the line breaks extraction lost put back.
///
/// What arrives is one paragraph thousands of characters long - the whole of a
/// Turkish tender announcement flattened. The outline is still in it as
/// numbering, so this renders that: sections as headings, numbered items
/// indented under them, and a label in bold beside its value.
///
/// Wrapped in one SelectionArea rather than made of SelectableTexts, so a
/// selection can run across items the way it did when this was a single block
/// of text. Copying an address out of it is the commonest thing anybody does
/// on this screen.
class _Announcement extends StatelessWidget {
  const _Announcement({required this.text});

  final String? text;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final List<AnnouncementBlock> blocks = readAnnouncement(text);

    if (blocks.isEmpty) {
      return Text(Strings.noAnnouncement, style: theme.textTheme.bodyMedium);
    }

    return SelectionArea(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          for (final AnnouncementBlock block in blocks)
            switch (block) {
              AnnouncementProse(:final String text) => Padding(
                  padding: const EdgeInsets.only(bottom: 12),
                  child: Text(text, style: theme.textTheme.bodyMedium),
                ),
              AnnouncementSection(:final String number, :final String title) =>
                Padding(
                  padding: const EdgeInsets.only(top: 16, bottom: 8),
                  child: Text(
                    '$number. $title',
                    style: theme.textTheme.titleSmall?.copyWith(
                      color: theme.colorScheme.primary,
                      fontWeight: FontWeight.w700,
                    ),
                  ),
                ),
              AnnouncementItem item => Padding(
                  // Indented by depth, capped: 4.3.1.1 is four levels in and a
                  // proportional indent would leave a phone-width column about
                  // as wide as the word it holds.
                  padding: EdgeInsets.only(
                    left: (item.depth - 1).clamp(0, 2) * 14.0,
                    bottom: 6,
                  ),
                  child: _Item(item: item),
                ),
            },
        ],
      ),
    );
  }
}

class _Item extends StatelessWidget {
  const _Item({required this.item});

  final AnnouncementItem item;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final TextStyle? body = theme.textTheme.bodyMedium;

    // One RichText rather than a Row of two, because a value is usually far
    // longer than its label and a two-column layout would leave the label
    // column mostly empty and wrap the value into a narrow ribbon. The number
    // is dimmed: it orders the document and nobody reads it for content.
    return Text.rich(
      TextSpan(
        children: <InlineSpan>[
          TextSpan(
            text: '${item.number}  ',
            style: body?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          if (item.label case final String label)
            TextSpan(
              text: '$label: ',
              style: body?.copyWith(fontWeight: FontWeight.w600),
            ),
          TextSpan(text: item.value, style: body),
        ],
      ),
    );
  }
}

class _Field extends StatelessWidget {
  const _Field({required this.label, required this.value, this.copyable = false});

  final String label;
  final String value;
  final bool copyable;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            label,
            style: theme.textTheme.labelSmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          const SizedBox(height: 2),
          Row(
            children: <Widget>[
              Expanded(
                child: SelectableText(value, style: theme.textTheme.bodyLarge),
              ),
              if (copyable)
                IconButton(
                  visualDensity: VisualDensity.compact,
                  onPressed: () => Clipboard.setData(ClipboardData(text: value)),
                  icon: const Icon(Icons.copy_outlined, size: 18),
                ),
            ],
          ),
        ],
      ),
    );
  }
}

class _DeadlineField extends StatelessWidget {
  const _DeadlineField({required this.deadlineUtc, required this.urgency});

  final DateTime? deadlineUtc;
  final Urgency urgency;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final Color colour = urgencyColor(urgency, theme.colorScheme);

    return Padding(
      padding: const EdgeInsets.only(bottom: 12),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            Strings.deadlinePrefix,
            style: theme.textTheme.labelSmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          const SizedBox(height: 2),
          Text(
            deadlineUtc == null
                ? Strings.deadlineUnknown
                : '${formatDeadline(deadlineUtc)} · '
                    '${remaining(deadlineUtc, DateTime.now().toUtc())}',
            style: theme.textTheme.bodyLarge
                ?.copyWith(color: colour, fontWeight: FontWeight.w600),
          ),
        ],
      ),
    );
  }
}

class _SourceLink extends StatelessWidget {
  const _SourceLink({required this.url});

  final String url;

  @override
  Widget build(BuildContext context) {
    return Align(
      alignment: Alignment.centerLeft,
      child: OutlinedButton.icon(
        onPressed: () async {
          final Uri? uri = Uri.tryParse(url);
          final bool opened = uri != null &&
              await launchUrl(uri, mode: LaunchMode.externalApplication);

          if (!opened && context.mounted) {
            ScaffoldMessenger.of(context).showSnackBar(
              const SnackBar(content: Text(Strings.couldNotOpenSource)),
            );
          }
        },
        icon: const Icon(Icons.open_in_new, size: 18),
        label: const Text(Strings.openSource),
      ),
    );
  }
}

class _Verdict extends ConsumerStatefulWidget {
  const _Verdict({required this.notice});

  final NoticeDetail notice;

  @override
  ConsumerState<_Verdict> createState() => _VerdictState();
}

class _VerdictState extends ConsumerState<_Verdict> {
  bool _saving = false;

  Future<void> _select(String status) async {
    setState(() => _saving = true);

    try {
      await changeNoticeStatus(ref, widget.notice.noticeId, status);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.statusSaved)),
        );
      }
    } on Object catch (error) {
      // Saying nothing here is the bad case: the button would look pressed, the
      // chip would not move, and the verdict the whole threshold calibration
      // rests on (spec Bolum 10) would be silently missing.
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(describeFailure(error).message)),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(Strings.verdict, style: theme.textTheme.labelLarge),
            const SizedBox(width: 12),
            if (_saving)
              const SizedBox(
                width: 14,
                height: 14,
                child: CircularProgressIndicator(strokeWidth: 2),
              ),
          ],
        ),
        const SizedBox(height: 8),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: <Widget>[
            for (final (String value, String label, IconData icon) in _verdicts)
              ChoiceChip(
                selected: widget.notice.status == value,
                avatar: Icon(icon, size: 18),
                label: Text(label),
                // Re-selecting what is already set would be a second write of
                // the same row for nothing.
                onSelected: _saving || widget.notice.status == value
                    ? null
                    : (bool _) => _select(value),
              ),
          ],
        ),
        if (widget.notice.status == 'new')
          Padding(
            padding: const EdgeInsets.only(top: 8),
            child: Text(
              statusLabel(widget.notice.status),
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
          ),
      ],
    );
  }
}
