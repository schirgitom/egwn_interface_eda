namespace EGWNInterfaceEda.Domain;

public sealed record EdaTriggerRequest(
    string? MeterId,
    DateTimeOffset? FromUtc,
    DateTimeOffset? ToUtc,
    string? CommunityId = null);
