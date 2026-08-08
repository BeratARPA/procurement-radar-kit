// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'filter_preview_request.g.dart';

@JsonSerializable()
class FilterPreviewRequest {
  const FilterPreviewRequest({
    required this.keywords,
    this.threshold,
    this.limit,
  });
  
  factory FilterPreviewRequest.fromJson(Map<String, Object?> json) => _$FilterPreviewRequestFromJson(json);
  
  /// A keyword list in the shape of config/keywords.json.
  final String keywords;

  /// Overrides the list's own notify_threshold when given.
  final int? threshold;

  /// How much of the archive to score. Everything, by default.
  final int? limit;

  Map<String, Object?> toJson() => _$FilterPreviewRequestToJson(this);
}
