// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'source_check_response.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SourceCheckResponse _$SourceCheckResponseFromJson(Map<String, dynamic> json) =>
    SourceCheckResponse(
      source: json['source'] as String,
      byteCount: (json['byteCount'] as num).toInt(),
      noticeCount: (json['noticeCount'] as num).toInt(),
      withDeadline: (json['withDeadline'] as num).toInt(),
      withDocuments: (json['withDocuments'] as num).toInt(),
      sample: (json['sample'] as List<dynamic>)
          .map((e) => CheckedNotice.fromJson(e as Map<String, dynamic>))
          .toList(),
      warnings: (json['warnings'] as List<dynamic>)
          .map((e) => e as String)
          .toList(),
      problemCode: json['problemCode'] as String?,
      problem: json['problem'] as String?,
      fetchedAt: json['fetchedAt'] == null
          ? null
          : DateTime.parse(json['fetchedAt'] as String),
      charSet: json['charSet'] as String?,
    );

Map<String, dynamic> _$SourceCheckResponseToJson(
  SourceCheckResponse instance,
) => <String, dynamic>{
  'problemCode': instance.problemCode,
  'problem': instance.problem,
  'source': instance.source,
  'fetchedAt': instance.fetchedAt?.toIso8601String(),
  'charSet': instance.charSet,
  'byteCount': instance.byteCount,
  'noticeCount': instance.noticeCount,
  'withDeadline': instance.withDeadline,
  'withDocuments': instance.withDocuments,
  'sample': instance.sample,
  'warnings': instance.warnings,
};
