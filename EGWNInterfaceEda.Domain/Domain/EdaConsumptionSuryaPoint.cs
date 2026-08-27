namespace EGWNInterfaceEda.Domain;

public sealed record EdaConsumptionSuryaPoint(
    DateTimeOffset? Timestamp,
    decimal? TotalConsumptionValue,
    decimal? GridShareValue,
    decimal? CommunityShareValue);
