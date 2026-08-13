@TestOn('windows')
library;

import 'dart:io';

import 'package:flutter_test/flutter_test.dart';
import 'package:procurement_radar/src/system/system_control_native.dart';

/// Whether the binary that is running was built from the code that is here.
///
/// The panel's most useful line and the one that has been wrong twice, in
/// opposite directions:
///
///   * It walked every .cs under src/, including the Cli and the Migrator,
///     neither of which is published - so editing a cli command announced that
///     the running api was stale.
///   * It then compared every project against publish/ProcurementRadar.Api.dll,
///     taking that timestamp for "when publish last ran". `dotnet publish` is
///     incremental and only rewrites assemblies whose content changed. On
///     13 August one project was rebuilt and seven correctly were not, and this
///     reported the deployment a day out of date.
///
/// A warning that is wrong is a warning people learn to scroll past, which is
/// worse than not having it - so both directions are pinned here.
void main() {
  late Directory root;
  late NativeSystemControl control;

  /// A directory the control will accept as a checkout.
  Future<void> makeCheckout() async {
    root = await Directory.systemTemp.createTemp('radar_stale_');
    File('${root.path}\\docker-compose.yml').writeAsStringSync('services:');
    File('${root.path}\\ProcurementRadar.sln').writeAsStringSync('sln');
    Directory('${root.path}\\publish').createSync();

    control = NativeSystemControl();
    expect(await control.useProjectRoot(root.path), isTrue);
  }

  /// A project with one source file, and optionally an assembly beside it.
  void project(String name, {required bool published, required int sourceAgeMinutes}) {
    final Directory dir = Directory('${root.path}\\src\\$name')
      ..createSync(recursive: true);

    final DateTime now = DateTime.now();

    if (published) {
      final File dll = File('${root.path}\\publish\\$name.dll')
        ..writeAsStringSync('MZ');
      dll.setLastModifiedSync(now.subtract(const Duration(hours: 2)));
    }

    final File source = File('${dir.path}\\Thing.cs')..writeAsStringSync('class Thing {}');
    source.setLastModifiedSync(
      now.subtract(Duration(hours: 2)).add(Duration(minutes: sourceAgeMinutes)),
    );
  }

  setUp(makeCheckout);
  tearDown(() => root.deleteSync(recursive: true));

  test('says nothing when every assembly is newer than its own sources', () async {
    project('ProcurementRadar.Api', published: true, sourceAgeMinutes: -30);
    project('ProcurementRadar.Worker', published: true, sourceAgeMinutes: -10);

    expect(await control.staleDeployment(), isNull);
  });

  test('a project rebuilt today does not make the others look stale', () async {
    // 13 August exactly: Notifier changed and was republished, the other seven
    // did not change and kept their old timestamps. Nothing here is stale.
    project('ProcurementRadar.Api', published: true, sourceAgeMinutes: -60);
    project('ProcurementRadar.Notifier', published: true, sourceAgeMinutes: -1);

    File('${root.path}\\publish\\ProcurementRadar.Notifier.dll')
        .setLastModifiedSync(DateTime.now());

    expect(await control.staleDeployment(), isNull);
  });

  test('names the project whose source moved after its assembly', () async {
    project('ProcurementRadar.Api', published: true, sourceAgeMinutes: -60);
    project('ProcurementRadar.Scoring', published: true, sourceAgeMinutes: 45);

    final String? warning = await control.staleDeployment();

    expect(warning, isNotNull);
    expect(warning, contains('ProcurementRadar.Scoring'));
    expect(warning, isNot(contains('ProcurementRadar.Api')));
  });

  test('ignores a project that is not published at all', () async {
    // The Cli and the Migrator live under src/ and are never published. Editing
    // one of them is not a stale deployment, and saying so was the first bug.
    project('ProcurementRadar.Api', published: true, sourceAgeMinutes: -60);
    project('ProcurementRadar.Cli', published: false, sourceAgeMinutes: 90);

    expect(await control.staleDeployment(), isNull);
  });

  test('build output under a project is not mistaken for source', () async {
    project('ProcurementRadar.Api', published: true, sourceAgeMinutes: -60);

    // bin/ and obj/ are newer than the publish by definition; counting them
    // would make this warn for ever.
    final Directory bin = Directory('${root.path}\\src\\ProcurementRadar.Api\\obj\\Debug')
      ..createSync(recursive: true);
    File('${bin.path}\\Generated.cs')
      ..writeAsStringSync('// generated')
      ..setLastModifiedSync(DateTime.now());

    expect(await control.staleDeployment(), isNull);
  });
}
