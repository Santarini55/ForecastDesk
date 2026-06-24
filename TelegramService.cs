using System.Net.Http.Headers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ForecastDesk;

public sealed class TelegramService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    public async Task SendMessageAsync(
        string token,
        string chatId,
        string text,
        string messageThreadId = "",
        CancellationToken cancellationToken = default)
    {
        Validate(token, chatId);
        var url = BuildUrl(token, "sendMessage");
        var fields = new Dictionary<string, string>
        {
            ["chat_id"] = chatId.Trim(),
            ["text"] = text
        };
        AddMessageThreadId(fields, messageThreadId);

        using var form = new FormUrlEncodedContent(fields);
        await SendAsync(url, form, cancellationToken);
    }

    public async Task SendPhotoAsync(
        string token,
        string chatId,
        string imagePath,
        string caption,
        string messageThreadId = "",
        CancellationToken cancellationToken = default)
    {
        Validate(token, chatId);

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("Скриншот не найден.", imagePath);
        }

        if (caption.Length > 1000)
        {
            await SendPhotoAsync(token, chatId, imagePath, "", messageThreadId, cancellationToken);
            await SendMessageAsync(token, chatId, caption, messageThreadId, cancellationToken);
            return;
        }

        var url = BuildUrl(token, "sendPhoto");
        await using var file = File.OpenRead(imagePath);
        using var content = new MultipartFormDataContent();
        content.Add(new StringContent(chatId.Trim(), Encoding.UTF8), "chat_id");
        AddMessageThreadId(content, messageThreadId);
        if (!string.IsNullOrWhiteSpace(caption))
        {
            content.Add(new StringContent(caption, Encoding.UTF8), "caption");
        }

        var fileContent = new StreamContent(file);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
        content.Add(fileContent, "photo", Path.GetFileName(imagePath));

        await SendAsync(url, content, cancellationToken);
    }

    private static void AddMessageThreadId(IDictionary<string, string> fields, string messageThreadId)
    {
        if (!string.IsNullOrWhiteSpace(messageThreadId))
        {
            fields["message_thread_id"] = messageThreadId.Trim();
        }
    }

    private static void AddMessageThreadId(MultipartFormDataContent content, string messageThreadId)
    {
        if (!string.IsNullOrWhiteSpace(messageThreadId))
        {
            content.Add(new StringContent(messageThreadId.Trim(), Encoding.UTF8), "message_thread_id");
        }
    }

    private static async Task SendAsync(string url, HttpContent content, CancellationToken cancellationToken)
    {
        using var response = await Http.PostAsync(url, content, cancellationToken);
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            if (TryGetMigrateToChatId(body, out var migratedChatId))
            {
                throw new TelegramChatMigratedException(migratedChatId);
            }

            if (IsMessageThreadNotFound(body))
            {
                throw new TelegramMessageThreadNotFoundException();
            }

            if (IsChatWriteForbidden(body))
            {
                throw new TelegramWriteForbiddenException();
            }

            throw new InvalidOperationException($"Telegram HTTP {(int)response.StatusCode}: {body}");
        }

        using var document = JsonDocument.Parse(body);
        if (!document.RootElement.TryGetProperty("ok", out var ok) || !ok.GetBoolean())
        {
            if (TryGetMigrateToChatId(body, out var migratedChatId))
            {
                throw new TelegramChatMigratedException(migratedChatId);
            }

            if (IsMessageThreadNotFound(body))
            {
                throw new TelegramMessageThreadNotFoundException();
            }

            if (IsChatWriteForbidden(body))
            {
                throw new TelegramWriteForbiddenException();
            }

            var description = document.RootElement.TryGetProperty("description", out var value)
                ? value.GetString()
                : "неизвестная ошибка";
            throw new InvalidOperationException($"Telegram отклонил запрос: {description}");
        }
    }

    private static bool TryGetMigrateToChatId(string body, out string chatId)
    {
        chatId = "";
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("parameters", out var parameters)
                && parameters.TryGetProperty("migrate_to_chat_id", out var migrateToChatId))
            {
                chatId = migrateToChatId.ValueKind == JsonValueKind.Number
                    ? migrateToChatId.GetInt64().ToString(CultureInfo.InvariantCulture)
                    : migrateToChatId.GetString() ?? "";
                return !string.IsNullOrWhiteSpace(chatId);
            }
        }
        catch (JsonException)
        {
        }

        return false;
    }

    private static bool IsMessageThreadNotFound(string body)
    {
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("description", out var description))
            {
                return (description.GetString() ?? "")
                    .Contains("message thread not found", StringComparison.OrdinalIgnoreCase);
            }
        }
        catch (JsonException)
        {
        }

        return body.Contains("message thread not found", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsChatWriteForbidden(string body)
    {
        return body.Contains("CHAT_WRITE_FORBIDDEN", StringComparison.OrdinalIgnoreCase)
            || body.Contains("not enough rights to send", StringComparison.OrdinalIgnoreCase);
    }

    private static string BuildUrl(string token, string method)
    {
        return $"https://api.telegram.org/bot{token.Trim()}/{method}";
    }

    private static void Validate(string token, string chatId)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            throw new InvalidOperationException("Укажите Telegram Bot Token.");
        }

        if (string.IsNullOrWhiteSpace(chatId))
        {
            throw new InvalidOperationException("Укажите Telegram Chat ID.");
        }
    }
}

public sealed class TelegramMessageThreadNotFoundException()
    : InvalidOperationException("Telegram не нашел тему группы. Topic ID будет очищен.")
{
}

public sealed class TelegramWriteForbiddenException()
    : InvalidOperationException("Telegram не разрешает боту писать в этот чат. Сделайте бота администратором группы или разрешите участникам отправлять сообщения и медиа.")
{
}

public sealed class TelegramChatMigratedException(string chatId)
    : InvalidOperationException($"Telegram перенес группу в supergroup. Новый Chat ID: {chatId}")
{
    public string ChatId { get; } = chatId;
}
