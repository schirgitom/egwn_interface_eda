using EGWNInterfaceEda.Domain;

namespace EGWNInterfaceEda.Application.Abstractions;

public interface ICentralApiClient
{
    Task<IReadOnlyList<CustomerMeterPoint>> GetCustomersAsync(CancellationToken cancellationToken);
}
