using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Fetching;

/// <summary>
/// The two ways a fetch can be refused must stay distinguishable.
/// </summary>
/// <remarks>
/// Both stop the fetch, so a test that only checks IsAllowed cannot tell them
/// apart - and neither could the log until 08.08.2026, which is how a day of an
/// unreachable ODTÜ server got read as a university forbidding us. One of these
/// means take the source off the list; the other means try again in an hour.
/// </remarks>
public sealed class RobotsRefusalTests
{
    [Fact]
    public void Both_refusals_stop_the_fetch()
    {
        Assert.False(RobotsRules.DenyAll.IsAllowed("/ihaleler"));
        Assert.False(RobotsRules.Unreadable.IsAllowed("/ihaleler"));
    }

    [Fact]
    public void Only_a_written_rule_is_the_hosts_own_decision()
    {
        Assert.True(RobotsRules.DenyAll.IsRefusalByPolicy);
        Assert.False(RobotsRules.Unreadable.IsRefusalByPolicy);
    }

    /// <summary>A parsed Disallow is the host's decision, like DenyAll.</summary>
    [Fact]
    public void A_parsed_disallow_is_a_policy_refusal()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /
            """,
            "ProcurementRadar");

        Assert.False(rules.IsAllowed("/duyurular"));
        Assert.True(rules.IsRefusalByPolicy);
    }

    /// <summary>And an ordinary allow-all is not a refusal at all.</summary>
    [Fact]
    public void An_open_host_stays_open()
    {
        Assert.True(RobotsRules.AllowAll.IsAllowed("/duyurular"));
        Assert.True(RobotsRules.AllowAll.IsRefusalByPolicy);
    }
}
