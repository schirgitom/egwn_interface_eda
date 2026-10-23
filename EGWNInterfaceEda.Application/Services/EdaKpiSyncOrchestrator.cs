using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Application.Services;

public sealed class EdaKpiSyncOrchestrator(
    ICentralApiClient centralApiClient,
    IEdaPortalClient edaPortalClient,
    IEdaResultPublisher publisher,
    IClock clock,
    IOptions<EdaOptions> edaOptions,
    ILogger<EdaKpiSyncOrchestrator> logger) : IEdaKpiSyncOrchestrator
{
    private readonly EdaOptions _edaOptions = edaOptions.Value;

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting KPI synchronization run");

        var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
        if (customers.Count == 0 && _edaOptions.GetCommunities().Count == 0)
        {
            logger.LogWarning("Skipping KPI synchronization because no customers were returned");
            return;
        }

        var timestamp = clock.UtcNow;
        var period = new EdaPeriodDefinition("kpi-sync", timestamp.AddDays(-31), timestamp, "day");
        var communityIds = new HashSet<string>(
            _edaOptions.GetCommunities().Select(community => community.CommunityId),
            StringComparer.OrdinalIgnoreCase);
        foreach (var customer in customers)
        {
            communityIds.Add(customer.EnergyCommunityId ?? _edaOptions.DefaultCommunityId);
        }

        foreach (var communityId in communityIds)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var community = _edaOptions.ResolveCommunity(communityId);
            var kpi = await edaPortalClient.FetchKpiAsync(communityId, period, cancellationToken);
            if (kpi is null)
            {
                logger.LogWarning("No KPI values were returned for community {CommunityId}", communityId);
                continue;
            }

            await publisher.PublishAsync(new EdaKpiSyncPublication(
                communityId,
                communityId,
                community.Name,
                [new EdaKpiSeriesPoint(timestamp, kpi)],
                clock.UtcNow), cancellationToken);
        }
    }
}
