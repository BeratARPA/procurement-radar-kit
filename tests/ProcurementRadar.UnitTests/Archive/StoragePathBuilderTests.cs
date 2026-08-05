using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Archive;

public sealed class StoragePathBuilderTests
{
    private static readonly StoragePathBuilder Paths = new(Path.Combine(Path.GetTempPath(), "pr-path-tests"));

    [Fact]
    public void The_path_matches_the_layout_in_D_04_exactly()
    {
        var path = Paths.RawFetchPath(
            sourceId: 12,
            fetchedAt: new DateTimeOffset(2026, 3, 9, 14, 5, 0, TimeSpan.Zero),
            rawFetchId: 3471);

        // D-04: data/raw/{source_id}/{yyyy}/{MM}/{dd}/{raw_fetch_id}.html.gz,
        // where data/ is PR_DATA_DIR and therefore not part of the stored value.
        Assert.Equal("raw/12/2026/03/09/3471.html.gz", path);
    }

    [Fact]
    public void Months_and_days_are_padded()
    {
        var path = Paths.RawFetchPath(1, new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero), 5);

        Assert.Equal("raw/1/2026/01/02/5.html.gz", path);
    }

    [Fact]
    public void The_folder_follows_the_utc_date_not_the_local_one()
    {
        // 01:30 on the 10th in Istanbul is still the 9th in UTC. Every other
        // timestamp in the system is UTC (D-08), and the archive has to agree or
        // a day's files end up split across two folders depending on the machine.
        var istanbulEarlyMorning = new DateTimeOffset(2026, 3, 10, 1, 30, 0, TimeSpan.FromHours(3));

        var path = Paths.RawFetchPath(7, istanbulEarlyMorning, 99);

        Assert.Equal("raw/7/2026/03/09/99.html.gz", path);
    }

    [Fact]
    public void Stored_paths_use_forward_slashes_on_every_platform()
    {
        var path = Paths.RawFetchPath(3, DateTimeOffset.UnixEpoch, 4);

        // The row is written on one machine and may be read on another.
        Assert.DoesNotContain('\\', path);
    }

    [Fact]
    public void A_stored_path_that_climbs_out_of_the_archive_is_refused()
    {
        // body_path is data, and data can be wrong or tampered with. Resolving it
        // must not turn into reading an arbitrary file on the host.
        Assert.Throws<ArgumentException>(() => Paths.Absolute("../../../etc/passwd"));
    }

    [Fact]
    public void The_data_directory_comes_from_the_environment_variable()
    {
        Assert.Equal("PR_DATA_DIR", StoragePathBuilder.DataDirectoryVariable);
        Assert.Equal("./data", StoragePathBuilder.DefaultDataDirectory);
    }
}
