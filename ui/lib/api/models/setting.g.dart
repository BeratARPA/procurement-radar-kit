// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'setting.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

Setting _$SettingFromJson(Map<String, dynamic> json) => Setting(
  key: json['key'] as String,
  value: json['value'] as String,
  updatedAt: DateTime.parse(json['updatedAt'] as String),
  updatedBy: json['updatedBy'] as String?,
);

Map<String, dynamic> _$SettingToJson(Setting instance) => <String, dynamic>{
  'key': instance.key,
  'value': instance.value,
  'updatedAt': instance.updatedAt.toIso8601String(),
  'updatedBy': instance.updatedBy,
};
