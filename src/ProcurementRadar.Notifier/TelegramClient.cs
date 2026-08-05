using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;

namespace ProcurementRadar.Notifier;

public sealed record TelegramOptions
{
    public required string BotToken { get; init; }

    public required string ChatId { get; init; }

    /// <summary>How long Telegram holds a poll open with nothing to say.</summary>
    public int LongPollSeconds { get; init; } = 25;
}

public sealed record TelegramButton(string Label, string CallbackData);

public sealed record TelegramCallback(string Id, string Data, long UpdateId);

/// <summary>
/// The Bot API, spoken directly.
/// </summary>
/// <remarks>
/// No client library: this uses four methods, and a dependency would be more
/// code to keep current than the requests themselves. Deliberately not routed
/// through IHttpFetcher either - the politeness rules exist to keep authority
/// sites happy, and Telegram is neither an authority nor scraped.
/// </remarks>
public sealed class TelegramClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly HttpClient _httpClient;
    private readonly TelegramOptions _options;
    private readonly ILogger<TelegramClient> _logger;

    /// <summary>
    /// Telegram treats asking for updates past an id as confirmation of
    /// everything before it, so nothing has to be persisted: a restart simply
    /// picks up whatever was never confirmed.
    /// </summary>
    private long _offset;

    public TelegramClient(HttpClient httpClient, TelegramOptions options, ILogger<TelegramClient> logger)
    {
        _httpClient = httpClient;
        _options = options;
        _logger = logger;
    }

    public async Task<bool> SendAsync(
        string text,
        IReadOnlyList<TelegramButton> buttons,
        CancellationToken cancellationToken)
    {
        var payload = new
        {
            chat_id = _options.ChatId,
            text,
            disable_web_page_preview = true,
            reply_markup = buttons.Count == 0
                ? null
                : new
                {
                    inline_keyboard = new[]
                    {
                        buttons.Select(button => new
                        {
                            text = button.Label,
                            callback_data = button.CallbackData
                        }).ToArray()
                    }
                }
        };

        return await PostAsync("sendMessage", payload, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TelegramCallback>> GetCallbacksAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            offset = _offset,
            timeout = _options.LongPollSeconds,
            allowed_updates = new[] { "callback_query" }
        };

        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync(Endpoint("getUpdates"), payload, Json, cancellationToken)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Telegram getUpdates answered {StatusCode}", (int)response.StatusCode);
                return [];
            }

            var body = await response.Content
                .ReadFromJsonAsync<UpdatesResponse>(Json, cancellationToken)
                .ConfigureAwait(false);

            if (body is not { Ok: true })
            {
                return [];
            }

            var callbacks = new List<TelegramCallback>();

            foreach (var update in body.Result)
            {
                _offset = Math.Max(_offset, update.UpdateId + 1);

                if (update.CallbackQuery is { Id.Length: > 0, Data.Length: > 0 } query)
                {
                    callbacks.Add(new TelegramCallback(query.Id, query.Data, update.UpdateId));
                }
            }

            return callbacks;
        }
        catch (Exception exception) when (exception is HttpRequestException or JsonException)
        {
            _logger.LogWarning(exception, "Telegram getUpdates failed");
            return [];
        }
    }

    public Task AnswerCallbackAsync(string callbackQueryId, string? text, CancellationToken cancellationToken)
        => PostAsync("answerCallbackQuery", new { callback_query_id = callbackQueryId, text }, cancellationToken);

    private async Task<bool> PostAsync(string method, object payload, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient
                .PostAsJsonAsync(Endpoint(method), payload, Json, cancellationToken)
                .ConfigureAwait(false);

            if (response.IsSuccessStatusCode)
            {
                return true;
            }

            // The body is read for the reason, never the request: the url carries
            // the bot token and must not reach a log file.
            var reason = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);

            _logger.LogError(
                "Telegram {Method} answered {StatusCode}: {Reason}",
                method,
                (int)response.StatusCode,
                Shorten(reason));

            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            _logger.LogError(exception, "Telegram {Method} could not be reached", method);
            return false;
        }
    }

    private string Endpoint(string method) => $"https://api.telegram.org/bot{_options.BotToken}/{method}";

    private static string Shorten(string text) => text.Length <= 300 ? text : text[..300];

    private sealed record UpdatesResponse
    {
        [JsonPropertyName("ok")]
        public bool Ok { get; init; }

        [JsonPropertyName("result")]
        public IReadOnlyList<Update> Result { get; init; } = [];
    }

    private sealed record Update
    {
        [JsonPropertyName("update_id")]
        public long UpdateId { get; init; }

        [JsonPropertyName("callback_query")]
        public CallbackQuery? CallbackQuery { get; init; }
    }

    private sealed record CallbackQuery
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;
    }
}
