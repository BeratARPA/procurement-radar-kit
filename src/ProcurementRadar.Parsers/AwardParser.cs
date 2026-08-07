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
    private readonly AwardLabels _labels;
    private readonly ITurkishDateParser _dates;
    private readonly string _terminator;

    public AwardParser(AwardLabels labels, ITurkishDateParser dates)
    {
        _labels = labels;
        _dates = dates;

        // A value runs until the next lettered item ("b) Bedeli"), the next
        // numbered section ("4- Sözleşmenin"), or the end of the announcement.
        var letters = Regex.Escape(_labels.ItemLetters);
        _terminator = $@"(?=\s+[{letters}]\)\s|\s+\d+\s*-\s|$)";
    }

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
        if (!announcementText.Contains(_labels.ResultMarker, StringComparison.Ordinal))
        {
            return null;
        }

        // Everything about the contract is read from its own section. Before it,
        // "Tarihi" means the day the tender was held and "Bedeli" does not
        // appear at all; taking the first match would silently record the wrong
        // date on every award.
        var contractIndex = announcementText.IndexOf(_labels.ContractSection, StringComparison.Ordinal);
        var contract = contractIndex >= 0 ? announcementText[contractIndex..] : string.Empty;

        var award = new ParsedAward
        {
            // Not masked. A winner is the counterparty on a public contract,
            // published by the state in its own announcement - it is not the
            // contact name and phone number CLAUDE.md keeps out of the database,
            // and an award nobody can be identified with is an award worth
            // nothing. Only these four fields are taken; the rest of the text,
            // which does carry an officer and a switchboard number, is not stored
            // by this path at all.
            WinnerName = Value(contract, _labels.WinnerLabel),
            ContractAmount = TurkishMoneyParser.TryParse(Value(contract, _labels.AmountLabel)),
            BidCount = Count(announcementText),
            AwardedAt = _dates.TryParse(Value(contract, _labels.DateLabel))
        };

        return award.IsEmpty ? null : award;
    }

    private string? Value(string text, string label)
    {
        if (text.Length == 0)
        {
            return null;
        }

        // The colon has to follow the label immediately, which is what keeps
        // "Yüklenici :" apart from "Yüklenicinin uyruğu :" two rows below it.
        var match = Regex.Match(
            text,
            Regex.Escape(label) + @"\s*:\s*(?<value>\S.*?)" + _terminator,
            RegexOptions.CultureInvariant,
            TimeSpan.FromSeconds(2));

        return match.Success ? match.Groups["value"].Value.Trim() : null;
    }

    /// <summary>
    /// How many bids arrived. Read from the whole text: it sits in the bids
    /// section, above the contract.
    /// </summary>
    private int? Count(string text)
    {
        // The valid-bid row is read first and preferred. A tender with four bids
        // of which one was valid was not a contested tender, and the number worth
        // keeping is the one that says so.
        var value = Value(text, _labels.ValidBidCountLabel) ?? Value(text, _labels.BidCountLabel);

        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var count)
            ? count
            : null;
    }
}
