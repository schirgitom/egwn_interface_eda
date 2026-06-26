namespace EGWNInterfaceEda.Application.Options;

public sealed class EdaOptions
{
    public const string SectionName = "Eda";

    public string BaseUrl { get; set; } = "https://prod-api.eda-portal.at/api";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string CommunityId { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 30;

    public DateOnly? CustomFrom { get; set; }

    public DateOnly? CustomTo { get; set; }
}
