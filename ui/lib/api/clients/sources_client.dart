// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/active_change_request.dart';
import '../models/source.dart';
import '../models/source_health.dart';

part 'sources_client.g.dart';

@RestApi()
abstract class SourcesClient {
  factory SourcesClient(Dio dio, {String? baseUrl}) = _SourcesClient;

  /// Sources currently in the rotation.
  @GET('/api/sources')
  Future<List<Source>> listActiveSources();

  /// Every source, switched-off ones included.
  ///
  /// The collector only ever wants the active ones; a person wants all of them.  ///
  /// Twenty-five are registered and off, each with its reason in its notes, and.  ///
  /// one was switched off automatically after ten consecutive failures with no.  ///
  /// way to see it afterwards.
  @GET('/api/sources/all')
  Future<List<Source>> listAllSources();

  /// Sources that are failing or have gone quiet.
  @GET('/api/sources/troubled')
  Future<List<SourceHealth>> listTroubledSources();

  @GET('/api/sources/{id}')
  Future<Source> getSource({
    @Path('id') required int id,
  });

  /// Puts a source in or out of the rotation.
  ///
  /// Activating clears the failure count as well. Leaving it at ten would take.  ///
  /// the source straight back out on its next stumble, and the reason it failed.  ///
  /// ten times is usually the thing that was just fixed.
  @POST('/api/sources/{id}/active')
  Future<void> setSourceActive({
    @Path('id') required int id,
    @Body() ActiveChangeRequest? body,
  });
}
