using EGWNInterfaceEda.Domain;

namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaResultPublisher
{
    Task PublishAsync(EdaSyncPublication publication, CancellationToken cancellationToken);
}
