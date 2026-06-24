using System.Globalization;
using System.Text.Json;

namespace ForecastDesk;

public sealed class MarketDataService
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(20)
    };

    public async Task<decimal> GetLastPriceAsync(string exchange, string symbol, CancellationToken cancellationToken = default)
    {
        exchange = exchange.Trim().ToUpperInvariant();

        return exchange switch
        {
            "BINANCE" => await GetBinancePriceAsync(symbol, cancellationToken),
            "KUCOIN" => await GetKuCoinPriceAsync(symbol, cancellationToken),
            _ => throw new InvalidOperationException($"Биржа {exchange} пока не поддерживается для автопроверки цены.")
        };
    }

    public async Task<IReadOnlyList<MarketCandle>> GetCandlesAsync(
        string exchange,
        string symbol,
        string timeframe,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken = default)
    {
        exchange = exchange.Trim().ToUpperInvariant();
        if (endUtc <= startUtc)
        {
            endUtc = startUtc.Add(TimeframeToTimeSpan(timeframe));
        }

        return exchange switch
        {
            "BINANCE" => await GetBinanceCandlesAsync(symbol, timeframe, startUtc, endUtc, cancellationToken),
            "KUCOIN" => await GetKuCoinCandlesAsync(symbol, timeframe, startUtc, endUtc, cancellationToken),
            _ => throw new InvalidOperationException($"Биржа {exchange} пока не поддерживается для проверки свечей.")
        };
    }

    public static string BuildTradingViewUrl(string exchange, string symbol, string timeframe, string theme = "dark")
    {
        var tvExchange = exchange.Trim().ToUpperInvariant();
        var tvSymbol = NormalizeSymbol(symbol).Replace("-", "", StringComparison.Ordinal);
        var interval = ToTradingViewInterval(timeframe);
        var symbolParameter = Uri.EscapeDataString($"{tvExchange}:{tvSymbol}");
        var tvTheme = theme.Equals("light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
        return $"https://www.tradingview.com/chart/?symbol={symbolParameter}&interval={interval}&theme={tvTheme}";
    }

    public static bool TryParseTradingViewUrl(
        string? url,
        out string exchange,
        out string symbol,
        out string timeframe)
    {
        exchange = "";
        symbol = "";
        timeframe = "";

        if (string.IsNullOrWhiteSpace(url) || !Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            return false;
        }

        var query = ParseQuery(uri.Query);
        if (query.TryGetValue("symbol", out var symbolValue))
        {
            var decodedSymbol = Uri.UnescapeDataString(symbolValue).Trim().ToUpperInvariant();
            var parts = decodedSymbol.Split(':', 2, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length == 2)
            {
                exchange = parts[0];
                symbol = parts[1].Replace("/", "", StringComparison.Ordinal);
            }
            else if (parts.Length == 1)
            {
                symbol = parts[0].Replace("/", "", StringComparison.Ordinal);
            }
        }

        if (query.TryGetValue("interval", out var intervalValue))
        {
            timeframe = FromTradingViewInterval(intervalValue);
        }

        return !string.IsNullOrWhiteSpace(exchange)
            || !string.IsNullOrWhiteSpace(symbol)
            || !string.IsNullOrWhiteSpace(timeframe);
    }

    public static TimeSpan TimeframeToTimeSpan(string timeframe)
    {
        return timeframe.Trim().ToUpperInvariant() switch
        {
            "1M" => TimeSpan.FromMinutes(1),
            "3M" => TimeSpan.FromMinutes(3),
            "5M" => TimeSpan.FromMinutes(5),
            "15M" => TimeSpan.FromMinutes(15),
            "30M" => TimeSpan.FromMinutes(30),
            "1H" => TimeSpan.FromHours(1),
            "2H" => TimeSpan.FromHours(2),
            "4H" => TimeSpan.FromHours(4),
            "1D" => TimeSpan.FromDays(1),
            _ => TimeSpan.FromHours(1)
        };
    }

    private static string ToTradingViewInterval(string timeframe)
    {
        return timeframe.Trim().ToUpperInvariant() switch
        {
            "1M" => "1",
            "3M" => "3",
            "5M" => "5",
            "15M" => "15",
            "30M" => "30",
            "1H" => "60",
            "2H" => "120",
            "4H" => "240",
            "1D" => "D",
            _ => "60"
        };
    }

    private static string FromTradingViewInterval(string interval)
    {
        return interval.Trim().ToUpperInvariant() switch
        {
            "1" => "1M",
            "3" => "3M",
            "5" => "5M",
            "15" => "15M",
            "30" => "30M",
            "60" => "1H",
            "120" => "2H",
            "240" => "4H",
            "D" or "1D" => "1D",
            _ => interval.Trim().ToUpperInvariant()
        };
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var trimmed = query.TrimStart('?');
        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return values;
        }

        foreach (var pair in trimmed.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split('=', 2);
            if (parts.Length == 2)
            {
                values[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace("+", " ", StringComparison.Ordinal));
            }
        }

        return values;
    }

    private static string ToBinanceInterval(string timeframe)
    {
        return timeframe.Trim().ToUpperInvariant() switch
        {
            "1M" => "1m",
            "3M" => "3m",
            "5M" => "5m",
            "15M" => "15m",
            "30M" => "30m",
            "1H" => "1h",
            "2H" => "2h",
            "4H" => "4h",
            "1D" => "1d",
            _ => "1h"
        };
    }

    private static string ToKuCoinCandleType(string timeframe)
    {
        return timeframe.Trim().ToUpperInvariant() switch
        {
            "1M" => "1min",
            "3M" => "3min",
            "5M" => "5min",
            "15M" => "15min",
            "30M" => "30min",
            "1H" => "1hour",
            "2H" => "2hour",
            "4H" => "4hour",
            "1D" => "1day",
            _ => "1hour"
        };
    }

    private static async Task<decimal> GetBinancePriceAsync(string symbol, CancellationToken cancellationToken)
    {
        var apiSymbol = NormalizeSymbol(symbol).Replace("-", "", StringComparison.Ordinal);
        var url = $"https://api.binance.com/api/v3/ticker/price?symbol={Uri.EscapeDataString(apiSymbol)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var price = document.RootElement.GetProperty("price").GetString();
        return ParseDecimal(price);
    }

    private static async Task<decimal> GetKuCoinPriceAsync(string symbol, CancellationToken cancellationToken)
    {
        var apiSymbol = ToKuCoinSymbol(symbol);
        var url = $"https://api.kucoin.com/api/v1/market/orderbook/level1?symbol={Uri.EscapeDataString(apiSymbol)}";
        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var price = document.RootElement.GetProperty("data").GetProperty("price").GetString();
        return ParseDecimal(price);
    }

    private static async Task<IReadOnlyList<MarketCandle>> GetBinanceCandlesAsync(
        string symbol,
        string timeframe,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var apiSymbol = NormalizeSymbol(symbol).Replace("-", "", StringComparison.Ordinal);
        var interval = ToBinanceInterval(timeframe);
        var startMs = ToUnixMilliseconds(startUtc);
        var endMs = ToUnixMilliseconds(endUtc);
        var url =
            "https://api.binance.com/api/v3/klines" +
            $"?symbol={Uri.EscapeDataString(apiSymbol)}" +
            $"&interval={Uri.EscapeDataString(interval)}" +
            $"&startTime={startMs}" +
            $"&endTime={endMs}" +
            "&limit=1000";

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var candles = new List<MarketCandle>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            candles.Add(new MarketCandle
            {
                OpenTimeUtc = FromUnixMilliseconds(item[0].GetInt64()),
                Open = ParseDecimal(item[1].GetString()),
                High = ParseDecimal(item[2].GetString()),
                Low = ParseDecimal(item[3].GetString()),
                Close = ParseDecimal(item[4].GetString()),
                CloseTimeUtc = FromUnixMilliseconds(item[6].GetInt64())
            });
        }

        return candles
            .OrderBy(candle => candle.OpenTimeUtc)
            .ToList();
    }

    private static async Task<IReadOnlyList<MarketCandle>> GetKuCoinCandlesAsync(
        string symbol,
        string timeframe,
        DateTime startUtc,
        DateTime endUtc,
        CancellationToken cancellationToken)
    {
        var apiSymbol = ToKuCoinSymbol(symbol);
        var type = ToKuCoinCandleType(timeframe);
        var startSeconds = ToUnixSeconds(startUtc);
        var endSeconds = ToUnixSeconds(endUtc);
        var url =
            "https://api.kucoin.com/api/v1/market/candles" +
            $"?type={Uri.EscapeDataString(type)}" +
            $"&symbol={Uri.EscapeDataString(apiSymbol)}" +
            $"&startAt={startSeconds}" +
            $"&endAt={endSeconds}";

        using var response = await Http.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);

        var candles = new List<MarketCandle>();
        foreach (var item in document.RootElement.GetProperty("data").EnumerateArray())
        {
            var openTimeSeconds = long.Parse(item[0].GetString() ?? "0", CultureInfo.InvariantCulture);
            candles.Add(new MarketCandle
            {
                OpenTimeUtc = FromUnixSeconds(openTimeSeconds),
                Open = ParseDecimal(item[1].GetString()),
                Close = ParseDecimal(item[2].GetString()),
                High = ParseDecimal(item[3].GetString()),
                Low = ParseDecimal(item[4].GetString()),
                CloseTimeUtc = FromUnixSeconds(openTimeSeconds).Add(TimeframeToTimeSpan(timeframe))
            });
        }

        return candles
            .OrderBy(candle => candle.OpenTimeUtc)
            .ToList();
    }

    private static string NormalizeSymbol(string symbol)
    {
        return symbol.Trim().Replace("/", "", StringComparison.Ordinal).ToUpperInvariant();
    }

    private static string ToKuCoinSymbol(string symbol)
    {
        var normalized = NormalizeSymbol(symbol);
        if (normalized.Contains('-', StringComparison.Ordinal))
        {
            return normalized;
        }

        foreach (var quote in new[] { "USDT", "USDC", "BTC", "ETH", "USD" })
        {
            if (normalized.EndsWith(quote, StringComparison.Ordinal) && normalized.Length > quote.Length)
            {
                return $"{normalized[..^quote.Length]}-{quote}";
            }
        }

        return normalized;
    }

    private static decimal ParseDecimal(string? value)
    {
        if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
        {
            return result;
        }

        throw new InvalidOperationException("Биржа вернула цену в неизвестном формате.");
    }

    private static long ToUnixMilliseconds(DateTime utc)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
    }

    private static long ToUnixSeconds(DateTime utc)
    {
        return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeSeconds();
    }

    private static DateTime FromUnixMilliseconds(long milliseconds)
    {
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime;
    }

    private static DateTime FromUnixSeconds(long seconds)
    {
        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }
}
