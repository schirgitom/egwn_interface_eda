namespace EGWNInterfaceEda.Domain;

public sealed record EdaPeriodSnapshot(
    EdaPeriodDefinition Period,
    EdaKpiData? Kpi,
    EdaMeterData? Meter,
    DateTimeOffset FetchedAtUtc);
