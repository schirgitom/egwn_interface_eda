using System.Net;
using System.Text;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Infrastructure.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Tests;

public sealed class CentralApiClientTests
{
    [Fact]
    public async Task GetCustomersAsync_uses_community_source_as_eda_id_for_each_participation()
    {
        const string baseUrl = "https://central.example/";
        var requests = new List<(string Path, string? Authorization)>();
        var handler = new StubHttpMessageHandler(request =>
        {
            requests.Add((request.RequestUri!.AbsolutePath, request.Headers.Authorization?.ToString()));
            if (request.RequestUri.AbsolutePath.EndsWith("/api/auth/token", StringComparison.Ordinal))
            {
                return JsonResponse("""{"accessToken":"test-token"}""");
            }

            return JsonResponse("""
                [
                  {
                    "id": "10000000-0000-0000-0000-000000000001",
                    "name": "Community One",
                    "source": "eda-community-1",
                    "participations": [
                      {
                        "meteringPointId": "20000000-0000-0000-0000-000000000001",
                        "meteringPointCode": "meter-point-1",
                        "customerNumber": "customer-1",
                        "customerName": "Customer One",
                        "participationPercentage": 100
                      },
                      {
                        "meteringPointId": "20000000-0000-0000-0000-000000000002",
                        "meteringPointCode": "meter-point-shared",
                        "customerNumber": "customer-2",
                        "customerName": "Customer Two",
                        "participationPercentage": 50
                      }
                    ]
                  },
                  {
                    "id": "10000000-0000-0000-0000-000000000002",
                    "name": "Community Two",
                    "source": "eda-community-2",
                    "participations": [
                      {
                        "meteringPointId": "20000000-0000-0000-0000-000000000002",
                        "meteringPointCode": "meter-point-shared",
                        "customerNumber": "customer-2",
                        "customerName": "Customer Two",
                        "participationPercentage": 50
                      }
                    ]
                  },
                  {
                    "id": "10000000-0000-0000-0000-000000000003",
                    "name": "Unmapped Community",
                    "source": null,
                    "participations": [
                      {
                        "meteringPointId": "20000000-0000-0000-0000-000000000003",
                        "meteringPointCode": "meter-point-unmapped",
                        "customerNumber": "customer-3",
                        "customerName": "Customer Three",
                        "participationPercentage": 100
                      }
                    ]
                  }
                ]
                """);
        });

        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri(baseUrl) };
        var client = new CentralApiClient(
            httpClient,
            Options.Create(new CentralApiOptions
            {
                BaseUrl = baseUrl,
                AuthPath = "api/auth/token",
                EnergyCommunitiesPath = "api/energy-communities",
                Username = "central-user",
                Password = "central-password"
            }),
            NullLogger<CentralApiClient>.Instance);

        var customers = await client.GetCustomersAsync(CancellationToken.None);

        Assert.Equal(3, customers.Count);
        Assert.Collection(
            customers,
            item =>
            {
                Assert.Equal("customer-1", item.CustomerId);
                Assert.Equal("Customer One", item.CustomerName);
                Assert.Equal("meter-point-1", item.MeterPointNumber);
                Assert.Equal("eda-community-1", item.EnergyCommunityId);
            },
            item =>
            {
                Assert.Equal("customer-2", item.CustomerId);
                Assert.Equal("meter-point-shared", item.MeterPointNumber);
                Assert.Equal("eda-community-1", item.EnergyCommunityId);
            },
            item =>
            {
                Assert.Equal("customer-2", item.CustomerId);
                Assert.Equal("meter-point-shared", item.MeterPointNumber);
                Assert.Equal("eda-community-2", item.EnergyCommunityId);
            });
        Assert.Equal(new[] { "/api/auth/token", "/api/energy-communities" }, requests.Select(request => request.Path));
        Assert.Equal("Bearer test-token", requests[1].Authorization);
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> handler) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(handler(request));
    }
}
