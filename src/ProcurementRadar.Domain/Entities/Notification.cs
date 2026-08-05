using ProcurementRadar.Domain.Enums;

namespace ProcurementRadar.Domain.Entities;

/// <summary>One delivery attempt. Table <c>notification</c>.</summary>
public sealed record Notification
{
    public long Id { get; init; }

    public required long NoticeId { get; init; }

    public required NotificationChannel Channel { get; init; }

    public DateTimeOffset? SentAt { get; init; }

    /// <summary>
    /// Free text. The spec does not fix a value set for delivery outcomes, so
    /// this is deliberately not an enum.
    /// </summary>
    public string? Status { get; init; }
}
