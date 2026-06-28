using EGWNInterfaceEda.Domain;

namespace EGWNInterfaceEda.Application.Abstractions;

public interface IEdaPortalClient
{
    Task<EdaKpiData?> FetchKpiAsync(string communityId, string meterId, EdaPeriodDefinition period, CancellationToken cancellationToken);

    Task<EdaMeterData?> FetchMeterDataAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken);

    Task<EdaConsumptionSuryaData?> FetchConsumptionSuryaAsync(
        string communityId,
        string meterId,
        EdaPeriodDefinition period,
        EdaConsumptionSuryaRoute route,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<EdaConsumptionSuryaPoint>> FetchConsumptionSuryaPointsAsync(
        string communityId,
        string meterId,
        EdaPeriodDefinition period,
        CancellationToken cancellationToken);
}
