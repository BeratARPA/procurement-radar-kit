// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'system_status.g.dart';

@JsonSerializable()
class SystemStatus {
  const SystemStatus({
    required this.activeSources,
    required this.failingSources,
    required this.notices,
    required this.noticesToday,
    required this.announced,
    this.lastFetchAt,
  });
  
  factory SystemStatus.fromJson(Map<String, Object?> json) => _$SystemStatusFromJson(json);
  
  final int activeSources;
  final int failingSources;
  final int notices;
  final int noticesToday;
  final int announced;
  final DateTime? lastFetchAt;

  Map<String, Object?> toJson() => _$SystemStatusToJson(this);
}
