namespace EGWNInterfaceEda.Domain;

public sealed record EdaPeriodDefinition(string Key, DateTimeOffset From, DateTimeOffset To, string GroupBy)
{
    public static IReadOnlyList<EdaPeriodDefinition> CreateAll(DateTimeOffset now, DateOnly? customFrom, DateOnly? customTo)
    {
        var today = StartOfDay(now.Date, now.Offset);
        var yesterday = StartOfDay(now.Date.AddDays(-1), now.Offset);
        var weekStart = StartOfWeek(now.Date, now.Offset);
        var prevWeekStart = weekStart.AddDays(-7);
        var monthStart = StartOfDay(new DateTime(now.Year, now.Month, 1), now.Offset);
        var nextMonthStart = monthStart.AddMonths(1);
        var prevMonthStart = monthStart.AddMonths(-1);
        var yearStart = StartOfDay(new DateTime(now.Year, 1, 1), now.Offset);
        var nextYearStart = yearStart.AddYears(1);
        var prevYearStart = yearStart.AddYears(-1);

        var customFromDate = customFrom ?? DateOnly.FromDateTime(now.DateTime);
        var customToDate = customTo ?? customFromDate;

        return
        [
            new("heute", today, EndOfDay(today, now.Offset), "day"),
            new("gestern", yesterday, EndOfDay(yesterday, now.Offset), "day"),
            new("woche", weekStart, EndOfDay(weekStart.AddDays(6), now.Offset), "day"),
            new("vorwoche", prevWeekStart, EndOfDay(prevWeekStart.AddDays(6), now.Offset), "day"),
            new("monat", monthStart, EndOfDay(nextMonthStart.AddDays(-1), now.Offset), "month"),
            new("vormonat", prevMonthStart, EndOfDay(monthStart.AddDays(-1), now.Offset), "month"),
            new("jahr", yearStart, EndOfDay(nextYearStart.AddDays(-1), now.Offset), "year"),
            new("vorjahr", prevYearStart, EndOfDay(yearStart.AddDays(-1), now.Offset), "year"),
            new("custom", StartOfDay(customFromDate.ToDateTime(TimeOnly.MinValue), now.Offset), EndOfDay(StartOfDay(customToDate.ToDateTime(TimeOnly.MinValue), now.Offset).AddHours(23).AddMinutes(45), now.Offset), "month"),
        ];
    }

    private static DateTimeOffset StartOfWeek(DateTime date, TimeSpan offset)
    {
        var dayOfWeek = date.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)date.DayOfWeek;
        return StartOfDay(date.AddDays(-(dayOfWeek - 1)), offset);
    }

    private static DateTimeOffset StartOfDay(DateTime date, TimeSpan offset) =>
        new(date.Date, offset);

    private static DateTimeOffset EndOfDay(DateTimeOffset date, TimeSpan offset) =>
        new(date.Date.AddHours(23).AddMinutes(45), offset);
}
