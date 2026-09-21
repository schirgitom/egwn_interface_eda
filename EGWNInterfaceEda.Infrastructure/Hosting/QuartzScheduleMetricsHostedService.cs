using EGWNInterfaceEda.Application.Abstractions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz;

namespace EGWNInterfaceEda.Infrastructure.Hosting;

public sealed class QuartzScheduleMetricsHostedService(
    ISchedulerFactory schedulerFactory,
    IEdaMetrics metrics,
    ILogger<QuartzScheduleMetricsHostedService> logger) : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(10);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var scheduler = await schedulerFactory.GetScheduler(stoppingToken);
                var jobKeys = await scheduler.GetJobKeys(Quartz.Impl.Matchers.GroupMatcher<JobKey>.AnyGroup(), stoppingToken);

                foreach (var jobKey in jobKeys)
                {
                    var triggers = await scheduler.GetTriggersOfJob(jobKey, stoppingToken);
                    DateTimeOffset? nextFire = null;
                    DateTimeOffset? previousFire = null;

                    foreach (var trigger in triggers)
                    {
                        var next = trigger.GetNextFireTimeUtc();
                        if (next.HasValue && (!nextFire.HasValue || next.Value < nextFire.Value))
                        {
                            nextFire = next;
                        }

                        var previous = trigger.GetPreviousFireTimeUtc();
                        if (previous.HasValue && (!previousFire.HasValue || previous.Value > previousFire.Value))
                        {
                            previousFire = previous;
                        }
                    }

                    metrics.SetJobSchedule(jobKey.Name, nextFire, previousFire);
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Failed to update Quartz schedule metrics");
            }

            try
            {
                await Task.Delay(PollInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
