namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiPublication(
    string MeterId,
    string CommunityId,
    IReadOnlyList<EdaKpiSeriesPoint> Values,
    DateTimeOffset CreatedAtUtc);
