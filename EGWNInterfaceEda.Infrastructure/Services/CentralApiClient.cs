using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using EGWNInterfaceEda.Domain;
using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Infrastructure.Services;

public sealed class CentralApiClient(HttpClient httpClient, IOptions<CentralApiOptions> options, ILogger<CentralApiClient> logger) : ICentralApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);
    private readonly CentralApiOptions _options = options.Value;
    private readonly ILogger<CentralApiClient> _logger = logger;
    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _token;
    private DateTimeOffset? _tokenExpiryUtc;

    public async Task<IReadOnlyList<CustomerMeterPoint>> GetCustomersAsync(CancellationToken cancellationToken)
    {
        var token = await GetTokenAsync(cancellationToken);

        var meteringPoints = await FetchMeteringPointsAsync(token, cancellationToken);

        if (meteringPoints.Count == 0)
        {
            _logger.LogWarning("Central API returned no metering points");
        }
        else
        {
            _logger.LogInformation("Central API returned {Count} metering points", meteringPoints.Count);
        }

        return meteringPoints;
    }

    private async Task<IReadOnlyList<CustomerMeterPoint>> FetchMeteringPointsAsync(string token, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BuildUri(_options.MeteringPointsPath));
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var rawJson = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogDebug("Central API metering-points raw response: {Response}", rawJson);

        await using var stream = new System.IO.MemoryStream(System.Text.Encoding.UTF8.GetBytes(rawJson));
        return await ReadMeteringPointsAsync(stream, cancellationToken);
    }

    private async Task<IReadOnlyList<CustomerMeterPoint>> ReadMeteringPointsAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var array = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when TryGetCollection(root, out var items) => items,
            _ => throw new InvalidOperationException("Central API metering-points response does not contain an array")
        };

        var result = new List<CustomerMeterPoint>();
        foreach (var element in array.EnumerateArray())
        {
            var dto = element.Deserialize<CentralApiMeteringPointDto>(SerializerOptions)
                      ?? throw new InvalidOperationException("Central API metering point item could not be parsed");

            var id = FirstNonEmpty(dto.Id, dto.ExternalId);
            if (id is null)
            {
                _logger.LogWarning("Skipping metering point without id");
                continue;
            }

            var meteringPointCode = FirstNonEmpty(dto.MeteringPointCode, dto.MeterPointNumber, dto.Zaehlpunktnummer, dto.Code);
            if (meteringPointCode is null)
            {
                _logger.LogWarning("Skipping metering point '{Id}' because no metering point code is set", id);
                continue;
            }

            var customerId = FirstNonEmpty(dto.CustomerId, dto.CustomerExternalId);
            var customerName = FirstNonEmpty(dto.CustomerName, dto.Name) ?? customerId ?? id;
            var communityId = FirstNonEmpty(dto.EnergyCommunityId, dto.CommunityId);

            result.Add(new CustomerMeterPoint(customerId ?? id, customerName, meteringPointCode, communityId, dto.ExternalReference));
        }

        return result;
    }

    private static bool TryGetCollection(JsonElement root, out JsonElement collection)
    {
        foreach (var propertyName in new[] { "data", "items", "meteringPoints", "metering_points", "results" })
        {
            if (root.TryGetProperty(propertyName, out collection) && collection.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        collection = default;
        return false;
    }

    private static Uri BuildUri(string path) => new(path, UriKind.Relative);

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

            logger.LogInformation("Logging in to Central API at {Url}", new Uri(httpClient.BaseAddress!, _options.AuthPath));
            var response = await httpClient.PostAsJsonAsync(BuildUri(_options.AuthPath), new
            {
                username = _options.Username,
                password = _options.Password
            }, SerializerOptions, cancellationToken);
            response.EnsureSuccessStatusCode();

            var auth = await response.Content.ReadFromJsonAsync<CentralApiLoginResponse>(SerializerOptions, cancellationToken)
                       ?? throw new InvalidOperationException("Central API login did not return a token");
            if (string.IsNullOrWhiteSpace(auth.AccessToken))
            {
                throw new InvalidOperationException("Central API login did not return a token");
            }

            _token = auth.AccessToken;
            _tokenExpiryUtc = ParseJwtExpiry(_token);
            logger.LogInformation("Central API login succeeded; token expires at {Expiry}", _tokenExpiryUtc);
            return _token;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static DateTimeOffset? ParseJwtExpiry(string token)
    {
        try
        {
            var parts = token.Split('.');
            if (parts.Length < 2) return null;
            var padded = parts[1].Replace('-', '+').Replace('_', '/');
            padded = padded.PadRight(padded.Length + (4 - padded.Length % 4) % 4, '=');
            var payloadJson = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            using var doc = JsonDocument.Parse(payloadJson);
            return doc.RootElement.TryGetProperty("exp", out var exp)
                ? DateTimeOffset.FromUnixTimeSeconds(exp.GetInt64())
                : null;
        }
        catch { return null; }
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record CentralApiLoginResponse(
        [property: JsonPropertyName("accessToken")] string AccessToken);

    private sealed record CentralApiMeteringPointDto(
        string? Id,
        string? ExternalId,
        string? ExternalReference,
        string? MeteringPointCode,
        string? MeterPointNumber,
        string? Zaehlpunktnummer,
        string? Code,
        string? CustomerId,
        string? CustomerExternalId,
        string? CustomerName,
        string? Name,
        string? EnergyCommunityId,
        string? CommunityId);
}
