// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'problem_details.g.dart';

@JsonSerializable()
class ProblemDetails {
  const ProblemDetails({
    this.type,
    this.title,
    this.status,
    this.detail,
    this.instance,
  });
  
  factory ProblemDetails.fromJson(Map<String, Object?> json) => _$ProblemDetailsFromJson(json);
  
  final String? type;
  final String? title;
  final int? status;
  final String? detail;
  final String? instance;

  Map<String, Object?> toJson() => _$ProblemDetailsToJson(this);
}
