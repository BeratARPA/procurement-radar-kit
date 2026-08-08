// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'award_summary.g.dart';

@JsonSerializable()
class AwardSummary {
  const AwardSummary({
    required this.title,
    required this.authorityName,
    this.winnerName,
    this.contractAmount,
    this.estimatedCost,
    this.bidCount,
    this.awardedAt,
  });
  
  factory AwardSummary.fromJson(Map<String, Object?> json) => _$AwardSummaryFromJson(json);
  
  final String title;
  final String authorityName;
  final String? winnerName;
  final double? contractAmount;
  final double? estimatedCost;
  final int? bidCount;
  final DateTime? awardedAt;

  Map<String, Object?> toJson() => _$AwardSummaryToJson(this);
}
