using System.Text.Json;
using Serilog;

namespace ProcurementRadar.Cli;

/// <summary>
/// Answers the two questions that stand between a fresh bot and a working
/// notifier: is this token real, and what is the chat id.
/// </summary>
/// <remarks>
/// Both are otherwise found by pasting a url containing the token into a browser,
/// which puts a secret into history, and then reading a chat id out of raw json.
/// This does it without the token ever leaving the machine or appearing in any
/// output: what gets printed is the bot's username and the chats it can see.
/// </remarks>
internal static class TelegramCheckCommand
{
    private const string TokenVariable = "PR_TELEGRAM_BOT_TOKEN";
    private const string ChatVariable = "PR_TELEGRAM_CHAT_ID";

    public static async Task<int> RunAsync(string[] args, CancellationToken cancellationToken)
    {
        var token = Environment.GetEnvironmentVariable(TokenVariable);

        if (string.IsNullOrWhiteSpace(token))
        {
            Log.Error(
                "{Variable} is not set. Create a bot by messaging @BotFather on Telegram, "
                + "send it /newbot, and put the token it gives you in .env. See docs/deployment.md.",
                TokenVariable);

            return 1;
        }

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var me = await CallAsync(http, token, "getMe", cancellationToken).ConfigureAwait(false);

        if (me is null || !me.Value.TryGetProperty("result", out var bot))
        {
            // Deliberately not printing the token or the url that carries it.
            Log.Error(
                "Telegram did not accept the token in {Variable}. Check it was copied whole, "
                + "including the digits before the colon.",
                TokenVariable);

            return 1;
        }

        Log.Information(
            "Token works. Bot is @{Username} ({Name})",
            bot.TryGetProperty("username", out var username) ? username.GetString() : "?",
            bot.TryGetProperty("first_name", out var name) ? name.GetString() : "?");

        var configured = Environment.GetEnvironmentVariable(ChatVariable);

        if (!string.IsNullOrWhiteSpace(configured))
        {
            Log.Information("{Variable} is set to {ChatId}", ChatVariable, configured);

            if (args.Contains("--send", StringComparer.Ordinal))
            {
                return await SendTestAsync(http, token, configured, cancellationToken).ConfigureAwait(false);
            }

            Log.Information("Add --send to post a test message to that chat.");
            return 0;
        }

        // No chat id yet, so find it the only way Telegram allows: look at what
        // the bot has been sent. A bot cannot discover a chat it was never
        // spoken to.
        var updates = await CallAsync(http, token, "getUpdates", cancellationToken).ConfigureAwait(false);
        var chats = Chats(updates);

        if (chats.Count == 0)
        {
            Log.Warning(
                "No chats yet. Add the bot to your group, send any message there, then run this "
                + "again. A bot only sees chats that have spoken to it - Telegram gives it no way "
                + "to look itself up.");

            return 2;
        }

        foreach (var (id, title) in chats)
        {
            Log.Information("Found chat {ChatId} ({Title})", id, title);
        }

        Log.Information(
            "Put the id in {Variable} in .env. Group ids are negative, and the minus sign is "
            + "part of the id.",
            ChatVariable);

        return 0;
    }

    private static async Task<int> SendTestAsync(
        HttpClient http,
        string token,
        string chatId,
        CancellationToken cancellationToken)
    {
        var payload = new { chat_id = chatId, text = ReadConfigMessage() };

        using var content = new StringContent(
            JsonSerializer.Serialize(payload),
            System.Text.Encoding.UTF8,
            "application/json");

        using var response = await http
            .PostAsync($"https://api.telegram.org/bot{token}/sendMessage", content, cancellationToken)
            .ConfigureAwait(false);

        if (response.IsSuccessStatusCode)
        {
            Log.Information("Test message sent. If it arrived, the notifier is ready.");
            return 0;
        }

        Log.Error(
            "Telegram refused the message with {Status}. The usual cause is a chat the bot is "
            + "not a member of, or an id missing its minus sign.",
            (int)response.StatusCode);

        return 1;
    }

    /// <summary>
    /// The Turkish text lives in config, like every other message this project
    /// sends (CLAUDE.md rule 5).
    /// </summary>
    private static string ReadConfigMessage()
    {
        try
        {
            var messages = JsonDocument.Parse(
                ProcurementRadar.Collector.ConfigDirectory.Read(
                    ProcurementRadar.Collector.ConfigDirectory.Find(null),
                    "tr-messages.json"));

            if (messages.RootElement.TryGetProperty("telegram_test", out var text)
                && text.GetString() is { Length: > 0 } value)
            {
                return value;
            }
        }
        catch (Exception exception) when (exception is IOException or JsonException or InvalidOperationException)
        {
            // Falling back is better than failing the check over a message.
        }

        return "Procurement Radar";
    }

    private static async Task<JsonElement?> CallAsync(
        HttpClient http,
        string token,
        string method,
        CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http
                .GetAsync($"https://api.telegram.org/bot{token}/{method}", cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            return JsonDocument.Parse(body).RootElement.Clone();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Error(exception, "Could not reach Telegram");
            return null;
        }
    }

    private static IReadOnlyList<(string Id, string Title)> Chats(JsonElement? updates)
    {
        var found = new Dictionary<string, string>(StringComparer.Ordinal);

        if (updates is null || !updates.Value.TryGetProperty("result", out var result))
        {
            return [];
        }

        foreach (var update in result.EnumerateArray())
        {
            foreach (var key in new[] { "message", "channel_post", "my_chat_member" })
            {
                if (!update.TryGetProperty(key, out var envelope)
                    || !envelope.TryGetProperty("chat", out var chat)
                    || !chat.TryGetProperty("id", out var id))
                {
                    continue;
                }

                var title = chat.TryGetProperty("title", out var groupTitle)
                    ? groupTitle.GetString()
                    : chat.TryGetProperty("username", out var user)
                        ? user.GetString()
                        : chat.TryGetProperty("type", out var type)
                            ? type.GetString()
                            : "?";

                found[id.ToString()] = title ?? "?";
            }
        }

        return found.Select(pair => (pair.Key, pair.Value)).ToList();
    }
}
