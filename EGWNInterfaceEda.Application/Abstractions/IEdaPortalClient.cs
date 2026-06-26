using EGWNInterfaceEda.Domain;

namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaPortalClient
{
    Task<EdaPeriodSnapshot> FetchPeriodAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken);
}
