namespace EGWNInterfaceEda.Domain;

public sealed record EdaBackfillResponse(
    DateTimeOffset FromUtc,
    DateTimeOffset ToUtc,
    int MetersProcessed,
    int TotalMeterReadings,
    int TotalKpiReadings);
