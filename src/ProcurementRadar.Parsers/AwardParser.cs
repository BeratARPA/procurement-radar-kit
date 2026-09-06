using System.Globalization;
using System.Text.RegularExpressions;
using ProcurementRadar.Domain.Parsing;

namespace ProcurementRadar.Parsers;

/// <summary>What a concluded tender ended up as: who won, for how much, against how many.</summary>
public sealed record ParsedAward
{
    public string? WinnerName { get; init; }

    public decimal? ContractAmount { get; init; }

    public int? BidCount { get; init; }

    public DateTimeOffset? AwardedAt { get; init; }

    /// <summary>Nothing was found worth writing down.</summary>
    public bool IsEmpty
        => WinnerName is null && ContractAmount is null && BidCount is null && AwardedAt is null;
}

/// <summary>
/// Reads the result announcement EKAP publishes once a tender is concluded.
/// </summary>
/// <remarks>
/// The archive is the point of this project (spec Bolum 16), and until now it
/// recorded what was asked for and never what it went for. The gap matters for
/// pricing: the notice says the estimated cost was 3.405.000 TRY, the result says
/// the contract was signed at 3.300.000 against a single bid. Which authority
/// awards near its estimate, how many bidders actually turn up, who keeps winning
/// - none of that is answerable without this table.
///
/// A pure function over text, like every other parser here (architecture rule 1).
/// The browser round trip that produces the text lives in AwardWorker.
/// </remarks>
public sealed class AwardParser
{
    private readonly ProcurementRadar.Domain.Settings.ICurrent<AwardLabels> _labels;
    private readonly ITurkishDateParser _dates;

    public AwardParser(
        ProcurementRadar.Domain.Settings.ICurrent<AwardLabels> labels,
        ITurkishDateParser dates)
    {
        _labels = labels;
        _dates = dates;
    }

    /// <summary>
    /// Where a value ends: the next lettered item ("b) Bedeli"), the next
    /// numbered section ("4- Sözleşmenin"), or the end of the announcement.
    /// </summary>
    /// <remarks>
    /// Built per parse rather than once in the constructor, because the letters
    /// it is built from are a setting now and this object outlives a change to
    /// them. Two string joins against a browser round trip is nothing.
    /// </remarks>
    private static string Terminator(AwardLabels labels)
        => $@"(?=\s+[{Regex.Escape(labels.ItemLetters)}]\)\s|\s+\d+\s*-\s|$)";

    /// <summary>
    /// Returns null when the text is not a result announcement at all - which is
    /// the ordinary case, because the same fetch returns the tender announcement
    /// for anything still open.
    /// </summary>
    public ParsedAward? Parse(string? announcementText)
    {
        if (string.IsNullOrWhiteSpace(announcementText))
        {
            return null;
        }

        // Ordinal: the marker is already upper case in both the config and the
        // page, and Turkish case folding is the wrong tool for deciding whether
        // two identical strings are identical.
        var labels = _labels.Value;

        if (!announcementText.Contains(labels.ResultMarker, StringComparison.Ordinal))
        {
            return null;
        }

        // Everything about the contract is read from its own section. Before it,
        // "Tarihi" means the day the tender was held and "Bedeli" does not
        // appear at all; taking the first match would silently record the wrong
        // date on every award.
        var contractIndex = announcementText.IndexOf(labels.ContractSection, StringComparison.Ordinal);
        var contract = contractIndex >= 0 ? announcementText[contractIndex..] : string.Empty;

        var terminator = Terminator(labels);

        var award = new ParsedAward
        {
            // Not masked. A winner is the counterparty on a public contract,
            // published by the state in its own announcement - it is not the
            // contact name and phone number CLAUDE.md keeps out of the database,
            // and an award nobody can be identified with is an award worth
            // nothing. Only these four fields are taken; the rest of the text,
            // which does carry an officer and a switchboard number, is not stored
            // by this path at all.
            WinnerName = FirstValue(contract, terminator, labels.WinnerLabels),
            ContractAmount = TurkishMoneyParser.TryParse(Value(contract, terminator, labels.AmountLabel)),
            BidCount = Count(announcementText, terminator),
            AwardedAt = _dates.TryParse(Value(contract, terminator, labels.DateLabel))
        };

        return award.IsEmpty ? null : award;
    }

    /// <summary>The first of the wordings that matches, in the order given.</summary>
    /// <remarks>
    /// Order is the config's, and it puts the current spelling first so the
    /// common case is one regex. The older ones stay because announcements
    /// published before the rename are still out there in the archive, and
    /// reprocessing them is the whole reason the archive exists.
    /// </remarks>
    private static string? FirstValue(string text, string terminator, IReadOnlyList<string> labels)
    {
        foreach (var label in labels)
        {
            if (Value(text, terminator, label) is { Length: > 0 } value)
            {
                return value;
            }
        }

        return null;
    }

    private static string? Value(string text, string terminator, string label)
    {
        if (text.Length == 0)
        {
            return null;
        }

        // The colon has to follow the label immediately, which is what keeps
        // "Yüklenici :" apart from "Yüklenicinin uyruğu :" two rows below it.
        var match = Regex.Match(
            text,
            Regex.Escape(label) + @"\s*:\s*(?<value>\S.*?)" + terminator,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>
    /// How many bids arrived. Read from the whole text: it sits in the bids
    /// section, above the contract.
    /// </summary>
    private int? Count(string text, string terminator)
    {
        // The valid-bid row is read first and preferred. A tender with four bids
        // of which one was valid was not a contested tender, and the number worth
        // keeping is the one that says so.
        var value = Value(text, terminator, _labels.Value.ValidBidCountLabel) ?? Value(text, terminator, _labels.Value.BidCountLabel);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
    }
}
