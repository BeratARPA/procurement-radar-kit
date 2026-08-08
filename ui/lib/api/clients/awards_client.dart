// coverage:ignore-file
// GENERATED CODE - DO NOT MODIFY BY HAND
// ignore_for_file: type=lint, unused_import, invalid_annotation_target, unnecessary_import

import 'package:dio/dio.dart';
import 'package:retrofit/retrofit.dart';

import '../models/award_candidate.dart';
import '../models/award_summary.dart';

part 'awards_client.g.dart';

@RestApi()
abstract class AwardsClient {
  factory AwardsClient(Dio dio, {String? baseUrl}) = _AwardsClient;

  /// Concluded tenders, newest first, with the estimate beside the contract.
  ///
  /// The pair is the point: 3.405.000 estimated, 3.300.000 signed, one bid.  ///
  /// A notice says what is wanted; only this says what it goes for.
  @GET('/api/awards')
  Future<List<AwardSummary>> listAwards({
    @Query('limit') int? limit,
  });

  /// Tenders that have closed and whose result has not been read yet.
  ///
  /// So that an empty results screen can say why it is empty. This table fills.  ///
  /// slowly on purpose - D-15 asks EKAP about five tenders every six hours,.  ///
  /// and only for notices with an IKN whose deadline passed a fortnight ago,.  ///
  /// because the result announcement goes up after the contract is signed.  ///
  /// Without this number, a screen with nothing on it looks broken rather than.  ///
  /// early.
  @GET('/api/awards/pending')
  Future<List<AwardCandidate>> listPendingAwards();
}
