// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'source_health.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SourceHealth _$SourceHealthFromJson(Map<String, dynamic> json) => SourceHealth(
  authorityName: json['authorityName'] as String,
  consecutiveFailures: (json['consecutiveFailures'] as num).toInt(),
  lastSuccessAt: json['lastSuccessAt'] == null
      ? null
      : DateTime.parse(json['lastSuccessAt'] as String),
  lastNewNoticeAt: json['lastNewNoticeAt'] == null
      ? null
      : DateTime.parse(json['lastNewNoticeAt'] as String),
);

Map<String, dynamic> _$SourceHealthToJson(SourceHealth instance) =>
    <String, dynamic>{
      'authorityName': instance.authorityName,
      'consecutiveFailures': instance.consecutiveFailures,
      'lastSuccessAt': instance.lastSuccessAt?.toIso8601String(),
      'lastNewNoticeAt': instance.lastNewNoticeAt?.toIso8601String(),
    };
