// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'scored_notice.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ScoredNotice _$ScoredNoticeFromJson(Map<String, dynamic> json) => ScoredNotice(
  noticeId: (json['noticeId'] as num).toInt(),
  title: json['title'] as String,
  authorityName: json['authorityName'] as String,
  score: (json['score'] as num).toInt(),
);

Map<String, dynamic> _$ScoredNoticeToJson(ScoredNotice instance) =>
    <String, dynamic>{
      'noticeId': instance.noticeId,
      'title': instance.title,
      'authorityName': instance.authorityName,
      'score': instance.score,
    };
