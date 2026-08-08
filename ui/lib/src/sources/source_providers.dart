import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../data/providers.dart';

/// Every source, switched-off ones included.
///
/// The whole list rather than the active one: the collector only ever wants the
/// active sources, and a person wants to see the twenty-five that are registered
/// and off - each with its reason in its notes - and the one that was switched
/// off automatically after ten failures.
final allSourcesProvider = FutureProvider.autoDispose<List<Source>>(
  (Ref ref) => ref.watch(radarApiProvider).sources.listAllSources(),
  retry: (int count, Object error) => null,
);

final sourceProvider = FutureProvider.autoDispose.family<Source, int>(
  (Ref ref, int id) => ref.watch(radarApiProvider).sources.getSource(id: id),
  retry: (int count, Object error) => null,
);

/// What a set of selectors made of a page.
///
/// Asked for on a press rather than watched: a check reads an archived page and
/// runs a parser, and doing that on every keystroke would be work nobody asked
/// for.
final NotifierProvider<SourceCheckNotifier, AsyncValue<SourceCheckResponse>?>
    sourceCheckProvider =
    NotifierProvider<SourceCheckNotifier, AsyncValue<SourceCheckResponse>?>(
  SourceCheckNotifier.new,
);

class SourceCheckNotifier
    extends Notifier<AsyncValue<SourceCheckResponse>?> {
  @override
  AsyncValue<SourceCheckResponse>? build() => null;

  void clear() => state = null;

  Future<void> check({
    required String adapterConfig,
    int? sourceId,
    String? html,
    String? url,
  }) async {
    state = const AsyncValue<SourceCheckResponse>.loading();

    try {
      final SourceCheckResponse answer =
          await ref.read(radarApiProvider).sources.checkSource(
                body: SourceCheckRequest(
                  adapterConfig: adapterConfig,
                  sourceId: sourceId,
                  html: html,
                  url: url,
                ),
              );

      state = AsyncValue<SourceCheckResponse>.data(answer);
    } on Object catch (error, stack) {
      state = AsyncValue<SourceCheckResponse>.error(error, stack);
    }
  }
}

/// Registers a source. It arrives switched off, on purpose (spec Bolum 5).
Future<SourceSavedResponse> saveSource(
  WidgetRef ref,
  SourceDefinitionRequest definition,
) async {
  final SourceSavedResponse saved =
      await ref.read(radarApiProvider).sources.saveSource(body: definition);

  ref.invalidate(allSourcesProvider);

  return saved;
}

Future<void> updateSource(
  WidgetRef ref,
  int id,
  SourceDefinitionRequest definition,
) async {
  await ref.read(radarApiProvider).sources.updateSource(id: id, body: definition);

  ref
    ..invalidate(allSourcesProvider)
    ..invalidate(sourceProvider(id));
}

Future<void> setSourceActive(WidgetRef ref, int id, bool isActive) async {
  await ref
      .read(radarApiProvider)
      .sources
      .setSourceActive(id: id, body: ActiveChangeRequest(isActive: isActive));

  ref
    ..invalidate(allSourcesProvider)
    ..invalidate(sourceProvider(id));
}
