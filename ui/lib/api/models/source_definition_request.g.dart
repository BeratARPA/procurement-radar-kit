// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'source_definition_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SourceDefinitionRequest _$SourceDefinitionRequestFromJson(
  Map<String, dynamic> json,
) => SourceDefinitionRequest(
  authorityName: json['authorityName'] as String,
  authorityType: json['authorityType'] as String,
  province: json['province'] as String,
  url: json['url'] as String,
  adapterKey: json['adapterKey'] as String,
  district: json['district'] as String?,
  adapterConfig: json['adapterConfig'] as String?,
  pollIntervalMinutes: (json['pollIntervalMinutes'] as num?)?.toInt(),
  notes: json['notes'] as String?,
);

Map<String, dynamic> _$SourceDefinitionRequestToJson(
  SourceDefinitionRequest instance,
) => <String, dynamic>{
  'authorityName': instance.authorityName,
  'authorityType': instance.authorityType,
  'province': instance.province,
  'district': instance.district,
  'url': instance.url,
  'adapterKey': instance.adapterKey,
  'adapterConfig': instance.adapterConfig,
  'pollIntervalMinutes': instance.pollIntervalMinutes,
  'notes': instance.notes,
};
