import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../formatting.dart';
import '../sources/deactivation.dart';
import '../sources/source_providers.dart';
import '../strings.dart';
import 'source_editor.dart';
import 'widgets.dart';

/// Every page being polled, and the ones that are not.
class SourcesScreen extends ConsumerWidget {
  const SourcesScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final AsyncValue<List<Source>> sources = ref.watch(allSourcesProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text(Strings.sources),
        actions: <Widget>[
          IconButton(
            tooltip: Strings.refresh,
            onPressed: () => ref.invalidate(allSourcesProvider),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      floatingActionButton: FloatingActionButton.extended(
        onPressed: () => openSourceEditor(context, ref, null),
        icon: const Icon(Icons.add),
        label: const Text(Strings.addSource),
      ),
      body: sources.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) => FailureView(
          error: error,
          onRetry: () => ref.invalidate(allSourcesProvider),
        ),
        data: (List<Source> rows) {
          if (rows.isEmpty) {
            return const EmptyView(message: Strings.noSources);
          }

          // Active first, then by name. A list ordered by id would put the
          // twenty-five switched-off ones in the middle of the working ones.
          // The nullables below are the schema being honest rather than the
          // interface being careless: `is_active` and `consecutive_failures`
          // have defaults in C# rather than being required, so the document does
          // not promise them and the generated model says so.
          final List<Source> ordered = <Source>[...rows]
            ..sort((Source a, Source b) {
              final bool activeA = a.isActive ?? false;
              final bool activeB = b.isActive ?? false;

              if (activeA != activeB) return activeA ? -1 : 1;

              return a.authorityName.compareTo(b.authorityName);
            });

          return ListView.separated(
            // Room under the last row for the floating button, which otherwise
            // sits on top of it - and the row it covers carries the switch that
            // turns a source off.
            padding: const EdgeInsets.only(bottom: 88),
            itemCount: ordered.length,
            separatorBuilder: (BuildContext context, int index) =>
                const Divider(height: 1),
            itemBuilder: (BuildContext context, int index) =>
                _SourceRow(source: ordered[index]),
          );
        },
      ),
    );
  }
}

class _SourceRow extends ConsumerWidget {
  const _SourceRow({required this.source});

  final Source source;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final bool isActive = source.isActive ?? false;
    final int failures = source.consecutiveFailures ?? 0;

    return ListTile(
      leading: Icon(
        isActive ? Icons.radar : Icons.pause_circle_outline,
        color: failures > 0
            ? theme.colorScheme.error
            : isActive
            ? theme.colorScheme.primary
            : theme.colorScheme.outline,
      ),
      title: Text(
        source.authorityName,
        maxLines: 2,
        overflow: TextOverflow.ellipsis,
      ),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            source.url,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodySmall?.copyWith(
              color: theme.colorScheme.outline,
            ),
          ),
          Text(
            <String>[
              isActive ? Strings.sourceActive : Strings.sourceInactive,
              source.adapterKey,
              if (failures > 0) '${Strings.failures}: $failures',
              '${Strings.lastNewNotice}: ${formatDay(source.lastNewNoticeAt)}',
            ].join(' · '),
            style: theme.textTheme.bodySmall?.copyWith(
              color: failures > 0
                  ? theme.colorScheme.error
                  : theme.colorScheme.onSurfaceVariant,
            ),
          ),
          // Why it is off, and whether anything is still looking at it. Until
          // now a switched-off source said only "Kapalı", and finding out why
          // meant reading twenty-four free-text notes one at a time - which is
          // exactly what the 11 August audit had to do.
          if (!isActive) _WhyOff(source: source),
        ],
      ),
      isThreeLine: true,
      trailing: Switch(
        value: isActive,
        onChanged: (bool value) => _toggle(context, ref, value),
      ),
      onTap: () => openSourceEditor(context, ref, source),
    );
  }

  Future<void> _toggle(BuildContext context, WidgetRef ref, bool value) async {
    if (value) {
      await setSourceActive(ref, source.id ?? 0, true);
      return;
    }

    final DeactivationReason? reason = await showDialog<DeactivationReason>(
      context: context,
      builder: (BuildContext context) => const _ReasonDialog(),
    );

    // Dismissed. Nothing is sent, because sending without a reason is what
    // silently marked everything `manual`.
    if (reason == null) return;

    await setSourceActive(ref, source.id ?? 0, false, reason: reason);
  }
}

/// The line under a switched-off source saying why, and whether it is watched.
class _WhyOff extends StatelessWidget {
  const _WhyOff({required this.source});

  final Source source;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final DeactivationChoice? why = DeactivationChoice.describe(
      source.deactivatedReason,
    );

    // Written before the probe is attempted, not after, so a source whose DNS
    // never resolves does not sit at the head of the queue for ever. It is
    // therefore "when we last looked", not "when it last answered".
    final String probe = source.quarantineProbedAt == null
        ? Strings.quarantineWaiting
        : '${Strings.quarantineProbed}: ${formatDay(source.quarantineProbedAt)}';

    return Padding(
      padding: const EdgeInsets.only(top: 2),
      child: Row(
        children: <Widget>[
          Flexible(
            child: Text(
              why?.label ?? Strings.reasonUnknown,
              maxLines: 1,
              overflow: TextOverflow.ellipsis,
              style: theme.textTheme.bodySmall?.copyWith(
                color: why == null
                    ? theme.colorScheme.outline
                    : theme.colorScheme.onSurfaceVariant,
                fontStyle: why == null ? FontStyle.italic : null,
              ),
            ),
          ),
          if (why?.retried ?? false) ...<Widget>[
            const SizedBox(width: 8),
            const _RetryChip(retried: true),
            const SizedBox(width: 8),
            Text(
              probe,
              style: theme.textTheme.labelSmall?.copyWith(
                color: theme.colorScheme.outline,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

/// Which of the five, and what each one costs.
class _ReasonDialog extends StatefulWidget {
  const _ReasonDialog();

  @override
  State<_ReasonDialog> createState() => _ReasonDialogState();
}

class _ReasonDialogState extends State<_ReasonDialog> {
  DeactivationReason? _chosen;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return AlertDialog(
      title: const Text(Strings.deactivateTitle),
      content: SizedBox(
        // Wide enough to read the five explanations, and never wider than the
        // dialog has room for. A flat 460 overflowed a 320 px phone by two
        // hundred; the subtraction is what AlertDialog actually takes - 40 of
        // inset either side, then 24 of content padding either side.
        width: (MediaQuery.sizeOf(context).width - 128).clamp(160.0, 460.0),
        child: SingleChildScrollView(
          child: Column(
            mainAxisSize: MainAxisSize.min,
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(
                Strings.deactivateHelp,
                style: theme.textTheme.bodySmall?.copyWith(
                  color: theme.colorScheme.onSurfaceVariant,
                ),
              ),
              const SizedBox(height: 8),
              RadioGroup<DeactivationReason>(
                groupValue: _chosen,
                onChanged: (DeactivationReason? value) =>
                    setState(() => _chosen = value),
                child: Column(
                  mainAxisSize: MainAxisSize.min,
                  children: <Widget>[
                    for (final DeactivationChoice choice
                        in DeactivationChoice.selectable)
                      RadioListTile<DeactivationReason>(
                        value: choice.reason,
                        contentPadding: EdgeInsets.zero,
                        // A Wrap, not a Row: on a narrow phone the label and
                        // the chip do not fit on one line, and in a Row that is
                        // an overflow rather than a second line.
                        //
                        // The consequence sits beside the choice rather than in
                        // a paragraph above it. Which of these gets looked at
                        // again is the only thing the reason decides.
                        title: Wrap(
                          spacing: 8,
                          runSpacing: 4,
                          crossAxisAlignment: WrapCrossAlignment.center,
                          children: <Widget>[
                            Text(choice.label),
                            _RetryChip(retried: choice.retried),
                          ],
                        ),
                        subtitle: Text(
                          choice.help,
                          style: theme.textTheme.bodySmall?.copyWith(
                            color: theme.colorScheme.outline,
                          ),
                        ),
                      ),
                  ],
                ),
              ),
            ],
          ),
        ),
      ),
      actions: <Widget>[
        TextButton(
          onPressed: () => Navigator.of(context).pop(),
          child: const Text(Strings.cancel),
        ),
        FilledButton(
          onPressed: _chosen == null
              ? null
              : () => Navigator.of(context).pop(_chosen),
          child: const Text(Strings.deactivate),
        ),
      ],
    );
  }
}

class _RetryChip extends StatelessWidget {
  const _RetryChip({required this.retried});

  final bool retried;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final Color colour = retried
        ? theme.colorScheme.primary
        : theme.colorScheme.outline;

    return Container(
      padding: const EdgeInsets.symmetric(horizontal: 8, vertical: 2),
      decoration: BoxDecoration(
        color: colour.withValues(alpha: 0.12),
        borderRadius: BorderRadius.circular(20),
      ),
      child: Text(
        retried ? Strings.reasonRetried : Strings.reasonLeftAlone,
        style: theme.textTheme.labelSmall?.copyWith(color: colour),
      ),
    );
  }
}
