namespace ProcurementRadar.Api.Contracts;

/// <summary>One row an adapter config found on the page.</summary>
public sealed record CheckedNotice
{
    public required string Title { get; init; }

    public required string SourceUrl { get; init; }

    /// <summary>Null when the date could not be read from this row.</summary>
    public DateTimeOffset? BidDeadlineAt { get; init; }

    public required int DocumentCount { get; init; }
}

/// <summary>
/// What a set of selectors made of a page.
/// </summary>
/// <remarks>
/// The counts matter more than the rows. A config that finds thirty titles and
/// not one date usually has the date selector pointing at the wrong cell rather
/// than an authority that omits dates - and the bid deadline is the field
/// spec Bolum 7 calls the most important one.
/// </remarks>
public sealed record SourceCheckResponse
{
    /// <summary>
    /// Why nothing could be checked, as a code. Nothing else is set.
    /// </summary>
    /// <remarks>
    /// A code and not a sentence, because the sentence would be Turkish and
    /// Turkish does not go in a .cs file (CLAUDE.md, and there is a test).
    /// The interface holds every word it shows in one place; this says which one.
    /// </remarks>
    public string? ProblemCode { get; init; }

    /// <summary>
    /// The parser's own complaint about the config, verbatim.
    /// </summary>
    /// <remarks>
    /// English and developer-facing, which is right: it names the field that is
    /// wrong, and the person reading it is writing CSS selectors.
    /// </remarks>
    public string? Problem { get; init; }

    /// <summary>Where the page came from: the archive, or the request.</summary>
    public required string Source { get; init; }

    /// <summary>When the archived page was fetched. Null for a pasted one.</summary>
    public DateTimeOffset? FetchedAt { get; init; }

    /// <summary>How the bytes were decoded (D-11).</summary>
    public string? CharSet { get; init; }

    public required int ByteCount { get; init; }

    public required int NoticeCount { get; init; }

    public required int WithDeadline { get; init; }

    public required int WithDocuments { get; init; }

    /// <summary>The first few, so a person can see whether they are the right rows.</summary>
    public required IReadOnlyList<CheckedNotice> Sample { get; init; }

    /// <summary>What looks wrong, as codes. Empty when nothing does.</summary>
    public required IReadOnlyList<string> Warnings { get; init; }
}

/// <summary>Why a check could not run.</summary>
public static class CheckProblems
{
    public const string ConfigUnusable = "config_unusable";
    public const string NoArchivedPage = "no_archived_page";
    public const string NothingToCheck = "nothing_to_check";
}

/// <summary>What a check noticed about the result.</summary>
public static class CheckWarnings
{
    /// <summary>Selectors matched nothing, or the page has no notices today.</summary>
    public const string NoRows = "no_rows";

    /// <summary>Rows were found and not one date could be read.</summary>
    public const string NoDeadlineRead = "no_deadline_read";

    /// <summary>Fewer than half the rows gave a date.</summary>
    public const string FewDeadlinesRead = "few_deadlines_read";
}

/// <summary>Whether a definition created a source or refreshed one.</summary>
public sealed record SourceSavedResponse
{
    public required long Id { get; init; }

    public required bool WasCreated { get; init; }
}

/// <summary>One notice as a proposed filter would have scored it.</summary>
public sealed record ScoredNotice
{
    public required long NoticeId { get; init; }

    public required string Title { get; init; }

    public required string AuthorityName { get; init; }

    public required int Score { get; init; }
}

/// <summary>
/// What a keyword list and a threshold would have done to the notices already
/// collected.
/// </summary>
/// <remarks>
/// Both halves are returned. What a filter catches is easy to be pleased with;
/// what it just misses is where the threshold is actually decided - at 30, a
/// maintenance contract for an ERP system scores 25 and is never mentioned.
/// </remarks>
public sealed record FilterPreviewResponse
{
    /// <summary>How many archived notices were scored.</summary>
    public required int Considered { get; init; }

    /// <summary>How many of them would have produced a notification.</summary>
    public required int WouldNotify { get; init; }

    public required int Threshold { get; init; }

    /// <summary>The highest scoring of those that pass.</summary>
    public required IReadOnlyList<ScoredNotice> Caught { get; init; }

    /// <summary>Those that scored something and still fell short.</summary>
    public required IReadOnlyList<ScoredNotice> Missed { get; init; }
}

/// <summary>What a rescore changed.</summary>
public sealed record RescoreResponse
{
    /// <summary>How many archived notices were scored again.</summary>
    public required int Considered { get; init; }

    /// <summary>How many came out with a different number than they carried.</summary>
    public required int Changed { get; init; }

    /// <summary>How many crossed up over the threshold - the point of the exercise.</summary>
    public required int Gained { get; init; }

    /// <summary>How many dropped below it.</summary>
    public required int Lost { get; init; }

    public required int Threshold { get; init; }

    /// <summary>The biggest movers, so the change can be sanity-checked by eye.</summary>
    public required IReadOnlyList<MovedNotice> Notable { get; init; }

    /// <summary>
    /// Everything that stopped qualifying, in full.
    /// </summary>
    /// <remarks>
    /// Separate from the movers because those are ordered by how far they rose,
    /// so a notice that went quiet sorts last and gets cut - and that is the half
    /// of a keyword change worth looking at.
    /// </remarks>
    public required IReadOnlyList<MovedNotice> Dropped { get; init; }
}

public sealed record MovedNotice
{
    public required long NoticeId { get; init; }

    public required string Title { get; init; }

    public required int Before { get; init; }

    public required int After { get; init; }
}
