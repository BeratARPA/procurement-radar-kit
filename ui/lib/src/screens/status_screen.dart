import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../data/providers.dart';
import '../formatting.dart';
import '../strings.dart';
import 'widgets.dart';

/// Is this thing running.
///
/// The most dangerous failure in this system is not a crash, it is silence
/// (spec Bolum 11): everything returns 200, nothing is parsed, and no alarm
/// fires. Two numbers here answer that faster than a log does - how long since
/// anything was fetched, and how many sources are failing.
class StatusScreen extends ConsumerWidget {
  const StatusScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final AsyncValue<SystemStatus> status = ref.watch(systemStatusProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text(Strings.status),
        actions: <Widget>[
          IconButton(
            tooltip: Strings.refresh,
            onPressed: () => ref.invalidate(systemStatusProvider),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: status.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) => FailureView(
          error: error,
          onRetry: () => ref.invalidate(systemStatusProvider),
        ),
        data: (SystemStatus data) => RefreshIndicator(
          onRefresh: () async => ref.invalidate(systemStatusProvider),
          child: ListView(
            physics: const AlwaysScrollableScrollPhysics(),
            padding: const EdgeInsets.all(16),
            children: <Widget>[
              Center(
                child: ConstrainedBox(
                  constraints: const BoxConstraints(maxWidth: 560),
                  child: Column(
                    children: <Widget>[
                      _Row(
                        label: Strings.lastFetch,
                        value: formatMoment(data.lastFetchAt),
                        warn: data.lastFetchAt == null,
                      ),
                      _Row(
                        label: Strings.activeSources,
                        value: '${data.activeSources}',
                      ),
                      _Row(
                        label: Strings.failingSources,
                        value: '${data.failingSources}',
                        warn: data.failingSources > 0,
                      ),
                      _Row(label: Strings.totalNotices, value: '${data.notices}'),
                      _Row(
                        label: Strings.noticesToday,
                        value: '${data.noticesToday}',
                      ),
                      _Row(label: Strings.announced, value: '${data.announced}'),
                    ],
                  ),
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Row extends StatelessWidget {
  const _Row({required this.label, required this.value, this.warn = false});

  final String label;
  final String value;
  final bool warn;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return ListTile(
      title: Text(label),
      trailing: Text(
        value,
        style: theme.textTheme.titleMedium?.copyWith(
          color: warn ? theme.colorScheme.error : null,
          fontWeight: FontWeight.w600,
        ),
      ),
    );
  }
}
