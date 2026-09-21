using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Application.Services;

public sealed class EdaReadingOrchestrator(
    IEdaPortalClient portalClient,
    IEdaTriggerPublisher publisher,
    ICentralApiClient centralApiClient,
    IOptions<EdaOptions> options,
    IClock clock,
    ILogger<EdaReadingOrchestrator> logger) : IEdaReadingOrchestrator
{
    private readonly EdaOptions _options = options.Value;
    private const int BackfillYears = 2;

    public async Task<EdaReadingTriggerResponse> TriggerMeterReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken)
    {
        var period = BuildPeriod(request);

        // If a specific meter id is provided in the request, use it directly
        if (!string.IsNullOrWhiteSpace(request.MeterId))
        {
            return await FetchAndPublishMeterReadingAsync(request.MeterId, _options.CommunityId, period, cancellationToken);
        }

        // Otherwise fetch all meters from Central API
        var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
        if (customers.Count == 0)
        {
            logger.LogWarning("Skipping meter reading because no customers/meters found via Central API");
            return new EdaReadingTriggerResponse(null, period.From, period.To, 0);
        }

        var totalPoints = 0;
        foreach (var customer in customers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var communityId = customer.EnergyCommunityId ?? _options.CommunityId;
            var result = await FetchAndPublishMeterReadingAsync(customer.MeterPointNumber, communityId, period, cancellationToken);
            totalPoints += result.ReadingCount;
        }

        return new EdaReadingTriggerResponse(null, period.From, period.To, totalPoints);
    }

    public async Task<EdaReadingTriggerResponse> TriggerKpiReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken)
    {
        var period = BuildKpiPeriod(request);
        var kpi = await portalClient.FetchKpiAsync(_options.CommunityId, period, cancellationToken);

        if (kpi is null)
        {
            logger.LogWarning("KPI reading returned no data for community {CommunityId} – skipping RabbitMQ publish", _options.CommunityId);
            return new EdaReadingTriggerResponse(null, period.From, period.To, 0);
        }

        logger.LogInformation("KPI reading fetched data for community {CommunityId} ({From} – {To})",
            _options.CommunityId, period.From, period.To);

        await publisher.PublishKpiReadingsAsync(new EdaKpiReadingsPublication(
            _options.CommunityId,
            _options.CommunityId,
            [new EdaKpiValue(clock.UtcNow, kpi)],
            clock.UtcNow), cancellationToken);

        return new EdaReadingTriggerResponse(null, period.From, period.To, 1);
    }

    public async Task<EdaBackfillResponse> TriggerHistoricalBackfillAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var from = now.AddYears(-BackfillYears);
        var meterPeriod = new EdaPeriodDefinition("backfill", from, now, "hour");

        var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
        if (customers.Count == 0)
        {
            logger.LogWarning("Backfill: no customers/meters found via Central API");
            return new EdaBackfillResponse(from, now, 0, 0, 0);
        }

        var totalMeterReadings = 0;
        var totalKpiReadings = 0;

        foreach (var customer in customers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var communityId = customer.EnergyCommunityId ?? _options.CommunityId;
            var meterId = customer.MeterPointNumber;

            logger.LogInformation("Backfill: fetching meter data for {MeterId} ({CustomerId})", meterId, customer.CustomerId);

            try
            {
                var result = await FetchAndPublishMeterReadingAsync(meterId, communityId, meterPeriod, cancellationToken);
                totalMeterReadings += result.ReadingCount;
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Backfill: failed to fetch/publish meter data for {MeterId}", meterId);
            }

            // KPI-API liefert pro Request immer einen aggregierten Wert für den angefragten Zeitraum.
            // Um tagesweise Datenpunkte zu erhalten, wird pro Tag ein eigener Request gestellt.
            logger.LogInformation("Backfill: fetching daily KPI data for community {CommunityId} ({Days} days)", communityId, (int)(now - from).TotalDays);
            var kpiValues = new List<EdaKpiValue>();
            for (var day = from.Date; day <= now.Date; day = day.AddDays(1))
            {
                cancellationToken.ThrowIfCancellationRequested();
                var dayStart = new DateTimeOffset(day, from.Offset);
                var dayEnd = dayStart.AddDays(1).AddSeconds(-1);
                var kpiPeriod = new EdaPeriodDefinition("backfill-kpi-day", dayStart, dayEnd, "day");
                try
                {
                    var kpi = await portalClient.FetchKpiAsync(communityId, kpiPeriod, cancellationToken);
                    if (kpi is not null)
                    {
                        kpiValues.Add(new EdaKpiValue(dayStart, kpi));
                    }
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Backfill: failed to fetch KPI for community {CommunityId} on {Day}", communityId, day);
                }
            }

            if (kpiValues.Count > 0)
            {
                try
                {
                    await publisher.PublishKpiReadingsAsync(new EdaKpiReadingsPublication(
                        meterId,
                        communityId,
                        kpiValues,
                        clock.UtcNow), cancellationToken);
                    totalKpiReadings += kpiValues.Count;
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Backfill: failed to publish KPI readings for community {CommunityId}", communityId);
                }
            }
            else
            {
                logger.LogWarning("Backfill: no KPI data received for community {CommunityId}", communityId);
            }
        }

        logger.LogInformation("Backfill complete: {Meters} meters, {MeterReadings} meter readings, {KpiReadings} KPI day-values",
            customers.Count, totalMeterReadings, totalKpiReadings);

        return new EdaBackfillResponse(from, now, customers.Count, totalMeterReadings, totalKpiReadings);
    }

    private async Task<EdaReadingTriggerResponse> FetchAndPublishMeterReadingAsync(
        string meterPointNumber, string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
    {
        var readings = await portalClient.FetchConsumptionSuryaPointsAsync(communityId, meterPointNumber, period, cancellationToken);

        logger.LogInformation("Meter reading fetched {Count} points for {MeterId} ({From} – {To})",
            readings.Points.Count, meterPointNumber, period.From, period.To);

        if (readings.Points.Count == 0)
        {
            logger.LogWarning("Meter reading returned no data for {MeterId} – skipping RabbitMQ publish", meterPointNumber);
            return new EdaReadingTriggerResponse(meterPointNumber, period.From, period.To, 0);
        }

        await publisher.PublishMeterReadingsAsync(new EdaMeterReadingsPublication(
            meterPointNumber,
            communityId,
            readings.Points.Select(point => new EdaMeterReading(
                point.Timestamp ?? clock.UtcNow,
                point.TotalConsumptionValue,
                point.GridShareValue,
                point.CommunityShareValue)).ToArray(),
            clock.UtcNow), cancellationToken);

        return new EdaReadingTriggerResponse(meterPointNumber, period.From, period.To, readings.Points.Count);
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

