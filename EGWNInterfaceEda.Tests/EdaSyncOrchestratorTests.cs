using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Application.Services;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Tests;

public sealed class EdaSyncOrchestratorTests
{
    [Fact]
    public void CreateAll_uses_one_hourly_window_for_the_last_three_weeks_by_default()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

        var periods = EdaPeriodDefinition.CreateAll(fixedNow, null, null);

        var period = Assert.Single(periods);
        Assert.Equal("letzte-21-tage-stündlich", period.Key);
        Assert.Equal("hour", period.GroupBy);
        Assert.Equal(new DateTimeOffset(2026, 6, 7, 0, 0, 0, TimeSpan.Zero), period.From);
        Assert.Equal(new DateTimeOffset(2026, 6, 28, 23, 45, 0, TimeSpan.Zero), period.To);
    }

    [Fact]
    public async Task RunAsync_fetches_meter_once_per_period_and_kpi_per_meter_id()
    {
        var fixedNow = new DateTimeOffset(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);
        var expectedPeriods = EdaPeriodDefinition.CreateAll(fixedNow, null, null).Count;

        var centralApiClient = new FakeCentralApiClient(
        [
            new CustomerMeterPoint("c-1", "Customer 1", "meter-point-1", "community-1", "meter-id-1"),
            new CustomerMeterPoint("c-2", "Customer 2", "meter-point-2", "community-1", null)
        ]);

        var portalClient = new FakeEdaPortalClient();
        var publisher = new FakePublisher();
        var options = Options.Create(new EdaOptions
        {
            CommunityId = "default-community"
        });
        var clock = new FakeClock(fixedNow);

        var sut = new EdaSyncOrchestrator(
            centralApiClient,
            portalClient,
            publisher,
            options,
            clock,
            NullLogger<EdaSyncOrchestrator>.Instance);

        await sut.RunAsync(CancellationToken.None);

        Assert.Equal(expectedPeriods, portalClient.MeterCalls.Count);
        Assert.Contains(portalClient.MeterCalls, call => call.Period.GroupBy == "hour");
        Assert.All(portalClient.MeterCalls, call => Assert.Equal("community-1", call.CommunityId));

        Assert.Equal(expectedPeriods * 2, portalClient.KpiCalls.Count);
        Assert.Contains(portalClient.KpiCalls, call => call.Period.GroupBy == "hour");
        Assert.Equal(expectedPeriods, portalClient.KpiCalls.Count(call => call.MeterId == "meter-id-1"));
        Assert.Equal(expectedPeriods, portalClient.KpiCalls.Count(call => call.MeterId == "meter-point-2"));

        Assert.Equal(expectedPeriods * 2, publisher.Publications.Count);
        Assert.All(publisher.Publications, publication =>
        {
            Assert.NotNull(publication.Snapshot.Meter);
            Assert.NotNull(publication.Snapshot.Kpi);
            Assert.False(string.IsNullOrWhiteSpace(publication.MeterPointNumber));
            Assert.Equal(publication.Customer.MeterPointNumber, publication.MeterPointNumber);
        });
    }

    private sealed class FakeCentralApiClient(IReadOnlyList<CustomerMeterPoint> customers) : ICentralApiClient
    {
        public Task<IReadOnlyList<CustomerMeterPoint>> GetCustomersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(customers);
    }

    private sealed class FakeEdaPortalClient : IEdaPortalClient
    {
        public List<(string CommunityId, EdaPeriodDefinition Period)> MeterCalls { get; } = [];
        public List<(string CommunityId, string MeterId, EdaPeriodDefinition Period)> KpiCalls { get; } = [];

        public Task<EdaKpiData?> FetchKpiAsync(DateTimeOffset timestamp, string meterId, CancellationToken cancellationToken)
        {
            KpiCalls.Add(("community-1", meterId, new EdaPeriodDefinition("kpi", timestamp, timestamp, "hour")));
            return Task.FromResult<EdaKpiData?>(new EdaKpiData(1m, 2m, 3m, 4m, 5m));
        }

        public Task<EdaMeterData?> FetchMeterDataAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
        {
            MeterCalls.Add((communityId, period));
            return Task.FromResult<EdaMeterData?>(new EdaMeterData(false, 10m, 20m, [], []));
        }

        public Task<EdaConsumptionSuryaData?> FetchConsumptionSuryaAsync(
            string communityId,
            string meterId,
            EdaPeriodDefinition period,
            EdaConsumptionSuryaRoute route,
            CancellationToken cancellationToken)
            => Task.FromResult<EdaConsumptionSuryaData?>(null);

        public Task<EdaConsumptionSuryaCombinedData> FetchConsumptionSuryaPointsAsync(
            string communityId,
            string meterId,
            EdaPeriodDefinition period,
            CancellationToken cancellationToken)
            => Task.FromResult(new EdaConsumptionSuryaCombinedData([], null, null, null, null, null));
    }

    private sealed class FakePublisher : IEdaResultPublisher
    {
        public List<EdaSyncPublication> Publications { get; } = [];

        public Task PublishAsync(EdaSyncPublication publication, CancellationToken cancellationToken)
        {
            Publications.Add(publication);
            return Task.CompletedTask;
        }

        public Task PublishAsync(EdaKpiSyncPublication publication, CancellationToken cancellationToken) =>
            Task.CompletedTask;
    }

    private sealed class FakeClock(DateTimeOffset now) : IClock
    {
        public DateTimeOffset UtcNow => now;
    }
}
