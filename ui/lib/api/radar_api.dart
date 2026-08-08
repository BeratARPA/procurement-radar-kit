// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';

import 'clients/awards_client.dart';
import 'clients/filter_client.dart';
import 'clients/health_client.dart';
import 'clients/notices_client.dart';
import 'clients/settings_client.dart';
import 'clients/sources_client.dart';

/// Procurement Radar `vv1`.
///
/// The local api behind the interface (D-16). Runs on the machine that collects, reachable on the home network, and every request wants the X-Api-Token header.
class RadarApi {
  RadarApi(
    Dio dio, {
    String? baseUrl,
  })  : _dio = dio,
        _baseUrl = baseUrl;

  final Dio _dio;
  final String? _baseUrl;

  static String get version => 'v1';

  AwardsClient? _awards;
  FilterClient? _filter;
  HealthClient? _health;
  NoticesClient? _notices;
  SettingsClient? _settings;
  SourcesClient? _sources;

  AwardsClient get awards => _awards ??= AwardsClient(_dio, baseUrl: _baseUrl);

  FilterClient get filter => _filter ??= FilterClient(_dio, baseUrl: _baseUrl);

  HealthClient get health => _health ??= HealthClient(_dio, baseUrl: _baseUrl);

  NoticesClient get notices => _notices ??= NoticesClient(_dio, baseUrl: _baseUrl);

  SettingsClient get settings => _settings ??= SettingsClient(_dio, baseUrl: _baseUrl);

  SourcesClient get sources => _sources ??= SourcesClient(_dio, baseUrl: _baseUrl);
}
