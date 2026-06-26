namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaSyncOrchestrator
{
    Task RunAsync(CancellationToken cancellationToken);
}
