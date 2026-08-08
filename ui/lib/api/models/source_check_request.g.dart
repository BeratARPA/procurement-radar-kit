// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'source_check_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SourceCheckRequest _$SourceCheckRequestFromJson(Map<String, dynamic> json) =>
    SourceCheckRequest(
      adapterConfig: json['adapterConfig'] as String,
      sourceId: (json['sourceId'] as num?)?.toInt(),
      html: json['html'] as String?,
      url: json['url'] as String?,
    );

Map<String, dynamic> _$SourceCheckRequestToJson(SourceCheckRequest instance) =>
    <String, dynamic>{
      'adapterConfig': instance.adapterConfig,
      'sourceId': instance.sourceId,
      'html': instance.html,
      'url': instance.url,
    };
