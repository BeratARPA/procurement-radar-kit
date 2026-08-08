// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'setting.g.dart';

@JsonSerializable()
class Setting {
  const Setting({
    required this.key,
    required this.value,
    required this.updatedAt,
    this.updatedBy,
  });
  
  factory Setting.fromJson(Map<String, Object?> json) => _$SettingFromJson(json);
  
  final String key;
  final String value;
  final DateTime updatedAt;
  final String? updatedBy;

  Map<String, Object?> toJson() => _$SettingToJson(this);
}
