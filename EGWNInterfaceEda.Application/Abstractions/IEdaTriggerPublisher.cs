using EGWNInterfaceEda.Domain;

namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaTriggerPublisher
{
    Task PublishMeterReadingsAsync(EdaMeterReadingsPublication publication, CancellationToken cancellationToken);

    Task PublishKpiReadingsAsync(EdaKpiReadingsPublication publication, CancellationToken cancellationToken);
}
