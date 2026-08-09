// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'rescore_response.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

RescoreResponse _$RescoreResponseFromJson(Map<String, dynamic> json) =>
    RescoreResponse(
      considered: (json['considered'] as num).toInt(),
      changed: (json['changed'] as num).toInt(),
      gained: (json['gained'] as num).toInt(),
      lost: (json['lost'] as num).toInt(),
      threshold: (json['threshold'] as num).toInt(),
      notable: (json['notable'] as List<dynamic>)
          .map((e) => MovedNotice.fromJson(e as Map<String, dynamic>))
          .toList(),
      dropped: (json['dropped'] as List<dynamic>)
          .map((e) => MovedNotice.fromJson(e as Map<String, dynamic>))
          .toList(),
    );

Map<String, dynamic> _$RescoreResponseToJson(RescoreResponse instance) =>
    <String, dynamic>{
      'considered': instance.considered,
      'changed': instance.changed,
      'gained': instance.gained,
      'lost': instance.lost,
      'threshold': instance.threshold,
      'notable': instance.notable,
      'dropped': instance.dropped,
    };
