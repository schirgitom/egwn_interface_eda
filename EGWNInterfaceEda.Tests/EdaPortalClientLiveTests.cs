using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Domain;
using EGWNInterfaceEda.Infrastructure.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Tests;

public sealed class EdaPortalClientLiveTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task FetchPeriodAsync_queries_the_live_eda_portal()
    {
        var options = BuildOptions();
        using var client = new HttpClient
        {
            BaseAddress = new Uri(options.BaseUrl, UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var sut = new EdaPortalClient(client, Options.Create(options), NullLogger<EdaPortalClient>.Instance);

        var now = DateTimeOffset.Now;
        var from = new DateTimeOffset(now.Date.AddDays(-1), now.Offset);
        var to = new DateTimeOffset(now.Date, now.Offset);
        var period = new EdaPeriodDefinition("integration", from, to, "day");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var snapshot = await sut.FetchPeriodAsync(options.CommunityId, period, cts.Token);

        Assert.Equal(period, snapshot.Period);
        Assert.True(snapshot.FetchedAtUtc <= DateTimeOffset.UtcNow);
        Assert.True(snapshot.Kpi is not null || snapshot.Meter is not null);
    }

    private static EdaOptions BuildOptions()
    {
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
            .Build();

        return new EdaOptions
        {
            BaseUrl = GetRequiredValue(configuration, "Eda:BaseUrl"),
            Username = GetRequiredValue(configuration, "Eda:Username"),
            Password = GetRequiredValue(configuration, "Eda:Password"),
            CommunityId = GetRequiredValue(configuration, "Eda:CommunityId"),
            TimeoutSeconds = GetIntValue(configuration, "Eda:TimeoutSeconds", 30)
        };
    }

    private static string GetRequiredValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        if (!string.IsNullOrWhiteSpace(value))
        {
            return value;
        }

        throw new InvalidOperationException($"Configuration value '{key}' must be set in appsettings.json to run this live integration test.");
    }

    private static int GetIntValue(IConfiguration configuration, string key, int defaultValue)
    {
        var value = configuration[key];
        return int.TryParse(value, out var parsed) ? parsed : defaultValue;
    }
}
