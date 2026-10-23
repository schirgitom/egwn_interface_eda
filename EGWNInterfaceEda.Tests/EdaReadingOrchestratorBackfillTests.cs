using EGWNInterfaceEda.Application.Abstractions;
using EGWNInterfaceEda.Application.Options;
using EGWNInterfaceEda.Application.Services;
using EGWNInterfaceEda.Domain;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace EGWNInterfaceEda.Tests;

public sealed class EdaReadingOrchestratorBackfillTests
{
    private static readonly DateTimeOffset FixedNow = new(2026, 6, 28, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Meter_backfill_filters_customers_by_community_id()
    {
        var customers = new FakeCentralApiClient(
        [
            new CustomerMeterPoint("c-1", "Customer 1", "meter-1", "community-1"),
            new CustomerMeterPoint("c-2", "Customer 2", "meter-2", "community-2")
        ]);
        var portal = new FakeEdaPortalClient();
        var publisher = new FakePublisher();
        var sut = CreateOrchestrator(customers, portal, publisher);

        var response = await sut.TriggerHistoricalMeterBackfillAsync(CancellationToken.None, "community-2");

        Assert.Equal(1, response.MetersProcessed);
        Assert.Equal(0, response.MetersFailed);
        Assert.Equal([("community-2", "meter-2")], portal.MeterCalls);
        Assert.Equal("community-2", Assert.Single(publisher.MeterPublications).CommunityId);
        Assert.Equal("Community Two", publisher.MeterPublications[0].CommunityName);
    }

    [Fact]
    public async Task Kpi_backfill_fetches_and_publishes_only_the_selected_community()
    {
        var portal = new FakeEdaPortalClient();
        var publisher = new FakePublisher();
        var sut = CreateOrchestrator(new FakeCentralApiClient([]), portal, publisher);

        var response = await sut.TriggerHistoricalKpiBackfillAsync(CancellationToken.None, "community-2");

        Assert.Equal(1, response.CommunitiesProcessed);
        Assert.Equal(0, response.CommunitiesFailed);
        Assert.NotEmpty(portal.KpiCalls);
        Assert.All(portal.KpiCalls, call => Assert.Equal("community-2", call));
        Assert.Equal("community-2", Assert.Single(publisher.KpiPublications).CommunityId);
        Assert.Equal("Community Two", publisher.KpiPublications[0].CommunityName);
    }

    private static EdaReadingOrchestrator CreateOrchestrator(
        FakeCentralApiClient centralApi,
        FakeEdaPortalClient portal,
        FakePublisher publisher)
    {
        var options = Options.Create(new EdaOptions
        {
            Communities =
            [
                new EdaCommunityOptions { Name = "Community One", CommunityId = "community-1", Username = "one", Password = "secret-one" },
                new EdaCommunityOptions { Name = "Community Two", CommunityId = "community-2", Username = "two", Password = "secret-two" }
            ]
        });

        return new EdaReadingOrchestrator(
            portal,
            publisher,
            centralApi,
            options,
            new FakeClock(),
            new FakeMetrics(),
            NullLogger<EdaReadingOrchestrator>.Instance);
    }

    private sealed class FakeCentralApiClient(IReadOnlyList<CustomerMeterPoint> customers) : ICentralApiClient
    {
        public Task<IReadOnlyList<CustomerMeterPoint>> GetCustomersAsync(CancellationToken cancellationToken) =>
            Task.FromResult(customers);
    }

    private sealed class FakeEdaPortalClient : IEdaPortalClient
    {
        public List<(string CommunityId, string MeterId)> MeterCalls { get; } = [];

        public List<string> KpiCalls { get; } = [];

        public Task<EdaKpiData?> FetchKpiAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken)
        {
            KpiCalls.Add(communityId);
            return Task.FromResult<EdaKpiData?>(new EdaKpiData(1m, 2m, 3m, 4m, 5m));
        }

        public Task<EdaMeterData?> FetchMeterDataAsync(string communityId, EdaPeriodDefinition period, CancellationToken cancellationToken) =>
            Task.FromResult<EdaMeterData?>(null);

        public Task<EdaConsumptionSuryaData?> FetchConsumptionSuryaAsync(
            string communityId,
            string meterId,
            EdaPeriodDefinition period,
            EdaConsumptionSuryaRoute route,
            CancellationToken cancellationToken) =>
            Task.FromResult<EdaConsumptionSuryaData?>(null);

        public Task<EdaConsumptionSuryaCombinedData> FetchConsumptionSuryaPointsAsync(
            string communityId,
            string meterId,
            EdaPeriodDefinition period,
            CancellationToken cancellationToken)
        {
            MeterCalls.Add((communityId, meterId));
            IReadOnlyList<EdaConsumptionSuryaPoint> points =
            [
                new EdaConsumptionSuryaPoint(FixedNow, 10m, 2m, 8m)
            ];
            return Task.FromResult(new EdaConsumptionSuryaCombinedData(points, 10m, 2m, 8m, 20m, 80m));
        }
    }

    private sealed class FakePublisher : IEdaTriggerPublisher
    {
        public List<EdaMeterReadingsPublication> MeterPublications { get; } = [];

        public List<EdaKpiReadingsPublication> KpiPublications { get; } = [];

        public Task PublishMeterReadingsAsync(EdaMeterReadingsPublication publication, CancellationToken cancellationToken)
        {
            MeterPublications.Add(publication);
            return Task.CompletedTask;
        }

        public Task PublishKpiReadingsAsync(EdaKpiReadingsPublication publication, CancellationToken cancellationToken)
        {
            KpiPublications.Add(publication);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow => FixedNow;
    }

    private sealed class FakeMetrics : IEdaMetrics
    {
        public void RecordMeterReading(string meterId, string communityId, int pointCount, TimeSpan duration, bool success) { }

        public void RecordKpiReading(string communityId, int valueCount, TimeSpan duration, bool success) { }

        public void RecordBackfillStarted(string kind, int itemCount) { }

        public void UpdateBackfillProgress(string kind, int completed, int failed, int remaining) { }

        public void RecordBackfillCompleted(string kind, int itemCount, int completed, int failed, int readings, TimeSpan duration) { }

        public void SetJobSchedule(string jobName, DateTimeOffset? nextFireUtc, DateTimeOffset? previousFireUtc) { }
    }
}
