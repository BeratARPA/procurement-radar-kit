import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import 'data/providers.dart';
import 'screens/notice_detail_screen.dart';
import 'screens/notices_screen.dart';
import 'screens/settings_screen.dart';
import 'screens/shell.dart';
import 'screens/status_screen.dart';
import 'screens/system_screen.dart';
import 'screens/token_screen.dart';

abstract final class Routes {
  static const String notices = '/notices';
  static const String status = '/status';
  static const String system = '/system';
  static const String settings = '/settings';
  static const String token = '/token';

  static String notice(int id) => '$notices/$id';
}

/// Built once.
///
/// The redirect reads the connection rather than watching it, so this provider
/// has no dependency to rebuild on - a rebuilt `GoRouter` would throw away the
/// navigation stack, which on this screen means bouncing the reader out of the
/// notice they were in the middle of.
final Provider<GoRouter> routerProvider = Provider<GoRouter>((Ref ref) {
  return GoRouter(
    initialLocation: Routes.notices,
    redirect: (BuildContext context, GoRouterState state) {
      final bool ready = ref.read(connectionProvider).isComplete;
      final bool atToken = state.matchedLocation == Routes.token;

      // Nothing can be read without a token, so there is nothing to show. An
      // empty list screen would look like an empty archive.
      if (!ready && !atToken) return Routes.token;
      if (ready && atToken) return Routes.notices;

      return null;
    },
    routes: <RouteBase>[
      GoRoute(
        path: Routes.token,
        builder: (BuildContext context, GoRouterState state) => const TokenScreen(),
      ),
      StatefulShellRoute.indexedStack(
        builder: (
          BuildContext context,
          GoRouterState state,
          StatefulNavigationShell navigationShell,
        ) =>
            AppShell(navigationShell: navigationShell),
        branches: <StatefulShellBranch>[
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: Routes.notices,
                builder: (BuildContext context, GoRouterState state) =>
                    const NoticesScreen(),
                routes: <RouteBase>[
                  GoRoute(
                    path: ':id',
                    builder: (BuildContext context, GoRouterState state) {
                      // A path segment is text until proven otherwise. A typed
                      // or stale url reaching int.parse would throw inside
                      // build, which shows a red screen rather than a message.
                      final int? id =
                          int.tryParse(state.pathParameters['id'] ?? '');

                      return NoticeDetailScreen(noticeId: id);
                    },
                  ),
                ],
              ),
            ],
          ),
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: Routes.status,
                builder: (BuildContext context, GoRouterState state) =>
                    const StatusScreen(),
              ),
            ],
          ),
          // Present in the route table on every platform, absent from the
          // navigation on the web. Keeping the branch means the indices the
          // shell hands back line up with one list of destinations rather than
          // two, and a bookmarked /system on the web lands on a screen that
          // says why it is empty instead of a router error.
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: Routes.system,
                builder: (BuildContext context, GoRouterState state) =>
                    const SystemScreen(),
              ),
            ],
          ),
          StatefulShellBranch(
            routes: <RouteBase>[
              GoRoute(
                path: Routes.settings,
                builder: (BuildContext context, GoRouterState state) =>
                    const SettingsScreen(),
              ),
            ],
          ),
        ],
      ),
    ],
  );
});
