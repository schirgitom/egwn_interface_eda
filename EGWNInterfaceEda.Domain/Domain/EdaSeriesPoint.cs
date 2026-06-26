namespace EGWNInterfaceEda.Domain;

public sealed record EdaSeriesPoint(
    DateTimeOffset? Timestamp,
    decimal Value);
