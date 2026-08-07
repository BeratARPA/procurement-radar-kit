using System.Text.Json;
using System.Text.Json.Serialization;

namespace ProcurementRadar.Parsers;

/// <summary>
/// The Turkish labels a result announcement uses, read from
/// <c>config/tr-award-labels.json</c>.
/// </summary>
/// <remarks>
/// Same reason as the keyword lists and the date patterns: the source text is
/// Turkish, so the words are data. When EKAP renames a row - and it renames rows
/// - the fix is a config edit, not a rebuild.
/// </remarks>
public sealed record AwardLabels
{
    [JsonPropertyName("result_marker")]
    public required string ResultMarker { get; init; }

    [JsonPropertyName("contract_section")]
    public required string ContractSection { get; init; }

    [JsonPropertyName("winner_label")]
    public required string WinnerLabel { get; init; }

    [JsonPropertyName("amount_label")]
    public required string AmountLabel { get; init; }

    [JsonPropertyName("date_label")]
    public required string DateLabel { get; init; }

    [JsonPropertyName("bid_count_label")]
    public required string BidCountLabel { get; init; }

    [JsonPropertyName("valid_bid_count_label")]
    public required string ValidBidCountLabel { get; init; }

    [JsonPropertyName("item_letters")]
    public required string ItemLetters { get; init; }

    public static AwardLabels Load(string json)
        => JsonSerializer.Deserialize<AwardLabels>(json)
           ?? throw new InvalidOperationException("tr-award-labels.json is empty.");
}
