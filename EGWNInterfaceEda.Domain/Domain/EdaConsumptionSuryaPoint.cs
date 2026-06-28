namespace EGWNInterfaceEda.Domain;

public sealed record EdaConsumptionSuryaPoint(
    DateTimeOffset? Timestamp,
    decimal? GValue,
    decimal? PValue,
    decimal? Difference);
