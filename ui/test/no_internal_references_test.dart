import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/strings.dart';

/// Nothing the user reads may name an internal record.
///
/// Seven sentences on screen carried things like "(D-14)", "spec Bölüm 5",
/// "T-17" and "tr-date-patterns.json" until 21 August. Every one of them was a
/// real explanation with a reference number stapled to it, and to anybody who
/// has not read this repository they look like scaffolding somebody forgot to
/// take out - which is exactly how it was noticed.
///
/// The reasoning stays; the citation moves into the comment above the string.
/// A grep would have found these on any day since they were written, so the
/// point of a test is that nobody has to remember to grep.
void main() {
  final RegExp internal = RegExp(
    r'\bD-\d+'                       // decision records
    r'|\bT-\d+'                      // tickets
    r'|spec\s+B[oö]l[uü]m\s*\d+'     // spec sections
    r'|\bBolum\s*\d+'
    r'|CLAUDE\.md'
    // A file name, not a Dart property: `type.json` is a getter on an enum
    // and `config/keywords.json` is a path somebody should not be shown.
    r'|(?<![\w.])[a-z0-9_-]+\.json\b'
    r'|\braw_fetch\b|\bprocurement_notice\b|\bnotice_score\b', // table names
    caseSensitive: false,
  );

  /// Only the lines that end up on screen. A `//` or `///` comment is written
  /// for whoever opens the file next and should go on citing chapter and verse.
  bool isComment(String line) {
    final String t = line.trimLeft();

    return t.startsWith('//') || t.startsWith('*') || t.startsWith('/*');
  }

  /// The quoted parts of a line, which is the only part a user can read.
  ///
  /// Scanning whole lines instead reported `_authorityType.json` and
  /// `type.json` - a getter on an enum - as config files. Text shown to
  /// somebody is always inside a literal, so that is where to look.
  final RegExp literal = RegExp(r"'((?:[^'\\]|\\.)*)'");

  test('no user-facing string cites a decision record, ticket or config file',
      () {
    final List<String> offenders = <String>[];

    for (final FileSystemEntity entity
        in Directory('lib/src').listSync(recursive: true)) {
      if (entity is! File || !entity.path.endsWith('.dart')) continue;

      final List<String> lines = entity.readAsLinesSync();

      for (int i = 0; i < lines.length; i++) {
        if (isComment(lines[i])) continue;

        for (final RegExpMatch quoted in literal.allMatches(lines[i])) {
          final RegExpMatch? hit = internal.firstMatch(quoted.group(1)!);

          if (hit != null) {
            offenders.add('${entity.path}:${i + 1}  ${hit.group(0)}'
                '\n      ${lines[i].trim()}');
          }
        }
      }
    }

    expect(
      offenders,
      isEmpty,
      reason: 'These are shown to whoever uses the app. Keep the explanation, '
          'move the reference into the comment above it:\n${offenders.join('\n')}',
    );
  });

  test('the app is called what it is called', () {
    // The name reaches five platform manifests and one Dart constant, and they
    // drifted before: the Android label said "Procurement Radar" while the web
    // title said "procurement_radar", the raw package name.
    expect(Strings.appTitle, 'İhale Radar');

    for (final String path in <String>[
      'web/index.html',
      'web/manifest.json',
      'android/app/src/main/AndroidManifest.xml',
    ]) {
      final String text = File(path).readAsStringSync();

      expect(text, contains('İhale Radar'), reason: '$path does not name the app');
      expect(text, isNot(contains('procurement_radar')),
          reason: '$path still shows the package name to the user');
    }
  });
}
