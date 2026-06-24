using System.Text.Json.Serialization;

namespace ForecastDesk;

public sealed class AppSettings
{
    public string Exchange { get; set; } = "KUCOIN";
    public string Symbol { get; set; } = "BTCUSDT";
    public string Timeframe { get; set; } = "1H";
    public string Theme { get; set; } = "Dark";
    public string TelegramBotToken { get; set; } = "";
    public string TelegramChatId { get; set; } = "";
    public string TelegramMessageThreadId { get; set; } = "";
    public bool TelegramEnabled { get; set; } = true;
    public string TelegramMessageTemplate { get; set; } = "";
    public string Language { get; set; } = "RU";
    public int TimeZoneOffsetMinutes { get; set; } = 0;
    public decimal TakeProfitPercent { get; set; } = 1;
    public string LineBaseMode { get; set; } = ForecastLineBaseMode.CandleColor;
    public bool SendInitialToTelegram { get; set; } = true;
    public bool SendResultToTelegram { get; set; } = true;
}

public sealed class ForecastRecord
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Exchange { get; set; } = "KUCOIN";
    public string Symbol { get; set; } = "BTCUSDT";
    public string Timeframe { get; set; } = "1H";
    public string Direction { get; set; } = "UP";
    public string CheckMode { get; set; } = ForecastCheckMode.Price;
    public string Language { get; set; } = "RU";
    public string TimeZoneLabel { get; set; } = "";
    public int TimeZoneOffsetMinutes { get; set; }
    public string Comment { get; set; } = "";
    public string Status { get; set; } = ForecastStatus.Waiting;
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? TimeLineAtUtc { get; set; }
    public DateTime CheckAtUtc { get; set; } = DateTime.UtcNow.AddHours(1);
    public DateTime? CompletedAtUtc { get; set; }
    public DateTime? LastCheckAtUtc { get; set; }
    public decimal StartPrice { get; set; }
    public decimal? LastPrice { get; set; }
    public decimal? HighestPrice { get; set; }
    public decimal? LowestPrice { get; set; }
    public decimal? HitPrice { get; set; }
    public DateTime? HitAtUtc { get; set; }
    public decimal? TargetPrice { get; set; }
    public decimal? TakeProfitPercent { get; set; }
    public string LineBaseMode { get; set; } = "";
    public decimal? LineBasePrice { get; set; }
    public DateTime? LineBaseCandleOpenUtc { get; set; }
    public DateTime? LineBaseCandleCloseUtc { get; set; }
    public string LineBaseCandleKind { get; set; } = "";
    public decimal? StopPrice { get; set; }
    public decimal? ChangePercent { get; set; }
    public int CheckAfterBars { get; set; }
    public int ExtendBars { get; set; }
    public int MaxExtensions { get; set; }
    public int ExtensionsUsed { get; set; }
    public bool AutoExtend { get; set; }
    public string InitialScreenshotPath { get; set; } = "";
    public string LastScreenshotPath { get; set; } = "";
    public string ResultText { get; set; } = "";
}

public static class ForecastStatus
{
    public const string Waiting = "Ожидает";
    public const string Extended = "Продлен";
    public const string Success = "Исполнен";
    public const string Failed = "Не исполнен";
    public const string Ambiguous = "Нужна проверка";
    public const string Error = "Ошибка";
}

public static class ForecastCheckMode
{
    public const string TimeOnly = "Time";
    public const string Price = "Price";
    public const string PriceAndTime = "Price + Time";
    public const string Bars = "Через бары";
    public const string Time = "Реакция у линии";

    public static bool HasTime(string mode)
    {
        return mode == Time
            || mode == TimeOnly
            || mode == PriceAndTime
            || mode == "Время"
            || mode == "Цена + время";
    }

    public static bool UsesTimedTakeProfit(string mode)
    {
        return mode == Time || mode == TimeOnly || mode == "Время";
    }

    public static bool UsesPriceTarget(string mode)
    {
        return !UsesTimedTakeProfit(mode);
    }
}

public static class ForecastLineBaseMode
{
    public const string CandleColor = "По цвету свечи";
    public const string Direction = "По направлению";
    public const string High = "High свечи";
    public const string Low = "Low свечи";
    public const string Close = "Close свечи";
}

public sealed class MarketCandle
{
    public DateTime OpenTimeUtc { get; init; }
    public DateTime CloseTimeUtc { get; init; }
    public decimal Open { get; init; }
    public decimal High { get; init; }
    public decimal Low { get; init; }
    public decimal Close { get; init; }
}

public sealed class AppData
{
    public AppSettings Settings { get; set; } = new();
    public List<ForecastRecord> Forecasts { get; set; } = [];
}

public sealed class ForecastCheckResult
{
    public required string Status { get; init; }
    public required string ResultText { get; init; }
    public required bool ShouldExtend { get; init; }
    public required decimal ChangePercent { get; init; }
    public decimal? HighestPrice { get; init; }
    public decimal? LowestPrice { get; init; }
    public decimal? HitPrice { get; init; }
    public DateTime? HitAtUtc { get; init; }
    public decimal? TargetPrice { get; init; }
    public decimal? LineBasePrice { get; init; }
    public DateTime? LineBaseCandleOpenUtc { get; init; }
    public DateTime? LineBaseCandleCloseUtc { get; init; }
    public string LineBaseCandleKind { get; init; } = "";
}

[JsonSerializable(typeof(AppData))]
internal partial class ForecastJsonContext : JsonSerializerContext
{
}
