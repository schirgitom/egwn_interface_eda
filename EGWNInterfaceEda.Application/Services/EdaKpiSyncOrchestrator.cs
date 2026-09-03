using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging;

namespace EGWNInterfaceEda.Application.Services;

public sealed class EdaKpiSyncOrchestrator(
    ICentralApiClient centralApiClient,
    IEdaPortalClient edaPortalClient,
    IEdaResultPublisher publisher,
    IClock clock,
    ILogger<EdaKpiSyncOrchestrator> logger) : IEdaKpiSyncOrchestrator
{
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting KPI synchronization run");

        var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
        if (customers.Count == 0)
        {
            logger.LogWarning("Skipping KPI synchronization because no customers were returned");
            return;
        }

        var timestamp = clock.UtcNow;
        var communityId = customers[0].EnergyCommunityId ?? string.Empty;
        var period = new EdaPeriodDefinition("kpi-sync", timestamp.AddDays(-31), timestamp, "day");
        var kpi = await edaPortalClient.FetchKpiAsync(communityId, period, cancellationToken);
        if (kpi is null)
        {
            logger.LogWarning("No KPI values were returned for community {CommunityId}", communityId);
            return;
        }
        await publisher.PublishAsync(new EdaKpiSyncPublication(communityId, communityId, [new EdaKpiSeriesPoint(timestamp, kpi)], clock.UtcNow), cancellationToken);
    }
}
