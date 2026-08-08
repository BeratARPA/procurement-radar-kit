// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'checked_notice.g.dart';

/// One row an adapter config found on the page.
@JsonSerializable()
class CheckedNotice {
  const CheckedNotice({
    required this.title,
    required this.sourceUrl,
    required this.documentCount,
    this.bidDeadlineAt,
  });
  
  factory CheckedNotice.fromJson(Map<String, Object?> json) => _$CheckedNoticeFromJson(json);
  
  final String title;
  final String sourceUrl;

  /// Null when the date could not be read from this row.
  final DateTime? bidDeadlineAt;
  final int documentCount;

  Map<String, Object?> toJson() => _$CheckedNoticeToJson(this);
}
