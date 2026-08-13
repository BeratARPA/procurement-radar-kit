// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:json_annotation/json_annotation.dart';

@JsonEnum()
enum DeactivationReason {
  @JsonValue('health_rule')
  healthRule('health_rule'),
  @JsonValue('site_unreachable')
  siteUnreachable('site_unreachable'),
  @JsonValue('manual')
  manual('manual'),
  @JsonValue('no_tender_list')
  noTenderList('no_tender_list'),
  @JsonValue('robots')
  robots('robots'),
  @JsonValue('superseded')
  superseded('superseded'),
  /// Default value for all unparsed values, allows backward compatibility when adding new values on the backend.
  $unknown(null);

  const DeactivationReason(this.json);

  factory DeactivationReason.fromJson(String json) => values.firstWhere(
        (e) => e.json == json,
        orElse: () => $unknown,
      );

  final String? json;

  @override
  String toString() => json?.toString() ?? super.toString();
  /// Returns all defined enum values excluding the $unknown value.
  static List<DeactivationReason> get $valuesDefined => values.where((value) => value != $unknown).toList();
}
