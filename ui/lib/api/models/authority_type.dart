// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

@JsonEnum()
enum AuthorityType {
  @JsonValue('university')
  university('university'),
  @JsonValue('municipality')
  municipality('municipality'),
  @JsonValue('provincial_directorate')
  provincialDirectorate('provincial_directorate'),
  @JsonValue('hospital')
  hospital('hospital'),
  @JsonValue('soe_subsidiary')
  soeSubsidiary('soe_subsidiary'),
  @JsonValue('ekap')
  ekap('ekap'),
  /// Default value for all unparsed values, allows backward compatibility when adding new values on the backend.
  $unknown(null);

  const AuthorityType(this.json);

  factory AuthorityType.fromJson(String json) => values.firstWhere(
        (e) => e.json == json,
        orElse: () => $unknown,
      );

  final String? json;

  @override
  String toString() => json?.toString() ?? super.toString();
  /// Returns all defined enum values excluding the $unknown value.
  static List<AuthorityType> get $valuesDefined => values.where((value) => value != $unknown).toList();
}
