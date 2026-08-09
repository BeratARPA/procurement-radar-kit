// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'moved_notice.g.dart';

@JsonSerializable()
class MovedNotice {
  const MovedNotice({
    required this.noticeId,
    required this.title,
    required this.before,
    required this.after,
  });
  
  factory MovedNotice.fromJson(Map<String, Object?> json) => _$MovedNoticeFromJson(json);
  
  final int noticeId;
  final String title;
  final int before;
  final int after;

  Map<String, Object?> toJson() => _$MovedNoticeToJson(this);
}
