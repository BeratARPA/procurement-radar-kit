// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'source_check_request.g.dart';

/// Asks what an adapter config would make of a page.
@JsonSerializable()
class SourceCheckRequest {
  const SourceCheckRequest({
    required this.adapterConfig,
    this.sourceId,
    this.html,
    this.url,
  });
  
  factory SourceCheckRequest.fromJson(Map<String, Object?> json) => _$SourceCheckRequestFromJson(json);
  
  /// The selectors to try.
  final String adapterConfig;

  /// Test against this source's most recent archived page.
  final int? sourceId;

  /// A page pasted in, for a source that has never been fetched.
  final String? html;

  /// The page's own address, so relative links resolve as they would.
  final String? url;

  Map<String, Object?> toJson() => _$SourceCheckRequestToJson(this);
}
