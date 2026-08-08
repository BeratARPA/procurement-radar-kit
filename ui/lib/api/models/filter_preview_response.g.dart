// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'filter_preview_response.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

FilterPreviewResponse _$FilterPreviewResponseFromJson(
  Map<String, dynamic> json,
) => FilterPreviewResponse(
  considered: (json['considered'] as num).toInt(),
  wouldNotify: (json['wouldNotify'] as num).toInt(),
  threshold: (json['threshold'] as num).toInt(),
  caught: (json['caught'] as List<dynamic>)
      .map((e) => ScoredNotice.fromJson(e as Map<String, dynamic>))
      .toList(),
  missed: (json['missed'] as List<dynamic>)
      .map((e) => ScoredNotice.fromJson(e as Map<String, dynamic>))
      .toList(),
);

Map<String, dynamic> _$FilterPreviewResponseToJson(
  FilterPreviewResponse instance,
) => <String, dynamic>{
  'considered': instance.considered,
  'wouldNotify': instance.wouldNotify,
  'threshold': instance.threshold,
  'caught': instance.caught,
  'missed': instance.missed,
};
