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
  const _Destination(this.label, this.icon, this.selectedIcon, this.branch);

  final String label;
  final IconData icon;
  final IconData selectedIcon;

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
  _Destination(Strings.status, Icons.monitor_heart_outlined, Icons.monitor_heart, 1),
  _Destination(Strings.system, Icons.terminal_outlined, Icons.terminal, 2),
  _Destination(Strings.settings, Icons.settings_outlined, Icons.settings, 3),
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

    final List<_Destination> destinations = <_Destination>[
      for (final _Destination destination in _allDestinations)
        if (showSystem || destination.label != Strings.system) destination,
    ];

    final int selected = destinations
        .indexWhere((_Destination d) => d.branch == navigationShell.currentIndex);

    return LayoutBuilder(
      builder: (BuildContext context, BoxConstraints constraints) {
        void onSelected(int index) => _go(destinations[index].branch);

        // -1 when the current branch has no destination, which is what a
        // bookmarked /system on the web produces. Highlighting nothing would be
        // truthful but both widgets assert on a negative index.
        final int highlighted = selected < 0 ? 0 : selected;

        if (constraints.maxWidth < Breakpoints.medium) {
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
