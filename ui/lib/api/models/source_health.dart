// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'source_health.g.dart';

@JsonSerializable()
class SourceHealth {
  const SourceHealth({
    required this.authorityName,
    required this.consecutiveFailures,
    this.lastSuccessAt,
    this.lastNewNoticeAt,
  });
  
  factory SourceHealth.fromJson(Map<String, Object?> json) => _$SourceHealthFromJson(json);
  
  final String authorityName;
  final int consecutiveFailures;
  final DateTime? lastSuccessAt;
  final DateTime? lastNewNoticeAt;

  Map<String, Object?> toJson() => _$SourceHealthToJson(this);
}
