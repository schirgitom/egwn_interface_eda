namespace EGWNInterfaceEda.Application.Options;

public sealed class CentralApiOptions
{
    public const string SectionName = "CentralApi";

    public string BaseUrl { get; set; } = string.Empty;

    public string CustomersPath { get; set; } = "api/customers";

    public string MeteringPointsPath { get; set; } = "api/metering-points";

    public string AuthPath { get; set; } = "api/auth/token";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;
}
