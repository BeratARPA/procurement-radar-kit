import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../formatting.dart';
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
          final List<Source> ordered = <Source>[...rows]..sort((Source a, Source b) {
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
      title: Text(source.authorityName, maxLines: 2, overflow: TextOverflow.ellipsis),
      subtitle: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            source.url,
            maxLines: 1,
            overflow: TextOverflow.ellipsis,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.outline),
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
        ],
      ),
      isThreeLine: true,
      trailing: Switch(
        value: isActive,
        onChanged: (bool value) => setSourceActive(ref, source.id ?? 0, value),
      ),
      onTap: () => openSourceEditor(context, ref, source),
    );
  }
}
