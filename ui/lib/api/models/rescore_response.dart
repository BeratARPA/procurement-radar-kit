// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

import 'moved_notice.dart';

part 'rescore_response.g.dart';

/// What a rescore changed.
@JsonSerializable()
class RescoreResponse {
  const RescoreResponse({
    required this.considered,
    required this.changed,
    required this.gained,
    required this.lost,
    required this.threshold,
    required this.notable,
    required this.dropped,
  });
  
  factory RescoreResponse.fromJson(Map<String, Object?> json) => _$RescoreResponseFromJson(json);
  
  /// How many archived notices were scored again.
  final int considered;

  /// How many came out with a different number than they carried.
  final int changed;

  /// How many crossed up over the threshold - the point of the exercise.
  final int gained;

  /// How many dropped below it.
  final int lost;
  final int threshold;

  /// The biggest movers, so the change can be sanity-checked by eye.
  final List<MovedNotice> notable;

  /// Everything that stopped qualifying, in full.
  final List<MovedNotice> dropped;

  Map<String, Object?> toJson() => _$RescoreResponseToJson(this);
}
