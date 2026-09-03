namespace EGWNInterfaceEda.Domain;

public sealed record EdaReadingTriggerResponse(
    string? MeterId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    int ReadingCount);
