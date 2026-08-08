import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/intl.dart';

import '../../api/export.dart';
import '../awards/award_providers.dart';
import '../formatting.dart';
import '../strings.dart';
import 'widgets.dart';

/// What the tenders we watched actually closed at.
///
/// spec Bolum 16: after six months the value of this system is not the scraper,
/// it is the archive - which authority buys what, how often, and for how much.
/// A notice says what is wanted; only this says what it goes for, and that is
/// the number a bid price comes from.
class AwardsScreen extends ConsumerWidget {
  const AwardsScreen({super.key});

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final AsyncValue<List<AwardSummary>> awards = ref.watch(awardsProvider);

    return Scaffold(
      appBar: AppBar(
        title: const Text(Strings.awards),
        actions: <Widget>[
          IconButton(
            tooltip: Strings.refresh,
            onPressed: () {
              ref
                ..invalidate(awardsProvider)
                ..invalidate(pendingAwardsProvider);
            },
            icon: const Icon(Icons.refresh),
          ),
        ],
      ),
      body: awards.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) => FailureView(
          error: error,
          onRetry: () => ref.invalidate(awardsProvider),
        ),
        data: (List<AwardSummary> rows) =>
            rows.isEmpty ? const _Empty() : _Results(awards: rows),
      ),
    );
  }
}

/// Nothing yet, and why.
class _Empty extends ConsumerWidget {
  const _Empty();

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final AsyncValue<List<AwardCandidate>> pending = ref.watch(pendingAwardsProvider);

    return Center(
      child: SingleChildScrollView(
        padding: const EdgeInsets.all(32),
        child: ConstrainedBox(
          constraints: const BoxConstraints(maxWidth: 520),
          child: Column(
            mainAxisSize: MainAxisSize.min,
            children: <Widget>[
              Icon(Icons.gavel_outlined, size: 40, color: theme.colorScheme.outline),
              const SizedBox(height: 16),
              Text(Strings.noAwardsYet, style: theme.textTheme.titleMedium),
              const SizedBox(height: 12),
              Text(
                Strings.noAwardsWhy,
                textAlign: TextAlign.center,
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
              ),
              const SizedBox(height: 16),
              pending.when(
                loading: () => const SizedBox(
                    width: 16, height: 16, child: CircularProgressIndicator(strokeWidth: 2)),
                error: (Object error, StackTrace stackTrace) => const SizedBox.shrink(),
                data: (List<AwardCandidate> waiting) => Text(
                  Strings.awaitingResult(waiting.length),
                  textAlign: TextAlign.center,
                  style: theme.textTheme.bodyMedium,
                ),
              ),
            ],
          ),
        ),
      ),
    );
  }
}

class _Results extends StatelessWidget {
  const _Results({required this.awards});

  final List<AwardSummary> awards;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final List<AuthorityHistory> history = groupByAuthority(awards);

    return ListView(
      padding: const EdgeInsets.all(16),
      children: <Widget>[
        Center(
          child: ConstrainedBox(
            constraints: const BoxConstraints(maxWidth: 860),
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.stretch,
              children: <Widget>[
                Text(Strings.byAuthority, style: theme.textTheme.titleSmall),
                const SizedBox(height: 10),
                for (final AuthorityHistory row in history) _AuthorityRow(history: row),
                const Divider(height: 32),
                for (final AwardSummary award in awards) _AwardCard(award: award),
              ],
            ),
          ),
        ),
      ],
    );
  }
}

class _AuthorityRow extends StatelessWidget {
  const _AuthorityRow({required this.history});

  final AuthorityHistory history;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        children: <Widget>[
          Expanded(
            child: Text(history.authorityName,
                maxLines: 1,
                overflow: TextOverflow.ellipsis,
                style: theme.textTheme.bodyMedium),
          ),
          Text('${history.concluded} ${Strings.concluded}',
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
          if (history.averageRatio != null) ...<Widget>[
            const SizedBox(width: 14),
            Text(
              Strings.ratioLabel(history.averageRatio!),
              style: theme.textTheme.bodySmall?.copyWith(
                // Under the estimate is the ordinary outcome; over it is worth
                // noticing, because it says the estimate was low.
                color: history.averageRatio! > 1
                    ? theme.colorScheme.error
                    : theme.colorScheme.primary,
                fontWeight: FontWeight.w600,
              ),
            ),
          ],
        ],
      ),
    );
  }
}

class _AwardCard extends StatelessWidget {
  const _AwardCard({required this.award});

  final AwardSummary award;

  static final NumberFormat _money = NumberFormat.decimalPattern('tr_TR');

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    final double? ratio = award.contractAmount != null &&
            award.estimatedCost != null &&
            award.estimatedCost! > 0
        ? award.contractAmount! / award.estimatedCost!
        : null;

    return Card(
      margin: const EdgeInsets.only(bottom: 12),
      child: Padding(
        padding: const EdgeInsets.all(14),
        child: Column(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Text(award.title, style: theme.textTheme.titleSmall),
            const SizedBox(height: 2),
            Text(
              award.authorityName,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.outline),
            ),
            const SizedBox(height: 12),
            Wrap(
              spacing: 24,
              runSpacing: 10,
              children: <Widget>[
                if (award.winnerName != null)
                  _Fact(label: Strings.winner, value: award.winnerName!),
                if (award.estimatedCost != null)
                  _Fact(
                    label: Strings.estimatedCost,
                    value: '${_money.format(award.estimatedCost)} TL',
                  ),
                if (award.contractAmount != null)
                  _Fact(
                    label: Strings.contractAmount,
                    value: '${_money.format(award.contractAmount)} TL',
                    emphasis: true,
                  ),
                if (ratio != null)
                  _Fact(label: Strings.averageRatio, value: Strings.ratioLabel(ratio)),
                if (award.bidCount != null)
                  _Fact(label: Strings.bidCount, value: '${award.bidCount}'),
                if (award.awardedAt != null)
                  _Fact(label: Strings.awardedAt, value: formatDay(award.awardedAt)),
              ],
            ),
          ],
        ),
      ),
    );
  }
}

class _Fact extends StatelessWidget {
  const _Fact({required this.label, required this.value, this.emphasis = false});

  final String label;
  final String value;
  final bool emphasis;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      mainAxisSize: MainAxisSize.min,
      children: <Widget>[
        Text(label,
            style: theme.textTheme.labelSmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        Text(
          value,
          style: emphasis
              ? theme.textTheme.titleSmall?.copyWith(fontWeight: FontWeight.w700)
              : theme.textTheme.bodyMedium,
        ),
      ],
    );
  }
}
