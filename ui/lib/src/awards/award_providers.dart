import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../data/providers.dart';

final awardsProvider = FutureProvider.autoDispose<List<AwardSummary>>(
  (Ref ref) => ref.watch(radarApiProvider).awards.listAwards(limit: 200),
  retry: (int count, Object error) => null,
);

/// Tenders that have closed and whose result has not been read yet.
///
/// Only so the screen can say why it is empty. This table fills slowly by
/// design (D-15) and an empty screen with no explanation reads as a broken one.
final pendingAwardsProvider = FutureProvider.autoDispose<List<AwardCandidate>>(
  (Ref ref) => ref.watch(radarApiProvider).awards.listPendingAwards(),
  retry: (int count, Object error) => null,
);

/// What one authority's results look like taken together.
///
/// spec Bolum 16 is what this is for: after six months the archive says which
/// authority buys what, how often, and what it closes at. A notice says what is
/// wanted; only the result says what it goes for, and the ratio between the two
/// is the number that sets a bid price.
class AuthorityHistory {
  const AuthorityHistory({
    required this.authorityName,
    required this.concluded,
    required this.totalContract,
    required this.averageBidCount,
    required this.averageRatio,
  });

  final String authorityName;
  final int concluded;
  final double totalContract;

  /// How many bidders showed up, on average. One is a different market to six.
  final double? averageBidCount;

  /// Contract over estimate. Below one means it closed under the estimate.
  ///
  /// Null when no result carried both numbers - which is most of them at first,
  /// and saying nothing is better than averaging over the halves that happen to
  /// have both.
  final double? averageRatio;
}

/// Groups the results by authority.
///
/// Done here rather than in sql because the whole set is a few hundred rows at
/// most and it is the same data the list already fetched. A second endpoint
/// would be a second thing to keep in step for no gain at this size.
List<AuthorityHistory> groupByAuthority(List<AwardSummary> awards) {
  final Map<String, List<AwardSummary>> byAuthority = <String, List<AwardSummary>>{};

  for (final AwardSummary award in awards) {
    byAuthority.putIfAbsent(award.authorityName, () => <AwardSummary>[]).add(award);
  }

  final List<AuthorityHistory> history = <AuthorityHistory>[];

  for (final MapEntry<String, List<AwardSummary>> entry in byAuthority.entries) {
    final List<AwardSummary> rows = entry.value;

    final List<double> ratios = <double>[
      for (final AwardSummary row in rows)
        if (row.contractAmount != null &&
            row.estimatedCost != null &&
            row.estimatedCost! > 0)
          row.contractAmount! / row.estimatedCost!,
    ];

    final List<int> bidCounts = <int>[
      for (final AwardSummary row in rows)
        if (row.bidCount != null) row.bidCount!,
    ];

    history.add(AuthorityHistory(
      authorityName: entry.key,
      concluded: rows.length,
      totalContract: rows.fold<double>(
          0, (double sum, AwardSummary row) => sum + (row.contractAmount ?? 0)),
      averageBidCount: bidCounts.isEmpty
          ? null
          : bidCounts.reduce((int a, int b) => a + b) / bidCounts.length,
      averageRatio: ratios.isEmpty
          ? null
          : ratios.reduce((double a, double b) => a + b) / ratios.length,
    ));
  }

  history.sort((AuthorityHistory a, AuthorityHistory b) =>
      b.concluded.compareTo(a.concluded));

  return history;
}
