import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../formatting.dart';
import '../strings.dart';
import '../system/system_control.dart';
import '../system/system_providers.dart';
import 'widgets.dart';

/// What is running on this machine, and the switches for it.
///
/// Reads the machine, never the api. The moment a control panel is wanted is
/// usually the moment the api is not answering.
class SystemScreen extends ConsumerWidget {
  const SystemScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final SystemControl control = ref.watch(systemControlProvider);

    if (!control.isSupported) {
      return Scaffold(
        appBar: AppBar(title: const Text(Strings.system)),
        body: const EmptyView(message: Strings.systemOnDesktopOnly),
      );
    }

    // Re-reads on its own while the screen is open, so a service that falls over
    // in the background shows up without anybody pressing anything.
    ref.watch(systemTickerProvider);
    ref.listen(systemTickerProvider, (AsyncValue<int>? previous, AsyncValue<int> next) {
      ref.invalidate(systemOverviewProvider);
    });

    final AsyncValue<SystemOverview> overview = ref.watch(systemOverviewProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text(Strings.system),
        actions: <Widget>[
          IconButton(
            tooltip: Strings.refresh,
            onPressed: () => ref.invalidate(systemOverviewProvider),
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: overview.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) => FailureView(
          error: error,
          onRetry: () => ref.invalidate(systemOverviewProvider),
        ),
        data: (SystemOverview data) => _Body(overview: data),
      ),
    );
  }
}

class _Body extends ConsumerWidget {
  const _Body({required this.overview});

  final SystemOverview overview;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return ListView(
      padding: const EdgeInsets.all(16),
      children: <Widget>[
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 820),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: <Widget>[
                if (overview.projectRoot == null)
                  const _Warning(
                    message: Strings.projectFolderMissing,
                    severe: true,
                  ),
                if (overview.staleDeployment != null)
                  _Warning(message: overview.staleDeployment!, severe: true),
                if (overview.sleepWarning != null)
                  _Warning(message: overview.sleepWarning!),
                const SizedBox(height: 4),
                _PartGrid(overview: overview),
                const SizedBox(height: 24),
                const _Commands(),
                const SizedBox(height: 24),
                const _Log(),
                if (overview.projectRoot != null) ...<Widget>[
                  const SizedBox(height: 24),
                  Text(
                    '${Strings.projectFolder}: ${overview.projectRoot}',
                    style: Theme.of(context).textTheme.bodySmall?.copyWith(
                          color: Theme.of(context).colorScheme.outline,
                        ),
                  ),
                ],
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _Warning extends StatelessWidget {
  const _Warning({required this.message, this.severe = false});

  final String message;
  final bool severe;

  @override
  Widget build(BuildContext context) {
    final ColorScheme scheme = Theme.of(context).colorScheme;
    final Color colour = severe ? scheme.error : scheme.tertiary;

    return Container(
      margin: const EdgeInsets.only(bottom: 12),
      padding: const EdgeInsets.all(12),
      decoration: BoxDecoration(
        color: colour.withValues(alpha: 0.10),
        borderRadius: BorderRadius.circular(8),
      ),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Icon(severe ? Icons.error_outline : Icons.warning_amber_outlined,
              size: 18, color: colour),
          const SizedBox(width: 10),
          Expanded(
            child: Text(message, style: Theme.of(context).textTheme.bodyMedium),
          ),
        ],
      ),
    );
  }
}

class _PartGrid extends ConsumerWidget {
  const _PartGrid({required this.overview});

  final SystemOverview overview;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return LayoutBuilder(
      builder: (BuildContext context, BoxConstraints constraints) {
        final int columns = constraints.maxWidth < 560 ? 1 : 3;

        return GridView.count(
          crossAxisCount: columns,
          shrinkWrap: true,
          physics: const NeverScrollableScrollPhysics(),
          crossAxisSpacing: 12,
          mainAxisSpacing: 12,
          childAspectRatio: columns == 1 ? 3.4 : 1.35,
          children: <Widget>[
            for (final Part part in Part.values)
              _PartCard(status: overview.of(part)),
          ],
        );
      },
    );
  }
}

class _PartCard extends ConsumerStatefulWidget {
  const _PartCard({required this.status});

  final PartStatus? status;

  @override
  ConsumerState<_PartCard> createState() => _PartCardState();
}

class _PartCardState extends ConsumerState<_PartCard> {
  bool _busy = false;

  Future<void> _act(Future<CommandOutcome> Function() action) async {
    setState(() => _busy = true);

    final CommandOutcome outcome = await action();

    if (!mounted) return;

    setState(() => _busy = false);
    ref.invalidate(systemOverviewProvider);

    if (!outcome.succeeded && outcome.output.isNotEmpty) {
      ScaffoldMessenger.of(context)
          .showSnackBar(SnackBar(content: Text(outcome.output)));
    }
  }

  @override
  Widget build(BuildContext context) {
    final PartStatus? status = widget.status;
    final ThemeData theme = Theme.of(context);
    final SystemControl control = ref.watch(systemControlProvider);

    if (status == null) return const SizedBox.shrink();

    return Container(
      padding: const EdgeInsets.all(14),
      decoration: BoxDecoration(
        color: theme.colorScheme.surfaceContainerLow,
        borderRadius: BorderRadius.circular(12),
        border: Border.all(color: theme.colorScheme.outlineVariant),
      ),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        mainAxisSize: MainAxisSize.min,
        children: <Widget>[
          Row(
            children: <Widget>[
              _Dot(health: status.health),
              const SizedBox(width: 8),
              Expanded(
                child: Text(_label(status.part), style: theme.textTheme.bodySmall),
              ),
            ],
          ),
          const SizedBox(height: 6),
          Text(
            status.detail,
            style: theme.textTheme.titleSmall,
            maxLines: 2,
            overflow: TextOverflow.ellipsis,
          ),
          const Spacer(),
          Row(
            children: <Widget>[
              if (_busy)
                const SizedBox(
                  width: 14,
                  height: 14,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              else if (status.canStop)
                OutlinedButton(
                  onPressed: () => _act(() => control.stop(status.part)),
                  child: const Text(Strings.stop),
                )
              else if (status.canStart)
                FilledButton.tonal(
                  onPressed: () => _act(() => control.start(status.part)),
                  child: const Text(Strings.start),
                ),
              const Spacer(),
              if (status.startsAtLogon != null)
                Tooltip(
                  message: Strings.startsAtLogon,
                  child: Switch(
                    value: status.startsAtLogon!,
                    onChanged: _busy
                        ? null
                        : (bool value) => _act(
                              () => control.setStartsAtLogon(status.part, value),
                            ),
                  ),
                ),
            ],
          ),
        ],
      ),
    );
  }

  static String _label(Part part) => switch (part) {
        Part.database => Strings.partDatabase,
        Part.worker => Strings.partWorker,
        Part.api => Strings.partApi,
      };
}

class _Dot extends StatelessWidget {
  const _Dot({required this.health});

  final Health health;

  @override
  Widget build(BuildContext context) {
    final ColorScheme scheme = Theme.of(context).colorScheme;

    return Container(
      width: 9,
      height: 9,
      decoration: BoxDecoration(
        shape: BoxShape.circle,
        color: switch (health) {
          Health.up => scheme.primary,
          Health.degraded => scheme.tertiary,
          Health.down => scheme.outline,
          Health.unknown => scheme.outlineVariant,
        },
      ),
    );
  }
}

class _Commands extends ConsumerStatefulWidget {
  const _Commands();

  @override
  ConsumerState<_Commands> createState() => _CommandsState();
}

class _CommandsState extends ConsumerState<_Commands> {
  String? _running;

  Future<void> _run(CliCommand command) async {
    if (!command.isSafe && !await _confirmed(command)) return;

    setState(() => _running = command.name);

    final CommandOutcome outcome =
        await ref.read(systemControlProvider).runCli(command);

    if (!mounted) return;

    setState(() => _running = null);

    await showDialog<void>(
      context: context,
      builder: (BuildContext context) => AlertDialog(
        title: Text('${command.name} · ${outcome.exitCode}'),
        content: SizedBox(
          width: 640,
          child: SingleChildScrollView(
            child: SelectableText(
              outcome.output.isEmpty ? '—' : outcome.output,
              style: const TextStyle(fontFamily: 'monospace', fontSize: 12),
            ),
          ),
        ),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(context).pop(),
            child: const Text(Strings.close),
          ),
        ],
      ),
    );
  }

  Future<bool> _confirmed(CliCommand command) async {
    final bool? answer = await showDialog<bool>(
      context: context,
      builder: (BuildContext context) => AlertDialog(
        title: const Text(Strings.confirmTitle),
        content: Text('${command.name} — ${Strings.confirmWrites}'),
        actions: <Widget>[
          TextButton(
            onPressed: () => Navigator.of(context).pop(false),
            child: const Text(Strings.cancel),
          ),
          FilledButton(
            onPressed: () => Navigator.of(context).pop(true),
            child: const Text(Strings.proceed),
          ),
        ],
      ),
    );

    return answer ?? false;
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Text(Strings.commands, style: theme.textTheme.titleSmall),
        const SizedBox(height: 10),
        Wrap(
          spacing: 8,
          runSpacing: 8,
          children: <Widget>[
            for (final CliCommand command in cliCommands)
              Tooltip(
                message: command.description,
                child: OutlinedButton.icon(
                  onPressed: _running != null ? null : () => _run(command),
                  icon: _running == command.name
                      ? const SizedBox(
                          width: 14,
                          height: 14,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : Icon(
                          command.isSafe
                              ? Icons.play_arrow_outlined
                              : Icons.edit_note_outlined,
                          size: 18,
                        ),
                  label: Text(command.name),
                ),
              ),
          ],
        ),
      ],
    );
  }
}

class _Log extends ConsumerStatefulWidget {
  const _Log();

  @override
  ConsumerState<_Log> createState() => _LogState();
}

class _LogState extends ConsumerState<_Log> {
  bool _problemsOnly = true;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final AsyncValue<List<LogLine>> log =
        ref.watch(systemLogProvider(_problemsOnly));

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(Strings.logTitle, style: theme.textTheme.titleSmall),
            const Spacer(),
            Text(Strings.problemsOnly, style: theme.textTheme.bodySmall),
            Switch(
              value: _problemsOnly,
              onChanged: (bool value) => setState(() => _problemsOnly = value),
            ),
          ],
        ),
        const SizedBox(height: 6),
        Container(
          width: double.infinity,
          constraints: const BoxConstraints(maxHeight: 280),
          padding: const EdgeInsets.all(12),
          decoration: BoxDecoration(
            color: theme.colorScheme.surfaceContainerLow,
            borderRadius: BorderRadius.circular(12),
            border: Border.all(color: theme.colorScheme.outlineVariant),
          ),
          child: log.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (Object error, StackTrace stackTrace) =>
                Text('$error', style: theme.textTheme.bodySmall),
            data: (List<LogLine> lines) => lines.isEmpty
                ? Text(Strings.noLog, style: theme.textTheme.bodySmall)
                : ListView(
                    reverse: true,
                    shrinkWrap: true,
                    children: <Widget>[
                      for (final LogLine line in lines.reversed)
                        _LogRow(line: line),
                    ],
                  ),
          ),
        ),
      ],
    );
  }
}

class _LogRow extends StatelessWidget {
  const _LogRow({required this.line});

  final LogLine line;

  @override
  Widget build(BuildContext context) {
    final ColorScheme scheme = Theme.of(context).colorScheme;

    final Color colour = switch (line.level) {
      'Error' || 'Fatal' => scheme.error,
      'Warning' => scheme.tertiary,
      _ => scheme.onSurfaceVariant,
    };

    return Padding(
      padding: const EdgeInsets.only(bottom: 3),
      child: SelectableText.rich(
        TextSpan(
          style: const TextStyle(fontFamily: 'monospace', fontSize: 12, height: 1.5),
          children: <InlineSpan>[
            TextSpan(
              text: '${line.at == null ? '' : formatClock(line.at!)}  ',
              style: TextStyle(color: scheme.outline),
            ),
            TextSpan(text: line.text, style: TextStyle(color: colour)),
          ],
        ),
      ),
    );
  }
}
