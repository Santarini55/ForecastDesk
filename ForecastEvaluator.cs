namespace ForecastDesk;

public static class ForecastEvaluator
{
    public static ForecastCheckResult Evaluate(ForecastRecord forecast, decimal currentPrice)
    {
        return Evaluate(forecast, currentPrice, []);
    }

    public static ForecastCheckResult Evaluate(
        ForecastRecord forecast,
        decimal currentPrice,
        IReadOnlyList<MarketCandle> candles)
    {
        var isUp = forecast.Direction.Equals("UP", StringComparison.OrdinalIgnoreCase);
        var change = forecast.StartPrice == 0
            ? 0
            : decimal.Round((currentPrice - forecast.StartPrice) / forecast.StartPrice * 100, 4);

        if (ForecastCheckMode.UsesTimedTakeProfit(forecast.CheckMode)
            && forecast.TimeLineAtUtc.HasValue
            && forecast.TakeProfitPercent is > 0)
        {
            return EvaluateTimedTakeProfit(forecast, currentPrice, candles, isUp);
        }

        var evaluationStartUtc = forecast.TimeLineAtUtc ?? forecast.CreatedAtUtc;
        var orderedCandles = candles
            .Where(candle => candle.CloseTimeUtc >= evaluationStartUtc)
            .OrderBy(candle => candle.OpenTimeUtc)
            .ToList();

        if (orderedCandles.Count > 0)
        {
            return EvaluateCandles(forecast, currentPrice, orderedCandles, isUp, change);
        }

        if (forecast.StopPrice is { } stopPrice && StopReached(isUp, currentPrice, stopPrice))
        {
            return new ForecastCheckResult
            {
                Status = ForecastStatus.Failed,
                ResultText = $"Стоп достигнут: {currentPrice}",
                ShouldExtend = false,
                ChangePercent = change
            };
        }

        if (forecast.TargetPrice is { } targetPrice)
        {
            if (TargetReached(isUp, currentPrice, targetPrice))
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Success,
                    ResultText = $"Цель достигнута: {currentPrice}",
                    ShouldExtend = false,
                    ChangePercent = change
                };
            }

            return ExtendOrFail(forecast, change, $"Цель пока не достигнута. Текущая цена: {currentPrice}");
        }

        var movedInDirection = isUp ? currentPrice > forecast.StartPrice : currentPrice < forecast.StartPrice;
        if (movedInDirection)
        {
            return new ForecastCheckResult
            {
                Status = ForecastStatus.Success,
                ResultText = $"Цена пошла в сторону прогноза: {currentPrice}",
                ShouldExtend = false,
                ChangePercent = change
            };
        }

        return ExtendOrFail(forecast, change, $"Цена не пошла в сторону прогноза. Текущая цена: {currentPrice}");
    }

    private static ForecastCheckResult EvaluateCandles(
        ForecastRecord forecast,
        decimal currentPrice,
        IReadOnlyList<MarketCandle> candles,
        bool isUp,
        decimal change)
    {
        var highest = candles.Max(candle => candle.High);
        var lowest = candles.Min(candle => candle.Low);
        var lastClose = candles[^1].Close;
        var candleSummary = $"Свечей проверено: {candles.Count}. High: {highest}, Low: {lowest}, Close: {lastClose}.";

        foreach (var candle in candles)
        {
            var targetHit = forecast.TargetPrice is { } target && TargetReachedByCandle(isUp, candle, target);
            var stopHit = forecast.StopPrice is { } stop && StopReachedByCandle(isUp, candle, stop);

            if (targetHit && stopHit)
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Ambiguous,
                    ResultText = $"Цель и стоп попали в одну свечу {FormatUtc(candle.OpenTimeUtc)}. Нужна ручная проверка. {candleSummary}",
                    ShouldExtend = false,
                    ChangePercent = change,
                    HighestPrice = highest,
                    LowestPrice = lowest,
                    HitAtUtc = candle.OpenTimeUtc
                };
            }

            if (stopHit)
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Failed,
                    ResultText = $"Стоп был достигнут на свече {FormatUtc(candle.OpenTimeUtc)}. {candleSummary}",
                    ShouldExtend = false,
                    ChangePercent = change,
                    HighestPrice = highest,
                    LowestPrice = lowest,
                    HitPrice = forecast.StopPrice,
                    HitAtUtc = candle.OpenTimeUtc
                };
            }

            if (targetHit)
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Success,
                    ResultText = $"Цель была достигнута на свече {FormatUtc(candle.OpenTimeUtc)}. {candleSummary}",
                    ShouldExtend = false,
                    ChangePercent = change,
                    HighestPrice = highest,
                    LowestPrice = lowest,
                    HitPrice = forecast.TargetPrice,
                    HitAtUtc = candle.OpenTimeUtc
                };
            }
        }

        if (forecast.TargetPrice.HasValue)
        {
            return ExtendOrFail(
                forecast,
                change,
                $"Цель пока не достигнута. Текущая цена: {currentPrice}. {candleSummary}",
                highest,
                lowest);
        }

        var movedInDirection = isUp ? highest > forecast.StartPrice : lowest < forecast.StartPrice;
        if (movedInDirection)
        {
            return new ForecastCheckResult
            {
                Status = ForecastStatus.Success,
                ResultText = $"Цена заходила в сторону прогноза. {candleSummary}",
                ShouldExtend = false,
                ChangePercent = change,
                HighestPrice = highest,
                LowestPrice = lowest
            };
        }

        return ExtendOrFail(
            forecast,
            change,
            $"Цена не заходила в сторону прогноза. Текущая цена: {currentPrice}. {candleSummary}",
            highest,
            lowest);
    }

    private static ForecastCheckResult EvaluateTimedTakeProfit(
        ForecastRecord forecast,
        decimal currentPrice,
        IReadOnlyList<MarketCandle> candles,
        bool isUp)
    {
        var orderedCandles = candles
            .OrderBy(candle => candle.OpenTimeUtc)
            .ToList();

        var lineAtUtc = forecast.TimeLineAtUtc!.Value;
        var lineCandle = FindLineCandle(orderedCandles, lineAtUtc);
        if (lineCandle is null)
        {
            return new ForecastCheckResult
            {
                Status = ForecastStatus.Error,
                ResultText = "Не удалось найти свечу линии в данных биржи. Проверь актив, биржу, таймфрейм и время линии.",
                ShouldExtend = false,
                ChangePercent = 0
            };
        }

        var lineBaseMode = string.IsNullOrWhiteSpace(forecast.LineBaseMode)
            ? ForecastLineBaseMode.CandleColor
            : forecast.LineBaseMode;
        var lineBasePrice = ResolveLineBasePrice(lineCandle, lineBaseMode, isUp);
        var targetPrice = CalculatePercentTarget(lineBasePrice, forecast.TakeProfitPercent!.Value, isUp);
        var change = lineBasePrice == 0
            ? 0
            : decimal.Round((currentPrice - lineBasePrice) / lineBasePrice * 100, 4);

        var reactionCandles = orderedCandles
            .Where(candle => candle.OpenTimeUtc >= lineCandle.CloseTimeUtc)
            .ToList();

        if (reactionCandles.Count == 0)
        {
            return ExtendOrFail(
                forecast,
                change,
                $"Свеча линии найдена, но после нее еще нет закрытых свечей для проверки. База TP: {lineBasePrice}, цель: {targetPrice}.",
                null,
                null,
                targetPrice,
                lineBasePrice,
                lineCandle);
        }

        var highest = reactionCandles.Max(candle => candle.High);
        var lowest = reactionCandles.Min(candle => candle.Low);
        var lastClose = reactionCandles[^1].Close;
        var candleSummary =
            $"Свеча линии: {FormatUtc(lineCandle.OpenTimeUtc)} {GetCandleKind(lineCandle)}, база TP: {lineBasePrice}, цель: {targetPrice}. " +
            $"Свечей реакции проверено: {reactionCandles.Count}. High: {highest}, Low: {lowest}, Close: {lastClose}.";

        foreach (var candle in reactionCandles)
        {
            var targetHit = TargetReachedByPrice(isUp, candle, targetPrice);
            var stopHit = forecast.StopPrice is { } stop && StopReachedByCandle(isUp, candle, stop);

            if (targetHit && stopHit)
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Ambiguous,
                    ResultText = $"TP и стоп попали в одну свечу {FormatUtc(candle.OpenTimeUtc)}. Нужна ручная проверка. {candleSummary}",
                    ShouldExtend = false,
                    ChangePercent = change,
                    HighestPrice = highest,
                    LowestPrice = lowest,
                    HitAtUtc = candle.OpenTimeUtc,
                    TargetPrice = targetPrice,
                    LineBasePrice = lineBasePrice,
                    LineBaseCandleOpenUtc = lineCandle.OpenTimeUtc,
                    LineBaseCandleCloseUtc = lineCandle.CloseTimeUtc,
                    LineBaseCandleKind = GetCandleKind(lineCandle)
                };
            }

            if (stopHit)
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Failed,
                    ResultText = $"Стоп был достигнут на свече {FormatUtc(candle.OpenTimeUtc)}. {candleSummary}",
                    ShouldExtend = false,
                    ChangePercent = change,
                    HighestPrice = highest,
                    LowestPrice = lowest,
                    HitPrice = forecast.StopPrice,
                    HitAtUtc = candle.OpenTimeUtc,
                    TargetPrice = targetPrice,
                    LineBasePrice = lineBasePrice,
                    LineBaseCandleOpenUtc = lineCandle.OpenTimeUtc,
                    LineBaseCandleCloseUtc = lineCandle.CloseTimeUtc,
                    LineBaseCandleKind = GetCandleKind(lineCandle)
                };
            }

            if (targetHit)
            {
                return new ForecastCheckResult
                {
                    Status = ForecastStatus.Success,
                    ResultText = $"TP {forecast.TakeProfitPercent}% был достигнут на свече {FormatUtc(candle.OpenTimeUtc)}. {candleSummary}",
                    ShouldExtend = false,
                    ChangePercent = change,
                    HighestPrice = highest,
                    LowestPrice = lowest,
                    HitPrice = targetPrice,
                    HitAtUtc = candle.OpenTimeUtc,
                    TargetPrice = targetPrice,
                    LineBasePrice = lineBasePrice,
                    LineBaseCandleOpenUtc = lineCandle.OpenTimeUtc,
                    LineBaseCandleCloseUtc = lineCandle.CloseTimeUtc,
                    LineBaseCandleKind = GetCandleKind(lineCandle)
                };
            }
        }

        return ExtendOrFail(
            forecast,
            change,
            $"TP {forecast.TakeProfitPercent}% пока не достигнут. {candleSummary}",
            highest,
            lowest,
            targetPrice,
            lineBasePrice,
            lineCandle);
    }

    private static ForecastCheckResult ExtendOrFail(
        ForecastRecord forecast,
        decimal change,
        string text,
        decimal? highestPrice = null,
        decimal? lowestPrice = null,
        decimal? targetPrice = null,
        decimal? lineBasePrice = null,
        MarketCandle? lineBaseCandle = null)
    {
        var canExtend = forecast.AutoExtend && forecast.ExtensionsUsed < forecast.MaxExtensions;
        return new ForecastCheckResult
        {
            Status = canExtend ? ForecastStatus.Extended : ForecastStatus.Failed,
            ResultText = text,
            ShouldExtend = canExtend,
            ChangePercent = change,
            HighestPrice = highestPrice,
            LowestPrice = lowestPrice,
            TargetPrice = targetPrice,
            LineBasePrice = lineBasePrice,
            LineBaseCandleOpenUtc = lineBaseCandle?.OpenTimeUtc,
            LineBaseCandleCloseUtc = lineBaseCandle?.CloseTimeUtc,
            LineBaseCandleKind = lineBaseCandle is null ? "" : GetCandleKind(lineBaseCandle)
        };
    }

    private static bool TargetReached(bool isUp, decimal currentPrice, decimal targetPrice)
    {
        return isUp ? currentPrice >= targetPrice : currentPrice <= targetPrice;
    }

    private static bool StopReached(bool isUp, decimal currentPrice, decimal stopPrice)
    {
        return isUp ? currentPrice <= stopPrice : currentPrice >= stopPrice;
    }

    private static bool TargetReachedByCandle(bool isUp, MarketCandle candle, decimal targetPrice)
    {
        return isUp ? candle.High >= targetPrice : candle.Low <= targetPrice;
    }

    private static bool StopReachedByCandle(bool isUp, MarketCandle candle, decimal stopPrice)
    {
        return isUp ? candle.Low <= stopPrice : candle.High >= stopPrice;
    }

    private static bool TargetReachedByPrice(bool isUp, MarketCandle candle, decimal targetPrice)
    {
        return isUp ? candle.High >= targetPrice : candle.Low <= targetPrice;
    }

    private static decimal CalculatePercentTarget(decimal basePrice, decimal takeProfitPercent, bool isUp)
    {
        var multiplier = takeProfitPercent / 100;
        return decimal.Round(isUp ? basePrice * (1 + multiplier) : basePrice * (1 - multiplier), 8);
    }

    private static MarketCandle? FindLineCandle(IReadOnlyList<MarketCandle> candles, DateTime lineAtUtc)
    {
        return candles.FirstOrDefault(candle => candle.OpenTimeUtc <= lineAtUtc && candle.CloseTimeUtc > lineAtUtc)
            ?? candles.FirstOrDefault(candle => candle.OpenTimeUtc >= lineAtUtc);
    }

    private static decimal ResolveLineBasePrice(MarketCandle candle, string lineBaseMode, bool isUp)
    {
        return lineBaseMode switch
        {
            ForecastLineBaseMode.Direction => isUp ? candle.High : candle.Low,
            ForecastLineBaseMode.High => candle.High,
            ForecastLineBaseMode.Low => candle.Low,
            ForecastLineBaseMode.Close => candle.Close,
            _ => candle.Close >= candle.Open ? candle.High : candle.Low
        };
    }

    private static string GetCandleKind(MarketCandle candle)
    {
        if (candle.Close > candle.Open)
        {
            return "бычья";
        }

        if (candle.Close < candle.Open)
        {
            return "медвежья";
        }

        return "доджи";
    }

    private static string FormatUtc(DateTime utc)
    {
        return utc.ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }
}
