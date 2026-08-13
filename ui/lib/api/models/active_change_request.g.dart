// GENERATED CODE - DO NOT MODIFY BY HAND

part of 'active_change_request.dart';

// **************************************************************************
// JsonSerializableGenerator
// **************************************************************************

ActiveChangeRequest _$ActiveChangeRequestFromJson(Map<String, dynamic> json) =>
    ActiveChangeRequest(
      isActive: json['isActive'] as bool,
      reason: json['reason'] as String?,
    );

Map<String, dynamic> _$ActiveChangeRequestToJson(
  ActiveChangeRequest instance,
) => <String, dynamic>{
  'isActive': instance.isActive,
  'reason': instance.reason,
};
