using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Domain;
using EGWNInterfaceEda.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Tests;

public sealed class EdaPortalClientTests
{
    [Fact]
    public async Task FetchConsumptionSuryaAsync_route_p_uses_meter_id_in_url()
    {
        var options = new EdaOptions
        {
            BaseUrl = "https://prod-api.eda-portal.at/api",
            LoginUrl = "https://prod.eda-portal.at/api/login",
            ConsumptionSuryaBaseUrl = "https://prod.eda-portal.at/api",
            Username = "user@example.com",
            Password = "secret",
            CommunityId = "2OWxVjZ6MMjZzyRB",
            TimeoutSeconds = 30
        };
        const string meterId = "AT00820008184000000000003436301";

        var capturedUris = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedUris.Add(request.RequestUri!.AbsoluteUri);

            if (request.RequestUri.AbsoluteUri.Equals(options.LoginUrl, StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{ "token": "token-value", "exp": "2026-07-05T12:19:53Z" }""");
            }

            if (request.RequestUri.AbsolutePath.EndsWith($"/consumptionsurya/p/{meterId}", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{ "success": true, "data": [["2026-06-24T00:00:00", 0.0388]], "meta": { "scale_x": "day" } }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var sut = new EdaPortalClient(httpClient, Options.Create(options), NullLogger<EdaPortalClient>.Instance);
        var period = new EdaPeriodDefinition(
            "custom",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 23, 45, 0, TimeSpan.Zero),
            "day");

        var response = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.P, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains($"https://prod.eda-portal.at/api/consumptionsurya/p/{meterId}", capturedUris);
    }

    [Fact]
    public async Task FetchConsumptionSuryaAsync_route_g_uses_meter_id_in_url()
    {
        var options = new EdaOptions
        {
            BaseUrl = "https://prod-api.eda-portal.at/api",
            LoginUrl = "https://prod.eda-portal.at/api/login",
            ConsumptionSuryaBaseUrl = "https://prod.eda-portal.at/api",
            Username = "user@example.com",
            Password = "secret",
            CommunityId = "2OWxVjZ6MMjZzyRB",
            TimeoutSeconds = 30
        };
        const string meterId = "AT00820008184000000000003436301";

        var capturedUris = new List<string>();
        var handler = new StubHttpMessageHandler(request =>
        {
            capturedUris.Add(request.RequestUri!.AbsoluteUri);

            if (request.RequestUri.AbsoluteUri.Equals(options.LoginUrl, StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{ "token": "token-value", "exp": "2026-07-05T12:19:53Z" }""");
            }

            if (request.RequestUri.AbsolutePath.EndsWith($"/consumptionsurya/g/{meterId}", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""{ "success": true, "data": [["2026-06-24T00:00:00", 0.039]], "meta": { "scale_x": "day" } }""");
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var sut = new EdaPortalClient(httpClient, Options.Create(options), NullLogger<EdaPortalClient>.Instance);
        var period = new EdaPeriodDefinition(
            "custom",
            new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 30, 23, 45, 0, TimeSpan.Zero),
            "day");

        var response = await sut.FetchConsumptionSuryaAsync(options.CommunityId, meterId, period, EdaConsumptionSuryaRoute.G, CancellationToken.None);

        Assert.NotNull(response);
        Assert.Contains($"https://prod.eda-portal.at/api/consumptionsurya/g/{meterId}", capturedUris);
    }

    [Fact]
    public async Task FetchKpiAsync_meterdata_and_consumptionsurya_p_g_send_separate_payloads_and_map_responses()
    {
        var options = new EdaOptions
        {
            BaseUrl = "https://prod-api.eda-portal.at/api",
            LoginUrl = "https://prod.eda-portal.at/api/login",
            ConsumptionSuryaBaseUrl = "https://prod.eda-portal.at/api",
            Username = "user@example.com",
            Password = "secret",
            CommunityId = "2OWxVjZ6MMjZzyRB",
            TimeoutSeconds = 30
        };

        var capturedRequests = new List<CapturedRequest>();
        var sync = new object();
        var handler = new StubHttpMessageHandler(request =>
        {
            var body = request.Content is null
                ? null
                : request.Content.ReadAsStringAsync().GetAwaiter().GetResult();

            lock (sync)
            {
                capturedRequests.Add(new CapturedRequest(
                    request.RequestUri!,
                    request.Method,
                    request.Headers.Authorization,
                    body));
            }

            if (request.RequestUri!.AbsoluteUri.Equals(options.LoginUrl, StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "token": "token-value",
                      "exp": "2026-07-05T12:19:53Z"
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/kpiData", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "data": {
                        "autarky": 12.3,
                        "ownConsumption": 45.6,
                        "community": 78.9,
                        "feed": 1.2,
                        "remainingDemand": 3.4
                      }
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/meterdata", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "s": true,
                      "data": {
                        "substitutesOrMissingData": true,
                        "sumGeneration": 15442.247999999994,
                        "sumFeed": 7892.924010000001,
                        "generationSeries": [
                          {
                            "date": "2026-05-27T00:00:00",
                            "value": 627.8119999999998,
                            "methods": "L1"
                          },
                          {
                            "date": "2026-06-25T00:00:00",
                            "value": 0,
                            "methods": null
                          }
                        ],
                        "feedSeries": [
                          {
                            "date": "2026-05-27T00:00:00",
                            "value": 389.832,
                            "methods": "L2"
                          }
                        ]
                      }
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/consumptionsurya/p/meter-007", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "s": true,
                      "data": [
                        [ "2026-06-01T00:00:00", 20.6658 ],
                        [ "2026-06-02T00:00:00", 58.4524 ]
                      ],
                      "meta": {
                        "scale_x": "day"
                      }
                    }
                    """);
            }

            if (request.RequestUri.AbsolutePath.EndsWith("/consumptionsurya/g/meter-007", StringComparison.OrdinalIgnoreCase))
            {
                return JsonResponse("""
                    {
                      "success": true,
                      "s": true,
                      "data": [
                        [ "2026-06-01T00:00:00", 120.5 ],
                        [ "2026-06-02T00:00:00", 98.4 ]
                      ],
                      "meta": {
                        "scale_x": "day"
                      }
                    }
                    """);
            }

            return new HttpResponseMessage(HttpStatusCode.NotFound)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json")
            };
        });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri($"{options.BaseUrl.TrimEnd('/')}/", UriKind.Absolute),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };

        var sut = new EdaPortalClient(httpClient, Options.Create(options), NullLogger<EdaPortalClient>.Instance);
        var period = new EdaPeriodDefinition(
            "custom",
            new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 6, 27, 23, 45, 0, TimeSpan.Zero),
            "day");

        var kpi = await sut.FetchKpiAsync(options.CommunityId, "meter-007", period, CancellationToken.None);
        var meter = await sut.FetchMeterDataAsync(options.CommunityId, period, CancellationToken.None);
        var consumptionP = await sut.FetchConsumptionSuryaAsync(options.CommunityId, "meter-007", period, EdaConsumptionSuryaRoute.P, CancellationToken.None);
        var consumptionG = await sut.FetchConsumptionSuryaAsync(options.CommunityId, "meter-007", period, EdaConsumptionSuryaRoute.G, CancellationToken.None);
        var consumptionPoints = await sut.FetchConsumptionSuryaPointsAsync(options.CommunityId, "meter-007", period, CancellationToken.None);

        Assert.NotNull(kpi);
        Assert.Equal(12.3m, kpi.Autarky);
        Assert.Equal(45.6m, kpi.OwnConsumption);
        Assert.Equal(78.9m, kpi.Community);
        Assert.Equal(1.2m, kpi.Feed);
        Assert.Equal(3.4m, kpi.RemainingDemand);

        Assert.NotNull(meter);
        Assert.True(meter.SubstitutesOrMissingData);
        Assert.Equal(15442.247999999994m, meter.SumGeneration);
        Assert.Equal(7892.924010000001m, meter.SumFeed);

        var firstGenerationPoint = Assert.Single(meter.GenerationSeries, point => point.Methods == "L1");
        Assert.Equal(new DateTimeOffset(2026, 5, 27, 0, 0, 0, TimeSpan.Zero), firstGenerationPoint.Timestamp);
        Assert.Equal(627.8119999999998m, firstGenerationPoint.Value);
        Assert.Equal("L1", firstGenerationPoint.Methods);

        var lastGenerationPoint = Assert.Single(meter.GenerationSeries, point => point.Value == 0m);
        Assert.Null(lastGenerationPoint.Methods);

        var kpiRequest = Assert.Single(capturedRequests, request =>
            request.Uri.AbsolutePath.EndsWith($"/pwa/energycommunities/{options.CommunityId}/kpiData", StringComparison.OrdinalIgnoreCase));
        var meterRequest = Assert.Single(capturedRequests, request =>
            request.Uri.AbsolutePath.EndsWith($"/pwa/energycommunities/{options.CommunityId}/meterdata", StringComparison.OrdinalIgnoreCase));
        var consumptionPRequests = capturedRequests
            .Where(request => request.Uri.AbsoluteUri.Equals("https://prod.eda-portal.at/api/consumptionsurya/p/meter-007", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        var consumptionGRequests = capturedRequests
            .Where(request => request.Uri.AbsoluteUri.Equals("https://prod.eda-portal.at/api/consumptionsurya/g/meter-007", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        Assert.Equal(2, consumptionPRequests.Length);
        Assert.Equal(2, consumptionGRequests.Length);
        var consumptionPRequest = consumptionPRequests[0];
        var consumptionGRequest = consumptionGRequests[0];

        Assert.Equal(HttpMethod.Post, kpiRequest.Method);
        Assert.Equal(HttpMethod.Post, meterRequest.Method);
        Assert.Equal(HttpMethod.Post, consumptionPRequest.Method);
        Assert.Equal(HttpMethod.Post, consumptionGRequest.Method);
        Assert.All(new[] { kpiRequest, meterRequest, consumptionPRequest, consumptionGRequest }, request =>
        {
            Assert.NotNull(request.Authorization);
            Assert.Equal("Bearer", request.Authorization!.Scheme);
            Assert.Equal("token-value", request.Authorization.Parameter);
        });

        AssertPayload(kpiRequest.Body!, options.CommunityId, meterId: "meter-007", includeMeterIdProperty: true);
        AssertPayload(meterRequest.Body!, options.CommunityId, meterId: null, includeMeterIdProperty: false);
        AssertPayload(consumptionPRequest.Body!, options.CommunityId, meterId: "meter-007", includeMeterIdProperty: false, meterName: "meter-007");
        AssertPayload(consumptionGRequest.Body!, options.CommunityId, meterId: "meter-007", includeMeterIdProperty: false, meterName: "meter-007");

        Assert.NotNull(consumptionP);
        Assert.Equal("day", consumptionP.ScaleX);
        Assert.Equal(2, consumptionP.Series.Count);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), consumptionP.Series[0].Timestamp);
        Assert.Equal(20.6658m, consumptionP.Series[0].Value);
        Assert.Null(consumptionP.Series[0].Methods);

        Assert.NotNull(consumptionG);
        Assert.Equal("day", consumptionG.ScaleX);
        Assert.Equal(2, consumptionG.Series.Count);
        Assert.Equal(new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero), consumptionG.Series[0].Timestamp);
        Assert.Equal(120.5m, consumptionG.Series[0].Value);
        Assert.Null(consumptionG.Series[0].Methods);
        Assert.Empty(consumptionG.Points);

        var comparisonPoint = Assert.Single(consumptionPoints, point => point.Timestamp == new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
        Assert.Equal(120.5m, comparisonPoint.GValue);
        Assert.Equal(20.6658m, comparisonPoint.PValue);
        Assert.Equal(99.8342m, comparisonPoint.Difference);

        Assert.Single(capturedRequests, request => request.Uri.AbsoluteUri.Equals(options.LoginUrl, StringComparison.OrdinalIgnoreCase));
    }

    private static void AssertPayload(
        string body,
        string expectedCommunityId,
        string? meterId,
        bool includeMeterIdProperty,
        string? meterName = null)
    {
        var payload = JsonDocument.Parse(body);
        Assert.Equal(expectedCommunityId, payload.RootElement.GetProperty("energyCommunityId").GetString());
        Assert.Equal("day", payload.RootElement.GetProperty("groupBy").GetString());

        var time = payload.RootElement.GetProperty("time").GetProperty("in");
        Assert.Equal("2026-05-27T00:00", time.GetProperty("min").GetString());
        Assert.Equal("2026-06-27T23:45", time.GetProperty("max").GetString());

        if (!includeMeterIdProperty)
        {
            Assert.False(payload.RootElement.TryGetProperty("meterId", out _));
        }
        else
        {
            Assert.Equal(meterId, payload.RootElement.GetProperty("meterId").GetString());
        }
        if (meterName is not null)
        {
            Assert.Equal(meterName, payload.RootElement.GetProperty("name").GetString());
        }
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed record CapturedRequest(
        Uri Uri,
        HttpMethod Method,
        AuthenticationHeaderValue? Authorization,
        string? Body);

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(handler(request));
    }
}
