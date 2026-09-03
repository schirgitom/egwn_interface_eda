namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiValue(
    DateTimeOffset Timestamp,
    EdaKpiData Values);
