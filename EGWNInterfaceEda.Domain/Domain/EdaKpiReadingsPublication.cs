namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiReadingsPublication(
    string CommunityId,
    IReadOnlyList<EdaKpiValue> Values,
    DateTimeOffset CreatedAtUtc);
