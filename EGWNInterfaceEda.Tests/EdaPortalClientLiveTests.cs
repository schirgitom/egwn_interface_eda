using System.Net.Http.Json;
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
    public async Task Login_returns_a_token_from_live_eda_portal()
    {
        var options = BuildOptions();
        using var client = CreateHttpClient(options);

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        using var response = await client.PostAsJsonAsync(options.LoginUrl, new
        {
            email = options.Username,
            password = options.Password
        }, cancellationToken: cts.Token);
        response.EnsureSuccessStatusCode();

        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: cts.Token);
        Assert.False(string.IsNullOrWhiteSpace(login?.Token));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FetchKpiAsync_queries_live_kpidata_endpoint()
    {
        var options = BuildOptions();
        using var client = CreateHttpClient(options);
        var sut = CreateSut(client, options);

        var now = DateTimeOffset.Now;
        var from = new DateTimeOffset(now.Date.AddDays(-1), now.Offset);
        var to = new DateTimeOffset(now.Date, now.Offset);
        var period = new EdaPeriodDefinition("integration", from, to, "day");
        var meterId = options.MeterId ?? options.CommunityId;

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var kpi = await sut.FetchKpiAsync(options.CommunityId, meterId, period, cts.Token);

        Assert.NotNull(kpi);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FetchMeterDataAsync_queries_live_meterdata_endpoint()
    {
        var options = BuildOptions();
        using var client = CreateHttpClient(options);
        var sut = CreateSut(client, options);

        var now = DateTimeOffset.Now;
        var from = new DateTimeOffset(now.Date.AddDays(-1), now.Offset);
        var to = new DateTimeOffset(now.Date, now.Offset);
        var period = new EdaPeriodDefinition("integration", from, to, "day");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var meter = await sut.FetchMeterDataAsync(options.CommunityId, period, cts.Token);

        Assert.NotNull(meter);
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FetchConsumptionSuryaAsync_queries_live_p_and_g_endpoints_and_maps_series()
    {
        var options = BuildOptions();
        using var client = CreateHttpClient(options);
        var sut = CreateSut(client, options);
        var meterId = GetRequiredMeterId(options);

        var now = DateTimeOffset.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(monthStart, now.Offset);
        var to = new DateTimeOffset(monthStart.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59), now.Offset);
        var period = new EdaPeriodDefinition("integration", from, to, "day");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var pData = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.P, cts.Token);
        var gData = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.G, cts.Token);

        Assert.NotNull(pData);
        Assert.NotNull(gData);
        Assert.NotEmpty(pData.Series);
        Assert.NotEmpty(gData.Series);
        Assert.All(pData.Series, point => Assert.NotEqual(default, point.Timestamp));
        Assert.All(gData.Series, point => Assert.NotEqual(default, point.Timestamp));
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FetchConsumptionSuryaPointsAsync_hourly_combines_live_p_and_g_endpoints_with_time_window()
    {
        var options = BuildOptions();
        using var client = CreateHttpClient(options);
        var sut = CreateSut(client, options);
        var meterId = GetRequiredMeterId(options);

        var nowUtc = DateTimeOffset.UtcNow;
        var from = new DateTimeOffset(new DateTime(nowUtc.Year, nowUtc.Month, 1, 0, 0, 0, DateTimeKind.Utc), TimeSpan.Zero);
        var to = new DateTimeOffset(from.Date.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59), TimeSpan.Zero);
        var period = new EdaPeriodDefinition("integration-hourly", from, to, "hour");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var pData = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.P, cts.Token);
        var gData = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.G, cts.Token);
        var combinedPoints = await sut.FetchConsumptionSuryaPointsAsync(options.CommunityId, meterId, period, cts.Token);

        Assert.NotNull(pData);
        Assert.NotNull(gData);
        Assert.Equal("hour", pData.ScaleX);
        Assert.Equal("hour", gData.ScaleX);
        Assert.NotEmpty(pData.Series);
        Assert.NotEmpty(gData.Series);
        Assert.NotEmpty(combinedPoints.Points);

        foreach (var point in combinedPoints.Points)
        {
            Assert.NotEqual(default, point.Timestamp);
            Assert.True(point.Timestamp >= from && point.Timestamp <= to, $"Timestamp {point.Timestamp} should be within {from} and {to}.");
        }

        var pByTimestamp = pData.Series.ToDictionary(point => point.Timestamp, point => point.Value);
        var gByTimestamp = gData.Series.ToDictionary(point => point.Timestamp, point => point.Value);
        foreach (var combined in combinedPoints.Points)
        {
            if (gByTimestamp.TryGetValue(combined.Timestamp, out var totalConsumptionValue))
            {
                Assert.Equal(totalConsumptionValue, combined.TotalConsumptionValue);
            }
            else
            {
                Assert.Null(combined.TotalConsumptionValue);
            }

            if (pByTimestamp.TryGetValue(combined.Timestamp, out var gridShareValue))
            {
                Assert.Equal(gridShareValue, combined.GridShareValue);
            }
            else
            {
                Assert.Null(combined.GridShareValue);
            }

            if (combined.TotalConsumptionValue.HasValue && combined.GridShareValue.HasValue)
            {
                Assert.Equal(combined.TotalConsumptionValue.Value - combined.GridShareValue.Value, combined.CommunityShareValue);
            }
            else
            {
                Assert.Null(combined.CommunityShareValue);
            }
        }
    }

    [Fact]
    [Trait("Category", "Integration")]
    public async Task FetchConsumptionSuryaPointsAsync_combines_live_g_and_p_with_difference()
    {
        var options = BuildOptions();
        using var client = CreateHttpClient(options);
        var sut = CreateSut(client, options);
        var meterId = GetRequiredMeterId(options);

        var now = DateTimeOffset.Now;
        var monthStart = new DateTime(now.Year, now.Month, 1, 0, 0, 0, DateTimeKind.Unspecified);
        var from = new DateTimeOffset(monthStart, now.Offset);
        var to = new DateTimeOffset(monthStart.AddMonths(1).AddDays(-1).AddHours(23).AddMinutes(59), now.Offset);
        var period = new EdaPeriodDefinition("integration", from, to, "day");

        using var cts = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var pData = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.P, cts.Token);
        var gData = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.G, cts.Token);
        var combinedPoints = await sut.FetchConsumptionSuryaPointsAsync(options.CommunityId, meterId, period, cts.Token);

        Assert.NotEmpty(combinedPoints.Points);
        Assert.True(combinedPoints.TotalConsumption.HasValue && combinedPoints.TotalConsumption.Value > 0m);
        Assert.True(combinedPoints.GridShareTotal.HasValue && combinedPoints.GridShareTotal.Value > 0m);
        Assert.True(combinedPoints.CommunityShareTotal.HasValue && combinedPoints.CommunityShareTotal.Value > 0m);
        Assert.True(combinedPoints.GridSharePercentage.HasValue && combinedPoints.GridSharePercentage.Value >= 0m);
        Assert.True(combinedPoints.CommunitySharePercentage.HasValue && combinedPoints.CommunitySharePercentage.Value >= 0m);

        var pByTimestamp = pData.Series.ToDictionary(point => point.Timestamp, point => point.Value);
        var gByTimestamp = gData.Series.ToDictionary(point => point.Timestamp, point => point.Value);

        foreach (var combined in combinedPoints.Points)
        {
            if (gByTimestamp.TryGetValue(combined.Timestamp, out var totalConsumptionValue))
            {
                Assert.Equal(totalConsumptionValue, combined.TotalConsumptionValue);
            }
            else
            {
                Assert.Null(combined.TotalConsumptionValue);
            }

            if (pByTimestamp.TryGetValue(combined.Timestamp, out var gridShareValue))
            {
                Assert.Equal(gridShareValue, combined.GridShareValue);
            }
            else
            {
                Assert.Null(combined.GridShareValue);
            }

            if (combined.TotalConsumptionValue.HasValue && combined.GridShareValue.HasValue)
            {
                Assert.Equal(combined.TotalConsumptionValue.Value - combined.GridShareValue.Value, combined.CommunityShareValue);
            }
            else
            {
                Assert.Null(combined.CommunityShareValue);
            }
        }
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
            LoginUrl = GetRequiredValue(configuration, "Eda:LoginUrl"),
            ConsumptionSuryaBaseUrl = GetRequiredValue(configuration, "Eda:ConsumptionSuryaBaseUrl"),
            Username = GetRequiredValue(configuration, "Eda:Username"),
            Password = GetRequiredValue(configuration, "Eda:Password"),
            CommunityId = GetRequiredValue(configuration, "Eda:CommunityId"),
            MeterId = GetOptionalValue(configuration, "Eda:MeterId"),
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

    private static string? GetOptionalValue(IConfiguration configuration, string key)
    {
        var value = configuration[key];
        return string.IsNullOrWhiteSpace(value) ? null : value;
    }

    private static HttpClient CreateHttpClient(EdaOptions options) =>
        new()
        {
            BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

    private static string GetRequiredMeterId(EdaOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.MeterId))
        {
            return options.MeterId;
        }

        throw new InvalidOperationException("Configuration value 'Eda:MeterId' must be set in appsettings.json to run consumptionsurya live integration tests.");
    }

    private static EdaPortalClient CreateSut(HttpClient client, EdaOptions options) =>
        new(client, Options.Create(options), NullLogger<EdaPortalClient>.Instance);

    private sealed record LoginResponse(string Token);
}
