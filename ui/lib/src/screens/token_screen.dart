import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../router.dart';
import '../strings.dart';
import 'connection_form.dart';

/// Shown until there is something to authenticate with.
///
/// Outside the navigation shell on purpose: with no token every screen behind it
/// would show the same 401, so three tabs of the same error is worse than one
/// screen that says what is missing.
class TokenScreen extends ConsumerWidget {
  const TokenScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    return Scaffold(
      appBar: AppBar(title: const Text(Strings.appTitle)),
      body: Center(
        child: SingleChildScrollView(
          padding: const EdgeInsets.all(24),
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 460),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: <Widget>[
                Text(
                  Strings.tokenNeededTitle,
                  style: Theme.of(context).textTheme.headlineSmall,
                ),
                const SizedBox(height: 8),
                Text(
                  Strings.tokenNeeded,
                  style: Theme.of(context).textTheme.bodyMedium,
                ),
                const SizedBox(height: 24),
                ConnectionForm(
                  submitLabel: Strings.connect,
                  onSaved: () => context.go(Routes.notices),
                ),
              ],
            ),
          ),
        ),
      ),
    );
  }
}
