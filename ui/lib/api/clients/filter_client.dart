// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/filter_preview_request.dart';
import '../models/filter_preview_response.dart';

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
}
