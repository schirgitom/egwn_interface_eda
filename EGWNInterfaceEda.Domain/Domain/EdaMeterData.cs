namespace EGWNInterfaceEda.Domain;

public sealed record EdaMeterData(
    bool? SubstitutesOrMissingData,
    decimal? SumGeneration,
    decimal? SumFeed,
    IReadOnlyList<EdaSeriesPoint> GenerationSeries,
    IReadOnlyList<EdaSeriesPoint> FeedSeries);
