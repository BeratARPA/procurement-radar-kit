// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'scored_notice.g.dart';

/// One notice as a proposed filter would have scored it.
@JsonSerializable()
class ScoredNotice {
  const ScoredNotice({
    required this.noticeId,
    required this.title,
    required this.authorityName,
    required this.score,
  });
  
  factory ScoredNotice.fromJson(Map<String, Object?> json) => _$ScoredNoticeFromJson(json);
  
  final int noticeId;
  final String title;
  final String authorityName;
  final int score;

  Map<String, Object?> toJson() => _$ScoredNoticeToJson(this);
}
