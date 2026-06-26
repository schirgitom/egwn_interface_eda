using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EGWNInterfaceEda.Domain;
using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Infrastructure.Services;

public sealed class EdaPortalClient(HttpClient httpClient, IOptions<EdaOptions> options, ILogger<EdaPortalClient> logger) : IEdaPortalClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly EdaOptions _options = options.Value;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset? _tokenExpiryUtc;

    public async Task<EdaPeriodSnapshot> FetchPeriodAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
    {
        var body = new
        {
            energyCommunityId = communityId,
            groupBy = period.GroupBy,
            time = new
            {
                @in = new
                {
                    min = ToEdaTimestamp(period.From),
                    max = ToEdaTimestamp(period.To)
                }
            }
        };

        var kpiTask = PostAsync<EdaApiEnvelope<EdaKpiDto>>($"/pwa/energycommunities/{communityId}/kpiData", body, cancellationToken);
        var meterTask = PostAsync<EdaApiEnvelope<EdaMeterDto>>($"/pwa/energycommunities/{communityId}/meterdata", body, cancellationToken);

        await Task.WhenAll(kpiTask, meterTask);

        return new EdaPeriodSnapshot(
            period,
            MapKpi(kpiTask.Result),
            MapMeter(meterTask.Result),
            DateTimeOffset.UtcNow);
    }

    private async Task<T> PostAsync<T>(string relativePath, object body, CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);
        using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
        {
            Content = JsonContent.Create(body, options: SerializerOptions)
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var payload = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException($"EDA response from '{relativePath}' could not be parsed");
    }

    private async Task<string> GetTokenAsync(CancellationToken cancellationToken)
    {
        if (_token is not null && _tokenExpiryUtc is not null && _tokenExpiryUtc - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
        {
            return _token;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_token is not null && _tokenExpiryUtc is not null && _tokenExpiryUtc - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
            {
                return _token;
            }

            logger.LogInformation("Logging in to EDA portal");
            using var response = await httpClient.PostAsJsonAsync("/v4/auth/login", new
            {
                email = _options.Username,
                password = _options.Password
            }, SerializerOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var auth = await response.Content.ReadFromJsonAsync<EdaLoginResponse>(SerializerOptions, cancellationToken)
                       ?? throw new InvalidOperationException("EDA login did not return a token");
            if (string.IsNullOrWhiteSpace(auth.Token))
            {
                throw new InvalidOperationException("EDA login did not return a token");
            }

            _token = auth.Token;
            _tokenExpiryUtc = ParseJwtExpiry(auth.Token);
            logger.LogInformation("EDA login succeeded; token expires at {Expiry}", _tokenExpiryUtc);
            return auth.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static EdaKpiData? MapKpi(EdaApiEnvelope<EdaKpiDto> envelope)
    {
        if (!envelope.Success || envelope.Data is null)
        {
            return null;
        }

        return new EdaKpiData(envelope.Data.Autarky, envelope.Data.OwnConsumption, envelope.Data.Community, envelope.Data.Feed, envelope.Data.RemainingDemand);
    }

    private static EdaMeterData? MapMeter(EdaApiEnvelope<EdaMeterDto> envelope)
    {
        if (!envelope.Success || envelope.Data is null)
        {
            return null;
        }

        return new EdaMeterData(
            envelope.Data.SumGeneration,
            envelope.Data.SumFeed,
            envelope.Data.GenerationSeries ?? [],
            envelope.Data.FeedSeries ?? []);
    }

    private static string ToEdaTimestamp(DateTimeOffset value) => value.ToString("yyyy-MM-ddTHH:mm", CultureInfo.InvariantCulture);

    private static DateTimeOffset? ParseJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2)
            {
                return null;
            }

            var payloadJson = Encoding.UTF8.GetString(Base64UrlDecode(parts[1]));
            using var document = JsonDocument.Parse(payloadJson);
            if (!document.RootElement.TryGetProperty("exp", out var expElement))
            {
                return null;
            }

            return DateTimeOffset.FromUnixTimeSeconds(expElement.GetInt64());
        }
        catch
        {
            return null;
        }
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(output.PadRight(output.Length + (4 - output.Length % 4) % 4, '='));
    }

    private sealed record EdaLoginResponse([property: JsonPropertyName("token")] string Token);

    private sealed record EdaApiEnvelope<T>(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("data")] T? Data);

    private sealed record EdaKpiDto(
        [property: JsonPropertyName("autarky")] decimal? Autarky,
        [property: JsonPropertyName("ownConsumption")] decimal? OwnConsumption,
        [property: JsonPropertyName("community")] decimal? Community,
        [property: JsonPropertyName("feed")] decimal? Feed,
        [property: JsonPropertyName("remainingDemand")] decimal? RemainingDemand);

    private sealed record EdaMeterDto(
        [property: JsonPropertyName("sumGeneration")] decimal? SumGeneration,
        [property: JsonPropertyName("sumFeed")] decimal? SumFeed,
        [property: JsonPropertyName("generationSeries")] IReadOnlyList<EdaSeriesPoint>? GenerationSeries,
        [property: JsonPropertyName("feedSeries")] IReadOnlyList<EdaSeriesPoint>? FeedSeries);
}
