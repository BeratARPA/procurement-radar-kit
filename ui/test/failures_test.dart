import 'package:dio/dio.dart';
import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/api/export.dart';
import 'package:procurement_radar/src/data/failures.dart';

void main() {
  final RequestOptions request = RequestOptions(path: '/api/notices');

  group('what the reader is told', () {
    test('a refused token is not called a network problem', () {
      final ApiFailure failure = describeFailure(
        DioException(
          requestOptions: request,
          type: DioExceptionType.badResponse,
          response: Response<void>(requestOptions: request, statusCode: 401),
        ),
      );

      expect(failure, isA<Unauthorized>());
    });

    test('a dead server is', () {
      final ApiFailure failure = describeFailure(
        DioException(requestOptions: request, type: DioExceptionType.connectionError),
      );

      expect(failure, isA<Unreachable>());
    });

    test('a 500 is neither', () {
      final ApiFailure failure = describeFailure(
        DioException(
          requestOptions: request,
          type: DioExceptionType.badResponse,
          response: Response<void>(requestOptions: request, statusCode: 500),
        ),
      );

      expect(failure, isA<ServerSaidNo>());
      expect((failure as ServerSaidNo).statusCode, 500);
    });
  });

  group('an api older than this app', () {
    // The payload that actually happened. The api answering on the port was the
    // copy under publish/, built before authorityName existed: 200 OK, every
    // other field present, this one absent.
    const Map<String, Object?> staleRow = <String, Object?>{
      'noticeId': 20042,
      'title': 'Dijital ve Kurumsal Arşiv Yönetim Sistemi Yazılımı',
      'score': 30,
      'bidDeadlineAt': '2026-08-10T08:00:00+00:00',
    };

    test('the generated model refuses it', () {
      expect(() => CommandNotice.fromJson(staleRow), throwsA(isA<TypeError>()));
    });

    test('and the reader is told the versions disagree, not that the api is down',
        () {
      // The whole point. This failure arrived as a bare TypeError - retrofit
      // rethrows it untouched - and was reported as "could not reach the api"
      // while the api was up and answering 200. The message sent us to the
      // network for an hour.
      late final Object thrown;

      try {
        CommandNotice.fromJson(staleRow);
        fail('the stale row should not deserialise');
      } on Object catch (error) {
        thrown = error;
      }

      final ApiFailure failure = describeFailure(thrown);

      expect(failure, isA<ResponseNotUnderstood>());
      expect(failure, isNot(isA<Unreachable>()));
      expect(failure.message, contains('eski bir derleme'));
    });

    test('a complete row still reads', () {
      final CommandNotice notice = CommandNotice.fromJson(<String, Object?>{
        ...staleRow,
        'authorityName': 'BAYRAMPAŞA BELEDİYE BAŞKANLIĞI',
      });

      expect(notice.authorityName, 'BAYRAMPAŞA BELEDİYE BAŞKANLIĞI');
      expect(notice.bidDeadlineAt?.isUtc, isTrue);
    });
  });
}
