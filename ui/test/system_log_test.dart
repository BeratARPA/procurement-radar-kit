@TestOn('vm')
library;

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/system/system_control_native.dart';
import 'package:procurement_radar/src/system/system_control.dart';

void main() {
  group('a Serilog line', () {
    test('is rendered with its values put back into the template', () {
      // Straight out of publish/logs. Serilog keeps the template and the values
      // apart, which is right for grepping and unreadable on a screen: without
      // this the panel would show "{Rows} archive row(s), {Seen} notice(s) seen"
      // exactly as written.
      const String line = '{"@t":"2026-08-08T17:42:04.5776820Z",'
          '"@mt":"Parse run: {Rows} archive row(s), {Seen} notice(s) seen, {New} new",'
          '"Rows":3,"Seen":12,"New":2,"Application":"ProcurementRadar.Worker"}';

      final LogLine? parsed = NativeSystemControl.parseLogLine(line);

      expect(parsed, isNotNull);
      expect(parsed!.text, 'Parse run: 3 archive row(s), 12 notice(s) seen, 2 new');
      expect(parsed.text, isNot(contains('{')));
    });

    test('with no level is Information, because Serilog leaves that one out', () {
      const String line = '{"@t":"2026-08-08T17:42:04.0000000Z","@mt":"Started"}';

      final LogLine parsed = NativeSystemControl.parseLogLine(line)!;

      expect(parsed.level, 'Information');
      expect(parsed.isProblem, isFalse);
    });

    test('a warning and an error both count as problems', () {
      for (final String level in <String>['Warning', 'Error', 'Fatal']) {
        final LogLine parsed = NativeSystemControl.parseLogLine(
          '{"@t":"2026-08-08T17:42:04.0000000Z","@l":"$level","@mt":"x"}',
        )!;

        expect(parsed.isProblem, isTrue, reason: level);
      }
    });

    test('the timestamp is turned into local time', () {
      final LogLine parsed = NativeSystemControl.parseLogLine(
        '{"@t":"2026-08-08T17:42:04.0000000Z","@mt":"x"}',
      )!;

      // Same rule as the deadline: the file holds UTC, the screen shows the
      // clock on the wall. A log panel three hours out is worse than none.
      expect(parsed.at!.isUtc, isFalse);
      expect(
        parsed.at!.toUtc(),
        DateTime.utc(2026, 8, 8, 17, 42, 4),
      );
    });

    test('an exception is kept, not dropped', () {
      final LogLine parsed = NativeSystemControl.parseLogLine(
        '{"@t":"2026-08-08T17:42:04.0000000Z","@l":"Error","@mt":"Fetch failed",'
        '"@x":"System.Net.Http.HttpRequestException: 403"}',
      )!;

      expect(parsed.text, contains('Fetch failed'));
      expect(parsed.text, contains('403'));
    });

    test('a line that is not json is skipped rather than thrown on', () {
      // Serilog can be interrupted mid-write when a process is killed, and a
      // half-written last line must not take the panel down.
      expect(NativeSystemControl.parseLogLine('{"@t":"2026-08-08T17:4'), isNull);
      expect(NativeSystemControl.parseLogLine(''), isNull);
    });

    test('a placeholder with no value is left alone rather than blanked', () {
      final LogLine parsed = NativeSystemControl.parseLogLine(
        '{"@t":"2026-08-08T17:42:04.0000000Z","@mt":"Content root: {ContentRoot}"}',
      )!;

      // Showing "Content root: " would read as an empty path. Leaving the name
      // visible says the value was not recorded.
      expect(parsed.text, 'Content root: {ContentRoot}');
    });
  });

  group('the checkout', () {
    test('is found by walking up from wherever the app was started', () {
      // flutter test runs from ui/, so the root is one level up. This is the
      // thing that decides whether every button on the screen does anything.
      final NativeSystemControl control = NativeSystemControl();

      expect(control.projectRoot, isNotNull);
      expect(control.isSupported, isTrue);
    });

    test('refuses a directory that is not one', () async {
      final NativeSystemControl control = NativeSystemControl();

      expect(await control.useProjectRoot(r'C:\Windows'), isFalse);
    });
  });
}
