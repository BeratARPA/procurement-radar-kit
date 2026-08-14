// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'command_notice.g.dart';

@JsonSerializable()
class CommandNotice {
  const CommandNotice({
    required this.noticeId,
    required this.title,
    required this.authorityName,
    required this.score,
    this.bidDeadlineAt,
    this.procurementType,
    this.procedureCode,
  });
  
  factory CommandNotice.fromJson(Map<String, Object?> json) => _$CommandNoticeFromJson(json);
  
  final int noticeId;
  final String title;
  final String authorityName;
  final int score;
  final DateTime? bidDeadlineAt;
  final String? procurementType;
  final String? procedureCode;

  Map<String, Object?> toJson() => _$CommandNoticeToJson(this);
}
