// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'filter_preview_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

FilterPreviewRequest _$FilterPreviewRequestFromJson(
  Map<String, dynamic> json,
) => FilterPreviewRequest(
  keywords: json['keywords'] as String,
  threshold: (json['threshold'] as num?)?.toInt(),
  limit: (json['limit'] as num?)?.toInt(),
);

Map<String, dynamic> _$FilterPreviewRequestToJson(
  FilterPreviewRequest instance,
) => <String, dynamic>{
  'keywords': instance.keywords,
  'threshold': instance.threshold,
  'limit': instance.limit,
};
