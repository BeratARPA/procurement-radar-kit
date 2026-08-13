import 'dart:convert';
import 'dart:io';
import 'dart:typed_data';

import 'package:dio/dio.dart';
import 'package:flutter/widgets.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:procurement_radar/src/data/connection_store.dart';
import 'package:procurement_radar/src/data/providers.dart';

/// Answers dio out of files captured from the running api.
///
/// The adapter is replaced rather than the client, on purpose. Faking
/// `RadarApi` would prove the widgets and skip the two layers that actually
/// break: retrofit's request building and the generated `fromJson`. This
/// project has already paid for that lesson on the C# side - a green test with
/// a fake behind an interface proves the code and not the driver (D-17) - and
/// the same gap exists here, between a hand-written model and one generated
/// from a schema that moved.
///
/// The fixtures are real responses, not invented ones, for the reason D-09
/// gives: an invented fixture agrees with whatever the author believed.
class FakeApiAdapter implements HttpClientAdapter {
  FakeApiAdapter(this.routes);

  /// Path (without query) to the body to answer with.
  final Map<String, String> routes;

  /// Every request that arrived, so a test can assert on the header.
  final List<RequestOptions> requests = <RequestOptions>[];

  int status = 200;

  @override
  Future<ResponseBody> fetch(
    RequestOptions options,
    Stream<Uint8List>? requestStream,
    Future<void>? cancelFuture,
  ) async {
    requests.add(options);

    if (status != 200) {
      return ResponseBody.fromString('', status, headers: _jsonHeaders);
    }

    final String? body = routes[options.uri.path];

    if (body == null) {
      return ResponseBody.fromString(
        jsonEncode(<String, Object?>{'title': 'Not Found'}),
        404,
        headers: _jsonHeaders,
      );
    }

    return ResponseBody.fromString(body, 200, headers: _jsonHeaders);
  }

  @override
  void close({bool force = false}) {}

  static const Map<String, List<String>> _jsonHeaders = <String, List<String>>{
    Headers.contentTypeHeader: <String>['application/json; charset=utf-8'],
  };
}

String fixture(String name) =>
    File('test/fixtures/$name').readAsStringSync();

/// Wraps a widget in a scope wired to answer from fixtures, with a connection
/// already set so nothing redirects to the token screen.
Widget withFakeApi({required FakeApiAdapter adapter, required Widget child}) {
  return ProviderScope(
    overrides: [
      initialConnectionProvider.overrideWithValue(
        const Connection(baseUrl: 'http://localhost:60080', token: 'test-token'),
      ),
      dioProvider.overrideWith((Ref ref) {
        final Dio dio = Dio(BaseOptions(baseUrl: 'http://localhost:60080'))
          ..httpClientAdapter = adapter;

        // The same interceptor the real one installs, so a test can assert the
        // header is actually attached.
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

        return dio;
      }),
    ],
    child: child,
  );
}
