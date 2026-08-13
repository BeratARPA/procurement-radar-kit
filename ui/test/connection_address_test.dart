import 'package:flutter/foundation.dart';
import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/screens/connection_form.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

import 'support/fake_api.dart';

/// The address field, and the default that cannot work on a phone.
///
/// `http://localhost:60080` is right on the machine doing the collecting and
/// means the device itself everywhere else. On the phone that showed up as "the
/// api cannot be reached" - which was true and unhelpful, because the field
/// arrives already filled in and looks correct.
void main() {
  setUp(() {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();
  });

  /// Runs the form on one platform and says whether the address was refused.
  ///
  /// The override is put back inside the test body, not in a tearDown: the test
  /// framework asserts that foundation debug variables are unset before tearDown
  /// runs, so resetting there fails every test that sets one.
  Future<String?> errorFor(
    WidgetTester tester,
    String address, {
    required TargetPlatform on,
  }) async {
    debugDefaultTargetPlatformOverride = on;

    await tester.pumpWidget(
      withFakeApi(
        adapter: FakeApiAdapter(<String, String>{}),
        child: MaterialApp(
          home: Scaffold(
            body: ConnectionForm(submitLabel: 'Bağlan', onSaved: () {}),
          ),
        ),
      ),
    );
    await tester.pumpAndSettle();

    await tester.enterText(find.byType(TextFormField).first, address);
    await tester.enterText(find.byType(TextFormField).last, 'x');
    await tester.tap(find.widgetWithText(FilledButton, 'Bağlan'));
    await tester.pumpAndSettle();

    final bool refused =
        find.textContaining('localhost bu cihazın kendisi').evaluate().isNotEmpty;

    debugDefaultTargetPlatformOverride = null;

    return refused ? 'refused' : null;
  }

  group('on a phone', () {
    testWidgets('localhost is refused, with a sentence saying why',
        (WidgetTester tester) async {
      expect(
        await errorFor(tester, 'http://localhost:60080', on: TargetPlatform.android),
        'refused',
      );
    });

    testWidgets('so is 127.0.0.1, which is the same claim spelled differently',
        (WidgetTester tester) async {
      expect(
        await errorFor(tester, 'http://127.0.0.1:60080', on: TargetPlatform.iOS),
        'refused',
      );
    });

    testWidgets('a home-network address is accepted', (WidgetTester tester) async {
      expect(
        await errorFor(tester, 'http://192.168.1.219:60080', on: TargetPlatform.android),
        isNull,
      );
    });
  });

  group('on the machine doing the collecting', () {
    testWidgets('localhost is exactly right and must not be refused',
        (WidgetTester tester) async {
      // The half that would hurt more: a rule that locks the desktop build out
      // of its own api would be worse than the problem it was added for.
      expect(
        await errorFor(tester, 'http://localhost:60080', on: TargetPlatform.windows),
        isNull,
      );
    });

    testWidgets('and so is a home-network address', (WidgetTester tester) async {
      expect(
        await errorFor(tester, 'http://192.168.1.219:60080', on: TargetPlatform.macOS),
        isNull,
      );
    });
  });
}
