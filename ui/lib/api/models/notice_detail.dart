// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'notice_detail.g.dart';

@JsonSerializable()
class NoticeDetail {
  const NoticeDetail({
    required this.noticeId,
    required this.title,
    required this.authorityName,
    required this.score,
    required this.sourceUrl,
    required this.status,
    this.ikn,
    this.rationale,
    this.bidDeadlineAt,
    this.announcementText,
  });
  
  factory NoticeDetail.fromJson(Map<String, Object?> json) => _$NoticeDetailFromJson(json);
  
  final int noticeId;
  final String title;
  final String authorityName;
  final String? ikn;
  final int score;
  final String? rationale;
  final DateTime? bidDeadlineAt;
  final String? announcementText;
  final String sourceUrl;
  final String status;

  Map<String, Object?> toJson() => _$NoticeDetailToJson(this);
}
