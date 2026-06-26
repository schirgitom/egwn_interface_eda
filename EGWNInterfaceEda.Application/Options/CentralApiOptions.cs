namespace EGWNInterfaceEda.Application.Options;

public sealed class CentralApiOptions
{
    public const string SectionName = "CentralApi";

    public string BaseUrl { get; set; } = string.Empty;

    public string CustomersPath { get; set; } = "api/customers";

    public int TimeoutSeconds { get; set; } = 30;
}
