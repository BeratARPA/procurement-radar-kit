using ProcurementRadar.Collector;

namespace ProcurementRadar.UnitTests.Fetching;

public sealed class RobotsRulesTests
{
    private const string Us = "ProcurementRadar";

    [Fact]
    public void A_file_with_no_rules_permits_everything()
    {
        Assert.True(RobotsRules.Parse("", Us).IsAllowed("/ihaleler"));
    }

    [Fact]
    public void A_disallow_line_with_nothing_after_it_means_everything_is_permitted()
    {
        var rules = RobotsRules.Parse("User-agent: *\nDisallow:", Us);

        Assert.True(rules.IsAllowed("/anything"));
    }

    [Fact]
    public void The_wildcard_group_applies_when_we_are_not_named()
    {
        var rules = RobotsRules.Parse("User-agent: *\nDisallow: /admin", Us);

        Assert.False(rules.IsAllowed("/admin/login"));
        Assert.True(rules.IsAllowed("/ihaleler"));
    }

    [Fact]
    public void A_group_that_names_us_wins_over_the_wildcard()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /

            User-agent: ProcurementRadar
            Disallow: /admin
            """,
            Us);

        Assert.True(rules.IsAllowed("/ihaleler"));
        Assert.False(rules.IsAllowed("/admin"));
    }

    [Fact]
    public void Consecutive_agent_lines_share_one_group()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: SomeoneElse
            User-agent: ProcurementRadar
            Disallow: /gizli
            """,
            Us);

        Assert.False(rules.IsAllowed("/gizli/belge"));
    }

    [Fact]
    public void The_longest_matching_rule_wins()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /duyuru
            Allow: /duyuru/ihale
            """,
            Us);

        Assert.False(rules.IsAllowed("/duyuru/personel"));
        Assert.True(rules.IsAllowed("/duyuru/ihale/2026"));
    }

    [Fact]
    public void Allow_breaks_a_tie_of_equal_length()
    {
        var rules = RobotsRules.Parse(
            """
            User-agent: *
            Disallow: /x
            Allow: /x
            """,
            Us);

        Assert.True(rules.IsAllowed("/x/y"));
    }

    [Fact]
    public void A_star_inside_a_path_matches_any_run_of_characters()
    {
        var rules = RobotsRules.Parse("User-agent: *\nDisallow: /*.pdf", Us);

        Assert.False(rules.IsAllowed("/dosyalar/sartname.pdf"));
        Assert.True(rules.IsAllowed("/dosyalar/sartname.html"));
    }

    [Fact]
    public void A_trailing_dollar_anchors_the_end_of_the_path()
    {
        var rules = RobotsRules.Parse("User-agent: *\nDisallow: /ihale$", Us);

        Assert.False(rules.IsAllowed("/ihale"));
        Assert.True(rules.IsAllowed("/ihale/liste"));
    }

    [Fact]
    public void Comments_and_odd_spacing_are_ignored()
    {
        var rules = RobotsRules.Parse(
            """
            # kurum robots dosyasi
            User-agent:   *      # herkes
              Disallow:  /admin
            """,
            Us);

        Assert.False(rules.IsAllowed("/admin"));
        Assert.True(rules.IsAllowed("/ihaleler"));
    }

    [Fact]
    public void Agent_matching_ignores_case()
    {
        var rules = RobotsRules.Parse("user-agent: procurementradar\ndisallow: /", Us);

        Assert.False(rules.IsAllowed("/ihaleler"));
    }

    [Fact]
    public void Deny_all_refuses_the_root()
    {
        Assert.False(RobotsRules.DenyAll.IsAllowed("/"));
        Assert.False(RobotsRules.DenyAll.IsAllowed("/ihaleler"));
    }
}
