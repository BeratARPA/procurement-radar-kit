import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:intl/intl.dart';
import 'package:procurement_radar/src/app.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

import 'support/fake_api.dart';

void main() {
  setUpAll(() async {
    await initializeDateFormatting('tr_TR');
    Intl.defaultLocale = 'tr_TR';
  });

  late FakeApiAdapter adapter;

  setUp(() {
    // The remembered theme is read through the real shared_preferences, which
    // has no platform behind it in a test. This is the implementation the
    // package ships for exactly that, so the code under test is the code that
    // ships rather than a branch that only exists for tests.
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();

    adapter = FakeApiAdapter(<String, String>{
      '/api/notices': fixture('notices.json'),
      '/api/notices/by-id/20042': fixture('notice_detail.json'),
      '/api/health': fixture('health.json'),
    });
  });

  Future<void> pumpApp(WidgetTester tester, {required Size size}) async {
    tester.view.physicalSize = size;
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(
      withFakeApi(adapter: adapter, child: const RadarApp()),
    );
    await tester.pumpAndSettle();
  }

  group('the list', () {
    testWidgets('shows what the api returned, through the generated models',
        (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(500, 900));

      // From test/fixtures/notices.json, captured from the running api. If the
      // schema and the generated model drift apart, fromJson throws here rather
      // than in front of somebody.
      expect(find.textContaining('Dijital ve Kurumsal Arşiv'), findsWidgets);
      expect(
        find.textContaining('BAYRAMPAŞA BELEDİYE BAŞKANLIĞI'),
        findsWidgets,
      );
    });

    testWidgets('sends the token on every request', (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(500, 900));

      expect(adapter.requests, isNotEmpty);
      expect(
        adapter.requests.every(
          (dynamic request) => request.headers['X-Api-Token'] == 'test-token',
        ),
        isTrue,
      );
    });

    testWidgets('says so when the token is refused rather than showing nothing',
        (WidgetTester tester) async {
      adapter.status = 401;

      await pumpApp(tester, size: const Size(500, 900));

      // An empty list here would read as an empty archive, which is the one
      // thing this screen must never imply.
      expect(find.textContaining('Anahtar kabul edilmedi'), findsOneWidget);
    });
  });

  group('the layout', () {
    testWidgets('puts navigation at the bottom on a narrow screen',
        (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(500, 900));

      expect(find.byType(NavigationBar), findsOneWidget);
      expect(find.byType(NavigationRail), findsNothing);
    });

    testWidgets('puts navigation down the side on a wide screen',
        (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(1200, 900));

      expect(find.byType(NavigationRail), findsOneWidget);
      expect(find.byType(NavigationBar), findsNothing);
    });

    testWidgets('crosses the breakpoint without losing the screen',
        (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(500, 900));
      expect(find.byType(NavigationBar), findsOneWidget);

      tester.view.physicalSize = const Size(1200, 900);
      await tester.pumpAndSettle();

      expect(find.byType(NavigationRail), findsOneWidget);
      expect(find.textContaining('Dijital ve Kurumsal Arşiv'), findsWidgets);
    });
  });

  group('the detail', () {
    testWidgets('opens from a row and can be navigated back from',
        (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(500, 900));

      await tester.tap(find.textContaining('Dijital ve Kurumsal Arşiv').first);
      await tester.pumpAndSettle();

      // The row carries an id and no IKN, and the detail is fetched by that id.
      // 814 of the archive's first 924 notices have no IKN at all, so a screen
      // keyed on one would open almost none of them.
      expect(
        adapter.requests.any(
          (dynamic request) => request.uri.path == '/api/notices/by-id/20042',
        ),
        isTrue,
      );
      expect(find.text('İdare'), findsOneWidget);
      expect(find.text('Kararınız'), findsOneWidget);
    });

    testWidgets('offers a verdict for every status but the one it starts in',
        (WidgetTester tester) async {
      await pumpApp(tester, size: const Size(500, 900));

      await tester.tap(find.textContaining('Dijital ve Kurumsal Arşiv').first);
      await tester.pumpAndSettle();

      expect(find.widgetWithText(ChoiceChip, 'İncelendi'), findsOneWidget);
      expect(find.widgetWithText(ChoiceChip, 'Teklif verildi'), findsOneWidget);
      expect(find.widgetWithText(ChoiceChip, 'İlgilenmiyoruz'), findsOneWidget);
      expect(find.widgetWithText(ChoiceChip, 'Kaçırıldı'), findsOneWidget);
    });

    testWidgets('a verdict is sent to the api as the schema spells it',
        (WidgetTester tester) async {
      adapter.routes['/api/notices/20042/status'] = '';

      await pumpApp(tester, size: const Size(500, 900));

      await tester.tap(find.textContaining('Dijital ve Kurumsal Arşiv').first);
      await tester.pumpAndSettle();

      await tester.tap(find.widgetWithText(ChoiceChip, 'İlgilenmiyoruz'));
      await tester.pumpAndSettle();

      final Iterable<dynamic> posts = adapter.requests.where(
        (dynamic request) => request.method == 'POST',
      );

      expect(posts, isNotEmpty);
      expect(posts.first.uri.path, '/api/notices/20042/status');
      // not_relevant, with the underscore, is what procurement_notice.status
      // stores. The api parses it back by stripping the underscore; sending the
      // enum spelling would land as a 400 nobody would see.
      expect(posts.first.data['status'], 'not_relevant');
    });
  });
}
