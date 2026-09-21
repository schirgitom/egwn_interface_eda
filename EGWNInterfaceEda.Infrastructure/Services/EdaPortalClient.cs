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

    public async Task<EdaKpiData?> FetchKpiAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
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

        var envelope = await PostAsync<EdaApiEnvelope<EdaKpiDto>>($"pwa/energycommunities/{communityId}/kpiData", body, cancellationToken);
        return MapKpi(envelope);
    }

    public async Task<EdaMeterData?> FetchMeterDataAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
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

        var envelope = await PostAsync<EdaApiEnvelope<EdaMeterDto>>($"pwa/energycommunities/{communityId}/meterdata", body, cancellationToken);
        return MapMeter(envelope);
    }

    public async Task<EdaConsumptionSuryaData?> FetchConsumptionSuryaAsync(
        string communityId,
        string meterId,
        EdaPeriodDefinition period,
        EdaConsumptionSuryaRoute route,
        CancellationToken cancellationToken)
    {
        var body = new
        {
            energyCommunityId = communityId,
            name = meterId,
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

        var routeSegment = route == EdaConsumptionSuryaRoute.G ? "g" : "p";
        var endpoint = $"{_options.ConsumptionSuryaBaseUrl.TrimEnd('/')}/consumptionsurya/{routeSegment}/{Uri.EscapeDataString(meterId)}";
        var envelope = await PostAsync<EdaConsumptionSuryaEnvelope>(endpoint, body, cancellationToken);
        return MapConsumptionSurya(envelope);
    }

    public async Task<EdaConsumptionSuryaCombinedData> FetchConsumptionSuryaPointsAsync(
        string communityId,
        string meterId,
        EdaPeriodDefinition period,
        CancellationToken cancellationToken)
    {
        var gTask = FetchConsumptionSuryaAsync(communityId, meterId, period, EdaConsumptionSuryaRoute.G, cancellationToken);
        var pTask = FetchConsumptionSuryaAsync(communityId, meterId, period, EdaConsumptionSuryaRoute.P, cancellationToken);
        await Task.WhenAll(gTask, pTask);

        return MergeConsumptionSurya(gTask.Result, pTask.Result);
    }

    private async Task<T?> PostAsync<T>(string relativePath, object body, CancellationToken cancellationToken)
    {
        for (var attempt = 1; ; attempt++)
        {
            var token = await GetTokenAsync(forceRefresh: attempt > 1, cancellationToken);
            using var request = new HttpRequestMessage(HttpMethod.Post, relativePath)
            {
                Content = JsonContent.Create(body, options: SerializerOptions)
            };
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Accept.ParseAdd("application/json");

            using var response = await httpClient.SendAsync(request, cancellationToken);
            var raw = await response.Content.ReadAsStringAsync(cancellationToken);
            var mediaType = response.Content.Headers.ContentType?.MediaType;
            var looksLikeJson = raw.AsSpan().TrimStart() is var trimmed && trimmed.Length > 0 && (trimmed[0] == '{' || trimmed[0] == '[');
            var isJsonResponse = (mediaType is null || mediaType.Contains("json", StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(raw) || looksLikeJson);

            var authFailed = response.StatusCode == System.Net.HttpStatusCode.Unauthorized
                             || response.StatusCode == System.Net.HttpStatusCode.Forbidden
                             || (response.IsSuccessStatusCode && !isJsonResponse);

            if (authFailed && attempt == 1)
            {
                logger.LogWarning(
                    "EDA endpoint '{Path}' responded as unauthenticated (status {StatusCode}, content-type {ContentType}); refreshing token and retrying once",
                    relativePath,
                    (int)response.StatusCode,
                    mediaType ?? "<none>");
                InvalidateToken(token);
                continue;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogError(
                    "EDA endpoint '{Path}' returned {StatusCode}. Content-Type: {ContentType}. Body: {Body}",
                    relativePath,
                    (int)response.StatusCode,
                    mediaType ?? "<none>",
                    Truncate(raw, 1000));
                response.EnsureSuccessStatusCode();
            }

            if (string.IsNullOrWhiteSpace(raw))
            {
                logger.LogWarning("EDA endpoint '{Path}' returned an empty response body", relativePath);
                return default;
            }

            if (!isJsonResponse)
            {
                logger.LogError(
                    "EDA endpoint '{Path}' did not return JSON. Status: {StatusCode}. Content-Type: {ContentType}. Body: {Body}",
                    relativePath,
                    (int)response.StatusCode,
                    mediaType ?? "<none>",
                    Truncate(raw, 1000));
                throw new InvalidOperationException(
                    $"EDA endpoint '{relativePath}' returned a non-JSON response (Content-Type: {mediaType ?? "<none>"}). See logs for details.");
            }

            try
            {
                return JsonSerializer.Deserialize<T>(raw, SerializerOptions);
            }
            catch (JsonException ex)
            {
                logger.LogError(
                    ex,
                    "Failed to deserialize response from EDA endpoint '{Path}'. Body: {Body}",
                    relativePath,
                    Truncate(raw, 1000));
                throw;
            }
        }
    }

    private void InvalidateToken(string? expectedToken)
    {
        _tokenLock.Wait();
        try
        {
            if (expectedToken is null || string.Equals(_token, expectedToken, StringComparison.Ordinal))
            {
                _token = null;
                _tokenExpiryUtc = null;
            }
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string Truncate(string? value, int maxLength)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        return value.Length <= maxLength ? value : value[..maxLength] + "...";
    }

    private async Task<string> GetTokenAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (!forceRefresh && _token is not null && _tokenExpiryUtc is not null && _tokenExpiryUtc - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
        {
            return _token;
        }

        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (!forceRefresh && _token is not null && _tokenExpiryUtc is not null && _tokenExpiryUtc - DateTimeOffset.UtcNow > TimeSpan.FromMinutes(5))
            {
                return _token;
            }

            logger.LogInformation("Logging in to EDA portal");
            using var response = await httpClient.PostAsJsonAsync(_options.LoginUrl, new
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
            _tokenExpiryUtc = ParseExpiry(auth.Exp) ?? ParseJwtExpiry(auth.Token);
            logger.LogInformation("EDA login succeeded; token expires at {Expiry}", _tokenExpiryUtc);
            return auth.Token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static EdaKpiData? MapKpi(EdaApiEnvelope<EdaKpiDto>? envelope)
    {
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            return null;
        }

        return new EdaKpiData(envelope.Data.Autarky, envelope.Data.OwnConsumption, envelope.Data.Community, envelope.Data.Feed, envelope.Data.RemainingDemand);
    }

    private static EdaMeterData? MapMeter(EdaApiEnvelope<EdaMeterDto>? envelope)
    {
        if (envelope is null || !envelope.Success || envelope.Data is null)
        {
            return null;
        }

        return new EdaMeterData(
            envelope.Data.SubstitutesOrMissingData,
            envelope.Data.SumGeneration,
            envelope.Data.SumFeed,
            MapSeries(envelope.Data.GenerationSeries),
            MapSeries(envelope.Data.FeedSeries));
    }

    private static EdaConsumptionSuryaData? MapConsumptionSurya(EdaConsumptionSuryaEnvelope? envelope)
    {
        if (envelope is null || !envelope.Success || envelope.Data.ValueKind != JsonValueKind.Array)
        {
            return null;
        }

        var points = new List<EdaSeriesPoint>();
        foreach (var item in envelope.Data.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Array || item.GetArrayLength() < 2)
            {
                continue;
            }

            var timestamp = ParseEdaTimestamp(item[0].GetString());
            var value = item[1].ValueKind switch
            {
                JsonValueKind.Number => item[1].GetDecimal(),
                JsonValueKind.String when decimal.TryParse(item[1].GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) => parsed,
                _ => 0m
            };

            points.Add(new EdaSeriesPoint(timestamp, value, null));
        }

        return new EdaConsumptionSuryaData(points, envelope.Meta?.ScaleX, []);
    }

    private static EdaConsumptionSuryaCombinedData MergeConsumptionSurya(EdaConsumptionSuryaData? g, EdaConsumptionSuryaData? p)
    {
        var series = new Dictionary<DateTimeOffset, (decimal? G, decimal? P)>();

        if (g is not null)
        {
            foreach (var point in g.Series)
            {
                if (point.Timestamp is null)
                {
                    continue;
                }

                var timestamp = point.Timestamp.Value;
                series[timestamp] = series.TryGetValue(timestamp, out var existing)
                    ? (point.Value, existing.P)
                    : (point.Value, null);
            }
        }

        if (p is not null)
        {
            foreach (var point in p.Series)
            {
                if (point.Timestamp is null)
                {
                    continue;
                }

                var timestamp = point.Timestamp.Value;
                series[timestamp] = series.TryGetValue(timestamp, out var existing)
                    ? (existing.G, point.Value)
                    : (null, point.Value);
            }
        }

        var totalConsumption = series.Values.Where(entry => entry.G.HasValue).Sum(entry => entry.G!.Value);
        var totalGridShare = series.Values.Where(entry => entry.P.HasValue).Sum(entry => entry.P!.Value);
        var totalCommunityShare = totalConsumption - totalGridShare;

        var points = series
            .OrderBy(entry => entry.Key)
            .Select(entry =>
            {
                var totalConsumptionValue = entry.Value.G;
                var gridShareValue = entry.Value.P;
                var communityShareValue = totalConsumptionValue.HasValue && gridShareValue.HasValue
                    ? (decimal?)(totalConsumptionValue.Value - gridShareValue.Value)
                    : null;

                return new EdaConsumptionSuryaPoint(entry.Key, totalConsumptionValue, gridShareValue, communityShareValue);
            })
            .ToArray();

        return new EdaConsumptionSuryaCombinedData(
            points,
            totalConsumption,
            totalGridShare,
            totalCommunityShare,
            totalConsumption > 0m ? totalGridShare / totalConsumption * 100m : 0m,
            totalConsumption > 0m ? totalCommunityShare / totalConsumption * 100m : 0m);
    }

    private static IReadOnlyList<EdaSeriesPoint> MapSeries(IReadOnlyList<EdaSeriesPointDto>? series)
    {
        if (series is null || series.Count == 0)
        {
            return [];
        }

        return series.Select(point => new EdaSeriesPoint(
            ParseEdaTimestamp(point.Date),
            point.Value,
            point.Methods)).ToArray();
    }

    private static DateTimeOffset? ParseEdaTimestamp(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out var timestamp)
            ? timestamp
            : null;
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

    private static DateTimeOffset? ParseExpiry(JsonElement exp)
    {
        return exp.ValueKind switch
        {
            JsonValueKind.String when DateTimeOffset.TryParse(
                exp.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var dateTimeOffset) => dateTimeOffset,
            JsonValueKind.Number when exp.TryGetInt64(out var unixSeconds) => DateTimeOffset.FromUnixTimeSeconds(unixSeconds),
            _ => null
        };
    }

    private static byte[] Base64UrlDecode(string input)
    {
        var output = input.Replace('-', '+').Replace('_', '/');
        return Convert.FromBase64String(output.PadRight(output.Length + (4 - output.Length % 4) % 4, '='));
    }

    private sealed record EdaLoginResponse(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("exp")] JsonElement Exp);

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
        [property: JsonPropertyName("substitutesOrMissingData")] bool? SubstitutesOrMissingData,
        [property: JsonPropertyName("sumGeneration")] decimal? SumGeneration,
        [property: JsonPropertyName("sumFeed")] decimal? SumFeed,
        [property: JsonPropertyName("generationSeries")] IReadOnlyList<EdaSeriesPointDto>? GenerationSeries,
        [property: JsonPropertyName("feedSeries")] IReadOnlyList<EdaSeriesPointDto>? FeedSeries);

    private sealed record EdaConsumptionSuryaEnvelope(
        [property: JsonPropertyName("success")] bool Success,
        [property: JsonPropertyName("data")] JsonElement Data,
        [property: JsonPropertyName("meta")] EdaConsumptionSuryaMeta? Meta);

    private sealed record EdaConsumptionSuryaMeta(
        [property: JsonPropertyName("scale_x")] string? ScaleX);

    private sealed record EdaSeriesPointDto(
        [property: JsonPropertyName("date")] string? Date,
        [property: JsonPropertyName("value")] decimal Value,
        [property: JsonPropertyName("methods")] string? Methods);
}
