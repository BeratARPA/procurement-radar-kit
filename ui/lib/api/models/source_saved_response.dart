// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'source_saved_response.g.dart';

/// Whether a definition created a source or refreshed one.
@JsonSerializable()
class SourceSavedResponse {
  const SourceSavedResponse({
    required this.id,
    required this.wasCreated,
  });
  
  factory SourceSavedResponse.fromJson(Map<String, Object?> json) => _$SourceSavedResponseFromJson(json);
  
  final int id;
  final bool wasCreated;

  Map<String, Object?> toJson() => _$SourceSavedResponseToJson(this);
}
