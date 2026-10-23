namespace EGWNInterfaceEda.Application.Options;

public sealed class EdaOptions
{
    public const string SectionName = "Eda";

    public string BaseUrl { get; set; } = "https://prod-api.eda-portal.at/api";

    public string LoginUrl { get; set; } = "https://prod.eda-portal.at/api/login";

    public string ConsumptionSuryaBaseUrl { get; set; } = "https://prod.eda-portal.at/api";

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string CommunityId { get; set; } = string.Empty;

    public string? CommunityName { get; set; }

    public List<EdaCommunityOptions> Communities { get; set; } = [];

    public string? MeterId { get; set; }

    public int TimeoutSeconds { get; set; } = 30;

    public DateOnly? CustomFrom { get; set; }

    public DateOnly? CustomTo { get; set; }

    public string DefaultCommunityId =>
        Communities.Count > 0 ? Communities[0].CommunityId : CommunityId;

    public bool HasValidCommunityConfiguration
    {
        get
        {
            var communities = GetCommunities();
            return communities.Count > 0
                   && communities.All(community =>
                       !string.IsNullOrWhiteSpace(community.Name)
                       && !string.IsNullOrWhiteSpace(community.CommunityId)
                       && !string.IsNullOrWhiteSpace(community.Username)
                       && !string.IsNullOrWhiteSpace(community.Password))
                   && communities.Select(community => community.CommunityId)
                       .Distinct(StringComparer.OrdinalIgnoreCase)
                       .Count() == communities.Count;
        }
    }

    public IReadOnlyList<EdaCommunityOptions> GetCommunities()
    {
        if (Communities.Count > 0)
        {
            return Communities;
        }

        if (string.IsNullOrWhiteSpace(CommunityId))
        {
            return [];
        }

        return
        [
            new EdaCommunityOptions
            {
                Name = string.IsNullOrWhiteSpace(CommunityName) ? CommunityId : CommunityName,
                CommunityId = CommunityId,
                Username = Username,
                Password = Password
            }
        ];
    }

    public EdaCommunityOptions ResolveCommunity(string communityId)
    {
        var community = GetCommunities().FirstOrDefault(
            item => string.Equals(item.CommunityId, communityId, StringComparison.OrdinalIgnoreCase));
        if (community is not null)
        {
            return community;
        }

        if (Communities.Count == 0 && !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password))
        {
            return new EdaCommunityOptions
            {
                Name = string.IsNullOrWhiteSpace(CommunityName) ? communityId : CommunityName,
                CommunityId = communityId,
                Username = Username,
                Password = Password
            };
        }

        throw new InvalidOperationException($"No EDA credentials are configured for community '{communityId}'.");
    }
}

public sealed class EdaCommunityOptions
{
    public string Name { get; set; } = string.Empty;

    public string CommunityId { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;
}
