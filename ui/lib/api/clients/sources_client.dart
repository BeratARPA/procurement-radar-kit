// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/active_change_request.dart';
import '../models/source.dart';
import '../models/source_check_request.dart';
import '../models/source_check_response.dart';
import '../models/source_definition_request.dart';
import '../models/source_health.dart';
import '../models/source_saved_response.dart';

part 'sources_client.g.dart';

@RestApi()
abstract class SourcesClient {
  factory SourcesClient(Dio dio, {String? baseUrl}) = _SourcesClient;

  /// Sources currently in the rotation.
  @GET('/api/sources')
  Future<List<Source>> listActiveSources();

  /// Registers a source, or refreshes the definition of the one at that url.
  ///
  /// A new source is created **switched off**. spec Bolum 5 ends its.  ///
  /// procedure with three days of observation before `is_active = true`, and a.  ///
  /// screen that skipped straight to polling would skip the one step that.  ///
  /// catches a bad selector before it burns politeness budget. Switching it on.  ///
  /// is a separate, deliberate press.
  @POST('/api/sources')
  Future<SourceSavedResponse> saveSource({
    @Body() SourceDefinitionRequest? body,
  });

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

  /// Edits a source that already exists, url included.
  ///
  /// Leaves the rotation and the health columns alone: editing a selector must.  ///
  /// not be able to switch a source back on or clear the failure count that.  ///
  /// took it out.
  @PUT('/api/sources/{id}')
  Future<void> updateSource({
    @Path('id') required int id,
    @Body() SourceDefinitionRequest? body,
  });

  /// What a set of selectors would find on a page.
  ///
  /// Nothing is fetched and nothing is written. The page comes from the.  ///
  /// archive - the one the collector actually got - or from the request, so.  ///
  /// trying a selector costs the authority nothing.
  @POST('/api/sources/check')
  Future<SourceCheckResponse> checkSource({
    @Body() SourceCheckRequest? body,
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
