using Microsoft.Extensions.Logging;
using ProcurementRadar.Domain.Health;

namespace ProcurementRadar.Notifier;

/// <summary>Sends alarms to the group.</summary>
public sealed class TelegramAlertChannel : IAlertChannel
{
    private readonly TelegramClient _telegram;

    public TelegramAlertChannel(TelegramClient telegram)
    {
        _telegram = telegram;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
        => _telegram.SendAsync(message, [], cancellationToken);
}

/// <summary>
/// Where alarms go before the bot exists.
/// </summary>
/// <remarks>
/// Health monitoring is the part that must not wait on setup: a system nobody
/// has finished wiring up is exactly when a source goes silent unnoticed. The
/// alarms land in the log until there is somewhere better to put them.
/// </remarks>
public sealed class LoggingAlertChannel : IAlertChannel
{
    private readonly ILogger<LoggingAlertChannel> _logger;

    public LoggingAlertChannel(ILogger<LoggingAlertChannel> logger)
    {
        _logger = logger;
    }

    public Task SendAsync(string message, CancellationToken cancellationToken)
    {
        _logger.LogWarning("Health alarm, with no Telegram configured to send it to: {Message}", message);

        return Task.CompletedTask;
    }
}
