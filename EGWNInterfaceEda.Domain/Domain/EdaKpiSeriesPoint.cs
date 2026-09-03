namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiSeriesPoint(
    DateTimeOffset Timestamp,
    EdaKpiData Values);
