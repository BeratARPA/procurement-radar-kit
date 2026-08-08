// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'award_summary.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

AwardSummary _$AwardSummaryFromJson(Map<String, dynamic> json) => AwardSummary(
  title: json['title'] as String,
  authorityName: json['authorityName'] as String,
  winnerName: json['winnerName'] as String?,
  contractAmount: (json['contractAmount'] as num?)?.toDouble(),
  estimatedCost: (json['estimatedCost'] as num?)?.toDouble(),
  bidCount: (json['bidCount'] as num?)?.toInt(),
  awardedAt: json['awardedAt'] == null
      ? null
      : DateTime.parse(json['awardedAt'] as String),
);

Map<String, dynamic> _$AwardSummaryToJson(AwardSummary instance) =>
    <String, dynamic>{
      'title': instance.title,
      'authorityName': instance.authorityName,
      'winnerName': instance.winnerName,
      'contractAmount': instance.contractAmount,
      'estimatedCost': instance.estimatedCost,
      'bidCount': instance.bidCount,
      'awardedAt': instance.awardedAt?.toIso8601String(),
    };
