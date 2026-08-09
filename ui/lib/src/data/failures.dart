import 'package:dio/dio.dart';

import '../strings.dart';

/// The four ways a call fails, told apart.
///
/// Worth separating because the reader's next move differs: a rejected token is
/// fixed in the settings screen, an unreachable api is fixed at the machine, a
/// 500 is fixed by reading the api's log, and a response this app cannot read is
/// fixed by publishing the api again. One "bir hata oluştu" would send somebody
/// to the wrong one of those four - and the fourth already happened here, wearing
/// the third's clothes.
sealed class ApiFailure implements Exception {
  const ApiFailure();

  String get message;
}

/// The api answered and refused the token.
final class Unauthorized extends ApiFailure {
  const Unauthorized();

  @override
  String get message => Strings.unauthorized;
}

/// Nothing came back.
///
/// On the web this covers a blocked cross-origin request as well as a dead
/// server: the browser will not show the page a response it refused, so dio sees
/// the same nothing either way. The message names both rather than guessing.
final class Unreachable extends ApiFailure {
  const Unreachable(this.detail);

  final String detail;

  @override
  String get message => Strings.unreachable;
}

/// The api answered with something that was not a 401 and not a success.
final class ServerSaidNo extends ApiFailure {
  const ServerSaidNo(this.statusCode);

  final int statusCode;

  @override
  String get message => Strings.serverSaidNo(statusCode);
}

/// The api answered, and this app could not read what it said.
///
/// This is a version mismatch, not a fault in either half. `lib/api/` is
/// generated from a schema, so a field the schema promises and the running api
/// does not send makes `fromJson` throw a cast error - which retrofit rethrows
/// untouched, so it arrives here as a plain `TypeError` and not a
/// `DioException`.
///
/// It is separated because it cost an hour the first time. The api on the port
/// was the copy under `publish/`, built before the field existed; every request
/// succeeded with 200 and the screen said the api could not be reached. The
/// message has to name the real thing or it sends the reader to the network.
final class ResponseNotUnderstood extends ApiFailure {
  const ResponseNotUnderstood(this.detail);

  final String detail;

  @override
  String get message => Strings.responseNotUnderstood;
}

ApiFailure describeFailure(Object error) {
  if (error is ApiFailure) return error;

  if (error is DioException) {
    if (error.type == DioExceptionType.badResponse) {
      final int status = error.response?.statusCode ?? 0;

      return status == 401 ? const Unauthorized() : ServerSaidNo(status);
    }

    // connectionError, the timeouts, cancel, unknown. Nothing came back - and
    // on the web a blocked cross-origin request looks exactly like this too.
    return Unreachable(error.message ?? error.type.name);
  }

  // A failed cast out of a generated fromJson. Everything else that is not a
  // DioException is unknown territory, and calling that unreachable would be the
  // same lie in a different place.
  if (error is TypeError) return ResponseNotUnderstood(error.toString());

  return Unreachable(error.toString());
}
