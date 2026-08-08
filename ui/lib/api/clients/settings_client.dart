// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/setting.dart';
import '../models/setting_change_request.dart';

part 'settings_client.g.dart';

@RestApi()
abstract class SettingsClient {
  factory SettingsClient(Dio dio, {String? baseUrl}) = _SettingsClient;

  /// Every setting, with when it last changed and by whom.
  @GET('/api/settings')
  Future<List<Setting>> listSettings();

  /// [key] - keywords, messages, date_patterns, normalization, personal_data, award_labels, ekap_ui or politeness.
  @GET('/api/settings/{key}')
  Future<Setting> getSetting({
    @Path('key') required String key,
  });

  /// Replaces a setting, filing the previous value in the history.
  ///
  /// Validated by the loader that will read it, not by a second description of.  ///
  /// what valid looks like: two validators drift, one cannot. The politeness.  ///
  /// setting is refused below five seconds between requests to a host, and with.  ///
  /// robots.txt turned off, whoever is asking (D-16).  ///
  ///              ///
  /// The worker would survive a bad value - it keeps the previous one and logs.  ///
  /// an error - but the person who pressed save would never hear about it.
  @PUT('/api/settings/{key}')
  Future<void> saveSetting({
    @Path('key') required String key,
    @Body() SettingChangeRequest? body,
  });

  /// Previous values, newest first.
  ///
  /// One mistyped keyword silences the notifications and nothing about the.  ///
  /// system looks broken while it happens. A settings screen that cannot be.  ///
  /// undone is worse than no settings screen.
  @GET('/api/settings/{key}/history')
  Future<List<Setting>> getSettingHistory({
    @Path('key') required String key,
    @Query('limit') int? limit,
  });
}
