import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:intl/date_symbol_data_local.dart';
import 'package:intl/intl.dart';

import 'src/app.dart';
import 'src/data/connection_store.dart';
import 'src/data/providers.dart';

Future<void> main() async {
  WidgetsFlutterBinding.ensureInitialized();

  // Must happen before the first DateFormat(..., 'tr_TR'). Without it the app
  // compiles, starts, and throws on the first notice it renders - which is
  // every notice, because every one of them has a date on it.
  await initializeDateFormatting('tr_TR');
  Intl.defaultLocale = 'tr_TR';

  // Read once, here, so that everything downstream - including the router's
  // redirect - is synchronous.
  final Connection connection = await ConnectionStore().read();

  runApp(
    ProviderScope(
      // No type argument: Riverpod 3 does not export the type of this list, and
      // the parameter infers it anyway.
      overrides: [initialConnectionProvider.overrideWithValue(connection)],
      child: const RadarApp(),
    ),
  );
}
