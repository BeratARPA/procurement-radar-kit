// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'checked_notice.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CheckedNotice _$CheckedNoticeFromJson(Map<String, dynamic> json) =>
    CheckedNotice(
      title: json['title'] as String,
      sourceUrl: json['sourceUrl'] as String,
      documentCount: (json['documentCount'] as num).toInt(),
      bidDeadlineAt: json['bidDeadlineAt'] == null
          ? null
          : DateTime.parse(json['bidDeadlineAt'] as String),
    );

Map<String, dynamic> _$CheckedNoticeToJson(CheckedNotice instance) =>
    <String, dynamic>{
      'title': instance.title,
      'sourceUrl': instance.sourceUrl,
      'bidDeadlineAt': instance.bidDeadlineAt?.toIso8601String(),
      'documentCount': instance.documentCount,
    };
