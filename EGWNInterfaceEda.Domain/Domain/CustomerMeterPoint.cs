namespace EGWNInterfaceEda.Domain;

public sealed record CustomerMeterPoint(
    string CustomerId,
    string CustomerName,
    string MeterPointNumber,
    string? EnergyCommunityId = null,
    string? ExternalReference = null);
