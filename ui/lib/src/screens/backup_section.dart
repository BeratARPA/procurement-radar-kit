import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../filter/backup_draft.dart';
import '../filter/filter_providers.dart';
import '../strings.dart';
import 'widgets.dart';

/// Where the backup goes (D-24).
///
/// The one setting whose right answer changes with the hardware rather than
/// with the project: a partition today, a USB disk the day one is plugged in,
/// somewhere else when this moves machine. That is why it is a setting and why
/// it belongs on a screen - it was editable through the api from the first day
/// and that is not the same as being changeable.
class BackupSection extends ConsumerStatefulWidget {
  const BackupSection({super.key});

  @override
  ConsumerState<BackupSection> createState() => _BackupSectionState();
}

class _BackupSectionState extends ConsumerState<BackupSection> {
  bool _saving = false;
  TextEditingController? _destination;

  @override
  void dispose() {
    _destination?.dispose();
    super.dispose();
  }

  Future<void> _save(BackupDraft draft) async {
    setState(() => _saving = true);

    try {
      await saveBackup(ref, draft);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.saved)),
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
    final AsyncValue<Setting> stored = ref.watch(storedBackupProvider);
    final BackupDraft? draft = ref.watch(backupDraftProvider);

    if (draft == null && stored.hasValue) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || ref.read(backupDraftProvider) != null) return;

        ref.read(backupDraftProvider.notifier).load(stored.value!.value);
      });
    }

    if (draft == null) {
      return stored.hasError
          ? FailureView(
              error: stored.error!,
              onRetry: () => ref.invalidate(storedBackupProvider),
            )
          : const Padding(
              padding: EdgeInsets.symmetric(vertical: 24),
              child: Center(child: CircularProgressIndicator()),
            );
    }

    _destination ??= TextEditingController(text: draft.destination);

    final String? refusal = draft.refusal;

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Text(Strings.backup, style: theme.textTheme.titleMedium),
        const SizedBox(height: 4),
        Text(
          Strings.backupHelp,
          style: theme.textTheme.bodySmall
              ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
        ),
        const SizedBox(height: 16),
        TextField(
          controller: _destination,
          decoration: const InputDecoration(
            labelText: Strings.backupDestination,
            helperText: Strings.backupDestinationHelp,
            helperMaxLines: 4,
            border: OutlineInputBorder(),
          ),
          onChanged: (String value) =>
              ref.read(backupDraftProvider.notifier).set('destination', value),
        ),
        if (draft.isOff) ...<Widget>[
          const SizedBox(height: 8),
          Text(
            Strings.backupOff,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.error),
          ),
        ],
        const SizedBox(height: 20),
        _Number(
          label: Strings.backupKeepDumps,
          help: Strings.backupKeepDumpsHelp,
          value: draft.keepDumps,
          minimum: 1,
          onChanged: (int value) =>
              ref.read(backupDraftProvider.notifier).set('keep_dumps', value),
        ),
        const SizedBox(height: 16),
        _Number(
          label: Strings.backupArchiveEvery,
          help: Strings.backupArchiveEveryHelp,
          value: draft.archiveEveryDays,
          minimum: 1,
          onChanged: (int value) => ref
              .read(backupDraftProvider.notifier)
              .set('archive_every_days', value),
        ),
        const SizedBox(height: 8),
        SwitchListTile(
          value: draft.archive,
          onChanged: (bool value) =>
              ref.read(backupDraftProvider.notifier).set('archive', value),
          contentPadding: EdgeInsets.zero,
          title: const Text(Strings.backupArchive),
          subtitle: const Text(Strings.backupArchiveHelp),
        ),
        if (refusal != null) ...<Widget>[
          const SizedBox(height: 8),
          Text(
            refusal,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.error),
          ),
        ],
        const SizedBox(height: 16),
        // The sentence that matters most on this screen, and it is a warning
        // rather than a feature: C: and D: are partitions of one SSD.
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Icon(Icons.info_outline, size: 16, color: theme.colorScheme.outline),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                Strings.backupSameDiskWarning,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline),
              ),
            ),
          ],
        ),
        const SizedBox(height: 16),
        FilledButton(
          onPressed: _saving || refusal != null ? null : () => _save(draft),
          child: _saving
              ? const SizedBox(
                  width: 16,
                  height: 16,
                  child: CircularProgressIndicator(strokeWidth: 2),
                )
              : const Text(Strings.save),
        ),
      ],
    );
  }
}

class _Number extends StatelessWidget {
  const _Number({
    required this.label,
    required this.help,
    required this.value,
    required this.minimum,
    required this.onChanged,
  });

  final String label;
  final String help;
  final int value;
  final int minimum;
  final ValueChanged<int> onChanged;

  @override
  Widget build(BuildContext context) {
    return TextFormField(
      initialValue: '$value',
      keyboardType: TextInputType.number,
      inputFormatters: <TextInputFormatter>[FilteringTextInputFormatter.digitsOnly],
      decoration: InputDecoration(
        labelText: label,
        helperText: help,
        helperMaxLines: 3,
        suffixText: '≥ $minimum',
        border: const OutlineInputBorder(),
      ),
      onChanged: (String text) => onChanged(int.tryParse(text) ?? 0),
    );
  }
}
