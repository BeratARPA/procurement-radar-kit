// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'system_status.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SystemStatus _$SystemStatusFromJson(Map<String, dynamic> json) => SystemStatus(
  activeSources: (json['activeSources'] as num).toInt(),
  failingSources: (json['failingSources'] as num).toInt(),
  notices: (json['notices'] as num).toInt(),
  noticesToday: (json['noticesToday'] as num).toInt(),
  announced: (json['announced'] as num).toInt(),
  lastFetchAt: json['lastFetchAt'] == null
      ? null
      : DateTime.parse(json['lastFetchAt'] as String),
);

Map<String, dynamic> _$SystemStatusToJson(SystemStatus instance) =>
    <String, dynamic>{
      'activeSources': instance.activeSources,
      'failingSources': instance.failingSources,
      'notices': instance.notices,
      'noticesToday': instance.noticesToday,
      'announced': instance.announced,
      'lastFetchAt': instance.lastFetchAt?.toIso8601String(),
    };
