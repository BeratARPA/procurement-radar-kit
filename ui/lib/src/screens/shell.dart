import 'package:flutter/material.dart';
import 'package:flutter_riverpod/flutter_riverpod.dart';
import 'package:go_router/go_router.dart';

import '../strings.dart';
import '../system/system_providers.dart';

/// Where the width decides the layout.
///
/// Material 3's own window classes: compact below 600, medium to 839, expanded
/// above. Below the first, navigation goes along the bottom where a thumb is;
/// above it, down the side where the content wants the height. The rail only
/// carries its labels once there is room for them.
abstract final class Breakpoints {
  static const double medium = 600;
  static const double expanded = 840;
}

class _Destination {
  const _Destination(
    this.label,
    this.icon,
    this.selectedIcon,
    this.branch, {
    this.wideOnly = false,
  });

  final String label;
  final IconData icon;
  final IconData selectedIcon;

  /// Kept out of the bottom bar.
  ///
  /// Material allows at most five destinations there and there are seven. The
  /// two that go are the two nobody does on a phone: editing a keyword list and
  /// writing CSS selectors both want a keyboard and a wide screen. The routes
  /// still exist, so a link to one still opens.
  final bool wideOnly;

  /// Which shell branch this destination drives.
  ///
  /// Carried explicitly because the list is shorter than the branch list on the
  /// web - Sistem has a branch everywhere and a destination only on the desktop.
  /// Deriving the branch from the position in the list would put a reader on the
  /// wrong screen the moment those two lengths differ.
  final int branch;
}

const List<_Destination> _allDestinations = <_Destination>[
  _Destination(Strings.notices, Icons.list_alt_outlined, Icons.list_alt, 0),
  _Destination(Strings.filter, Icons.tune_outlined, Icons.tune, 1, wideOnly: true),
  _Destination(Strings.sources, Icons.travel_explore_outlined, Icons.travel_explore, 2,
      wideOnly: true),
  _Destination(Strings.awards, Icons.gavel_outlined, Icons.gavel, 3),
  _Destination(Strings.status, Icons.monitor_heart_outlined, Icons.monitor_heart, 4),
  _Destination(Strings.system, Icons.terminal_outlined, Icons.terminal, 5),
  _Destination(Strings.settings, Icons.settings_outlined, Icons.settings, 6),
];

/// One shell, two arrangements of the same destinations.
class AppShell extends ConsumerWidget {
  const AppShell({required this.navigationShell, super.key});

  final StatefulNavigationShell navigationShell;

  void _go(int index) {
    // Tapping the destination you are already in returns to the top of that
    // branch - so a tap on "İlanlar" from inside a notice goes back to the
    // list, which is what a second tap is asking for.
    navigationShell.goBranch(
      index,
      initialLocation: index == navigationShell.currentIndex,
    );
  }

  @override
  Widget build(BuildContext context, WidgetRef ref) {
    // Sistem only exists where a process can be started. On the web the
    // destination is dropped and its branch simply becomes unreachable by tap.
    final bool showSystem = ref.watch(systemSupportedProvider);

    final List<_Destination> available = <_Destination>[
      for (final _Destination destination in _allDestinations)
        if (showSystem || destination.label != Strings.system) destination,
    ];

    return LayoutBuilder(
      builder: (BuildContext context, BoxConstraints constraints) {
        final bool narrow = constraints.maxWidth < Breakpoints.medium;

        final List<_Destination> destinations = <_Destination>[
          for (final _Destination destination in available)
            if (!narrow || !destination.wideOnly) destination,
        ];

        void onSelected(int index) => _go(destinations[index].branch);

        final int selected = destinations.indexWhere(
            (_Destination d) => d.branch == navigationShell.currentIndex);

        // -1 when the branch on screen has no destination in this layout - a
        // bookmarked /system on the web, or /filter on a phone. Highlighting
        // nothing would be truthful and both widgets assert on a negative index.
        final int highlighted = selected < 0 ? 0 : selected;

        if (narrow) {
          return Scaffold(
            body: navigationShell,
            bottomNavigationBar: NavigationBar(
              selectedIndex: highlighted,
              onDestinationSelected: onSelected,
              destinations: <Widget>[
                for (final _Destination destination in destinations)
                  NavigationDestination(
                    icon: Icon(destination.icon),
                    selectedIcon: Icon(destination.selectedIcon),
                    label: destination.label,
                  ),
              ],
            ),
          );
        }

        final bool extended = constraints.maxWidth >= Breakpoints.expanded;

        return Scaffold(
          body: Row(
            children: <Widget>[
              NavigationRail(
                selectedIndex: highlighted,
                onDestinationSelected: onSelected,
                extended: extended,
                labelType: extended
                    ? NavigationRailLabelType.none
                    : NavigationRailLabelType.all,
                destinations: <NavigationRailDestination>[
                  for (final _Destination destination in destinations)
                    NavigationRailDestination(
                      icon: Icon(destination.icon),
                      selectedIcon: Icon(destination.selectedIcon),
                      label: Text(destination.label),
                    ),
                ],
              ),
              const VerticalDivider(width: 1, thickness: 1),
              Expanded(child: navigationShell),
            ],
          ),
        );
      },
    );
  }
}
