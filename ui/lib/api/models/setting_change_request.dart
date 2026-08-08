// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'setting_change_request.g.dart';

@JsonSerializable()
class SettingChangeRequest {
  const SettingChangeRequest({
    required this.value,
    this.updatedBy,
  });
  
  factory SettingChangeRequest.fromJson(Map<String, Object?> json) => _$SettingChangeRequestFromJson(json);
  
  /// The setting as json text, validated with the loader that will read it.
  final String value;

  /// Who changed it. Recorded in the history beside the value.
  final String? updatedBy;

  Map<String, Object?> toJson() => _$SettingChangeRequestToJson(this);
}
