import 'package:intl/intl.dart';

import 'strings.dart';

/// Dates arrive UTC and must be shown local.
///
/// D-08 calls the bid deadline the most critical field in the project and says a
/// one hour drift means a missed job. The api stores timestamptz and serialises
/// UTC, so `DateTime.parse` hands back a UTC `DateTime` and `DateFormat` prints
/// whatever zone the value is in - printing it straight shows 11:00 for a
/// deadline of 14:00 and looks entirely plausible.
///
/// So: convert at the moment of rendering, and nowhere earlier. Everything above
/// this file keeps UTC, which is also what makes the arithmetic below correct.
final DateFormat _dayAndTime = DateFormat('d MMMM y HH:mm', 'tr_TR');
final DateFormat _day = DateFormat('d MMMM y', 'tr_TR');

String formatDeadline(DateTime? deadlineUtc) {
  if (deadlineUtc == null) return Strings.deadlineUnknown;

  return _dayAndTime.format(deadlineUtc.toLocal());
}

String formatDay(DateTime? instantUtc) {
  if (instantUtc == null) return Strings.never;

  return _day.format(instantUtc.toLocal());
}

String formatMoment(DateTime? instantUtc) {
  if (instantUtc == null) return Strings.never;

  return _dayAndTime.format(instantUtc.toLocal());
}

/// "kalan 31 saat", the way spec Bolum 10 writes it.
///
/// Both sides are UTC, so this is a plain subtraction with no zone in it. Under
/// an hour it counts minutes, because at that point hours round to a shrug.
String remaining(DateTime? deadlineUtc, DateTime nowUtc) {
  if (deadlineUtc == null) return Strings.deadlineUnknown;

  final Duration left = deadlineUtc.difference(nowUtc);

  if (left.isNegative) return Strings.deadlinePassed;
  if (left.inMinutes < 60) return 'kalan ${left.inMinutes} dakika';
  if (left.inHours < 48) return 'kalan ${left.inHours} saat';

  return 'kalan ${left.inDays} gün';
}

/// Just the clock, for a log line.
///
/// Takes an already-local time, unlike everything above it: log timestamps are
/// converted where they are read, because that is the only place that knows they
/// came out of a file rather than off the wire.
String formatClock(DateTime local) =>
    '${local.hour.toString().padLeft(2, '0')}:'
    '${local.minute.toString().padLeft(2, '0')}:'
    '${local.second.toString().padLeft(2, '0')}';

/// What the status column holds, in words.
///
/// The values are the ones the schema stores; the api takes the same spellings
/// back (spec Bolum 4).
String statusLabel(String status) => switch (status) {
      'new' => Strings.statusNew,
      'reviewed' => Strings.statusReviewed,
      'bid_submitted' => Strings.statusBidSubmitted,
      'missed' => Strings.statusMissed,
      'not_relevant' => Strings.statusNotRelevant,
      _ => status,
    };
