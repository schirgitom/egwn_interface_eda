namespace EGWNInterfaceEda.Domain;

public sealed record EdaMeterReadingsPublication(
    string MeterPointNumber,
    string CommunityId,
    IReadOnlyList<EdaMeterReading> Readings,
    DateTimeOffset CreatedAtUtc);
