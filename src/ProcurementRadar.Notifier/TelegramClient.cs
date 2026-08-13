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

public sealed record TelegramCallback(string Id, string Data, long UpdateId, long MessageId);

/// <summary>A slash command somebody typed, and whatever followed it.</summary>
public sealed record TelegramCommand(string Name, string Argument, long UpdateId);

/// <summary>Everything one poll returned. One poller, because the offset is shared.</summary>
public sealed record TelegramUpdates(
    IReadOnlyList<TelegramCallback> Callbacks,
    IReadOnlyList<TelegramCommand> Commands);

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

    /// <summary>
    /// One long poll for button presses and commands.
    /// </summary>
    /// <remarks>
    /// Throws when Telegram could not be asked at all, and that is deliberate.
    /// This used to catch the transport failure and return nothing, which reads
    /// as "asked, nothing there" - and the caller loops immediately on that,
    /// because a successful long poll is what normally paces it. So an
    /// unreachable Telegram turned a blocking loop into a spin: on 13 August
    /// api.telegram.org stopped resolving for 37 minutes and this wrote 25,925
    /// stack traces, twelve a second. Over four days: 85,711 lines, and the
    /// bulk of a 318 MB log directory.
    ///
    /// CallbackWorker already had the answer - a 30 second pause after a
    /// failure - and could never reach it, because nothing ever failed as far
    /// as it could tell.
    /// </remarks>
    public async Task<TelegramUpdates> GetUpdatesAsync(CancellationToken cancellationToken)
    {
        var payload = new
        {
            offset = _offset,
            timeout = _options.LongPollSeconds,
            // Both kinds on one poll: two pollers would fight over the offset and
            // Telegram answers the second with a 409.
            allowed_updates = new[] { "callback_query", "message" }
        };

        using var response = await _httpClient
            .PostAsJsonAsync(Endpoint("getUpdates"), payload, Json, cancellationToken)
            .ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            // Also a throw, and for the same reason as the transport failure
            // above. The two answers that actually turn up here are 429, which
            // is Telegram asking to be left alone, and 409, which means a second
            // poller is running - and returning nothing on either put the caller
            // straight back into the same call with no pause at all. Answering a
            // rate limit by asking twelve times a second is the one response
            // guaranteed to make it last longer.
            throw new HttpRequestException(
                $"Telegram getUpdates answered {(int)response.StatusCode}.",
                null,
                response.StatusCode);
        }

        var body = await response.Content
            .ReadFromJsonAsync<UpdatesResponse>(Json, cancellationToken)
            .ConfigureAwait(false);

        if (body is not { Ok: true })
        {
            return Empty;
        }

        var callbacks = new List<TelegramCallback>();
        var commands = new List<TelegramCommand>();

        foreach (var update in body.Result)
        {
            _offset = Math.Max(_offset, update.UpdateId + 1);

            if (update.CallbackQuery is { Id.Length: > 0, Data.Length: > 0 } query)
            {
                callbacks.Add(new TelegramCallback(
                    query.Id,
                    query.Data,
                    update.UpdateId,
                    query.Message?.MessageId ?? 0));
            }

            // A command, and only the name of it. Telegram appends @thebot in
            // groups, and anything after a space is an argument no command
            // here takes.
            if (update.Message?.Text is { Length: > 1 } text && text[0] == '/')
            {
                // Telegram appends @thebot in groups. Everything after the
                // first space is the argument, kept whole - a search term can
                // have spaces in it and splitting further would lose them.
                var space = text.IndexOf(' ', StringComparison.Ordinal);
                var name = (space < 0 ? text : text[..space]).Split('@')[0];
                var argument = space < 0 ? string.Empty : text[(space + 1)..].Trim();

                commands.Add(new TelegramCommand(name, argument, update.UpdateId));
            }
        }

        return new TelegramUpdates(callbacks, commands);
    }

    /// <summary>
    /// Replaces the buttons under a message that has already been sent.
    /// </summary>
    /// <remarks>
    /// This is the only place the team can see what it decided. There is no web
    /// interface and there is not going to be one in v1 (spec Bölüm 2), so the
    /// message has to carry its own state: press a button and the chosen option
    /// comes back ticked. A toast says it happened; this says what it is.
    ///
    /// Both buttons stay live afterwards, because deciding again is normal - a
    /// notice dismissed on the title often looks different once the şartname has
    /// been read.
    /// </remarks>
    public async Task<bool> EditButtonsAsync(
        long messageId,
        IReadOnlyList<TelegramButton> buttons,
        CancellationToken cancellationToken)
    {
        if (messageId == 0)
        {
            return false;
        }

        var payload = new
        {
            chat_id = _options.ChatId,
            message_id = messageId,
            reply_markup = new
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

        return await PostAsync("editMessageReplyMarkup", payload, cancellationToken).ConfigureAwait(false);
    }

    private static readonly TelegramUpdates Empty = new([], []);

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

        [JsonPropertyName("message")]
        public IncomingMessage? Message { get; init; }
    }

    private sealed record IncomingMessage
    {
        [JsonPropertyName("text")]
        public string? Text { get; init; }
    }

    private sealed record CallbackQuery
    {
        [JsonPropertyName("id")]
        public string Id { get; init; } = string.Empty;

        [JsonPropertyName("data")]
        public string Data { get; init; } = string.Empty;

        /// <summary>The message the button belongs to, so it can be marked.</summary>
        [JsonPropertyName("message")]
        public CallbackMessage? Message { get; init; }
    }

    private sealed record CallbackMessage
    {
        [JsonPropertyName("message_id")]
        public long MessageId { get; init; }
    }
}
