// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

import 'authority_type.dart';

part 'source.g.dart';

@JsonSerializable()
class Source {
  const Source({
    required this.authorityName,
    required this.authorityType,
    required this.province,
    required this.url,
    required this.adapterKey,
    this.id,
    this.district,
    this.adapterConfig,
    this.pollIntervalMinutes,
    this.isActive,
    this.robotsCheckedAt,
    this.lastFetchAt,
    this.lastSuccessAt,
    this.lastNewNoticeAt,
    this.consecutiveFailures,
    this.notes,
  });
  
  factory Source.fromJson(Map<String, Object?> json) => _$SourceFromJson(json);
  
  final int? id;
  final String authorityName;
  final AuthorityType authorityType;
  final String province;
  final String? district;
  final String url;
  final String adapterKey;
  final String? adapterConfig;
  final int? pollIntervalMinutes;
  final bool? isActive;
  final DateTime? robotsCheckedAt;
  final DateTime? lastFetchAt;
  final DateTime? lastSuccessAt;
  final DateTime? lastNewNoticeAt;
  final int? consecutiveFailures;
  final String? notes;

  Map<String, Object?> toJson() => _$SourceToJson(this);
}
