namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiSyncPublication(
    string MeterId,
    string CommunityId,
    string CommunityName,
    IReadOnlyList<EdaKpiSeriesPoint> Values,
    DateTimeOffset CreatedAtUtc);
