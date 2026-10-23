namespace EGWNInterfaceEda.Domain;

public sealed record EdaMeterReadingsPublication(
    string MeterPointNumber,
    string CommunityId,
    string CommunityName,
    IReadOnlyList<EdaMeterReading> Readings,
    DateTimeOffset CreatedAtUtc);
