import 'dart:convert';

/// One include term and what it is worth.
class IncludeTerm {
  const IncludeTerm({required this.term, required this.weight});

  final String term;
  final int weight;

  IncludeTerm copyWith({String? term, int? weight}) =>
      IncludeTerm(term: term ?? this.term, weight: weight ?? this.weight);
}

/// The keyword setting, opened for editing and closed again without losing
/// anything.
///
/// `keywords` is one json document and this screen understands three of its
/// keys. The rest - the okas prefix lists, the notes explaining why exclude
/// beats include, the two dead thresholds left over from T-13 - is carried
/// through untouched.
///
/// That is the whole reason for keeping `_raw`. Serialising only the fields the
/// screen knows about would quietly delete the okas configuration the first time
/// somebody moved a slider, and nothing would look wrong: the file would still
/// be valid json, the scorer would still load it, and the okas path (spec Bolum
/// 9 calls it far more reliable than words) would simply stop existing.
class KeywordDraft {
  const KeywordDraft._({
    required Map<String, Object?> raw,
    required this.include,
    required this.exclude,
    required this.notifyThreshold,
  }) : _raw = raw;

  factory KeywordDraft.parse(String json) {
    final Map<String, Object?> raw = jsonDecode(json) as Map<String, Object?>;

    return KeywordDraft._(
      raw: raw,
      include: <IncludeTerm>[
        for (final Object? entry in (raw['include'] as List<Object?>? ?? const <Object?>[]))
          if (entry is Map<String, Object?>)
            IncludeTerm(
              term: '${entry['term'] ?? ''}',
              // A term with no weight is not an error the screen should invent a
              // value for silently, but zero is what the scorer would give it.
              weight: (entry['weight'] as num?)?.toInt() ?? 0,
            ),
      ],
      exclude: <String>[
        for (final Object? entry in (raw['exclude'] as List<Object?>? ?? const <Object?>[]))
          '$entry',
      ],
      notifyThreshold: (raw['notify_threshold'] as num?)?.toInt() ?? 30,
    );
  }

  final Map<String, Object?> _raw;

  final List<IncludeTerm> include;
  final List<String> exclude;

  /// The only threshold that does anything (D-14, and the note in the file).
  final int notifyThreshold;

  KeywordDraft copyWith({
    List<IncludeTerm>? include,
    List<String>? exclude,
    int? notifyThreshold,
  }) =>
      KeywordDraft._(
        raw: _raw,
        include: include ?? this.include,
        exclude: exclude ?? this.exclude,
        notifyThreshold: notifyThreshold ?? this.notifyThreshold,
      );

  /// Back to json, with everything this screen does not edit still in it.
  ///
  /// Indented, because the value ends up in `setting_history` and in a diff, and
  /// one long line is not something a person can compare against the previous
  /// one when they are trying to undo a mistake.
  String toJson() {
    // Copied, not mutated: the draft is handed around and rebuilt while somebody
    // types, and a shared map underneath would make an abandoned edit stick.
    final Map<String, Object?> out = Map<String, Object?>.of(_raw);

    out['include'] = <Map<String, Object?>>[
      for (final IncludeTerm term in include)
        <String, Object?>{'term': term.term, 'weight': term.weight},
    ];
    out['exclude'] = exclude;
    out['notify_threshold'] = notifyThreshold;

    return const JsonEncoder.withIndent('  ').convert(out);
  }

  /// The keys this screen carries but does not show, so it can say so.
  List<String> get untouchedKeys => <String>[
        for (final String key in _raw.keys)
          if (key != 'include' && key != 'exclude' && key != 'notify_threshold')
            key,
      ];

  bool get isEmpty => include.isEmpty && exclude.isEmpty;
}
