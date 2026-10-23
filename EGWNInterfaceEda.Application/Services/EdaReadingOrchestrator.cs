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
    IEdaMetrics metrics,
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
            var communityId = string.IsNullOrWhiteSpace(request.CommunityId) ? _options.DefaultCommunityId : request.CommunityId;
            return await FetchAndPublishMeterReadingAsync(request.MeterId, communityId, period, cancellationToken);
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
            if (!string.IsNullOrWhiteSpace(request.CommunityId)
                && !string.Equals(customer.EnergyCommunityId ?? _options.DefaultCommunityId, request.CommunityId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            cancellationToken.ThrowIfCancellationRequested();
            var customerCommunityId = customer.EnergyCommunityId ?? _options.DefaultCommunityId;
            var result = await FetchAndPublishMeterReadingAsync(customer.MeterPointNumber, customerCommunityId, period, cancellationToken);
            totalPoints += result.ReadingCount;
        }

        return new EdaReadingTriggerResponse(null, period.From, period.To, totalPoints);
    }

    public async Task<EdaReadingTriggerResponse> TriggerKpiReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken)
    {
        var period = BuildKpiPeriod(request);
        var communities = string.IsNullOrWhiteSpace(request.CommunityId)
            ? _options.GetCommunities()
            : [_options.ResolveCommunity(request.CommunityId)];
        var totalValues = 0;

        foreach (var community in communities)
        {
            var start = clock.UtcNow;
            try
            {
                var kpiValues = await FetchDailyKpiValuesAsync(community.CommunityId, period.From, period.To, logProgress: false, cancellationToken);

                if (kpiValues.Count == 0)
                {
                    logger.LogWarning("KPI reading returned no data for community {CommunityId} ({From} – {To}) – skipping RabbitMQ publish",
                        community.CommunityId, period.From, period.To);
                    metrics.RecordKpiReading(community.CommunityId, 0, clock.UtcNow - start, success: false);
                    continue;
                }

                logger.LogInformation("KPI reading fetched {Count} daily values for community {CommunityId} ({From} – {To})",
                    kpiValues.Count, community.CommunityId, period.From, period.To);

                await publisher.PublishKpiReadingsAsync(new EdaKpiReadingsPublication(
                    community.CommunityId,
                    community.Name,
                    kpiValues,
                    clock.UtcNow), cancellationToken);

                metrics.RecordKpiReading(community.CommunityId, kpiValues.Count, clock.UtcNow - start, success: true);
                totalValues += kpiValues.Count;
            }
            catch
            {
                metrics.RecordKpiReading(community.CommunityId, 0, clock.UtcNow - start, success: false);
                throw;
            }
        }

        return new EdaReadingTriggerResponse(null, period.From, period.To, totalValues);
    }

    private async Task<List<EdaKpiValue>> FetchDailyKpiValuesAsync(
        string communityId,
        DateTimeOffset from,
        DateTimeOffset to,
        bool logProgress,
        CancellationToken cancellationToken)
    {
        var kpiValues = new List<EdaKpiValue>();
        var totalDays = (int)(to.Date - from.Date).TotalDays + 1;
        var dayIndex = 0;

        for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
        {
            cancellationToken.ThrowIfCancellationRequested();
            dayIndex++;
            var dayStart = new DateTimeOffset(day, from.Offset);
            var dayEnd = dayStart.AddDays(1).AddSeconds(-1);
            var kpiPeriod = new EdaPeriodDefinition("kpi-day", dayStart, dayEnd, "day");

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
                logger.LogError(ex, "Failed to fetch KPI for community {CommunityId} on {Day:yyyy-MM-dd}", communityId, day);
            }

            if (logProgress && (dayIndex % 30 == 0 || dayIndex == totalDays))
            {
                logger.LogInformation("KPI daily fetch progress {DayIndex}/{TotalDays} for community {CommunityId}",
                    dayIndex, totalDays, communityId);
            }
        }

        return kpiValues;
    }

    public async Task<EdaMeterBackfillResponse> TriggerHistoricalMeterBackfillAsync(
        CancellationToken cancellationToken,
        string? communityId = null)
    {
        const string kind = "meter";
        var now = clock.UtcNow;
        var from = now.AddYears(-BackfillYears);
        var meterPeriod = new EdaPeriodDefinition("backfill", from, now, null);
        var selectedCommunityId = string.IsNullOrWhiteSpace(communityId)
            ? null
            : _options.ResolveCommunity(communityId).CommunityId;

        var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
        if (selectedCommunityId is not null)
        {
            customers = customers
                .Where(customer => string.Equals(
                    customer.EnergyCommunityId ?? _options.DefaultCommunityId,
                    selectedCommunityId,
                    StringComparison.OrdinalIgnoreCase))
                .ToArray();
        }

        if (customers.Count == 0)
        {
            logger.LogWarning("Meter backfill: no customers/meters found for community {CommunityId}", selectedCommunityId ?? "all");
            return new EdaMeterBackfillResponse(from, now, 0, 0, 0, TimeSpan.Zero);
        }

        var totalReadings = 0;
        var completed = 0;
        var failed = 0;
        var meterCount = customers.Count;
        var backfillStart = clock.UtcNow;

        logger.LogInformation("Meter backfill: starting for {MeterCount} meters, period {From:u} – {To:u}",
            meterCount, from, now);
        metrics.RecordBackfillStarted(kind, meterCount);

        for (var index = 0; index < customers.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var customer = customers[index];
            var customerCommunityId = customer.EnergyCommunityId ?? _options.DefaultCommunityId;
            var meterId = customer.MeterPointNumber;
            var meterStart = clock.UtcNow;
            var meterFailed = false;
            var meterReadings = 0;

            logger.LogInformation("Meter backfill [{Index}/{Total}]: starting meter {MeterId} ({CustomerId})",
                index + 1, meterCount, meterId, customer.CustomerId);

            try
            {
                var result = await FetchAndPublishMeterReadingAsync(meterId, customerCommunityId, meterPeriod, cancellationToken);
                meterReadings = result.ReadingCount;
                totalReadings += meterReadings;
                completed++;
            }
            catch (Exception ex)
            {
                meterFailed = true;
                failed++;
                logger.LogError(ex, "Meter backfill [{Index}/{Total}]: failed for {MeterId}",
                    index + 1, meterCount, meterId);
            }

            var meterElapsed = clock.UtcNow - meterStart;
            logger.LogInformation(
                "Meter backfill [{Index}/{Total}]: {Status} meter {MeterId} in {Elapsed} — {Readings} readings. Progress: {Ok} ok / {Failed} failed / {Remaining} remaining",
                index + 1, meterCount,
                meterFailed ? "FAILED" : "completed",
                meterId, meterElapsed, meterReadings, completed, failed, meterCount - (index + 1));

            metrics.UpdateBackfillProgress(kind, completed, failed, meterCount - (index + 1));
        }

        var totalElapsed = clock.UtcNow - backfillStart;
        logger.LogInformation(
            "Meter backfill complete in {Elapsed}: {Ok}/{Total} meters ok, {Failed} failed, {Readings} meter readings",
            totalElapsed, completed, meterCount, failed, totalReadings);
        metrics.RecordBackfillCompleted(kind, meterCount, completed, failed, totalReadings, totalElapsed);

        return new EdaMeterBackfillResponse(from, now, completed, failed, totalReadings, totalElapsed);
    }

    public async Task<EdaKpiBackfillResponse> TriggerHistoricalKpiBackfillAsync(
        CancellationToken cancellationToken,
        string? communityId = null)
    {
        const string kind = "kpi";
        var now = clock.UtcNow;
        var from = now.AddYears(-BackfillYears);

        // KPI-Daten sind community-weit. Wir bestimmen die relevanten Communities aus den Kunden
        // und ergänzen die Default-Community aus den Options, falls keine Kunden gefunden werden.
        var communities = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(communityId))
        {
            communities.Add(_options.ResolveCommunity(communityId).CommunityId);
        }
        else
        {
            try
            {
                var customers = await centralApiClient.GetCustomersAsync(cancellationToken);
                foreach (var customer in customers)
                {
                    communities.Add(customer.EnergyCommunityId ?? _options.DefaultCommunityId);
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "KPI backfill: unable to load customers, falling back to configured communities");
            }

            if (communities.Count == 0)
            {
                communities.Add(_options.DefaultCommunityId);
            }

            communities.UnionWith(_options.GetCommunities().Select(community => community.CommunityId));
        }

        var communityList = communities.ToArray();
        var communityCount = communityList.Length;
        var totalValues = 0;
        var completed = 0;
        var failed = 0;
        var backfillStart = clock.UtcNow;
        var totalDays = (int)(now.Date - from.Date).TotalDays + 1;

        logger.LogInformation("KPI backfill: starting for {CommunityCount} communities × {Days} days, period {From:u} – {To:u}",
            communityCount, totalDays, from, now);
        metrics.RecordBackfillStarted(kind, communityCount);

        for (var index = 0; index < communityList.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var currentCommunityId = communityList[index];
            var communityStart = clock.UtcNow;
            var kpiSuccess = false;

            logger.LogInformation("KPI backfill [{Index}/{Total}]: starting community {CommunityId}",
                index + 1, communityCount, currentCommunityId);

            var kpiValues = await FetchDailyKpiValuesAsync(currentCommunityId, from, now, logProgress: true, cancellationToken);

            if (kpiValues.Count > 0)
            {
                try
                {
                    await publisher.PublishKpiReadingsAsync(new EdaKpiReadingsPublication(
                        currentCommunityId,
                        _options.ResolveCommunity(currentCommunityId).Name,
                        kpiValues,
                        clock.UtcNow), cancellationToken);
                    kpiSuccess = true;
                    totalValues += kpiValues.Count;
                    completed++;
                }
                catch (Exception ex)
                {
                    failed++;
                    logger.LogError(ex, "KPI backfill [{Index}/{Total}]: failed to publish KPI readings for community {CommunityId}",
                        index + 1, communityCount, currentCommunityId);
                }
            }
            else
            {
                failed++;
                logger.LogWarning("KPI backfill [{Index}/{Total}]: no KPI data received for community {CommunityId}",
                    index + 1, communityCount, currentCommunityId);
            }

            var communityElapsed = clock.UtcNow - communityStart;
            metrics.RecordKpiReading(currentCommunityId, kpiValues.Count, communityElapsed, success: kpiSuccess);
            metrics.UpdateBackfillProgress(kind, completed, failed, communityCount - (index + 1));

            logger.LogInformation(
                "KPI backfill [{Index}/{Total}]: {Status} community {CommunityId} in {Elapsed} — {Values} KPI values. Progress: {Ok} ok / {Failed} failed / {Remaining} remaining",
                index + 1, communityCount,
                kpiSuccess ? "completed" : "FAILED",
                currentCommunityId, communityElapsed, kpiValues.Count, completed, failed, communityCount - (index + 1));
        }

        var totalElapsed = clock.UtcNow - backfillStart;
        logger.LogInformation(
            "KPI backfill complete in {Elapsed}: {Ok}/{Total} communities ok, {Failed} failed, {Values} KPI day-values",
            totalElapsed, completed, communityCount, failed, totalValues);
        metrics.RecordBackfillCompleted(kind, communityCount, completed, failed, totalValues, totalElapsed);

        return new EdaKpiBackfillResponse(from, now, completed, failed, totalValues, totalElapsed);
    }

    private async Task<EdaReadingTriggerResponse> FetchAndPublishMeterReadingAsync(
        string meterPointNumber, string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
    {
        var start = clock.UtcNow;
        try
        {
            var readings = await portalClient.FetchConsumptionSuryaPointsAsync(communityId, meterPointNumber, period, cancellationToken);

            logger.LogInformation("Meter reading fetched {Count} points for {MeterId} ({From} – {To})",
                readings.Points.Count, meterPointNumber, period.From, period.To);

            if (readings.Points.Count == 0)
            {
                logger.LogWarning("Meter reading returned no data for {MeterId} – skipping RabbitMQ publish", meterPointNumber);
                metrics.RecordMeterReading(meterPointNumber, communityId, 0, clock.UtcNow - start, success: true);
                return new EdaReadingTriggerResponse(meterPointNumber, period.From, period.To, 0);
            }

            await publisher.PublishMeterReadingsAsync(new EdaMeterReadingsPublication(
                meterPointNumber,
                communityId,
                _options.ResolveCommunity(communityId).Name,
                readings.Points.Select(point => new EdaMeterReading(
                    point.Timestamp ?? clock.UtcNow,
                    point.TotalConsumptionValue,
                    point.GridShareValue,
                    point.CommunityShareValue)).ToArray(),
                clock.UtcNow), cancellationToken);

            metrics.RecordMeterReading(meterPointNumber, communityId, readings.Points.Count, clock.UtcNow - start, success: true);
            return new EdaReadingTriggerResponse(meterPointNumber, period.From, period.To, readings.Points.Count);
        }
        catch
        {
            metrics.RecordMeterReading(meterPointNumber, communityId, 0, clock.UtcNow - start, success: false);
            throw;
        }
    }

    private static EdaPeriodDefinition BuildPeriod(EdaTriggerRequest request)
    {
        var from = request.FromUtc ?? DateTimeOffset.UtcNow.AddDays(-21);
        var to = request.ToUtc ?? DateTimeOffset.UtcNow;
        return new EdaPeriodDefinition("trigger", from, to, null);
    }

    private static EdaPeriodDefinition BuildKpiPeriod(EdaTriggerRequest request)
    {
        var from = request.FromUtc ?? DateTimeOffset.UtcNow.AddDays(-31);
        var to = request.ToUtc ?? DateTimeOffset.UtcNow;
        return new EdaPeriodDefinition("kpi-trigger", from, to, "day");
    }
}
