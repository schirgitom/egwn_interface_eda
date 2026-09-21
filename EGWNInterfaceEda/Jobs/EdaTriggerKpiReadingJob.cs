using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Domain;
using Quartz;

namespace EGWNInterfaceEda.Jobs;

[DisallowConcurrentExecution]
public sealed class EdaTriggerKpiReadingJob(IEdaReadingOrchestrator orchestrator, ILogger<EdaTriggerKpiReadingJob> logger) : IJob
{
    public const string JobName = "eda-trigger-kpi-reading";
    public const string GroupName = "integration";

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("KPI reading trigger started");
        await orchestrator.TriggerKpiReadingAsync(new EdaTriggerRequest(null, null, null), context.CancellationToken);
        var nextFire = context.NextFireTimeUtc?.ToLocalTime();
        logger.LogInformation("KPI reading trigger completed. Next run: {NextFire}", nextFire?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "unknown");
    }
}
