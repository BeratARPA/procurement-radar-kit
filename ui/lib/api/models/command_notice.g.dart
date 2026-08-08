// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'command_notice.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

CommandNotice _$CommandNoticeFromJson(Map<String, dynamic> json) =>
    CommandNotice(
      noticeId: (json['noticeId'] as num).toInt(),
      title: json['title'] as String,
      authorityName: json['authorityName'] as String,
      score: (json['score'] as num).toInt(),
      bidDeadlineAt: json['bidDeadlineAt'] == null
          ? null
          : DateTime.parse(json['bidDeadlineAt'] as String),
    );

Map<String, dynamic> _$CommandNoticeToJson(CommandNotice instance) =>
    <String, dynamic>{
      'noticeId': instance.noticeId,
      'title': instance.title,
      'authorityName': instance.authorityName,
      'score': instance.score,
      'bidDeadlineAt': instance.bidDeadlineAt?.toIso8601String(),
    };
