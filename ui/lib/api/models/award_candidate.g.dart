// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'award_candidate.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

AwardCandidate _$AwardCandidateFromJson(Map<String, dynamic> json) =>
    AwardCandidate(
      noticeId: (json['noticeId'] as num).toInt(),
      ikn: json['ikn'] as String,
      title: json['title'] as String,
    );

Map<String, dynamic> _$AwardCandidateToJson(AwardCandidate instance) =>
    <String, dynamic>{
      'noticeId': instance.noticeId,
      'ikn': instance.ikn,
      'title': instance.title,
    };
