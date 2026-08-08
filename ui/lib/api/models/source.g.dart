// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'source.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Source _$SourceFromJson(Map<String, dynamic> json) => Source(
  authorityName: json['authorityName'] as String,
  authorityType: AuthorityType.fromJson(json['authorityType'] as String),
  province: json['province'] as String,
  url: json['url'] as String,
  adapterKey: json['adapterKey'] as String,
  id: (json['id'] as num?)?.toInt(),
  district: json['district'] as String?,
  adapterConfig: json['adapterConfig'] as String?,
  pollIntervalMinutes: (json['pollIntervalMinutes'] as num?)?.toInt(),
  isActive: json['isActive'] as bool?,
  robotsCheckedAt: json['robotsCheckedAt'] == null
      ? null
      : DateTime.parse(json['robotsCheckedAt'] as String),
  lastFetchAt: json['lastFetchAt'] == null
      ? null
      : DateTime.parse(json['lastFetchAt'] as String),
  lastSuccessAt: json['lastSuccessAt'] == null
      ? null
      : DateTime.parse(json['lastSuccessAt'] as String),
  lastNewNoticeAt: json['lastNewNoticeAt'] == null
      ? null
      : DateTime.parse(json['lastNewNoticeAt'] as String),
  consecutiveFailures: (json['consecutiveFailures'] as num?)?.toInt(),
  notes: json['notes'] as String?,
);

Map<String, dynamic> _$SourceToJson(Source instance) => <String, dynamic>{
  'id': instance.id,
  'authorityName': instance.authorityName,
  'authorityType': _$AuthorityTypeEnumMap[instance.authorityType]!,
  'province': instance.province,
  'district': instance.district,
  'url': instance.url,
  'adapterKey': instance.adapterKey,
  'adapterConfig': instance.adapterConfig,
  'pollIntervalMinutes': instance.pollIntervalMinutes,
  'isActive': instance.isActive,
  'robotsCheckedAt': instance.robotsCheckedAt?.toIso8601String(),
  'lastFetchAt': instance.lastFetchAt?.toIso8601String(),
  'lastSuccessAt': instance.lastSuccessAt?.toIso8601String(),
  'lastNewNoticeAt': instance.lastNewNoticeAt?.toIso8601String(),
  'consecutiveFailures': instance.consecutiveFailures,
  'notes': instance.notes,
};

const _$AuthorityTypeEnumMap = {
  AuthorityType.university: 'university',
  AuthorityType.municipality: 'municipality',
  AuthorityType.provincialDirectorate: 'provincial_directorate',
  AuthorityType.hospital: 'hospital',
  AuthorityType.soeSubsidiary: 'soe_subsidiary',
  AuthorityType.ekap: 'ekap',
  AuthorityType.$unknown: r'$unknown',
};
