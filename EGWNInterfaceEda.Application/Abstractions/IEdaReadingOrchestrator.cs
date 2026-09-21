using EGWNInterfaceEda.Domain;

namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaReadingOrchestrator
{
    Task<EdaReadingTriggerResponse> TriggerMeterReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken);

    Task<EdaReadingTriggerResponse> TriggerKpiReadingAsync(EdaTriggerRequest request, CancellationToken cancellationToken);

    Task<EdaMeterBackfillResponse> TriggerHistoricalMeterBackfillAsync(CancellationToken cancellationToken);

    Task<EdaKpiBackfillResponse> TriggerHistoricalKpiBackfillAsync(CancellationToken cancellationToken);
}
