// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'active_change_request.g.dart';

@JsonSerializable()
class ActiveChangeRequest {
  const ActiveChangeRequest({
    required this.isActive,
    this.reason,
  });
  
  factory ActiveChangeRequest.fromJson(Map<String, Object?> json) => _$ActiveChangeRequestFromJson(json);
  
  /// True puts the source back in the rotation and clears its failure count.
  final bool isActive;

  /// Why it is being switched off: `site_unreachable`, `manual`,.  ///
  /// `no_tender_list`, `robots` or `superseded`. Ignored when.  ///
  /// switching one on.
  final String? reason;

  Map<String, Object?> toJson() => _$ActiveChangeRequestToJson(this);
}
