import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../data/providers.dart';
import 'keyword_draft.dart';
import 'politeness_draft.dart';

/// The keyword setting as the table holds it right now.
///
/// The table, not `config/keywords.json`. D-16 is explicit: the file is what an
/// empty table is seeded from and nothing more, so a screen that read the file
/// would show a value the worker is not using.
final storedKeywordsProvider = FutureProvider.autoDispose<Setting>(
  (Ref ref) => ref.watch(radarApiProvider).settings.getSetting(key: 'keywords'),
  retry: (int count, Object error) => null,
);

/// What is being edited.
///
/// Null until the stored value has arrived; the screen shows a spinner rather
/// than an empty editor, because an empty editor invites somebody to save one.
final NotifierProvider<FilterDraftNotifier, KeywordDraft?> filterDraftProvider =
    NotifierProvider<FilterDraftNotifier, KeywordDraft?>(FilterDraftNotifier.new);

class FilterDraftNotifier extends Notifier<KeywordDraft?> {
  @override
  KeywordDraft? build() => null;

  void load(String json) => state = KeywordDraft.parse(json);

  void reset() => state = null;

  void _update(KeywordDraft draft) => state = draft;

  void setThreshold(int value) {
    final KeywordDraft? draft = state;

    if (draft != null) _update(draft.copyWith(notifyThreshold: value));
  }

  void addInclude(String term, int weight) {
    final KeywordDraft? draft = state;

    if (draft == null || term.trim().isEmpty) return;

    _update(draft.copyWith(
      include: <IncludeTerm>[
        ...draft.include,
        IncludeTerm(term: term.trim(), weight: weight),
      ],
    ));
  }

  void editInclude(int index, {String? term, int? weight}) {
    final KeywordDraft? draft = state;

    if (draft == null || index < 0 || index >= draft.include.length) return;

    final List<IncludeTerm> next = <IncludeTerm>[...draft.include];
    next[index] = next[index].copyWith(term: term, weight: weight);

    _update(draft.copyWith(include: next));
  }

  void removeInclude(int index) {
    final KeywordDraft? draft = state;

    if (draft == null || index < 0 || index >= draft.include.length) return;

    _update(draft.copyWith(
      include: <IncludeTerm>[...draft.include]..removeAt(index),
    ));
  }

  void addExclude(String term) {
    final KeywordDraft? draft = state;

    if (draft == null || term.trim().isEmpty) return;

    _update(draft.copyWith(exclude: <String>[...draft.exclude, term.trim()]));
  }

  void removeExclude(int index) {
    final KeywordDraft? draft = state;

    if (draft == null || index < 0 || index >= draft.exclude.length) return;

    _update(draft.copyWith(exclude: <String>[...draft.exclude]..removeAt(index)));
  }
}

/// What the draft would have announced, asked of the real archive.
///
/// Nothing is written. The api builds a scorer from the submitted list, uses it
/// and throws it away, so a preview cannot change what the worker is doing -
/// which would be the opposite of a preview.
final NotifierProvider<FilterPreviewNotifier, AsyncValue<FilterPreviewResponse>?>
    filterPreviewProvider =
    NotifierProvider<FilterPreviewNotifier, AsyncValue<FilterPreviewResponse>?>(
  FilterPreviewNotifier.new,
);

class FilterPreviewNotifier
    extends Notifier<AsyncValue<FilterPreviewResponse>?> {
  @override
  AsyncValue<FilterPreviewResponse>? build() => null;

  /// The draft that produced the value currently in state.
  ///
  /// Held so an unchanged draft does not send the request again - the screen
  /// asks on a timer after every keystroke, and most keystrokes leave the json
  /// identical to the one already answered.
  String? _asked;

  Future<void> preview(KeywordDraft draft) async {
    final String json = draft.toJson();

    if (json == _asked && state is AsyncData) return;

    _asked = json;
    state = const AsyncValue<FilterPreviewResponse>.loading();

    try {
      final FilterPreviewResponse answer =
          await ref.read(radarApiProvider).filter.previewFilter(
                body: FilterPreviewRequest(
                  keywords: json,
                  threshold: draft.notifyThreshold,
                ),
              );

      // A later edit may have already asked for something else while this was
      // in flight. Writing a stale answer would show numbers for a filter that
      // is no longer on screen.
      if (_asked == json) state = AsyncValue<FilterPreviewResponse>.data(answer);
    } on Object catch (error, stack) {
      if (_asked == json) {
        state = AsyncValue<FilterPreviewResponse>.error(error, stack);
      }
    }
  }

  void clear() {
    _asked = null;
    state = null;
  }
}

/// Writes the draft back, through the api rather than into the row.
///
/// CLAUDE.md is specific about this: push it through the api, do not edit the
/// row by hand, because the api validates it with the loader that will read it
/// and keeps the previous value in `setting_history` if it does not load.
final storedPolitenessProvider = FutureProvider.autoDispose<Setting>(
  (Ref ref) => ref.watch(radarApiProvider).settings.getSetting(key: 'politeness'),
  retry: (int count, Object error) => null,
);

final NotifierProvider<PolitenessDraftNotifier, PolitenessDraft?>
    politenessDraftProvider =
    NotifierProvider<PolitenessDraftNotifier, PolitenessDraft?>(
  PolitenessDraftNotifier.new,
);

class PolitenessDraftNotifier extends Notifier<PolitenessDraft?> {
  @override
  PolitenessDraft? build() => null;

  void load(String json) => state = PolitenessDraft.parse(json);

  void reset() => state = null;

  void set(String key, Object? value) {
    final PolitenessDraft? draft = state;

    if (draft != null) state = draft.withValue(key, value);
  }
}

Future<void> savePoliteness(WidgetRef ref, PolitenessDraft draft) async {
  await ref.read(radarApiProvider).settings.saveSetting(
        key: 'politeness',
        body: SettingChangeRequest(value: draft.toJson(), updatedBy: 'ui'),
      );

  ref.invalidate(storedPolitenessProvider);
}

Future<void> saveKeywords(WidgetRef ref, KeywordDraft draft) async {
  await ref.read(radarApiProvider).settings.saveSetting(
        key: 'keywords',
        body: SettingChangeRequest(value: draft.toJson(), updatedBy: 'ui'),
      );

  ref.invalidate(storedKeywordsProvider);
}
