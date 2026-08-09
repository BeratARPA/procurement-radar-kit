using ProcurementRadar.Domain.Entities;
using ProcurementRadar.Domain.Normalization;
using ProcurementRadar.Domain.Notifications;
using ProcurementRadar.Domain.Repositories;
using ProcurementRadar.Domain.Settings;
using ProcurementRadar.Scoring;
using ProcurementRadar.UnitTests.Normalization;

namespace ProcurementRadar.UnitTests.Scoring;

public sealed class NoticeRescorerTests
{
    private const string SmallList = """
        {
          "include": [{"term": "yazılım", "weight": 40}],
          "exclude": ["dizüstü"],
          "notify_threshold": 30,
          "rule_score_threshold": 20
        }
        """;

    [Fact]
    public async Task A_notice_the_new_list_matches_is_written_with_its_new_score()
    {
        // The reason this exists. A word added today reaches nothing already
        // collected, because a notice is scored once at parse time - so a CRM
        // tender kept reading 0 the day "crm" was added to the list.
        var scores = new RecordingScoreRepository();

        var result = await Rescore(
            scores,
            new ScorableNotice { NoticeId = 7, Title = "Yazılım Bakım İşi", CurrentScore = 0 });

        Assert.Equal(1, result.Considered);
        Assert.Equal(1, result.Changed);
        Assert.Equal(1, result.Gained);
        Assert.Equal(0, result.Lost);

        var written = Assert.Single(scores.Written);
        Assert.Equal(7, written.NoticeId);
        Assert.Equal(40, written.RuleScore);
        Assert.Equal(RuleScorer.ModelName, written.Model);
    }

    [Fact]
    public async Task A_notice_that_no_longer_matches_is_written_back_down()
    {
        // Removing a term has to reach the archive too, or the list and the
        // stored verdicts drift apart in the direction nobody is watching.
        var scores = new RecordingScoreRepository();

        var result = await Rescore(
            scores,
            new ScorableNotice { NoticeId = 8, Title = "Dizüstü Bilgisayar Alımı", CurrentScore = 40 });

        Assert.Equal(1, result.Changed);
        Assert.Equal(0, result.Gained);
        Assert.Equal(1, result.Lost);
        Assert.Equal(0, Assert.Single(scores.Written).RuleScore);
    }

    [Fact]
    public async Task An_unchanged_notice_is_still_written_but_not_counted_as_moved()
    {
        var scores = new RecordingScoreRepository();

        var result = await Rescore(
            scores,
            new ScorableNotice { NoticeId = 9, Title = "Yazılım Bakım İşi", CurrentScore = 40 });

        Assert.Equal(0, result.Changed);
        Assert.Empty(result.Notable);

        // Written anyway: a notice scored under an older model should carry
        // today's model name even when the two happen to agree.
        Assert.Equal(RuleScorer.ModelName, Assert.Single(scores.Written).Model);
    }

    [Fact]
    public async Task The_llm_score_is_never_part_of_what_is_written()
    {
        // D-03 leaves llm_score null in v1. The moment something does fill it,
        // re-running the cheap filter must not wipe the expensive answer - and
        // the repository can only leave it alone if nothing hands it a value.
        var scores = new RecordingScoreRepository();

        await Rescore(scores, new ScorableNotice { NoticeId = 10, Title = "Yazılım" });

        Assert.Null(Assert.Single(scores.Written).LlmScore);
    }

    [Fact]
    public async Task The_biggest_gains_are_reported_first()
    {
        var result = await Rescore(
            new RecordingScoreRepository(),
            new ScorableNotice { NoticeId = 1, Title = "Yazılım", CurrentScore = 30 },
            new ScorableNotice { NoticeId = 2, Title = "Yazılım", CurrentScore = 0 });

        Assert.Equal([2L, 1L], result.Notable.Select(row => row.NoticeId));
    }

    [Fact]
    public async Task What_went_quiet_is_reported_even_when_the_gains_crowd_it_out()
    {
        // The movers are ordered by how far they rose, so a notice that stopped
        // qualifying sorts last and falls off the end of the list. That is the
        // half of a keyword change nobody goes looking for, so it is reported on
        // its own and never truncated.
        var notices = Enumerable
            .Range(1, 40)
            .Select(id => new ScorableNotice { NoticeId = id, Title = "Yazılım İşi", CurrentScore = 0 })
            .Append(new ScorableNotice { NoticeId = 99, Title = "Dizüstü Alımı", CurrentScore = 40 })
            .ToArray();

        var result = await Rescore(new RecordingScoreRepository(), notices);

        Assert.Equal(1, result.Lost);
        Assert.DoesNotContain(99L, result.Notable.Select(row => row.NoticeId));

        var quiet = Assert.Single(result.Dropped);
        Assert.Equal(99, quiet.NoticeId);
        Assert.Equal(40, quiet.Before);
        Assert.Equal(0, quiet.After);
    }

    private static Task<RescoreResult> Rescore(
        INoticeScoreRepository scores,
        params ScorableNotice[] notices)
    {
        var scorer = new RuleScorer(
            new Fixed<KeywordRules>(KeywordRules.Load(SmallList)),
            new NoticeNormalizer(NoticeNormalizerTests.Rules));

        var rescorer = new NoticeRescorer(
            new StubQueries(notices),
            scores,
            new FakeTimeProvider(DateTimeOffset.Parse("2026-08-09T12:00:00Z")));

        return rescorer.RescoreAsync(scorer, int.MaxValue, CancellationToken.None);
    }

    private sealed class RecordingScoreRepository : INoticeScoreRepository
    {
        public List<NoticeScore> Written { get; } = [];

        public Task UpsertAsync(NoticeScore score, CancellationToken cancellationToken)
        {
            Written.Add(score);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>Answers the one read a rescore makes and throws on the rest.</summary>
    private sealed class StubQueries(IReadOnlyList<ScorableNotice> notices) : ICommandQueries
    {
        public Task<IReadOnlyList<ScorableNotice>> ListForRescoreAsync(
            int limit,
            CancellationToken cancellationToken)
            => Task.FromResult(notices);

        public Task<IReadOnlyList<CommandNotice>> ListOpenAsync(int minimumScore, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CommandNotice>> ListSeenSinceAsync(DateTimeOffset since, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<SystemStatus> GetStatusAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CommandNotice>> ListAllAsync(int minimumScore, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CommandNotice>> SearchAsync(string term, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<NoticeDetail?> GetDetailAsync(string ikn, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<NoticeDetail?> GetDetailByIdAsync(long noticeId, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<SourceHealth>> ListTroubledSourcesAsync(int silentDays, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<ScoreByStatus>> GetScoreReportAsync(CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PreviewNotice>> ListForPreviewAsync(int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<CommandNotice>> ListClosingSoonAsync(int withinHours, int limit, CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
