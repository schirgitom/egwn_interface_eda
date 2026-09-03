namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiSyncPublication(
    string MeterId,
    string CommunityId,
    IReadOnlyList<EdaKpiSeriesPoint> Values,
    DateTimeOffset CreatedAtUtc);
