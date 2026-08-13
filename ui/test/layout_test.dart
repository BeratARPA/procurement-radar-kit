import 'package:flutter/material.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:go_router/go_router.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:intl/intl.dart';
import 'package:procurement_radar/src/app.dart';
import 'package:procurement_radar/src/router.dart';
import 'package:procurement_radar/src/screens/shell.dart';
import 'package:shared_preferences_platform_interface/in_memory_shared_preferences_async.dart';
import 'package:shared_preferences_platform_interface/shared_preferences_async_platform_interface.dart';

import 'support/fake_api.dart';

/// Every screen at every width somebody will actually hold.
///
/// A responsive layout is a claim, and until now it was only checked at two
/// widths on one screen. Flutter reports an overflowing Row, Column or Text as
/// an exception during layout, which `tester.takeException()` returns and a test
/// otherwise fails on - so driving each screen at each width is a real check and
/// not a screenshot somebody has to look at.
///
/// The widths are devices, not round numbers:
///
/// | 320  | iPhone SE 1st gen, the narrowest thing still in use   |
/// | 360  | the commonest Android width in Turkey                 |
/// | 390  | iPhone 14/15                                          |
/// | 412  | Pixel, larger Android                                 |
/// | 600  | Material's compact/medium boundary - the rail appears  |
/// | 768  | iPad portrait                                          |
/// | 840  | Material's medium/expanded boundary - the rail extends  |
/// | 1024 | iPad landscape, small laptop                           |
/// | 1440 | the desktop build's usual window                       |
/// | 1920 | a maximised window on the machine this runs on          |
void main() {
  setUpAll(() async {
    await initializeDateFormatting('tr_TR');
    Intl.defaultLocale = 'tr_TR';
  });

  const List<double> widths = <double>[
    320, 360, 390, 412, 600, 768, 840, 1024, 1440, 1920,
  ];

  late FakeApiAdapter adapter;

  setUp(() {
    SharedPreferencesAsyncPlatform.instance = InMemorySharedPreferencesAsync.empty();

    adapter = FakeApiAdapter(<String, String>{
      '/api/notices': fixture('notices.json'),
      '/api/notices/by-id/20042': fixture('notice_detail.json'),
      '/api/health': fixture('health.json'),
      '/api/settings': fixture('settings.json'),
      '/api/settings/keywords': fixture('setting_keywords.json'),
      '/api/settings/politeness': fixture('setting_politeness.json'),
      '/api/sources/all': fixture('sources.json'),
      '/api/awards': fixture('awards.json'),
      '/api/awards/pending': fixture('awards_pending.json'),
    });
  });

  /// Mounts the app at one width and returns once it has stopped moving.
  Future<void> pumpAt(WidgetTester tester, double width) async {
    // Tall enough that a short viewport is not what is being measured. Vertical
    // overflow is what scrolling is for; horizontal overflow is the bug.
    tester.view.physicalSize = Size(width, 1000);
    tester.view.devicePixelRatio = 1;
    addTearDown(tester.view.reset);

    await tester.pumpWidget(withFakeApi(adapter: adapter, child: const RadarApp()));
    await tester.pumpAndSettle();
  }

  /// Taps a destination by label, wherever this width put it.
  Future<bool> goTo(WidgetTester tester, String label) async {
    final Finder destination = find.descendant(
      of: find.byType(NavigationBar).evaluate().isNotEmpty
          ? find.byType(NavigationBar)
          : find.byType(NavigationRail),
      matching: find.text(label),
    );

    if (destination.evaluate().isEmpty) return false;

    await tester.tap(destination.first, warnIfMissed: false);
    await tester.pumpAndSettle();

    return true;
  }

  group('nothing overflows sideways', () {
    for (final double width in widths) {
      testWidgets('at ${width.toInt()} px, on the screen you land on',
          (WidgetTester tester) async {
        await pumpAt(tester, width);

        expect(tester.takeException(), isNull);
        expect(find.textContaining('Dijital ve Kurumsal Arşiv'), findsWidgets);
      });

      testWidgets('at ${width.toInt()} px, on every screen reachable from it',
          (WidgetTester tester) async {
        await pumpAt(tester, width);

        for (final String label in const <String>[
          'Filtre',
          'Kaynaklar',
          'Sonuçlar',
          'Durum',
          'Ayarlar',
          'İlanlar',
        ]) {
          if (!await goTo(tester, label)) continue;

          expect(
            tester.takeException(),
            isNull,
            reason: '$label overflowed at ${width.toInt()} px',
          );
        }
      });

      testWidgets('at ${width.toInt()} px, inside a notice',
          (WidgetTester tester) async {
        await pumpAt(tester, width);

        await tester.tap(find.textContaining('Dijital ve Kurumsal Arşiv').first);
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
        expect(find.text('İdare'), findsOneWidget);
      });

      testWidgets('at ${width.toInt()} px, on a screen with no destination here',
          (WidgetTester tester) async {
        // Filtre and Kaynaklar are dropped from the bottom bar below 600, so
        // tapping cannot reach them and the sweep above skips them - which is
        // exactly the width where a keyword list or a table of CSS selectors is
        // most likely to overflow. The shell says the routes still exist and a
        // link still opens, so this opens them the way a link would.
        await pumpAt(tester, width);

        final GoRouter router =
            GoRouter.of(tester.element(find.byType(Scaffold).first));

        for (final String path in const <String>[
          Routes.filter,
          Routes.sources,
          Routes.awards,
          Routes.status,
          Routes.settings,
        ]) {
          router.go(path);
          await tester.pumpAndSettle();

          expect(
            tester.takeException(),
            isNull,
            reason: '$path overflowed at ${width.toInt()} px',
          );
        }
      });
    }
  });

  group('nothing is quietly cut off either', () {
    // The overflow sweep above passes on a layout nobody can use. A Row inside a
    // horizontal SingleChildScrollView never overflows - it scrolls - so the
    // notice filters sat off the right edge of a narrow window with no scrollbar
    // and no fade, and three of the five were simply invisible. An exception
    // check cannot see that. Asking whether a control is inside the window can.

    /// True when every pixel of the widget is inside the viewport.
    bool fullyVisible(WidgetTester tester, Finder finder) {
      final Size screen = tester.view.physicalSize / tester.view.devicePixelRatio;
      final Rect box = tester.getRect(finder);

      return box.left >= -0.5 && box.right <= screen.width + 0.5;
    }

    for (final double width in <double>[320, 360, 390, 412, 600, 768]) {
      testWidgets('at ${width.toInt()} px every notice filter is reachable',
          (WidgetTester tester) async {
        await pumpAt(tester, width);

        // Narrow enough that the controls live behind a button. The button is
        // what has to be visible, and it has to say how many are on.
        final Finder button = find.widgetWithIcon(FilledButton, Icons.tune);

        expect(button, findsOneWidget, reason: 'no way in at ${width.toInt()} px');
        expect(fullyVisible(tester, button), isTrue);

        await tester.tap(button);
        await tester.pumpAndSettle();

        // Every group, in a sheet, on one screen.
        for (final String label in const <String>[
          'Kapsam',
          'Puan',
          'Son teklife kalan',
          'Sırala',
        ]) {
          expect(
            find.text(label),
            findsOneWidget,
            reason: '$label missing from the sheet at ${width.toInt()} px',
          );
          expect(
            fullyVisible(tester, find.text(label)),
            isTrue,
            reason: '$label cut off at ${width.toInt()} px',
          );
        }

        expect(tester.takeException(), isNull);
      });
    }

    for (final double width in <double>[1024, 1440, 1920]) {
      testWidgets('at ${width.toInt()} px they are inline and none is cut off',
          (WidgetTester tester) async {
        await pumpAt(tester, width);

        expect(find.widgetWithIcon(FilledButton, Icons.tune), findsNothing);

        for (final String label in const <String>[
          'Kapsam',
          'Puan',
          'Son teklife kalan',
          'Sırala',
        ]) {
          expect(find.text(label), findsOneWidget);
          expect(
            fullyVisible(tester, find.text(label)),
            isTrue,
            reason: '$label cut off at ${width.toInt()} px',
          );
        }
      });
    }

    for (final double width in <double>[320, 390, 600, 1440]) {
      testWidgets('at ${width.toInt()} px the switch-off reasons all fit',
          (WidgetTester tester) async {
        // Five reasons with a sentence each, in a dialog. The first version
        // asked for a flat 460 px of width, which overflows a 320 px phone by
        // two hundred - and a source cannot be switched off from a dialog whose
        // buttons are past the edge of the screen.
        await pumpAt(tester, width);

        final GoRouter router =
            GoRouter.of(tester.element(find.byType(Scaffold).first));
        router.go(Routes.sources);
        await tester.pumpAndSettle();

        // The switch on the first switched-off row. Tapping it opens the dialog
        // rather than switching anything, which is the point of the change.
        await tester.tap(find.byType(Switch).first);
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
        expect(find.text('Site açılmıyor'), findsOneWidget);
        expect(find.text('Başka kaynak zaten kapsıyor'), findsOneWidget);

        // Nothing is sent until one is chosen.
        final Finder confirm = find.widgetWithText(FilledButton, 'Kapat');
        expect(confirm, findsOneWidget);
        expect(tester.widget<FilledButton>(confirm).onPressed, isNull);
      });
    }

    for (final double width in <double>[360, 600, 900, 1440]) {
      testWidgets('at ${width.toInt()} px the source editor shows its whole form',
          (WidgetTester tester) async {
        // Never opened before, and it was the worst of them: the form was pinned
        // to 62% of the dialog inside a second scroll view, so the notes field
        // was cut through the middle and the eight selector fields under it were
        // unreachable.
        await pumpAt(tester, width);

        final GoRouter router =
            GoRouter.of(tester.element(find.byType(Scaffold).first));
        router.go(Routes.sources);
        await tester.pumpAndSettle();

        // A row opens the editor by being tapped; there is no edit icon.
        await tester.tap(find.byType(ListTile).first);
        await tester.pumpAndSettle();

        expect(tester.takeException(), isNull);
        expect(find.byType(Dialog), findsOneWidget);

        // The end of the form, reached by scrolling the one scroll view the
        // dialog now has. If it cannot be scrolled to, the form is trapped -
        // which it was, behind a fixed-height box inside a second scroller.
        //
        // The dialog's own scrollable, not `.last`: every TextField carries a
        // horizontal one of its own, and dragging that scrolls a word of text
        // rather than the form.
        await tester.scrollUntilVisible(
          find.text('Kayıtlı sayfaya karşı dene'),
          300,
          scrollable: find
              .descendant(of: find.byType(Dialog), matching: find.byType(Scrollable))
              .first,
        );
        await tester.pumpAndSettle();

        expect(find.text('Kayıtlı sayfaya karşı dene'), findsOneWidget);

        expect(tester.takeException(), isNull);
      });
    }
  });

  group('the arrangement follows the width', () {
    testWidgets('a phone gets the bar along the bottom', (WidgetTester tester) async {
      await pumpAt(tester, 390);

      expect(find.byType(NavigationBar), findsOneWidget);
      expect(find.byType(NavigationRail), findsNothing);
    });

    testWidgets('a tablet gets a rail with labels under the icons',
        (WidgetTester tester) async {
      await pumpAt(tester, 768);

      final NavigationRail rail =
          tester.widget<NavigationRail>(find.byType(NavigationRail));

      expect(rail.extended, isFalse);
      expect(rail.labelType, NavigationRailLabelType.all);
    });

    testWidgets('a desktop window gets the rail opened out',
        (WidgetTester tester) async {
      await pumpAt(tester, 1440);

      expect(
        tester.widget<NavigationRail>(find.byType(NavigationRail)).extended,
        isTrue,
      );
    });

    for (final double width in <double>[320, 360, 390, 412, 599]) {
      testWidgets('at ${width.toInt()} px every screen can still be walked to',
          (WidgetTester tester) async {
        // The bar takes five and there are seven. The first version of this
        // simply dropped the two that did not fit, and since nothing else links
        // to them, narrowing the window made Filtre and Kaynaklar unreachable -
        // the routes existed and there was no way to walk to one.
        await pumpAt(tester, width);

        for (final String label in const <String>[
          'Filtre',
          'Kaynaklar',
          'Sonuçlar',
          'Durum',
          'Ayarlar',
          'İlanlar',
        ]) {
          if (await goTo(tester, label)) continue;

          // Not in the bar, so it has to be behind "Diğer".
          final Finder more = find.descendant(
            of: find.byType(NavigationBar),
            matching: find.text('Diğer'),
          );

          expect(
            more,
            findsOneWidget,
            reason: '$label is not in the bar and there is no way to it',
          );

          await tester.tap(more);
          await tester.pumpAndSettle();

          expect(
            find.widgetWithText(ListTile, label),
            findsOneWidget,
            reason: '$label is in neither the bar nor the sheet',
          );

          await tester.tap(find.widgetWithText(ListTile, label));
          await tester.pumpAndSettle();

          expect(tester.takeException(), isNull);
        }
      });
    }

    testWidgets('the overflow item lights up while one of its screens is open',
        (WidgetTester tester) async {
      // A bar highlighting İlanlar while Kaynaklar is on screen is a bar that
      // lies about where the reader is.
      await pumpAt(tester, 390);

      await tester.tap(find.descendant(
        of: find.byType(NavigationBar),
        matching: find.text('Diğer'),
      ));
      await tester.pumpAndSettle();

      await tester.tap(find.widgetWithText(ListTile, 'Kaynaklar'));
      await tester.pumpAndSettle();

      final NavigationBar bar =
          tester.widget<NavigationBar>(find.byType(NavigationBar));

      expect(bar.selectedIndex, bar.destinations.length - 1);
    });

    testWidgets('the bottom bar never asks Material for more than it allows',
        (WidgetTester tester) async {
      // Material caps a NavigationBar at five destinations. There are seven, and
      // two are marked wideOnly for this reason. If a seventh is ever added
      // without that flag, this is what says so - the widget itself does not.
      await pumpAt(tester, 320);

      final NavigationBar bar =
          tester.widget<NavigationBar>(find.byType(NavigationBar));

      expect(bar.destinations.length, lessThanOrEqualTo(5));
    });
  });

  group('crossing a breakpoint keeps the screen', () {
    testWidgets('a phone rotated to landscape does not lose where it was',
        (WidgetTester tester) async {
      await pumpAt(tester, 390);
      expect(find.byType(NavigationBar), findsOneWidget);

      // 390x1000 turned on its side. Wide enough for the rail, which means the
      // destination list changes length underneath the selected index.
      tester.view.physicalSize = const Size(1000, 390);
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.byType(NavigationRail), findsOneWidget);
      expect(find.textContaining('Dijital ve Kurumsal Arşiv'), findsWidgets);
    });

    testWidgets('a window dragged narrow while on a wide-only screen survives',
        (WidgetTester tester) async {
      // Filtre has no destination below 600. Being on it when the window narrows
      // used to be a negative selected index, which both nav widgets assert on.
      await pumpAt(tester, 1440);
      await goTo(tester, 'Filtre');

      tester.view.physicalSize = const Size(360, 1000);
      await tester.pumpAndSettle();

      expect(tester.takeException(), isNull);
      expect(find.byType(NavigationBar), findsOneWidget);
    });
  });

  group('the breakpoints are the ones Material names', () {
    test('compact ends and medium begins at 600', () {
      expect(Breakpoints.medium, 600);
    });

    test('expanded begins at 840', () {
      expect(Breakpoints.expanded, 840);
    });
  });
}
