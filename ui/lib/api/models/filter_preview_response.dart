// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

import 'scored_notice.dart';

part 'filter_preview_response.g.dart';

/// What a keyword list and a threshold would have done to the notices already.///
/// collected.
@JsonSerializable()
class FilterPreviewResponse {
  const FilterPreviewResponse({
    required this.considered,
    required this.wouldNotify,
    required this.threshold,
    required this.caught,
    required this.missed,
  });
  
  factory FilterPreviewResponse.fromJson(Map<String, Object?> json) => _$FilterPreviewResponseFromJson(json);
  
  /// How many archived notices were scored.
  final int considered;

  /// How many of them would have produced a notification.
  final int wouldNotify;
  final int threshold;

  /// The highest scoring of those that pass.
  final List<ScoredNotice> caught;

  /// Those that scored something and still fell short.
  final List<ScoredNotice> missed;

  Map<String, Object?> toJson() => _$FilterPreviewResponseToJson(this);
}
