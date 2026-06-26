namespace EGWNInterfaceEda.Domain;

public sealed record EdaKpiData(
    decimal? Autarky,
    decimal? OwnConsumption,
    decimal? Community,
    decimal? Feed,
    decimal? RemainingDemand);
