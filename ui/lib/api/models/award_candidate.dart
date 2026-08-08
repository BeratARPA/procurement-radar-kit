// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'award_candidate.g.dart';

@JsonSerializable()
class AwardCandidate {
  const AwardCandidate({
    required this.noticeId,
    required this.ikn,
    required this.title,
  });
  
  factory AwardCandidate.fromJson(Map<String, Object?> json) => _$AwardCandidateFromJson(json);
  
  final int noticeId;
  final String ikn;
  final String title;

  Map<String, Object?> toJson() => _$AwardCandidateToJson(this);
}
