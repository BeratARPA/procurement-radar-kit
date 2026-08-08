import 'dart:convert';

import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';

import '../../api/export.dart';
import '../formatting.dart';
import '../sources/source_providers.dart';
import '../strings.dart';

/// Opens the editor for a source, or for a new one when [source] is null.
Future<void> openSourceEditor(
  BuildContext context,
  WidgetRef ref,
  Source? source,
) async {
  ref.read(sourceCheckProvider.notifier).clear();

  await showDialog<void>(
    context: context,
    builder: (BuildContext context) => Dialog(
      insetPadding: const EdgeInsets.all(24),
      child: ConstrainedBox(
        constraints: const BoxConstraints(maxWidth: 980, maxHeight: 760),
        child: SourceEditor(source: source),
      ),
    ),
  );
}

/// Adding or editing a source, with the selectors testable beside them.
///
/// spec Bolum 5 lists five steps for adding a source and step 4 - write the
/// selectors and find out whether they are right - had no answer short of
/// switching the source on and waiting a day. The button on the right answers it
/// against the page already in the archive, so nothing is fetched and the
/// authority is not asked anything.
class SourceEditor extends ConsumerStatefulWidget {
  const SourceEditor({required this.source, super.key});

  final Source? source;

  @override
  ConsumerState<SourceEditor> createState() => _SourceEditorState();
}

class _SourceEditorState extends ConsumerState<SourceEditor> {
  final GlobalKey<FormState> _form = GlobalKey<FormState>();

  late final Map<String, TextEditingController> _fields;
  late final Map<String, TextEditingController> _selectors;
  final TextEditingController _html = TextEditingController();

  late String _authorityType;
  late String _adapterKey;
  bool _saving = false;
  String? _error;

  bool get _isNew => widget.source == null;

  @override
  void initState() {
    super.initState();

    final Source? source = widget.source;
    final Map<String, Object?> config = _decodeConfig(source?.adapterConfig);

    _fields = <String, TextEditingController>{
      'authorityName': TextEditingController(text: source?.authorityName ?? ''),
      'province': TextEditingController(text: source?.province ?? 'Ankara'),
      'district': TextEditingController(text: source?.district ?? ''),
      'url': TextEditingController(text: source?.url ?? ''),
      'pollIntervalMinutes':
          TextEditingController(text: '${source?.pollIntervalMinutes ?? 15}'),
      'notes': TextEditingController(text: source?.notes ?? ''),
    };

    _selectors = <String, TextEditingController>{
      for (final String key in const <String>[
        'row_selector',
        'title_selector',
        'link_selector',
        'date_selector',
        'time_selector',
        'amount_selector',
        'document_selector',
        'base_url',
      ])
        key: TextEditingController(text: '${config[key] ?? ''}'),
    };

    _authorityType = _toSnake(source?.authorityType.name ?? 'municipality');
    _adapterKey = source?.adapterKey ?? 'generic_table';
  }

  @override
  void dispose() {
    for (final TextEditingController controller in _fields.values) {
      controller.dispose();
    }
    for (final TextEditingController controller in _selectors.values) {
      controller.dispose();
    }
    _html.dispose();
    super.dispose();
  }

  static Map<String, Object?> _decodeConfig(String? json) {
    if (json == null || json.trim().isEmpty) return <String, Object?>{};

    try {
      final Object? decoded = jsonDecode(json);

      return decoded is Map<String, Object?> ? decoded : <String, Object?>{};
    } on FormatException {
      return <String, Object?>{};
    }
  }

  /// The api spells authority types the way the schema stores them.
  static String _toSnake(String pascalOrCamel) => pascalOrCamel
      .replaceAllMapped(RegExp('([a-z0-9])([A-Z])'),
          (Match m) => '${m[1]}_${m[2]}')
      .toLowerCase();

  String _configJson() {
    final Map<String, Object?> config = <String, Object?>{};

    for (final MapEntry<String, TextEditingController> entry in _selectors.entries) {
      final String value = entry.value.text.trim();

      // Empty means "not configured", which is a null and not an empty string:
      // an empty selector would match nothing and read as a broken one.
      config[entry.key] = value.isEmpty ? null : value;
    }

    return const JsonEncoder.withIndent('  ').convert(config);
  }

  SourceDefinitionRequest _definition() => SourceDefinitionRequest(
        authorityName: _fields['authorityName']!.text.trim(),
        authorityType: _authorityType,
        province: _fields['province']!.text.trim(),
        district: _fields['district']!.text.trim().isEmpty
            ? null
            : _fields['district']!.text.trim(),
        url: _fields['url']!.text.trim(),
        adapterKey: _adapterKey,
        adapterConfig: _configJson(),
        pollIntervalMinutes:
            int.tryParse(_fields['pollIntervalMinutes']!.text) ?? 15,
        notes: _fields['notes']!.text.trim().isEmpty
            ? null
            : _fields['notes']!.text.trim(),
      );

  Future<void> _check() {
    return ref.read(sourceCheckProvider.notifier).check(
          adapterConfig: _configJson(),
          sourceId: widget.source?.id,
          html: _html.text.trim().isEmpty ? null : _html.text,
          url: _fields['url']!.text.trim().isEmpty
              ? null
              : _fields['url']!.text.trim(),
        );
  }

  Future<void> _save() async {
    if (!(_form.currentState?.validate() ?? false)) return;

    setState(() {
      _saving = true;
      _error = null;
    });

    try {
      if (_isNew) {
        await saveSource(ref, _definition());
      } else {
        await updateSource(ref, widget.source!.id ?? 0, _definition());
      }

      if (mounted) Navigator.of(context).pop();
    } on Object catch (error) {
      // The api says which field it refused and why. Shown as written.
      if (mounted) setState(() => _error = '$error');
    } finally {
      if (mounted) setState(() => _saving = false);
    }
  }

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    return Column(
      crossAxisAlignment: CrossAxisAlignment.stretch,
      children: <Widget>[
        Padding(
          padding: const EdgeInsets.fromLTRB(20, 16, 12, 8),
          child: Row(
            children: <Widget>[
              Text(_isNew ? Strings.addSource : Strings.editSource,
                  style: theme.textTheme.titleMedium),
              const Spacer(),
              IconButton(
                onPressed: () => Navigator.of(context).pop(),
                icon: const Icon(Icons.close),
              ),
            ],
          ),
        ),
        const Divider(height: 1),
        Expanded(
          child: Row(
            crossAxisAlignment: CrossAxisAlignment.start,
            children: <Widget>[
              Expanded(
                flex: 3,
                child: Form(
                  key: _form,
                  child: ListView(
                    padding: const EdgeInsets.all(20),
                    children: <Widget>[
                      if (_isNew)
                        Padding(
                          padding: const EdgeInsets.only(bottom: 16),
                          child: Text(
                            Strings.newSourceIsOff,
                            style: theme.textTheme.bodySmall?.copyWith(
                                color: theme.colorScheme.onSurfaceVariant),
                          ),
                        ),
                      _text('authorityName', Strings.authorityName, required: true),
                      Row(
                        children: <Widget>[
                          Expanded(child: _text('province', Strings.province, required: true)),
                          const SizedBox(width: 12),
                          Expanded(child: _text('district', Strings.district)),
                        ],
                      ),
                      _text('url', Strings.sourceUrlLabel,
                          required: true, validator: _url),
                      Row(
                        children: <Widget>[
                          Expanded(child: _authorityTypeField()),
                          const SizedBox(width: 12),
                          Expanded(child: _adapterField()),
                        ],
                      ),
                      _text('pollIntervalMinutes', Strings.pollInterval),
                      _text('notes', Strings.notes, lines: 3, help: Strings.notesHelp),
                      const Divider(height: 32),
                      Text(Strings.selectors, style: theme.textTheme.titleSmall),
                      const SizedBox(height: 12),
                      _selector('row_selector', Strings.rowSelector),
                      _selector('title_selector', Strings.titleSelector),
                      _selector('link_selector', Strings.linkSelector),
                      _selector('date_selector', Strings.dateSelector),
                      _selector('time_selector', Strings.timeSelector),
                      _selector('amount_selector', Strings.amountSelector),
                      _selector('document_selector', Strings.documentSelector),
                      _selector('base_url', Strings.baseUrlLabel),
                      if (_isNew) ...<Widget>[
                        const Divider(height: 32),
                        TextField(
                          controller: _html,
                          maxLines: 4,
                          decoration: const InputDecoration(
                            labelText: Strings.pasteHtml,
                            helperText: Strings.pasteHtmlHelp,
                            helperMaxLines: 3,
                            border: OutlineInputBorder(),
                          ),
                        ),
                      ],
                    ],
                  ),
                ),
              ),
              const VerticalDivider(width: 1),
              Expanded(flex: 2, child: _CheckPanel(onCheck: _check)),
            ],
          ),
        ),
        const Divider(height: 1),
        Padding(
          padding: const EdgeInsets.all(16),
          child: Row(
            children: <Widget>[
              if (_error != null)
                Expanded(
                  child: Text(
                    _error!,
                    maxLines: 3,
                    overflow: TextOverflow.ellipsis,
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: theme.colorScheme.error),
                  ),
                )
              else
                const Spacer(),
              const SizedBox(width: 12),
              TextButton(
                onPressed: () => Navigator.of(context).pop(),
                child: const Text(Strings.cancel),
              ),
              const SizedBox(width: 8),
              FilledButton(
                onPressed: _saving ? null : _save,
                child: _saving
                    ? const SizedBox(
                        width: 16,
                        height: 16,
                        child: CircularProgressIndicator(strokeWidth: 2))
                    : const Text(Strings.save),
              ),
            ],
          ),
        ),
      ],
    );
  }

  Widget _text(
    String key,
    String label, {
    bool required = false,
    int lines = 1,
    String? help,
    String? Function(String?)? validator,
  }) =>
      Padding(
        padding: const EdgeInsets.only(bottom: 14),
        child: TextFormField(
          controller: _fields[key],
          maxLines: lines,
          decoration: InputDecoration(
            labelText: label,
            helperText: help,
            helperMaxLines: 3,
            isDense: true,
            border: const OutlineInputBorder(),
          ),
          validator: validator ??
              (required
                  ? (String? value) =>
                      (value ?? '').trim().isEmpty ? label : null
                  : null),
        ),
      );

  static String? _url(String? value) {
    final Uri? uri = Uri.tryParse((value ?? '').trim());

    return uri != null && (uri.scheme == 'http' || uri.scheme == 'https')
        ? null
        : Strings.addressNotAbsolute;
  }

  Widget _selector(String key, String label) => Padding(
        padding: const EdgeInsets.only(bottom: 10),
        child: TextFormField(
          controller: _selectors[key],
          decoration: InputDecoration(
            labelText: label,
            isDense: true,
            border: const OutlineInputBorder(),
          ),
        ),
      );

  Widget _authorityTypeField() => DropdownButtonFormField<String>(
        initialValue: _authorityType,
        decoration: const InputDecoration(
          labelText: Strings.authorityTypeLabel,
          isDense: true,
          border: OutlineInputBorder(),
        ),
        items: const <DropdownMenuItem<String>>[
          DropdownMenuItem<String>(value: 'university', child: Text('university')),
          DropdownMenuItem<String>(value: 'municipality', child: Text('municipality')),
          DropdownMenuItem<String>(
              value: 'provincial_directorate', child: Text('provincial_directorate')),
          DropdownMenuItem<String>(value: 'hospital', child: Text('hospital')),
          DropdownMenuItem<String>(value: 'soe_subsidiary', child: Text('soe_subsidiary')),
          DropdownMenuItem<String>(value: 'ekap', child: Text('ekap')),
        ],
        onChanged: (String? value) =>
            setState(() => _authorityType = value ?? _authorityType),
      );

  Widget _adapterField() => DropdownButtonFormField<String>(
        initialValue: _adapterKey,
        decoration: const InputDecoration(
          labelText: Strings.adapterKeyLabel,
          isDense: true,
          border: OutlineInputBorder(),
        ),
        items: const <DropdownMenuItem<String>>[
          DropdownMenuItem<String>(value: 'generic_table', child: Text('generic_table')),
          DropdownMenuItem<String>(value: 'json_list', child: Text('json_list')),
          DropdownMenuItem<String>(value: 'ekap_tender', child: Text('ekap_tender')),
          DropdownMenuItem<String>(
              value: 'ekap_direct_procurement', child: Text('ekap_direct_procurement')),
        ],
        onChanged: (String? value) =>
            setState(() => _adapterKey = value ?? _adapterKey),
      );
}

class _CheckPanel extends ConsumerWidget {
  const _CheckPanel({required this.onCheck});

  final Future<void> Function() onCheck;

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    final ThemeData theme = Theme.of(context);
    final AsyncValue<SourceCheckResponse>? check = ref.watch(sourceCheckProvider);

    return ListView(
      padding: const EdgeInsets.all(20),
      children: <Widget>[
        FilledButton.tonalIcon(
          onPressed: check is AsyncLoading ? null : onCheck,
          icon: const Icon(Icons.play_arrow_outlined),
          label: const Text(Strings.testAgainstArchive),
        ),
        const SizedBox(height: 16),
        if (check == null)
          Text(
            Strings.previewIdle,
            style: theme.textTheme.bodySmall
                ?.copyWith(color: theme.colorScheme.onSurfaceVariant),
          )
        else
          check.when(
            loading: () => const Center(child: CircularProgressIndicator()),
            error: (Object error, StackTrace stackTrace) => Text(
              '$error',
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.error),
            ),
            data: (SourceCheckResponse answer) => _Result(answer: answer),
          ),
      ],
    );
  }
}

class _Result extends StatelessWidget {
  const _Result({required this.answer});

  final SourceCheckResponse answer;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);

    if (answer.problemCode != null) {
      return Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text(
            Strings.checkProblem(answer.problemCode!),
            style: theme.textTheme.bodyMedium
                ?.copyWith(color: theme.colorScheme.error),
          ),
          if (answer.problem != null) ...<Widget>[
            const SizedBox(height: 8),
            SelectableText(
              answer.problem!,
              style: theme.textTheme.bodySmall?.copyWith(
                fontFamily: 'monospace',
                color: theme.colorScheme.onSurfaceVariant,
              ),
            ),
          ],
        ],
      );
    }

    return Column(
      crossAxisAlignment: CrossAxisAlignment.start,
      children: <Widget>[
        Text(
          <String>[
            answer.source == 'archive' ? Strings.fromArchive : Strings.fromPasted,
            if (answer.fetchedAt != null) formatMoment(answer.fetchedAt),
            if (answer.charSet != null) answer.charSet!,
          ].join(' · '),
          style: theme.textTheme.bodySmall
              ?.copyWith(color: theme.colorScheme.outline),
        ),
        const SizedBox(height: 12),
        Row(
          children: <Widget>[
            _Count(value: answer.noticeCount, label: Strings.rowsFound),
            const SizedBox(width: 12),
            _Count(
              value: answer.withDeadline,
              label: Strings.withDeadlineLabel,
              // The field spec Bolum 7 calls the most important one. Zero of it
              // is worth seeing in red even when the rows came out fine.
              alarming: answer.noticeCount > 0 && answer.withDeadline == 0,
            ),
            const SizedBox(width: 12),
            _Count(value: answer.withDocuments, label: Strings.withDocumentsLabel),
          ],
        ),
        for (final String warning in answer.warnings) ...<Widget>[
          const SizedBox(height: 14),
          Container(
            padding: const EdgeInsets.all(12),
            decoration: BoxDecoration(
              color: theme.colorScheme.tertiaryContainer,
              borderRadius: BorderRadius.circular(8),
            ),
            child: Text(
              Strings.checkWarning(warning),
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onTertiaryContainer),
            ),
          ),
        ],
        if (answer.sample.isNotEmpty) ...<Widget>[
          const Divider(height: 28),
          for (final CheckedNotice notice in answer.sample)
            Padding(
              padding: const EdgeInsets.only(bottom: 10),
              child: Column(
                crossAxisAlignment: CrossAxisAlignment.start,
                children: <Widget>[
                  Text(notice.title,
                      maxLines: 2,
                      overflow: TextOverflow.ellipsis,
                      style: theme.textTheme.bodySmall),
                  Text(
                    <String>[
                      formatDeadline(notice.bidDeadlineAt),
                      if (notice.documentCount > 0) '${notice.documentCount} dosya',
                    ].join(' · '),
                    style: theme.textTheme.bodySmall
                        ?.copyWith(color: theme.colorScheme.outline),
                  ),
                ],
              ),
            ),
        ],
      ],
    );
  }
}

class _Count extends StatelessWidget {
  const _Count({required this.value, required this.label, this.alarming = false});

  final int value;
  final String label;
  final bool alarming;

  @override
  Widget build(BuildContext context) {
    final ThemeData theme = Theme.of(context);
    final Color colour =
        alarming ? theme.colorScheme.error : theme.colorScheme.onSurface;

    return Expanded(
      child: Column(
        crossAxisAlignment: CrossAxisAlignment.start,
        children: <Widget>[
          Text('$value',
              style: theme.textTheme.headlineSmall
                  ?.copyWith(color: colour, fontWeight: FontWeight.w700)),
          Text(label,
              style: theme.textTheme.bodySmall
                  ?.copyWith(color: theme.colorScheme.onSurfaceVariant)),
        ],
      ),
    );
  }
}
