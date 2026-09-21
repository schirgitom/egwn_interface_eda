using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Domain;
using Quartz;

namespace EGWNInterfaceEda.Jobs;

[DisallowConcurrentExecution]
public sealed class EdaTriggerMeterReadingJob(IEdaReadingOrchestrator orchestrator, ILogger<EdaTriggerMeterReadingJob> logger) : IJob
{
    public const string JobName = "eda-trigger-meter-reading";
    public const string GroupName = "integration";

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Meter reading trigger started");
        await orchestrator.TriggerMeterReadingAsync(new EdaTriggerRequest(null, null, null), context.CancellationToken);
        var nextFire = context.NextFireTimeUtc?.ToLocalTime();
        logger.LogInformation("Meter reading trigger completed. Next run: {NextFire}", nextFire?.ToString("yyyy-MM-dd HH:mm:ss zzz") ?? "unknown");
    }
}
