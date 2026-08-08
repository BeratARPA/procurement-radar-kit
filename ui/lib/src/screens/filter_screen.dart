import 'dart:async';

import 'package:flutter/material.dart';
import 'package:flutter/services.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../filter/filter_providers.dart';
import '../filter/keyword_draft.dart';
import '../strings.dart';
import 'widgets.dart';

/// Tuning the one thing that decides what gets announced.
///
/// spec Bolum 9 puts it plainly: filter quality matters more than coverage. The
/// numbers on the right are what makes that tunable rather than guessed - D-14
/// moved the threshold from 60 to 30 by reading a sample by hand, and this asks
/// the same question of the whole archive in a second.
class FilterScreen extends ConsumerStatefulWidget {
  const FilterScreen({super.key});

  @override
  ConsumerState<FilterScreen> createState() => _FilterScreenState();
}

class _FilterScreenState extends ConsumerState<FilterScreen> {
  Timer? _debounce;
  bool _saving = false;

  @override
  void dispose() {
    _debounce?.cancel();
    super.dispose();
  }

  /// Asks for a preview once the typing stops.
  ///
  /// Every keystroke scores the archive server-side, so this waits rather than
  /// asking on each one. Half a second is long enough to finish a word and short
  /// enough that the numbers feel attached to the slider.
  void _schedulePreview() {
    _debounce?.cancel();
    _debounce = Timer(const Duration(milliseconds: 500), () {
      final KeywordDraft? draft = ref.read(filterDraftProvider);

      if (draft != null) {
        ref.read(filterPreviewProvider.notifier).preview(draft);
      }
    });
  }

  Future<void> _save(KeywordDraft draft) async {
    setState(() => _saving = true);

    try {
      await saveKeywords(ref, draft);

      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          const SnackBar(content: Text(Strings.savedAndLive)),
        );
      }
    } on Object catch (error) {
      if (mounted) {
        ScaffoldMessenger.of(context).showSnackBar(
          SnackBar(content: Text(_describe(error))),
        );
      }
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  static String _describe(Object error) {
    // The api refuses a list its own loader cannot read, and says why. That
    // sentence is more useful than anything this screen could invent.
    final String text = '$error';

    return text.length > 300 ? '${text.substring(0, 300)}…' : text;
  }

  @override
  Widget build(BuildContext context) {
    final AsyncValue<Setting> stored = ref.watch(storedKeywordsProvider);
    final KeywordDraft? draft = ref.watch(filterDraftProvider);

    // The stored value becomes the draft once, after the frame that delivered
    // it. After the frame because loading it during build would be writing to a
    // provider while another is being read; and only once, because doing it
    // again would throw away whatever is being edited.
    //
    // One mechanism, not a listener as well: a listener does not fire when the
    // value is already cached on the first build, which is exactly what happens
    // on the second visit to this screen.
    if (draft == null && stored.hasValue) {
      WidgetsBinding.instance.addPostFrameCallback((_) {
        if (!mounted || ref.read(filterDraftProvider) != null) return;

        ref.read(filterDraftProvider.notifier).load(stored.value!.value);
        _schedulePreview();
      });
    }

    return Scaffold(
      appBar: AppBar(
        title: const Text(Strings.filter),
        actions: <Widget>[
          if (draft != null) ...<Widget>[
            TextButton(
              onPressed: _saving
                  ? null
                  : () {
                      ref.read(filterDraftProvider.notifier).reset();
                      ref.read(filterPreviewProvider.notifier).clear();
                      ref.invalidate(storedKeywordsProvider);
                    },
              child: const Text(Strings.revert),
            ),
            const SizedBox(width: 8),
            FilledButton(
              onPressed: _saving ? null : () => _save(draft),
              child: _saving
                  ? const SizedBox(
                      width: 16,
                      height: 16,
                      child: CircularProgressIndicator(strokeWidth: 2),
                    )
                  : const Text(Strings.save),
            ),
            const SizedBox(width: 12),
          ],
        ],
      ),
      body: stored.when(
        loading: () => const Center(child: CircularProgressIndicator()),
        error: (Object error, StackTrace stackTrace) => FailureView(
          error: error,
          onRetry: () => ref.invalidate(storedKeywordsProvider),
        ),
        data: (Setting _) => draft == null
            ? const Center(child: CircularProgressIndicator())
            : LayoutBuilder(
                builder: (BuildContext context, BoxConstraints constraints) {
                  final bool sideBySide = constraints.maxWidth >= 900;

                  final Widget editor = _Editor(
                    draft: draft,
                    onChanged: _schedulePreview,
                  );
                  final Widget preview = _Preview(draft: draft);

                  if (!sideBySide) {
                    return ListView(
                      padding: const EdgeInsets.all(16),
                      children: <Widget>[editor, const SizedBox(height: 24), preview],
                    );
                  }

                  // Wide enough to see the cause and the effect at once, which
                  // is the entire point of the screen.
                  return Row(
                    crossAxisAlignment: CrossAxisAlignment.start,
                    children: <Widget>[
                      Expanded(
                        flex: 3,
                        child: ListView(
                          padding: const EdgeInsets.all(16),
                          children: <Widget>[editor],
                        ),
                      ),
                      const VerticalDivider(width: 1),
                      Expanded(
                        flex: 2,
                        child: ListView(
                          padding: const EdgeInsets.all(16),
                          children: <Widget>[preview],
                        ),
                      ),
                    ],
                  );
                },
              ),
      ),
    );
  }
}

class _Editor extends ConsumerWidget {
  const _Editor({required this.draft, required this.onChanged});

  final KeywordDraft draft;
  final VoidCallback onChanged;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final FilterDraftNotifier notifier = ref.read(filterDraftProvider.notifier);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Text(Strings.threshold, style: theme.textTheme.titleSmall),
        const SizedBox(height: 4),
        Row(
          children: <Widget>[
            Expanded(
              child: Slider(
                value: draft.notifyThreshold.toDouble().clamp(0, 100),
                min: 0,
                max: 100,
                divisions: 20,
                label: '${draft.notifyThreshold}',
                onChanged: (double value) {
                  notifier.setThreshold(value.round());
                  onChanged();
                },
              ),
            ),
            SizedBox(
              width: 44,
              child: Text(
                '${draft.notifyThreshold}',
                textAlign: TextAlign.end,
                style: theme.textTheme.titleMedium,
              ),
            ),
          ],
        ),
        Text(
          Strings.thresholdHelp,
          style: theme.textTheme.bodySmall
              ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
        ),
        const SizedBox(height: 24),
        _IncludeSection(draft: draft, onChanged: onChanged),
        const SizedBox(height: 24),
        _ExcludeSection(draft: draft, onChanged: onChanged),
        const SizedBox(height: 20),
        Row(
          crossAxisAlignment: CrossAxisAlignment.start,
          children: <Widget>[
            Icon(Icons.lock_outline, size: 15, color: theme.colorScheme.outline),
            const SizedBox(width: 8),
            Expanded(
              child: Text(
                '${Strings.untouchedKeysNote} (${draft.untouchedKeys.join(', ')})',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline),
              ),
            ),
          ],
        ),
      ],
    );
  }
}

class _IncludeSection extends ConsumerStatefulWidget {
  const _IncludeSection({required this.draft, required this.onChanged});

  final KeywordDraft draft;
  final VoidCallback onChanged;

  @override
  ConsumerState<_IncludeSection> createState() => _IncludeSectionState();
}

class _IncludeSectionState extends ConsumerState<_IncludeSection> {
  final TextEditingController _term = TextEditingController();
  int _weight = 30;

  @override
  void dispose() {
    _term.dispose();
    super.dispose();
  }

  void _add() {
    if (_term.text.trim().isEmpty) return;

    ref.read(filterDraftProvider.notifier).addInclude(_term.text, _weight);
    _term.clear();
    widget.onChanged();
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final FilterDraftNotifier notifier = ref.read(filterDraftProvider.notifier);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(Strings.includeTerms, style: theme.textTheme.titleSmall),
            const SizedBox(width: 8),
            Text('${widget.draft.include.length}',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline)),
          ],
        ),
        const SizedBox(height: 8),
        for (int i = 0; i < widget.draft.include.length; i++)
          _IncludeRow(
            key: ValueKey<String>('${widget.draft.include[i].term}-$i'),
            term: widget.draft.include[i],
            onWeight: (int value) {
              notifier.editInclude(i, weight: value);
              widget.onChanged();
            },
            onRemove: () {
              notifier.removeInclude(i);
              widget.onChanged();
            },
          ),
        const SizedBox(height: 8),
        Row(
          children: <Widget>[
            Expanded(
              child: TextField(
                controller: _term,
                decoration: const InputDecoration(
                  labelText: Strings.addTerm,
                  isDense: true,
                  border: OutlineInputBorder(),
                ),
                onSubmitted: (_) => _add(),
              ),
            ),
            const SizedBox(width: 8),
            SizedBox(
              width: 92,
              child: TextFormField(
                initialValue: '$_weight',
                keyboardType: TextInputType.number,
                inputFormatters: <TextInputFormatter>[
                  FilteringTextInputFormatter.digitsOnly,
                ],
                decoration: const InputDecoration(
                  labelText: Strings.weight,
                  isDense: true,
                  border: OutlineInputBorder(),
                ),
                onChanged: (String value) =>
                    _weight = int.tryParse(value) ?? _weight,
              ),
            ),
            const SizedBox(width: 8),
            IconButton.filledTonal(
              onPressed: _add,
              icon: const Icon(Icons.add),
            ),
          ],
        ),
      ],
    );
  }
}

class _IncludeRow extends StatelessWidget {
  const _IncludeRow({
    required this.term,
    required this.onWeight,
    required this.onRemove,
    super.key,
  });

  final IncludeTerm term;
  final ValueChanged<int> onWeight;
  final VoidCallback onRemove;

  @override
  Widget build(BuildContext context) {
    return Padding(
      padding: const EdgeInsets.only(bottom: 6),
      child: Row(
        children: <Widget>[
          Expanded(child: Text(term.term)),
          SizedBox(
            width: 74,
            child: TextFormField(
              initialValue: '${term.weight}',
              keyboardType: TextInputType.number,
              textAlign: TextAlign.end,
              inputFormatters: <TextInputFormatter>[
                FilteringTextInputFormatter.digitsOnly,
              ],
              decoration: const InputDecoration(isDense: true),
              onChanged: (String value) {
                final int? weight = int.tryParse(value);

                if (weight != null) onWeight(weight);
              },
            ),
          ),
          IconButton(
            visualDensity: VisualDensity.compact,
            onPressed: onRemove,
            icon: const Icon(Icons.close, size: 18),
          ),
        ],
      ),
    );
  }
}

class _ExcludeSection extends ConsumerStatefulWidget {
  const _ExcludeSection({required this.draft, required this.onChanged});

  final KeywordDraft draft;
  final VoidCallback onChanged;

  @override
  ConsumerState<_ExcludeSection> createState() => _ExcludeSectionState();
}

class _ExcludeSectionState extends ConsumerState<_ExcludeSection> {
  final TextEditingController _term = TextEditingController();

  @override
  void dispose() {
    _term.dispose();
    super.dispose();
  }

  void _add() {
    if (_term.text.trim().isEmpty) return;

    ref.read(filterDraftProvider.notifier).addExclude(_term.text);
    _term.clear();
    widget.onChanged();
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final FilterDraftNotifier notifier = ref.read(filterDraftProvider.notifier);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Row(
          children: <Widget>[
            Text(Strings.excludeTerms, style: theme.textTheme.titleSmall),
            const SizedBox(width: 8),
            Text('${widget.draft.exclude.length}',
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.outline)),
          ],
        ),
        const SizedBox(height: 4),
        Text(
          Strings.excludeHelp,
          style: theme.textTheme.bodySmall
              ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
        ),
        const SizedBox(height: 10),
        Wrap(
          spacing: 6,
          runSpacing: 6,
          children: <Widget>[
            for (int i = 0; i < widget.draft.exclude.length; i++)
              InputChip(
                label: Text(widget.draft.exclude[i]),
                onDeleted: () {
                  notifier.removeExclude(i);
                  widget.onChanged();
                },
              ),
          ],
        ),
        const SizedBox(height: 10),
        Row(
          children: <Widget>[
            Expanded(
              child: TextField(
                controller: _term,
                decoration: const InputDecoration(
                  labelText: Strings.addTerm,
                  isDense: true,
                  border: OutlineInputBorder(),
                ),
                onSubmitted: (_) => _add(),
              ),
            ),
            const SizedBox(width: 8),
            IconButton.filledTonal(onPressed: _add, icon: const Icon(Icons.add)),
          ],
        ),
      ],
    );
  }
}

class _Preview extends ConsumerWidget {
  const _Preview({required this.draft});

  final KeywordDraft draft;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final AsyncValue<FilterPreviewResponse>? preview =
        ref.watch(filterPreviewProvider);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Text(Strings.preview, style: theme.textTheme.titleSmall),
        const SizedBox(height: 10),
        if (preview == null)
          Text(Strings.previewIdle, style: theme.textTheme.bodySmall)
        else
          preview.when(
            loading: () => const Padding(
              padding: EdgeInsets.symmetric(vertical: 24),
              child: Center(child: CircularProgressIndicator()),
            ),
            error: (Object error, StackTrace stackTrace) => FailureView(
              error: error,
              onRetry: () =>
                  ref.read(filterPreviewProvider.notifier).preview(draft),
            ),
            data: (FilterPreviewResponse answer) => _Numbers(answer: answer),
          ),
      ],
    );
  }
}

class _Numbers extends StatelessWidget {
  const _Numbers({required this.answer});

  final FilterPreviewResponse answer;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Container(
          padding: const EdgeInsets.all(14),
          decoration: BoxDecoration(
            color: theme.colorScheme.primaryContainer,
            borderRadius: BorderRadius.circular(12),
          ),
          child: Column(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Text(
                '${answer.wouldNotify}',
                style: theme.textTheme.headlineMedium?.copyWith(
                  color: theme.colorScheme.onPrimaryContainer,
                  fontWeight: FontWeight.w700,
                ),
              ),
              Text(
                Strings.previewSummary(answer.considered, answer.wouldNotify),
                style: theme.textTheme.bodySmall
                    ?.copyWith(color: theme.colorScheme.onPrimaryContainer),
              ),
            ],
          ),
        ),
        const SizedBox(height: 20),
        if (answer.caught.isEmpty)
          Text(Strings.previewNothing, style: theme.textTheme.bodyMedium)
        else ...<Widget>[
          Text(Strings.previewCaught, style: theme.textTheme.labelLarge),
          const SizedBox(height: 6),
          for (final ScoredNotice notice in answer.caught)
            _ScoredRow(notice: notice),
        ],
        if (answer.missed.isNotEmpty) ...<Widget>[
          const SizedBox(height: 20),
          Text(Strings.previewMissed, style: theme.textTheme.labelLarge),
          const SizedBox(height: 2),
          Text(
            Strings.previewMissedHelp,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          ),
          const SizedBox(height: 6),
          for (final ScoredNotice notice in answer.missed)
            _ScoredRow(notice: notice, faded: true),
        ],
      ],
    );
  }
}

class _ScoredRow extends StatelessWidget {
  const _ScoredRow({required this.notice, this.faded = false});

  final ScoredNotice notice;
  final bool faded;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final Color colour =
        faded ? theme.colorScheme.onSurfaceVariant : theme.colorScheme.onSurface;

    return Padding(
      padding: const EdgeInsets.only(bottom: 8),
      child: Row(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          SizedBox(
            width: 32,
            child: Text(
              '${notice.score}',
              style: theme.textTheme.labelLarge?.copyWith(color: colour),
            ),
          ),
          Expanded(
            child: Column(
              crossAxisAlignment: CrossAxisAlignment.start,
              children: <Widget>[
                Text(
                  notice.title,
                  maxLines: 2,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall?.copyWith(color: colour),
                ),
                Text(
                  notice.authorityName,
                  maxLines: 1,
                  overflow: TextOverflow.ellipsis,
                  style: theme.textTheme.bodySmall
                      ?.copyWith(color: theme.colorScheme.outline),
                ),
              ],
            ),
          ),
        ],
      ),
    );
  }
}
