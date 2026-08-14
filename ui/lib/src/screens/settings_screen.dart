import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../data/providers.dart';
import '../strings.dart';
import 'connection_form.dart';
import 'backup_section.dart';
import 'politeness_section.dart';

/// Connection and appearance.
///
/// Not the settings screen T-20 describes - the keyword lists and the threshold
/// live in the `setting` table and are edited through the api. This is the
/// smaller thing the app needs to talk to anything at all.
class SettingsScreen extends ConsumerWidget {
  const SettingsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final ThemeMode mode = ref.watch(themeModeProvider);

    return Scaffold(
      appBar: AppBar(title: const Text(Strings.settings)),
      body: ListView(
        padding: const EdgeInsets.all(16),
        children: <Widget>[
          Center(
            child: ConstrainedBox(
              constraints: const BoxConstraints(maxWidth: 560),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.stretch,
                children: <Widget>[
                  Text(Strings.connection, style: theme.textTheme.titleMedium),
                  const SizedBox(height: 16),
                  const ConnectionForm(submitLabel: Strings.save),
                  const SizedBox(height: 32),
                  Text(Strings.theme, style: theme.textTheme.titleMedium),
                  const SizedBox(height: 12),
                  SegmentedButton<ThemeMode>(
                    segments: const <ButtonSegment<ThemeMode>>[
                      ButtonSegment<ThemeMode>(
                        value: ThemeMode.system,
                        label: Text(Strings.themeSystem),
                        icon: Icon(Icons.brightness_auto_outlined),
                      ),
                      ButtonSegment<ThemeMode>(
                        value: ThemeMode.light,
                        label: Text(Strings.themeLight),
                        icon: Icon(Icons.light_mode_outlined),
                      ),
                      ButtonSegment<ThemeMode>(
                        value: ThemeMode.dark,
                        label: Text(Strings.themeDark),
                        icon: Icon(Icons.dark_mode_outlined),
                      ),
                    ],
                    selected: <ThemeMode>{mode},
                    onSelectionChanged: (Set<ThemeMode> selection) =>
                        ref.read(themeModeProvider.notifier).select(selection.first),
                  ),
                  const Divider(height: 40),
                  const BackupSection(),
                  const Divider(height: 48),
                  const PolitenessSection(),
                  const Divider(height: 40),
                  Text(Strings.notOnThisScreen, style: theme.textTheme.titleSmall),
                  const SizedBox(height: 6),
                  Text(
                    Strings.notOnThisScreenHelp,
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
                  ),
                ],
              ),
            ),
          ),
        ],
      ),
    );
  }
}
