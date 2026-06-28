namespace EGWNInterfaceEda.Domain;

public sealed record EdaConsumptionSuryaData(
    IReadOnlyList<EdaSeriesPoint> Series,
    string? ScaleX,
    IReadOnlyList<EdaConsumptionSuryaPoint> Points);
