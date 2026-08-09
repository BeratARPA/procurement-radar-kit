// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/filter_preview_request.dart';
import '../models/filter_preview_response.dart';
import '../models/rescore_response.dart';

part 'filter_client.g.dart';

@RestApi()
abstract class FilterClient {
  factory FilterClient(Dio dio, {String? baseUrl}) = _FilterClient;

  /// What this keyword list and threshold would have announced.
  ///
  /// D-14 moved the threshold from 60 to 30 by reading a sample by hand. The.  ///
  /// same question is a second's work here and exact: against the archive as it.  ///
  /// stands, 60 announces 2 notices, 30 announces 14, 25 announces 19.  ///
  ///              ///
  /// Nothing is written. The scorer is built from the submitted list and thrown.  ///
  /// away, so a preview cannot change what the worker is doing - which would be.  ///
  /// the opposite of a preview.
  @POST('/api/filter/preview')
  Future<FilterPreviewResponse> previewFilter({
    @Body() FilterPreviewRequest? body,
  });

  /// Score the whole archive again with the keyword list as it is stored now.
  ///
  /// A notice is scored once, when it is parsed. So a word added today only.  ///
  /// ever reaches notices that arrive tomorrow, and everything already.  ///
  /// collected keeps the verdict of a list that no longer exists. It is not a.  ///
  /// hypothetical: the day 26 words were added, a CRM tender and an OCR tender.  ///
  /// the new list matches went on showing 0.  ///
  ///              ///
  /// Unlike the preview this writes. It takes no keyword list in the body on.  ///
  /// purpose - it applies what is stored, which is what the worker is using,.  ///
  /// so the archive and the worker cannot end up disagreeing. To try a list.  ///
  /// first, that is what the preview is for.  ///
  ///              ///
  /// `llm_score` is left where it is (D-03 keeps it null in v1; once something.  ///
  /// fills it, re-running the cheap filter must not wipe the expensive answer).
  @POST('/api/filter/rescore')
  Future<RescoreResponse> rescoreArchive({
    @Query('limit') int? limit,
  });
}
