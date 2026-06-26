using EGWNInterfaceEda.Application.Abstractions;
using Quartz;

namespace EGWNInterfaceEda.Jobs;

[DisallowConcurrentExecution]
public sealed class EdaSyncJob(IEdaSyncOrchestrator orchestrator, ILogger<EdaSyncJob> logger) : IJob
{
    public const string JobName = "eda-sync";
    public const string GroupName = "integration";

    public async Task Execute(IJobExecutionContext context)
    {
        logger.LogInformation("Quartz job {JobKey} started", context.JobDetail.Key);
        await orchestrator.RunAsync(context.CancellationToken);
        logger.LogInformation("Quartz job {JobKey} completed", context.JobDetail.Key);
    }
}
