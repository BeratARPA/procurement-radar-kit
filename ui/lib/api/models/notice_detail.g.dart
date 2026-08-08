// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'notice_detail.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

NoticeDetail _$NoticeDetailFromJson(Map<String, dynamic> json) => NoticeDetail(
  noticeId: (json['noticeId'] as num).toInt(),
  title: json['title'] as String,
  authorityName: json['authorityName'] as String,
  score: (json['score'] as num).toInt(),
  sourceUrl: json['sourceUrl'] as String,
  status: json['status'] as String,
  ikn: json['ikn'] as String?,
  rationale: json['rationale'] as String?,
  bidDeadlineAt: json['bidDeadlineAt'] == null
      ? null
      : DateTime.parse(json['bidDeadlineAt'] as String),
  announcementText: json['announcementText'] as String?,
);

Map<String, dynamic> _$NoticeDetailToJson(NoticeDetail instance) =>
    <String, dynamic>{
      'noticeId': instance.noticeId,
      'title': instance.title,
      'authorityName': instance.authorityName,
      'ikn': instance.ikn,
      'score': instance.score,
      'rationale': instance.rationale,
      'bidDeadlineAt': instance.bidDeadlineAt?.toIso8601String(),
      'announcementText': instance.announcementText,
      'sourceUrl': instance.sourceUrl,
      'status': instance.status,
    };
