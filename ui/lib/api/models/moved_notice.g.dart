// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'moved_notice.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

MovedNotice _$MovedNoticeFromJson(Map<String, dynamic> json) => MovedNotice(
  noticeId: (json['noticeId'] as num).toInt(),
  title: json['title'] as String,
  before: (json['before'] as num).toInt(),
  after: (json['after'] as num).toInt(),
);

Map<String, dynamic> _$MovedNoticeToJson(MovedNotice instance) =>
    <String, dynamic>{
      'noticeId': instance.noticeId,
      'title': instance.title,
      'before': instance.before,
      'after': instance.after,
    };
