using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Scoring;
using ProcurementRadar.UnitTests.Normalization;

namespace ProcurementRadar.UnitTests.Scoring;

public sealed class RuleScorerTests
{
    private static readonly KeywordRules Rules = KeywordRules.Load(
        File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "config", "keywords.json")));

    private static readonly RuleScorer Scorer =
        new(new Fixed<KeywordRules>(Rules), new NoticeNormalizer(NoticeNormalizerTests.Rules));

    [Fact]
    public void A_hardware_purchase_is_ruled_out()
    {
        var score = Scorer.Score("Bilgisayar Alımı");

        Assert.Equal(ScoreBasis.Excluded, score.Basis);
        Assert.Equal(0, score.Value);
        Assert.Equal("bilgisayar alımı", score.ExcludedBy);
    }

    [Fact]
    public void A_software_maintenance_notice_scores_well()
    {
        var score = Scorer.Score("Yazılım Bakım ve Destek Hizmeti Alımı");

        Assert.Equal(ScoreBasis.Keywords, score.Basis);
        Assert.True(
            score.Value >= Rules.RuleScoreThreshold,
            $"scored {score.Value}, under the {Rules.RuleScoreThreshold} threshold");
        Assert.Contains("yazılım bakım", score.MatchedTerms);
    }

    [Fact]
    public void Exclude_wins_when_a_notice_matches_both_lists()
    {
        // The config says so, and the reason is in it: a title like this is
        // hardware nine times out of ten.
        var score = Scorer.Score("Bilgisayar Alımı ve Yazılım Kurulumu");

        Assert.Equal(ScoreBasis.Excluded, score.Basis);
        Assert.Equal(0, score.Value);
    }

    [Theory]
    [InlineData("Yazılımı güncellenecektir")]
    [InlineData("Yazılımın bakımı yapılacaktır")]
    [InlineData("Otomasyona geçiş")]
    public void Turkish_endings_do_not_hide_a_term(string title)
    {
        // The language glues its endings on. A term has to match the start of a
        // word and let the rest run.
        Assert.NotEmpty(Scorer.Score(title).MatchedTerms);
    }

    [Theory]
    [InlineData("Bina Yapım İşi")]
    [InlineData("Çatı Yapımı")]
    public void A_short_term_does_not_match_inside_another_word(string title)
    {
        // "api" is a term worth fifteen points and it sits inside "yapım".
        // Substring matching would score a building contract as software.
        Assert.DoesNotContain("api", Scorer.Score(title).MatchedTerms);
    }

    [Fact]
    public void An_unrelated_notice_scores_nothing()
    {
        var score = Scorer.Score("Temizlik Hizmeti Alımı");

        Assert.Equal(0, score.Value);
        Assert.Empty(score.MatchedTerms);
        Assert.Equal(ScoreBasis.Keywords, score.Basis);
    }

    [Fact]
    public void Several_matches_add_up_but_stop_at_a_hundred()
    {
        var score = Scorer.Score(
            "Yazılım Bakım, Yazılım Destek, Yazılım Geliştirme, Mobil Uygulama, "
            + "Web Sitesi, Portal, Entegrasyon, Otomasyon ve API Hizmeti");

        Assert.Equal(100, score.Value);
    }

    [Fact]
    public void The_description_counts_as_well_as_the_title()
    {
        var fromTitleAlone = Scorer.Score("Modül Ekleme İşi");
        var withDescription = Scorer.Score("Modül Ekleme İşi", "Mevcut personel takip sistemine ek modül");

        Assert.Empty(fromTitleAlone.MatchedTerms);
        Assert.NotEmpty(withDescription.MatchedTerms);
    }

    [Fact]
    public void The_rationale_names_the_terms_that_produced_the_number()
    {
        var score = Scorer.Score("Mobil Uygulama Geliştirme");

        Assert.Contains("mobil uygulama", score.Rationale, StringComparison.Ordinal);
    }

    [Fact]
    public void Okas_codes_are_ignored_while_nobody_has_filled_the_lists_in()
    {
        // The mechanism is there and inert. Until the team fills the prefixes in,
        // a notice with codes scores exactly as it did before.
        Assert.False(Rules.Okas.IsConfigured);

        var withCodes = Scorer.Score("Yazılım Bakım Hizmeti", okasCodes: ["72000000"]);
        var withoutCodes = Scorer.Score("Yazılım Bakım Hizmeti");

        Assert.Equal(withoutCodes.Value, withCodes.Value);
        Assert.Equal(ScoreBasis.Keywords, withCodes.Basis);
    }

    [Fact]
    public void A_configured_okas_code_beats_the_words()
    {
        // Spec Bölüm 9: the code says the same thing however the title is worded.
        var configured = new RuleScorer(
            new Fixed<KeywordRules>(KeywordRules.Load("""
                {
                  "include": [ { "term": "yazılım", "weight": 30 } ],
                  "exclude": [ "toner" ],
                  "okas": { "include_prefixes": ["72"], "exclude_prefixes": ["301"], "include_score": 90 },
                  "rule_score_threshold": 30,
                  "notify_threshold": 60
                }
                """)),
            new NoticeNormalizer(NoticeNormalizerTests.Rules));

        var included = configured.Score("Bir başlık, hiçbir kelime eşleşmiyor", okasCodes: ["72261000"]);

        Assert.Equal(ScoreBasis.Okas, included.Basis);
        Assert.Equal(90, included.Value);

        var excluded = configured.Score("Yazılım Bakım Hizmeti", okasCodes: ["30125100"]);

        Assert.Equal(ScoreBasis.Excluded, excluded.Basis);
        Assert.Equal(0, excluded.Value);
    }

    [Fact]
    public void The_thresholds_come_from_the_config_file()
    {
        // Read, not hardcoded. The values themselves are a product decision that
        // moves, so this pins the mechanism rather than the numbers.
        var rules = KeywordRules.Load(
            """
            {
              "include": [{ "term": "yazilim", "weight": 30 }],
              "exclude": [],
              "rule_score_threshold": 11,
              "notify_threshold": 22
            }
            """);

        Assert.Equal(11, rules.RuleScoreThreshold);
        Assert.Equal(22, rules.NotifyThreshold);
    }

    [Fact]
    public void One_strong_keyword_is_enough_to_be_told_about()
    {
        // The invariant that actually matters, and the one that failed in
        // practice. With the notify threshold above every single term's weight, a
        // notice had to match two of them to be announced. A tender for a
        // "Hastane Bilgi Yonetim Sistemi" - months of work - matched one, scored
        // thirty against a threshold of sixty, and was never mentioned. Nothing
        // was broken. The system was quietly set to require a coincidence.
        var heaviest = Rules.Include.Max(term => term.Weight);

        Assert.True(
            heaviest >= Rules.NotifyThreshold,
            $"No single keyword can reach the notify threshold: the heaviest term is {heaviest} "
            + $"and the threshold is {Rules.NotifyThreshold}, so every notification needs two "
            + "separate matches and real work goes unreported.");
    }

    [Fact]
    public void A_config_with_no_include_terms_is_refused()
    {
        // Silently scoring everything zero is the failure nobody notices.
        Assert.Throws<InvalidOperationException>(() => KeywordRules.Load("""
            { "include": [], "exclude": [] }
            """));
    }
}
