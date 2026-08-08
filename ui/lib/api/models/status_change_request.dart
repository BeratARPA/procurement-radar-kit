// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'status_change_request.g.dart';

/// The bodies the interface sends.
@JsonSerializable()
class StatusChangeRequest {
  const StatusChangeRequest({
    required this.status,
  });
  
  factory StatusChangeRequest.fromJson(Map<String, Object?> json) => _$StatusChangeRequestFromJson(json);
  
  /// reviewed, bid_submitted, missed or not_relevant.
  final String status;

  Map<String, Object?> toJson() => _$StatusChangeRequestToJson(this);
}
