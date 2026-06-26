using System.Net.Http.Json;
using System.Text.Json;
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

    public async Task<IReadOnlyList<CustomerMeterPoint>> GetCustomersAsync(CancellationToken cancellationToken)
    {
        var response = await httpClient.GetAsync(BuildUri(_options.CustomersPath), cancellationToken);
        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var customers = await ReadCustomersAsync(stream, cancellationToken);

        if (customers.Count == 0)
        {
            logger.LogWarning("Central API returned no customers");
        }

        return customers;
    }

    private static Uri BuildUri(string path) => new(path, UriKind.Relative);

    private static async Task<IReadOnlyList<CustomerMeterPoint>> ReadCustomersAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
        var root = document.RootElement;

        var array = root.ValueKind switch
        {
            JsonValueKind.Array => root,
            JsonValueKind.Object when TryGetCollection(root, out var items) => items,
            _ => throw new InvalidOperationException("Central API response does not contain a customer collection")
        };

        var customers = new List<CustomerMeterPoint>();
        foreach (var element in array.EnumerateArray())
        {
            var dto = element.Deserialize<CentralApiCustomerDto>(SerializerOptions)
                      ?? throw new InvalidOperationException("Central API customer item could not be parsed");

            var customerId = FirstNonEmpty(dto.CustomerId, dto.Id, dto.ExternalId)
                             ?? throw new InvalidOperationException("Central API customer item is missing an identifier");
            var customerName = FirstNonEmpty(dto.CustomerName, dto.DisplayName, dto.Name)
                              ?? customerId;
            var meterPointNumber = FirstNonEmpty(dto.MeterPointNumber, dto.Zaehlpunktnummer, dto.MeterPoint, dto.PointNumber)
                                  ?? throw new InvalidOperationException($"Customer '{customerId}' is missing a meter point number");

            customers.Add(new CustomerMeterPoint(customerId, customerName, meterPointNumber, dto.EnergyCommunityId ?? dto.CommunityId, dto.ExternalReference));
        }

        return customers;
    }

    private static bool TryGetCollection(JsonElement root, out JsonElement collection)
    {
        foreach (var propertyName in new[] { "data", "items", "customers", "results" })
        {
            if (root.TryGetProperty(propertyName, out collection) && collection.ValueKind == JsonValueKind.Array)
            {
                return true;
            }
        }

        collection = default;
        return false;
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

    private sealed record CentralApiCustomerDto(
        string? Id,
        string? ExternalId,
        string? CustomerId,
        string? CustomerName,
        string? DisplayName,
        string? Name,
        string? MeterPointNumber,
        string? Zaehlpunktnummer,
        string? MeterPoint,
        string? PointNumber,
        string? EnergyCommunityId,
        string? CommunityId,
        string? ExternalReference);
}
