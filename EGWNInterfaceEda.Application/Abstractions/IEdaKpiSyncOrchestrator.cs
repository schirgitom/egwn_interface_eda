namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaKpiSyncOrchestrator
{
    Task RunAsync(CancellationToken cancellationToken);
}
