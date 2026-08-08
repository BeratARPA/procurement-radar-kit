// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'setting_change_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

SettingChangeRequest _$SettingChangeRequestFromJson(
  Map<String, dynamic> json,
) => SettingChangeRequest(
  value: json['value'] as String,
  updatedBy: json['updatedBy'] as String?,
);

Map<String, dynamic> _$SettingChangeRequestToJson(
  SettingChangeRequest instance,
) => <String, dynamic>{
  'value': instance.value,
  'updatedBy': instance.updatedBy,
};
