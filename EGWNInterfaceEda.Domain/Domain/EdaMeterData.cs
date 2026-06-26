namespace EGWNInterfaceEda.Domain;

public sealed record EdaMeterData(
    decimal? SumGeneration,
    decimal? SumFeed,
    IReadOnlyList<EdaSeriesPoint> GenerationSeries,
    IReadOnlyList<EdaSeriesPoint> FeedSeries);
