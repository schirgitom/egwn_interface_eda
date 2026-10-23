namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiReadingsPublication(
    string CommunityId,
    string CommunityName,
    IReadOnlyList<EdaKpiValue> Values,
    DateTimeOffset CreatedAtUtc);
