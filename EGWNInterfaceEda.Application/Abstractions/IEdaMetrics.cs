namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaMetrics
{
    void RecordMeterReading(string meterId, string communityId, int pointCount, TimeSpan duration, bool success);

    void RecordKpiReading(string communityId, int valueCount, TimeSpan duration, bool success);

    void RecordBackfillStarted(string kind, int itemCount);

    void UpdateBackfillProgress(string kind, int completed, int failed, int remaining);

    void RecordBackfillCompleted(string kind, int itemCount, int completed, int failed, int readings, TimeSpan duration);

    void SetJobSchedule(string jobName, DateTimeOffset? nextFireUtc, DateTimeOffset? previousFireUtc);
}
