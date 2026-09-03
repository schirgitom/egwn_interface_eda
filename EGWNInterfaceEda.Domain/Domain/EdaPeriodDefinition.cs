namespace EGWNInterfaceEda.Domain;

public sealed record EdaPeriodDefinition(string Key, DateTimeOffset From, DateTimeOffset To, string GroupBy)
{
    public const int DefaultHistoricalDays = 21;

    public static IReadOnlyList<EdaPeriodDefinition> CreateAll(DateTimeOffset now, DateOnly? customFrom, DateOnly? customTo)
    {
        if (customFrom.HasValue || customTo.HasValue)
        {
            var customStartDate = customFrom ?? customTo ?? DateOnly.FromDateTime(now.DateTime);
            var customEndDate = customTo ?? customFrom ?? DateOnly.FromDateTime(now.DateTime);
            var customStart = StartOfDay(customStartDate.ToDateTime(TimeOnly.MinValue), now.Offset);
            var customEnd = EndOfDay(customEndDate.ToDateTime(TimeOnly.MinValue), now.Offset);
            return [new("custom-stündlich", customStart, customEnd, "hour")];
        }

        var from = now.AddDays(-DefaultHistoricalDays);
        var start = StartOfDay(from.Date, now.Offset);
        var end = EndOfDay(now.Date, now.Offset);

        return [new($"letzte-{DefaultHistoricalDays}-tage-stündlich", start, end, "hour")];
    }

    private static DateTimeOffset StartOfDay(DateTime date, TimeSpan offset) =>
        new(date.Date, offset);

    private static DateTimeOffset EndOfDay(DateTimeOffset date, TimeSpan offset) =>
        new(date.Date.AddHours(23).AddMinutes(45), offset);
}
