namespace ProcurementRadar.Parsers.Tests;

public sealed class FixtureLayoutTests
{
    // Every adapter keeps its captured HTML under fixtures/{adapter_key}/. If the
    // folder ever stops being copied to the output directory, every parser test
    // fails with a file-not-found instead of saying what is actually wrong.
    [Fact]
    public void Fixture_root_is_copied_next_to_the_test_assembly()
    {
        var fixtureRoot = Path.Combine(AppContext.BaseDirectory, "fixtures");

        Assert.True(Directory.Exists(fixtureRoot), $"Expected the fixture root at '{fixtureRoot}'.");
    }
}
