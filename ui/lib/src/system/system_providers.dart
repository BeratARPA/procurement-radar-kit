import 'dart:async';

import 'package:flutter_riverpod/flutter_riverpod.dart';

import 'system_control.dart';

final Provider<SystemControl> systemControlProvider =
    Provider<SystemControl>((Ref ref) => createSystemControl());

/// Whether the Sistem screen exists at all.
///
/// One question, asked in one place. On the web this is false and the
/// destination never appears; the alternative - `kIsWeb` sprinkled through the
/// router and the shell - is three places to forget.
final Provider<bool> systemSupportedProvider =
    Provider<bool>((Ref ref) => ref.watch(systemControlProvider).isSupported);

/// Everything the screen shows, read together.
///
/// One provider rather than four, because the parts are read as a set: the
/// interesting reading is "the task is running but nothing is on the port", and
/// that is only visible when both were asked at the same moment.
final systemOverviewProvider = FutureProvider.autoDispose<SystemOverview>(
  (Ref ref) async {
    final SystemControl control = ref.watch(systemControlProvider);

    if (!control.isSupported) {
      return const SystemOverview(parts: <PartStatus>[]);
    }

    final List<PartStatus> parts = await Future.wait<PartStatus>(
      Part.values.map(control.statusOf),
    );

    return SystemOverview(
      parts: parts,
      projectRoot: control.projectRoot,
      sleepWarning: await control.sleepWarning(),
      staleDeployment: await control.staleDeployment(),
    );
  },
  // A control panel that retries by itself hides the thing it exists to show.
  retry: (int count, Object error) => null,
);

class SystemOverview {
  const SystemOverview({
    required this.parts,
    this.projectRoot,
    this.sleepWarning,
    this.staleDeployment,
  });

  final List<PartStatus> parts;
  final String? projectRoot;
  final String? sleepWarning;
  final String? staleDeployment;

  PartStatus? of(Part part) {
    for (final PartStatus status in parts) {
      if (status.part == part) return status;
    }

    return null;
  }
}

/// The log, newest last.
final systemLogProvider =
    FutureProvider.autoDispose.family<List<LogLine>, bool>(
  (Ref ref, bool problemsOnly) =>
      ref.watch(systemControlProvider).readLog(problemsOnly: problemsOnly),
  retry: (int count, Object error) => null,
);

/// Re-reads the overview on a timer while the screen is open.
///
/// Twelve seconds: slow enough that pressing a button and watching it take
/// effect feels immediate, cheap enough that it is three short process calls.
/// `autoDispose` on the providers means it stops the moment the screen closes.
final systemTickerProvider = StreamProvider.autoDispose<int>((Ref ref) {
  return Stream<int>.periodic(const Duration(seconds: 12), (int tick) => tick);
});
