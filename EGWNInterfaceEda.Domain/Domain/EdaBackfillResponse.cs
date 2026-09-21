namespace EGWNInterfaceEda.Domain;

public sealed record EdaBackfillResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MetersProcessed,
    int TotalMeterReadings,
    int TotalKpiReadings);

public sealed record EdaMeterBackfillResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MetersProcessed,
    int MetersFailed,
    int TotalMeterReadings,
    TimeSpan Duration);

public sealed record EdaKpiBackfillResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int CommunitiesProcessed,
    int CommunitiesFailed,
    int TotalKpiValues,
    TimeSpan Duration);
