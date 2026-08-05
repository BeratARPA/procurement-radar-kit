using ProcurementRadar.Domain.Enums;
using ProcurementRadar.Notifier;

namespace ProcurementRadar.UnitTests.Notifications;

public sealed class CallbackDataTests
{
    [Theory]
    [InlineData(NoticeStatus.Reviewed, 1)]
    [InlineData(NoticeStatus.NotRelevant, 987654321)]
    [InlineData(NoticeStatus.BidSubmitted, 42)]
    public void A_button_payload_survives_the_round_trip(NoticeStatus status, long noticeId)
    {
        Assert.True(CallbackData.TryParse(CallbackData.For(status, noticeId), out var parsed, out var id));

        Assert.Equal(status, parsed);
        Assert.Equal(noticeId, id);
    }

    [Fact]
    public void The_payload_uses_the_same_spelling_as_the_status_column()
    {
        // If the two ever drift, a button press writes a status nothing else
        // recognises.
        Assert.Equal("not_relevant:5", CallbackData.For(NoticeStatus.NotRelevant, 5));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("nonsense")]
    [InlineData("reviewed:")]
    [InlineData(":5")]
    [InlineData("archived:5")]
    [InlineData("reviewed:abc")]
    [InlineData("reviewed:-1")]
    public void Anything_else_is_refused_rather_than_guessed_at(string? data)
    {
        Assert.False(CallbackData.TryParse(data, out _, out _));
    }

    [Fact]
    public void Telegram_caps_callback_data_at_64_bytes_and_the_payload_fits()
    {
        var longest = CallbackData.For(NoticeStatus.NotRelevant, long.MaxValue);

        Assert.True(
            System.Text.Encoding.UTF8.GetByteCount(longest) <= 64,
            $"'{longest}' is {System.Text.Encoding.UTF8.GetByteCount(longest)} bytes");
    }
}
