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
  deactivatedAt: json['deactivatedAt'] == null
      ? null
      : DateTime.parse(json['deactivatedAt'] as String),
  deactivatedReason: json['deactivatedReason'] == null
      ? null
      : DeactivationReason.fromJson(json['deactivatedReason'] as String),
  quarantineProbedAt: json['quarantineProbedAt'] == null
      ? null
      : DateTime.parse(json['quarantineProbedAt'] as String),
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
  'deactivatedAt': instance.deactivatedAt?.toIso8601String(),
  'deactivatedReason': _$DeactivationReasonEnumMap[instance.deactivatedReason],
  'quarantineProbedAt': instance.quarantineProbedAt?.toIso8601String(),
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

const _$DeactivationReasonEnumMap = {
  DeactivationReason.healthRule: 'health_rule',
  DeactivationReason.siteUnreachable: 'site_unreachable',
  DeactivationReason.manual: 'manual',
  DeactivationReason.noTenderList: 'no_tender_list',
  DeactivationReason.robots: 'robots',
  DeactivationReason.superseded: 'superseded',
  DeactivationReason.$unknown: r'$unknown',
};
