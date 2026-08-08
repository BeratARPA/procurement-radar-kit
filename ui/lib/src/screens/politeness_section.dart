import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../filter/filter_providers.dart';
import '../filter/politeness_draft.dart';
import '../strings.dart';
import 'widgets.dart';

/// The politeness budget, editable, with its floors showing.
///
/// CLAUDE.md calls these rules non-negotiable and D-16 still put them on a
/// screen. The two are not in conflict: what cannot be crossed is enforced on
/// the server, so the screen's job is to show the number, show why it stops
/// where it stops, and refuse before the api has to.
///
/// The reasons are not written here. They are in the setting itself, in Turkish,
/// put there by whoever tuned it - including the measured one, that Ankara
/// Büyükşehir already answers us with 403.
class PolitenessSection extends ConsumerStatefulWidget {
  const PolitenessSection({super.key});

  @override
  ConsumerState<PolitenessSection> createState() => _PolitenessSectionState();
}

class _PolitenessSectionState extends ConsumerState<PolitenessSection> {
  bool _saving = false;

  Future<void> _save(PolitenessDraft draft) async {
    setState(() => _saving = true);

    try {
      await savePoliteness(ref, draft);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedAndLive)),
        );
      }
    } on Object catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context)
            .showSnackBar(SnackBar(content: Text('$error')));
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final AsyncValue<Setting> stored = ref.watch(storedPolitenessProvider);
    final PolitenessDraft? draft = ref.watch(politenessDraftProvider);

    if (draft == null && stored.hasValue) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || ref.read(politenessDraftProvider) != null) return;

        ref.read(politenessDraftProvider.notifier).load(stored.value!.value);
      });
    }

    return stored.when(
      loading: () => const Padding(
        padding: EdgeInsets.symmetric(vertical: 24),
        child: Center(child: CircularProgressIndicator()),
      ),
      error: (Object error, StackTrace stackTrace) => FailureView(
        error: error,
        onRetry: () => ref.invalidate(storedPolitenessProvider),
      ),
      data: (Setting _) {
        if (draft == null) {
          return const Padding(
            padding: EdgeInsets.symmetric(vertical: 24),
            child: Center(child: CircularProgressIndicator()),
          );
        }

        final PolitenessDraftNotifier notifier =
            ref.read(politenessDraftProvider.notifier);
        final String? refusal = draft.refusal;

        return Column(
          crossAxisAlignment: CrossAxisAlignment.stretch,
          children: <Widget>[
            Text(Strings.politeness, style: theme.textTheme.titleMedium),
            const SizedBox(height: 4),
            Text(
              Strings.politenessHeader,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
            const SizedBox(height: 16),
            _Number(
              label: Strings.hostInterval,
              value: draft.hostIntervalSeconds,
              floor: PolitenessDraft.hostIntervalFloorSeconds,
              note: draft.noteFor('_minimum_host_interval'),
              onChanged: (int v) =>
                  notifier.set('minimum_host_interval_seconds', v),
            ),
            _Number(
              label: Strings.jitter,
              value: draft.jitterSeconds,
              floor: 0,
              note: draft.noteFor('_maximum_jitter'),
              onChanged: (int v) => notifier.set('maximum_jitter_seconds', v),
            ),
            _Number(
              label: Strings.requestTimeout,
              value: draft.requestTimeoutSeconds,
              floor: 1,
              onChanged: (int v) => notifier.set('request_timeout_seconds', v),
            ),
            _Number(
              label: Strings.maximumAttempts,
              value: draft.maximumAttempts,
              floor: 1,
              onChanged: (int v) => notifier.set('maximum_attempts', v),
            ),
            const Divider(height: 28),
            Text(Strings.nightWindow, style: theme.textTheme.labelLarge),
            if (draft.noteFor('_night_window') case final String note) ...<Widget>[
              const SizedBox(height: 2),
              Text(
                note,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
              ),
            ],
            const SizedBox(height: 10),
            Row(
              children: <Widget>[
                Expanded(
                  child: _Number(
                    label: Strings.nightStart,
                    value: draft.nightStartHour,
                    floor: 0,
                    ceiling: 23,
                    onChanged: (int v) =>
                        notifier.set('night_window_start_hour', v),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _Number(
                    label: Strings.nightEnd,
                    value: draft.nightEndHour,
                    floor: 0,
                    ceiling: 23,
                    onChanged: (int v) =>
                        notifier.set('night_window_end_hour', v),
                  ),
                ),
                const SizedBox(width: 12),
                Expanded(
                  child: _Number(
                    label: Strings.nightMultiplier,
                    value: draft.nightMultiplier,
                    floor: 1,
                    onChanged: (int v) =>
                        notifier.set('night_interval_multiplier', v),
                  ),
                ),
              ],
            ),
            const Divider(height: 28),
            // Shown as a switch that cannot move, rather than hidden. Hiding it
            // would make the rule invisible; a switch that lies would be worse.
            Row(
              children: <Widget>[
                const Icon(Icons.lock_outline, size: 18),
                const SizedBox(width: 10),
                Expanded(
                  child: Column(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Text(Strings.respectRobots,
                          style: theme.textTheme.bodyMedium),
                      if (draft.noteFor('_respect_robots') case final String note)
                        Text(
                          note,
                          style: theme.textTheme.bodySmall?.copyWith(
                              color: theme.colorScheme.onSurfaceVariant),
                        ),
                    ],
                  ),
                ),
                Switch(value: draft.respectRobots, onChanged: null),
              ],
            ),
            const SizedBox(height: 16),
            if (refusal != null)
              Container(
                padding: const EdgeInsets.all(12),
                margin: const EdgeInsets.only(bottom: 12),
                decoration: BoxDecoration(
                  color: theme.colorScheme.errorContainer,
                  borderRadius: BorderRadius.circular(8),
                ),
                child: Text(
                  refusal,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: theme.colorScheme.onErrorContainer),
                ),
              ),
            Row(
              mainAxisAlignment: MainAxisAlignment.end,
              children: <Widget>[
                TextButton(
                  onPressed: _saving
                      ? null
                      : () {
                          ref.read(politenessDraftProvider.notifier).reset();
                          ref.invalidate(storedPolitenessProvider);
                        },
                  child: const Text(Strings.revert),
                ),
                const SizedBox(width: 8),
                FilledButton(
                  onPressed:
                      _saving || refusal != null ? null : () => _save(draft),
                  child: _saving
                      ? const SizedBox(
                          width: 16,
                          height: 16,
                          child: CircularProgressIndicator(strokeWidth: 2),
                        )
                      : const Text(Strings.save),
                ),
              ],
            ),
          ],
        );
      },
    );
  }
}

class _Number extends StatelessWidget {
  const _Number({
    required this.label,
    required this.value,
    required this.floor,
    required this.onChanged,
    this.ceiling,
    this.note,
  });

  final String label;
  final int value;
  final int floor;
  final int? ceiling;
  final String? note;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 14),
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          TextFormField(
            initialValue: '$value',
            keyboardType: TextInputType.number,
            inputFormatters: <TextInputFormatter>[
              FilteringTextInputFormatter.digitsOnly,
            ],
            decoration: InputDecoration(
              labelText: label,
              isDense: true,
              border: const OutlineInputBorder(),
              // The floor is on the field, not only in the error. A limit
              // somebody discovers by hitting it is a limit that reads as a bug.
              suffixText: ceiling == null ? '≥ $floor' : '$floor–$ceiling',
            ),
            onChanged: (String text) {
              final int? parsed = int.tryParse(text);

              if (parsed != null) onChanged(parsed);
            },
          ),
          if (note != null) ...<Widget>[
            const SizedBox(height: 4),
            Text(
              note!,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
            ),
          ],
        ],
      ),
    );
  }
}
