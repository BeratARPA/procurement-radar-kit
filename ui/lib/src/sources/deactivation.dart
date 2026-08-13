import '../../api/export.dart';
import '../strings.dart';

/// A reason a source is switched off, as the screen presents it.
///
/// The set is closed on the server (D-22) because the question being asked of it
/// is "which of these might have healed", and free text cannot be asked that.
/// The screen's job is to make the consequence of the choice visible: three of
/// the six put the source in a queue that is re-probed once a day, and three
/// take it out of circulation for good.
class DeactivationChoice {
  const DeactivationChoice({
    required this.reason,
    required this.label,
    required this.help,
    required this.retried,
  });

  final DeactivationReason reason;
  final String label;
  final String help;

  /// Whether the sweeper will look at this source again.
  final bool retried;

  /// What a person may choose.
  ///
  /// `health_rule` is not here and the api refuses it: it means ten consecutive
  /// failures, which only the monitor can observe. Offering it would let someone
  /// record a diagnosis the system did not make.
  ///
  /// The two that are re-probed come first. That ordering is the advice: if the
  /// site might come back, saying so is what brings it back.
  static const List<DeactivationChoice> selectable = <DeactivationChoice>[
    DeactivationChoice(
      reason: DeactivationReason.siteUnreachable,
      label: Strings.reasonSiteUnreachable,
      help: Strings.reasonSiteUnreachableHelp,
      retried: true,
    ),
    DeactivationChoice(
      reason: DeactivationReason.manual,
      label: Strings.reasonManual,
      help: Strings.reasonManualHelp,
      retried: false,
    ),
    DeactivationChoice(
      reason: DeactivationReason.noTenderList,
      label: Strings.reasonNoTenderList,
      help: Strings.reasonNoTenderListHelp,
      retried: false,
    ),
    DeactivationChoice(
      reason: DeactivationReason.robots,
      label: Strings.reasonRobots,
      help: Strings.reasonRobotsHelp,
      retried: false,
    ),
    DeactivationChoice(
      reason: DeactivationReason.superseded,
      label: Strings.reasonSuperseded,
      help: Strings.reasonSupersededHelp,
      retried: false,
    ),
  ];

  /// How a stored reason reads in a row, including the one nobody can choose.
  static DeactivationChoice? describe(DeactivationReason? reason) {
    if (reason == null || reason == DeactivationReason.$unknown) return null;

    if (reason == DeactivationReason.healthRule) {
      return const DeactivationChoice(
        reason: DeactivationReason.healthRule,
        label: Strings.reasonHealthRule,
        help: Strings.reasonHealthRuleHelp,
        retried: true,
      );
    }

    for (final DeactivationChoice choice in selectable) {
      if (choice.reason == reason) return choice;
    }

    return null;
  }
}
