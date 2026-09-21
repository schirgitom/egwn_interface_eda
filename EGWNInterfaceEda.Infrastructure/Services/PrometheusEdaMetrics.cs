using EGWNInterfaceEda.Application.Abstractions;
using Prometheus;

namespace EGWNInterfaceEda.Infrastructure.Services;

public sealed class PrometheusEdaMetrics : IEdaMetrics
{
    private static readonly string[] MeterLabels = ["meter_id", "community_id"];
    private static readonly string[] MeterStatusLabels = ["meter_id", "community_id", "status"];
    private static readonly string[] CommunityLabels = ["community_id"];
    private static readonly string[] CommunityStatusLabels = ["community_id", "status"];
    private static readonly string[] JobLabels = ["job"];

    private static readonly Gauge MeterLastSuccessTs = Metrics.CreateGauge(
        "eda_meter_reading_last_success_timestamp_seconds",
        "Unix timestamp of the last successful meter reading, per meter.",
        MeterLabels);

    private static readonly Gauge MeterLastAttemptTs = Metrics.CreateGauge(
        "eda_meter_reading_last_attempt_timestamp_seconds",
        "Unix timestamp of the last meter reading attempt (success or failure), per meter.",
        MeterLabels);

    private static readonly Gauge MeterLastPoints = Metrics.CreateGauge(
        "eda_meter_reading_last_points",
        "Number of points returned by the last successful meter reading.",
        MeterLabels);

    private static readonly Counter MeterReadingsTotal = Metrics.CreateCounter(
        "eda_meter_reading_total",
        "Total number of meter reading attempts, labeled by status (success/failure).",
        MeterStatusLabels);

    private static readonly Histogram MeterReadingDuration = Metrics.CreateHistogram(
        "eda_meter_reading_duration_seconds",
        "Duration of meter reading fetch+publish operations, in seconds.",
        new HistogramConfiguration
        {
            LabelNames = MeterLabels,
            Buckets = Histogram.ExponentialBuckets(0.5, 2, 10)
        });

    private static readonly Gauge KpiLastSuccessTs = Metrics.CreateGauge(
        "eda_kpi_reading_last_success_timestamp_seconds",
        "Unix timestamp of the last successful KPI reading, per community.",
        CommunityLabels);

    private static readonly Gauge KpiLastAttemptTs = Metrics.CreateGauge(
        "eda_kpi_reading_last_attempt_timestamp_seconds",
        "Unix timestamp of the last KPI reading attempt (success or failure), per community.",
        CommunityLabels);

    private static readonly Gauge KpiLastValues = Metrics.CreateGauge(
        "eda_kpi_reading_last_values",
        "Number of KPI values published in the last successful KPI reading.",
        CommunityLabels);

    private static readonly Counter KpiReadingsTotal = Metrics.CreateCounter(
        "eda_kpi_reading_total",
        "Total number of KPI reading attempts, labeled by status (success/failure).",
        CommunityStatusLabels);

    private static readonly Histogram KpiReadingDuration = Metrics.CreateHistogram(
        "eda_kpi_reading_duration_seconds",
        "Duration of KPI reading fetch+publish operations, in seconds.",
        new HistogramConfiguration
        {
            LabelNames = CommunityLabels,
            Buckets = Histogram.ExponentialBuckets(0.5, 2, 10)
        });

    private static readonly string[] BackfillLabels = ["kind"];

    private static readonly Gauge BackfillInProgress = Metrics.CreateGauge(
        "eda_backfill_in_progress",
        "1 if a backfill of the given kind (meter/kpi) is currently running, 0 otherwise.",
        BackfillLabels);

    private static readonly Gauge BackfillItemTotal = Metrics.CreateGauge(
        "eda_backfill_item_total",
        "Total number of items (meters or communities) in the current or last backfill run.",
        BackfillLabels);

    private static readonly Gauge BackfillItemsCompleted = Metrics.CreateGauge(
        "eda_backfill_items_completed",
        "Number of items successfully completed in the current or last backfill run.",
        BackfillLabels);

    private static readonly Gauge BackfillItemsFailed = Metrics.CreateGauge(
        "eda_backfill_items_failed",
        "Number of items that failed in the current or last backfill run.",
        BackfillLabels);

    private static readonly Gauge BackfillItemsRemaining = Metrics.CreateGauge(
        "eda_backfill_items_remaining",
        "Number of items still to process in the current backfill run.",
        BackfillLabels);

    private static readonly Gauge BackfillLastCompletedTs = Metrics.CreateGauge(
        "eda_backfill_last_completed_timestamp_seconds",
        "Unix timestamp of the last completed backfill run.",
        BackfillLabels);

    private static readonly Gauge BackfillLastDuration = Metrics.CreateGauge(
        "eda_backfill_last_duration_seconds",
        "Duration of the last completed backfill run, in seconds.",
        BackfillLabels);

    private static readonly Gauge BackfillLastReadings = Metrics.CreateGauge(
        "eda_backfill_last_readings",
        "Number of readings/values published in the last completed backfill run.",
        BackfillLabels);

    private static readonly Gauge JobNextFireTs = Metrics.CreateGauge(
        "eda_job_next_fire_timestamp_seconds",
        "Unix timestamp of the next scheduled fire time for each Quartz job.",
        JobLabels);

    private static readonly Gauge JobPreviousFireTs = Metrics.CreateGauge(
        "eda_job_previous_fire_timestamp_seconds",
        "Unix timestamp of the previous fire time for each Quartz job.",
        JobLabels);

    public void RecordMeterReading(string meterId, string communityId, int pointCount, TimeSpan duration, bool success)
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        MeterLastAttemptTs.WithLabels(meterId, communityId).Set(nowSeconds);
        MeterReadingsTotal.WithLabels(meterId, communityId, success ? "success" : "failure").Inc();
        MeterReadingDuration.WithLabels(meterId, communityId).Observe(duration.TotalSeconds);

        if (success)
        {
            MeterLastSuccessTs.WithLabels(meterId, communityId).Set(nowSeconds);
            MeterLastPoints.WithLabels(meterId, communityId).Set(pointCount);
        }
    }

    public void RecordKpiReading(string communityId, int valueCount, TimeSpan duration, bool success)
    {
        var nowSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        KpiLastAttemptTs.WithLabels(communityId).Set(nowSeconds);
        KpiReadingsTotal.WithLabels(communityId, success ? "success" : "failure").Inc();
        KpiReadingDuration.WithLabels(communityId).Observe(duration.TotalSeconds);

        if (success)
        {
            KpiLastSuccessTs.WithLabels(communityId).Set(nowSeconds);
            KpiLastValues.WithLabels(communityId).Set(valueCount);
        }
    }

    public void RecordBackfillStarted(string kind, int itemCount)
    {
        BackfillInProgress.WithLabels(kind).Set(1);
        BackfillItemTotal.WithLabels(kind).Set(itemCount);
        BackfillItemsCompleted.WithLabels(kind).Set(0);
        BackfillItemsFailed.WithLabels(kind).Set(0);
        BackfillItemsRemaining.WithLabels(kind).Set(itemCount);
    }

    public void UpdateBackfillProgress(string kind, int completed, int failed, int remaining)
    {
        BackfillItemsCompleted.WithLabels(kind).Set(completed);
        BackfillItemsFailed.WithLabels(kind).Set(failed);
        BackfillItemsRemaining.WithLabels(kind).Set(remaining);
    }

    public void RecordBackfillCompleted(string kind, int itemCount, int completed, int failed, int readings, TimeSpan duration)
    {
        BackfillInProgress.WithLabels(kind).Set(0);
        BackfillItemTotal.WithLabels(kind).Set(itemCount);
        BackfillItemsCompleted.WithLabels(kind).Set(completed);
        BackfillItemsFailed.WithLabels(kind).Set(failed);
        BackfillItemsRemaining.WithLabels(kind).Set(0);
        BackfillLastCompletedTs.WithLabels(kind).Set(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        BackfillLastDuration.WithLabels(kind).Set(duration.TotalSeconds);
        BackfillLastReadings.WithLabels(kind).Set(readings);
    }

    public void SetJobSchedule(string jobName, DateTimeOffset? nextFireUtc, DateTimeOffset? previousFireUtc)
    {
        if (nextFireUtc.HasValue)
        {
            JobNextFireTs.WithLabels(jobName).Set(nextFireUtc.Value.ToUnixTimeSeconds());
        }
        else
        {
            JobNextFireTs.WithLabels(jobName).Set(0);
        }

        if (previousFireUtc.HasValue)
        {
            JobPreviousFireTs.WithLabels(jobName).Set(previousFireUtc.Value.ToUnixTimeSeconds());
        }
    }
}
