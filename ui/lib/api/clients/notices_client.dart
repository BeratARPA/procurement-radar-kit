// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/command_notice.dart';
import '../models/notice_detail.dart';
import '../models/status_change_request.dart';

part 'notices_client.g.dart';

@RestApi()
abstract class NoticesClient {
  factory NoticesClient(Dio dio, {String? baseUrl}) = _NoticesClient;

  /// Open notices, or the whole archive when a term is given.
  ///
  /// [q] - Searches title and authority. Without it the answer is what is still open.
  ///
  /// [minScore] - Only used for the open list.
  ///
  /// [limit] - Capped at 200.
  @GET('/api/notices')
  Future<List<CommandNotice>> listNotices({
    @Query('q') String? q,
    @Query('minScore') int? minScore,
    @Query('limit') int? limit,
  });

  /// Notices closing within the window, soonest first.
  @GET('/api/notices/closing')
  Future<List<CommandNotice>> listClosingNotices({
    @Query('withinHours') int? withinHours,
  });

  /// One notice by its own id, announcement text included.
  ///
  /// This is the one a list links to. Only EKAP issues an IKN, so most of the.  ///
  /// archive has none - 814 of the first 924 notices - and a screen keyed on the.  ///
  /// IKN could open about one notice in eight.
  ///
  /// [id] - The id every list row carries.
  @GET('/api/notices/by-id/{id}')
  Future<NoticeDetail> getNotice({
    @Path('id') required int id,
  });

  /// One notice by its tender registration number, announcement text included.
  ///
  /// [ikn] - For example 2026/1166872.
  @GET('/api/notices/{ikn}')
  Future<NoticeDetail> getNoticeByIkn({
    @Path('ikn') required String ikn,
  });

  /// Records the team's verdict on a notice.
  ///
  /// These verdicts are what makes the threshold measurable later (spec section.  ///
  /// 10): if what was dismissed and what was pursued score the same, the filter.  ///
  /// is not discriminating.
  @POST('/api/notices/{id}/status')
  Future<void> changeNoticeStatus({
    @Path('id') required int id,
    @Body() StatusChangeRequest? body,
  });
}
