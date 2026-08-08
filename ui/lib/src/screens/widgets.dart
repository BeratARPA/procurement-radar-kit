import 'package:flutter/material.dart';
import 'package:go_router/go_router.dart';

import '../data/failures.dart';
import '../router.dart';
import '../strings.dart';

/// What went wrong, and the one thing worth doing about it.
///
/// A rejected token offers the settings screen; everything else offers a retry.
/// A single "hata" with a single button would send somebody to the wrong place
/// half the time.
class FailureView extends StatelessWidget {
  const FailureView({required this.error, required this.onRetry, super.key});

  final Object error;
  final VoidCallback onRetry;

  @override
  Widget build(BuildContext context) {
    final ApiFailure failure = describeFailure(error);
    final ThemeData theme = Theme.of(context);

    return Center(
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 420),
        child: Padding(
          padding: const EdgeInsets.all(24),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Icon(
                switch (failure) {
                  Unauthorized() => Icons.key_off_outlined,
                  ResponseNotUnderstood() => Icons.sync_problem_outlined,
                  _ => Icons.cloud_off_outlined,
                },
                size: 40,
                color: theme.colorScheme.outline,
              ),
              const SizedBox(height: 16),
              Text(
                failure.message,
                textAlign: TextAlign.center,
                style: theme.textTheme.bodyLarge,
              ),
              const SizedBox(height: 20),
              if (failure is Unauthorized)
                FilledButton.tonalIcon(
                  onPressed: () => context.go(Routes.settings),
                  icon: const Icon(Icons.settings_outlined),
                  label: const Text(Strings.settings),
                )
              else
                FilledButton.tonalIcon(
                  onPressed: onRetry,
                  icon: const Icon(Icons.refresh),
                  label: const Text(Strings.retry),
                ),
            ],
          ),
        ),
      ),
    );
  }
}

/// The score, sized to be read without being read.
class ScoreBadge extends StatelessWidget {
  const ScoreBadge({required this.score, super.key});

  final int score;

  @override
  Widget build(BuildContext context) {
    final ColorScheme scheme = Theme.of(context).colorScheme;

    // The threshold is 30 (D-14) - one strong keyword. Anything at or above it
    // is something the system would have announced, so it is worth a different
    // weight on the row than something that merely scored.
    final bool announced = score >= 30;

    return Container(
      width: 44,
      height: 44,
      alignment: Alignment.center,
      decoration: BoxDecoration(
        color: announced ? scheme.primaryContainer : scheme.surfaceContainerHighest,
        shape: BoxShape.circle,
      ),
      child: Text(
        '$score',
        style: Theme.of(context).textTheme.titleMedium?.copyWith(
              color: announced ? scheme.onPrimaryContainer : scheme.onSurfaceVariant,
              fontWeight: announced ? FontWeight.w700 : FontWeight.w400,
            ),
      ),
    );
  }
}

/// A centred message for the empty cases, which are normal here rather than
/// exceptional - a filter that announces nothing on a quiet day is working.
class EmptyView extends StatelessWidget {
  const EmptyView({required this.message, super.key});

  final String message;

  @override
  Widget build(BuildContext context) {
    return Center(
      child: Padding(
        padding: const EdgeInsets.all(32),
        child: Text(
          message,
          textAlign: TextAlign.center,
          style: Theme.of(context).textTheme.bodyLarge?.copyWith(
                color: Theme.of(context).colorScheme.onSurfaceVariant,
              ),
        ),
      ),
    );
  }
}
