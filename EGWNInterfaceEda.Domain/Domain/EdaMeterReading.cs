namespace EGWNInterfaceEda.Domain;

public sealed record EdaMeterReading(
    DateTimeOffset Timestamp,
    decimal? TotalConsumptionValue,
    decimal? GridShareValue,
    decimal? CommunityShareValue);
