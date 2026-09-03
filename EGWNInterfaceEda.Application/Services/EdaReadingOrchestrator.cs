using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Application.Services;

public sealed class EdaReadingOrchestrator(
    IEdaPortalClient portalClient,
    IEdaTriggerPublisher publisher,
    IOptions<EdaOptions> options,
    IClock clock,
    ILogger<EdaReadingOrchestrator> logger) : IEdaReadingOrchestrator
{
    private readonly EdaOptions _options = options.Value;

    public async Task<EdaReadingTriggerResponse> TriggerMeterReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken)
    {
        var communityId = _options.CommunityId;
        var meterPointNumber = request.MeterId ?? _options.MeterId;
        if (string.IsNullOrWhiteSpace(meterPointNumber))
        {
            logger.LogWarning("Skipping meter reading because no meter id is configured or provided");
            return new EdaReadingTriggerResponse(null, request.FromUtc, request.ToUtc, 0);
        }
        var period = BuildPeriod(request);
        var readings = await portalClient.FetchConsumptionSuryaPointsAsync(communityId, meterPointNumber, period, cancellationToken);

        await publisher.PublishMeterReadingsAsync(new EdaMeterReadingsPublication(
            meterPointNumber,
            communityId,
            readings.Points.Select(point => new EdaMeterReading(
                point.Timestamp ?? clock.UtcNow,
                point.TotalConsumptionValue,
                point.GridShareValue,
                point.CommunityShareValue)).ToArray(),
            clock.UtcNow), cancellationToken);

        return new EdaReadingTriggerResponse(request.MeterId ?? _options.MeterId, period.From, period.To, readings.Points.Count);
    }

    public async Task<EdaReadingTriggerResponse> TriggerKpiReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken)
    {
        var period = BuildKpiPeriod(request);
        var kpi = await portalClient.FetchKpiAsync(_options.CommunityId, period, cancellationToken);

        if (kpi is null)
        {
            logger.LogWarning("KPI reading returned no data for community {CommunityId}", _options.CommunityId);
            return new EdaReadingTriggerResponse(null, period.From, period.To, 0);
        }

        await publisher.PublishKpiReadingsAsync(new EdaKpiReadingsPublication(
            _options.CommunityId,
            _options.CommunityId,
            [new EdaKpiValue(clock.UtcNow, kpi)],
            clock.UtcNow), cancellationToken);

        return new EdaReadingTriggerResponse(null, period.From, period.To, 1);
    }

    private static EdaPeriodDefinition BuildPeriod(EdaTriggerRequest request)
    {
        var from = request.FromUtc ?? DateTimeOffset.UtcNow.AddDays(-21);
        var to = request.ToUtc ?? DateTimeOffset.UtcNow;
        return new EdaPeriodDefinition("trigger", from, to, "hour");
    }

    private static EdaPeriodDefinition BuildKpiPeriod(EdaTriggerRequest request)
    {
        var from = request.FromUtc ?? DateTimeOffset.UtcNow.AddDays(-31);
        var to = request.ToUtc ?? DateTimeOffset.UtcNow;
        return new EdaPeriodDefinition("kpi-trigger", from, to, "day");
    }
}
