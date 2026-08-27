namespace EGWNInterfaceEda.Domain;

public sealed record EdaConsumptionSuryaData(
    IReadOnlyList<EdaSeriesPoint> Series,
    string? ScaleX,
    IReadOnlyList<EdaConsumptionSuryaPoint> Points);

public sealed record EdaConsumptionSuryaCombinedData(
    IReadOnlyList<EdaConsumptionSuryaPoint> Points,
    decimal? TotalConsumption,
    decimal? GridShareTotal,
    decimal? CommunityShareTotal,
    decimal? GridSharePercentage,
    decimal? CommunitySharePercentage);
