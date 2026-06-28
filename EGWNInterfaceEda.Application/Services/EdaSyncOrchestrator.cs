using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Application.Services;

public sealed class EdaSyncOrchestrator(
    ICentralApiClient centralApiClient,
    IEdaPortalClient edaPortalClient,
    IEdaResultPublisher publisher,
    IOptions<EdaOptions> edaOptions,
    IClock clock,
    ILogger<EdaSyncOrchestrator> logger) : IEdaSyncOrchestrator
{
    private readonly EdaOptions _edaOptions = edaOptions.Value;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting EDA synchronization run");

        var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
        if (customers.Count == 0)
        {
            logger.LogWarning("Skipping EDA synchronization because no customers were returned");
            return;
        }

        var periods = EdaPeriodDefinition.CreateAll(clock.UtcNow, _edaOptions.CustomFrom, _edaOptions.CustomTo);
        var customersByCommunity = customers
            .GroupBy(customer => customer.EnergyCommunityId ?? _edaOptions.CommunityId, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var customerGroup in customersByCommunity)
        {
            foreach (var period in periods)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var meter = await edaPortalClient.FetchMeterDataAsync(customerGroup.Key, period, cancellationToken);
                foreach (var customer in customerGroup)
                {
                    var meterId = ResolveMeterId(customer);
                    var kpi = await edaPortalClient.FetchKpiAsync(customerGroup.Key, meterId, period, cancellationToken);
                    var snapshot = new EdaPeriodSnapshot(period, kpi, meter, clock.UtcNow);
                    var publication = new EdaSyncPublication(customer, customerGroup.Key, snapshot, clock.UtcNow);
                    await publisher.PublishAsync(publication, cancellationToken);
                }

                logger.LogInformation("Published {CustomerCount} EDA messages for community {CommunityId} and period {Period}", customerGroup.Count(), customerGroup.Key, period.Key);
            }
        }
    }

    private static string ResolveMeterId(CustomerMeterPoint customer) =>
        !string.IsNullOrWhiteSpace(customer.ExternalReference)
            ? customer.ExternalReference
            : customer.MeterPointNumber;
}
