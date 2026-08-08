import 'package:flutter/material.dart';

/// One seed, two schemes.
///
/// `useMaterial3` is not passed: it defaults to true on this Flutter, and
/// `copyWith(useMaterial3:)` is already deprecated - passing it now is a line to
/// delete later.
const Color _seed = Color(0xFF2E5E4E);

ThemeData _theme(Brightness brightness) => ThemeData(
      colorScheme: ColorScheme.fromSeed(seedColor: _seed, brightness: brightness),
      visualDensity: VisualDensity.adaptivePlatformDensity,
    );

final ThemeData lightTheme = _theme(Brightness.light);
final ThemeData darkTheme = _theme(Brightness.dark);

/// How urgent a deadline looks.
///
/// The colour is the one thing on a list row read without reading, so it says
/// the only thing that is actually urgent: how long is left. Score is a number
/// on the row; time is not recoverable.
enum Urgency { passed, hours, days, distant, unknown }

Urgency urgencyOf(DateTime? deadlineUtc, DateTime nowUtc) {
  if (deadlineUtc == null) return Urgency.unknown;

  final Duration left = deadlineUtc.difference(nowUtc);

  if (left.isNegative) return Urgency.passed;
  if (left.inHours < 24) return Urgency.hours;
  if (left.inDays < 3) return Urgency.days;

  return Urgency.distant;
}

Color urgencyColor(Urgency urgency, ColorScheme scheme) => switch (urgency) {
      Urgency.passed => scheme.outline,
      Urgency.hours => scheme.error,
      Urgency.days => scheme.tertiary,
      Urgency.distant => scheme.primary,
      Urgency.unknown => scheme.outline,
    };
