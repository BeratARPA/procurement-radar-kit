// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

part 'source_definition_request.g.dart';

/// A source definition as the interface sends it.
@JsonSerializable()
class SourceDefinitionRequest {
  const SourceDefinitionRequest({
    required this.authorityName,
    required this.authorityType,
    required this.province,
    required this.url,
    required this.adapterKey,
    this.district,
    this.adapterConfig,
    this.pollIntervalMinutes,
    this.notes,
  });
  
  factory SourceDefinitionRequest.fromJson(Map<String, Object?> json) => _$SourceDefinitionRequestFromJson(json);
  
  final String authorityName;

  /// university, municipality, provincial_directorate, hospital, soe_subsidiary or ekap.
  final String authorityType;
  final String province;
  final String? district;
  final String url;

  /// generic_table, json_list, ekap_tender, ekap_direct_procurement.
  final String adapterKey;

  /// The selectors, as json. Checked before it is stored.
  final String? adapterConfig;
  final int? pollIntervalMinutes;

  /// Why this source is here, or why it is switched off.
  final String? notes;

  Map<String, Object?> toJson() => _$SourceDefinitionRequestToJson(this);
}
