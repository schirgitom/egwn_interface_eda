namespace EGWNInterfaceEda.Domain;

public sealed record EdaSyncPublication(
    CustomerMeterPoint Customer,
    string CommunityId,
    EdaPeriodSnapshot Snapshot,
    DateTimeOffset CreatedAtUtc)
{
    public string MeterPointNumber => Customer.MeterPointNumber;
}
