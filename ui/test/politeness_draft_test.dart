import 'dart:convert';
import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/filter/politeness_draft.dart';

void main() {
  String realConfig() => File('../config/politeness.json').readAsStringSync();

  group('the floors', () {
    test('four seconds between requests is refused', () {
      // CLAUDE.md calls this non-negotiable and D-16 still put it on a screen.
      // The screen's job is to stop it before the api has to, not instead of
      // the api - the server refuses it too, which is what makes it hold for a
      // curl.
      final PolitenessDraft draft = PolitenessDraft.parse(realConfig())
          .withValue('minimum_host_interval_seconds', 4);

      expect(draft.refusal, isNotNull);
      expect(draft.refusal, contains('5'));
    });

    test('five is allowed, because five is the floor and not the forbidden one',
        () {
      final PolitenessDraft draft = PolitenessDraft.parse(realConfig())
          .withValue('minimum_host_interval_seconds', 5);

      expect(draft.refusal, isNull);
    });

    test('robots.txt cannot be turned off', () {
      final PolitenessDraft draft =
          PolitenessDraft.parse(realConfig()).withValue('respect_robots', false);

      expect(draft.refusal, isNotNull);
    });

    test('a night multiplier below one is refused, because it inverts the rule',
        () {
      // Below one would poll harder during the maintenance window, which is the
      // opposite of what spec Bolum 6 asks for.
      final PolitenessDraft draft = PolitenessDraft.parse(realConfig())
          .withValue('night_interval_multiplier', 0);

      expect(draft.refusal, isNotNull);
    });

    test('the shipped configuration passes its own floors', () {
      expect(PolitenessDraft.parse(realConfig()).refusal, isNull);
    });
  });

  group('the document', () {
    test('keeps every note through an edit', () {
      // The notes are the reasons, and the screen shows them beside the fields.
      // Serialising only the known values would leave the numbers with nothing
      // explaining them.
      final Map<String, Object?> before =
          jsonDecode(realConfig()) as Map<String, Object?>;

      final PolitenessDraft draft = PolitenessDraft.parse(realConfig())
          .withValue('maximum_attempts', 4);

      final Map<String, Object?> after =
          jsonDecode(draft.toJson()) as Map<String, Object?>;

      expect(after.keys, containsAll(before.keys));
      expect(after['_comment'], before['_comment']);
      expect(after['_respect_robots'], before['_respect_robots']);
      expect(after['maximum_attempts'], 4);
    });

    test('carries a note for the fields that have one', () {
      final PolitenessDraft draft = PolitenessDraft.parse(realConfig());

      expect(draft.noteFor('_minimum_host_interval'), isNotNull);
      expect(draft.noteFor('_respect_robots'), isNotNull);
      expect(draft.noteFor('_night_window'), isNotNull);
      // And nothing invented for the ones that do not.
      expect(draft.noteFor('_nothing_like_this'), isNull);
    });

    test('an edit does not change the draft it came from', () {
      final PolitenessDraft first = PolitenessDraft.parse(realConfig());
      final PolitenessDraft second = first.withValue('maximum_attempts', 9);

      expect(first.maximumAttempts, isNot(9));
      expect(second.maximumAttempts, 9);
    });
  });
}
