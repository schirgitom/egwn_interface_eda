namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiReadingsPublication(
    string MeterId,
    string CommunityId,
    IReadOnlyList<EdaKpiValue> Values,
    DateTimeOffset CreatedAtUtc);
