// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

import 'checked_notice.dart';

part 'source_check_response.g.dart';

/// What a set of selectors made of a page.
@JsonSerializable()
class SourceCheckResponse {
  const SourceCheckResponse({
    required this.source,
    required this.byteCount,
    required this.noticeCount,
    required this.withDeadline,
    required this.withDocuments,
    required this.sample,
    required this.warnings,
    this.problemCode,
    this.problem,
    this.fetchedAt,
    this.charSet,
  });
  
  factory SourceCheckResponse.fromJson(Map<String, Object?> json) => _$SourceCheckResponseFromJson(json);
  
  /// Why nothing could be checked, as a code. Nothing else is set.
  final String? problemCode;

  /// The parser's own complaint about the config, verbatim.
  final String? problem;

  /// Where the page came from: the archive, or the request.
  final String source;

  /// When the archived page was fetched. Null for a pasted one.
  final DateTime? fetchedAt;

  /// How the bytes were decoded (D-11).
  final String? charSet;
  final int byteCount;
  final int noticeCount;
  final int withDeadline;
  final int withDocuments;

  /// The first few, so a person can see whether they are the right rows.
  final List<CheckedNotice> sample;

  /// What looks wrong, as codes. Empty when nothing does.
  final List<String> warnings;

  Map<String, Object?> toJson() => _$SourceCheckResponseToJson(this);
}
