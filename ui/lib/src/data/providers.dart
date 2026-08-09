import 'dart:async';

import 'package:dio/dio.dart';
import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:shared_preferences/shared_preferences.dart';

import '../../api/export.dart';
import 'connection_store.dart';
import 'notice_filter.dart';

/// The connection as it was on disk when the app started.
///
/// Overridden in `main` with a value already read, so everything below this line
/// is synchronous. The alternative - an async provider the router waits on -
/// buys nothing here and costs a loading frame on every route decision.
final Provider<Connection> initialConnectionProvider = Provider<Connection>(
  (Ref ref) => throw UnimplementedError('overridden in main()'),
);

final Provider<ConnectionStore> connectionStoreProvider =
    Provider<ConnectionStore>((Ref ref) => ConnectionStore());

final NotifierProvider<ConnectionNotifier, Connection> connectionProvider =
    NotifierProvider<ConnectionNotifier, Connection>(ConnectionNotifier.new);

class ConnectionNotifier extends Notifier<Connection> {
  @override
  Connection build() => ref.watch(initialConnectionProvider);

  Future<void> save(Connection connection) async {
    state = connection;

    await ref.read(connectionStoreProvider).write(connection);
  }
}

/// One dio per connection.
///
/// Rebuilt when the address or the token changes, which is what makes the
/// settings screen take effect without a restart. The token is read from the
/// provider on each request rather than captured, so a save mid-flight cannot
/// leave a stale header behind.
final Provider<Dio> dioProvider = Provider<Dio>((Ref ref) {
  final Connection connection = ref.watch(connectionProvider);

  final Dio dio = Dio(
    BaseOptions(
      baseUrl: connection.baseUrl,
      connectTimeout: const Duration(seconds: 5),
      receiveTimeout: const Duration(seconds: 30),
      headers: const <String, Object?>{'Accept': 'application/json'},
      // Left false deliberately. Turning it on makes the api's
      // Access-Control-Allow-Origin: * stop meaning anything and every request
      // in the browser fails.
    ),
  );

  dio.interceptors.add(
    InterceptorsWrapper(
      onRequest: (RequestOptions options, RequestInterceptorHandler handler) {
        final String token = ref.read(connectionProvider).token;

        if (token.isNotEmpty) {
          options.headers['X-Api-Token'] = token;
        }

        handler.next(options);
      },
    ),
  );

  ref.onDispose(dio.close);

  return dio;
});

final Provider<RadarApi> radarApiProvider =
    Provider<RadarApi>((Ref ref) => RadarApi(ref.watch(dioProvider)));

/// When a failed read is worth trying again by itself.
///
/// Riverpod 3 retries a failed provider on its own, with exponential backoff and
/// no limit. That is right for a blip and wrong for the two failures this app
/// actually has: a refused token and a wrong address never fix themselves, so
/// the default turns a message the reader could act on into a screen that
/// flickers between error and loading while quietly calling the api forever.
///
/// So: nothing is retried automatically. Every screen has a button, and a person
/// deciding when to try again is the whole point of showing them what broke.
Duration? _noAutomaticRetry(int retryCount, Object error) => null;

/// What the list screen shows.
///
/// Empty term means "what is still open", which is the question the screen is
/// for; a term searches the whole archive instead. That is the api's own split
/// (`GET /api/notices`), kept rather than papered over, because "open" and
/// "everything ever seen" really are different questions.
/// How many rows the screen will take in one go.
///
/// The api caps at 5000. Asking for all of them is deliberate: the point of the
/// list is that the reader narrows it, and a screen showing the first 200 of 988
/// without saying so lies by omission. When this stops being a sensible number,
/// the answer is paging rather than a bigger one.
const int _noticePageSize = 5000;

/// Keyed on "term|scope|minScore" - the three things the api answers. Two
/// filters with the same key share one request, so changing the sort order or
/// the deadline window does not re-fetch.
final noticesProvider =
    FutureProvider.autoDispose.family<List<CommandNotice>, String>(
  (Ref ref, String key) {
    final List<String> parts = key.split('|');
    final String term = parts.first;
    final String scope = parts.length > 1 ? parts[1] : 'open';
    final int minScore = parts.length > 2 ? (int.tryParse(parts[2]) ?? 0) : 0;

    final RadarApi api = ref.watch(radarApiProvider);

    return term.isEmpty
        ? api.notices.listNotices(
            scope: scope,
            minScore: minScore,
            limit: _noticePageSize,
          )
        // A term searches the whole archive whatever the scope says, and the api
        // does not score-filter that path - the screen does it after.
        : api.notices.listNotices(q: term, limit: _noticePageSize);
  },
  retry: _noAutomaticRetry,
);

/// What the list screen is currently narrowed to.
final NotifierProvider<NoticeFilterNotifier, NoticeFilter> noticeFilterProvider =
    NotifierProvider<NoticeFilterNotifier, NoticeFilter>(NoticeFilterNotifier.new);

class NoticeFilterNotifier extends Notifier<NoticeFilter> {
  @override
  NoticeFilter build() => const NoticeFilter();

  void setTerm(String term) => state = state.copyWith(term: term.trim());

  void setScope(NoticeScope scope) => state = state.copyWith(scope: scope);

  void setMinScore(int score) => state = state.copyWith(minScore: score);

  void setWithinHours(int? hours) => state = hours == null
      ? state.copyWith(clearWithinHours: true)
      : state.copyWith(withinHours: hours);

  void setSort(NoticeSort sort) => state = state.copyWith(sort: sort);

  void setHideUndated(bool hide) => state = state.copyWith(hideUndated: hide);

  void clear() => state = NoticeFilter(term: state.term);
}

final noticeDetailProvider = FutureProvider.autoDispose.family<NoticeDetail, int>(
  (Ref ref, int id) => ref.watch(radarApiProvider).notices.getNotice(id: id),
  retry: _noAutomaticRetry,
);

final systemStatusProvider = FutureProvider.autoDispose<SystemStatus>(
  (Ref ref) => ref.watch(radarApiProvider).health.getHealth(),
  retry: _noAutomaticRetry,
);

/// Records a verdict, then makes both screens re-read.
///
/// Invalidating rather than patching the two caches by hand: the api decides
/// what a status change means for the open list - a notice marked not_relevant
/// keeps appearing there, because `ListOpen` filters on `status = 'new'` - and
/// duplicating that rule here is how the screen and the database start
/// disagreeing.
Future<void> changeNoticeStatus(WidgetRef ref, int noticeId, String status) async {
  await ref
      .read(radarApiProvider)
      .notices
      .changeNoticeStatus(id: noticeId, body: StatusChangeRequest(status: status));

  ref.invalidate(noticeDetailProvider(noticeId));
  ref.invalidate(noticesProvider);
}

final NotifierProvider<ThemeModeNotifier, ThemeMode> themeModeProvider =
    NotifierProvider<ThemeModeNotifier, ThemeMode>(ThemeModeNotifier.new);

class ThemeModeNotifier extends Notifier<ThemeMode> {
  static const String _key = 'theme_mode';

  final SharedPreferencesAsync _preferences = SharedPreferencesAsync();

  /// Following the system is the right first frame, so the stored choice is
  /// applied when it arrives rather than held for.
  @override
  ThemeMode build() {
    unawaited(_restore());

    return ThemeMode.system;
  }

  Future<void> _restore() async {
    final String? saved = await _preferences.getString(_key);

    if (saved == null) return;

    state = ThemeMode.values.firstWhere(
      (ThemeMode mode) => mode.name == saved,
      orElse: () => ThemeMode.system,
    );
  }

  Future<void> select(ThemeMode mode) async {
    state = mode;

    await _preferences.setString(_key, mode.name);
  }
}
