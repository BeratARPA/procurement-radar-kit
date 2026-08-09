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

/// How many destinations Material puts along the bottom.
///
/// Its own limit, not a taste: NavigationBar asserts above it.
const int _barLimit = 5;

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

  /// First to move into the overflow sheet when the bar runs out of room.
  ///
  /// Material allows at most five destinations along the bottom and there are
  /// seven. These two go first because they are the two nobody does on a phone:
  /// editing a keyword list and writing CSS selectors both want a keyboard and
  /// a wide screen.
  ///
  /// They move, they do not disappear. The first version of this dropped them
  /// from the bar entirely, and since nothing else in the interface links to
  /// them, narrowing the window made two whole screens unreachable - the routes
  /// were still there and there was no way to walk to them.
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

        if (narrow) {
          // Material's bottom bar takes five. Anything past that goes into a
          // sheet behind a "Diğer" item, which occupies the fifth slot - so the
          // bar itself holds four when there is an overflow and five when there
          // is not.
          final List<_Destination> bar =
              available.where((_Destination d) => !d.wideOnly).toList();
          List<_Destination> overflow =
              available.where((_Destination d) => d.wideOnly).toList();

          final int room = overflow.isEmpty ? _barLimit : _barLimit - 1;

          while (bar.length > room) {
            overflow = <_Destination>[bar.removeLast(), ...overflow];
          }

          // Back into the order the list declares them in, so the sheet reads
          // like the rail does rather than like the order they were evicted in.
          overflow = <_Destination>[
            for (final _Destination destination in available)
              if (overflow.any((_Destination d) => d.branch == destination.branch))
                destination,
          ];

          final int inBar = bar.indexWhere(
              (_Destination d) => d.branch == navigationShell.currentIndex);
          final bool inSheet = overflow.any(
              (_Destination d) => d.branch == navigationShell.currentIndex);

          return Scaffold(
            body: navigationShell,
            bottomNavigationBar: NavigationBar(
              // The "Diğer" item lights up while one of its screens is open, so
              // the bar is never highlighting somewhere the reader is not.
              selectedIndex: inBar >= 0 ? inBar : (inSheet ? bar.length : 0),
              onDestinationSelected: (int index) {
                if (index < bar.length) {
                  _go(bar[index].branch);
                  return;
                }

                showModalBottomSheet<void>(
                  context: context,
                  showDragHandle: true,
                  builder: (BuildContext sheetContext) => SafeArea(
                    child: Column(
                      mainAxisSize: MainAxisSize.min,
                      children: <Widget>[
                        for (final _Destination destination in overflow)
                          ListTile(
                            leading: Icon(
                              destination.branch == navigationShell.currentIndex
                                  ? destination.selectedIcon
                                  : destination.icon,
                            ),
                            title: Text(destination.label),
                            selected: destination.branch ==
                                navigationShell.currentIndex,
                            onTap: () {
                              Navigator.of(sheetContext).pop();
                              _go(destination.branch);
                            },
                          ),
                      ],
                    ),
                  ),
                );
              },
              destinations: <Widget>[
                for (final _Destination destination in bar)
                  NavigationDestination(
                    icon: Icon(destination.icon),
                    selectedIcon: Icon(destination.selectedIcon),
                    label: destination.label,
                  ),
                if (overflow.isNotEmpty)
                  const NavigationDestination(
                    icon: Icon(Icons.more_horiz),
                    label: Strings.more,
                  ),
              ],
            ),
          );
        }

        final List<_Destination> destinations = available;

        void onSelected(int index) => _go(destinations[index].branch);

        final int selected = destinations.indexWhere(
            (_Destination d) => d.branch == navigationShell.currentIndex);

        // -1 when the branch on screen has no destination in this layout - a
        // bookmarked /system on the web. Highlighting nothing would be truthful
        // and the widget asserts on a negative index.
        final int highlighted = selected < 0 ? 0 : selected;

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
