using System.Text.Json;

using Microsoft.Extensions.Logging.Abstractions;

using QuickPatch.Ranking.Application.Events;
using QuickPatch.Ranking.Application.Reputation;
using QuickPatch.Ranking.UnitTests.Support;

namespace QuickPatch.Ranking.UnitTests.Application;

/// <summary>Reputación desde <c>service-request.rated</c> (ADR-027, decisiones 3 y 4; RN-EV1).</summary>
public class ServiceRequestRatedHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 15, 0, 0, TimeSpan.Zero);
    private readonly InMemoryRanking store = new();
    private readonly Guid tenant = Guid.NewGuid();
    private readonly Guid technician = Guid.NewGuid();

    private ServiceRequestRatedHandler Handler() =>
        new(store, store, store, store, new FixedClock(Now), NullLogger<ServiceRequestRatedHandler>.Instance);

    private EventEnvelope<ServiceRequestRatedData> Rated(int score, Guid? request = null, Guid? eventId = null, Guid? tenantId = null) =>
        new(eventId ?? Guid.NewGuid(), EventTypes.ServiceRequestRated, 1, Now, Guid.NewGuid(), tenantId ?? tenant, "service-request-service",
            new ServiceRequestRatedData(request ?? Guid.NewGuid(), technician, score, Now));

    [Fact]
    public async Task PrimeraCalificacion_CreaLaReputacion_YPublicaElCambio()
    {
        var outcome = await Handler().HandleAsync(Rated(4), CancellationToken.None);

        Assert.Equal(RatedHandlingOutcome.Applied, outcome);
        var reputation = Assert.Single(store.Reputations);
        Assert.Equal((1, 4.0m), (reputation.RatingCount, reputation.AverageRating));
        Assert.Single(store.Applied);
        Assert.Single(store.ProcessedEvents);
        var message = Assert.Single(store.Outbox);
        Assert.Equal(EventTypes.TechnicianReputationChanged, message.EventType);
        Assert.Equal(technician, message.AggregateId);
        Assert.Equal(tenant, message.TenantId);
    }

    [Fact]
    public async Task EventoDeCambio_LlevaLosCamposDelAdr027()
    {
        await Handler().HandleAsync(Rated(5), CancellationToken.None);
        await Handler().HandleAsync(Rated(4), CancellationToken.None);

        var root = JsonDocument.Parse(store.Outbox[^1].Payload).RootElement;
        Assert.Equal("ranking.technician-reputation-changed", root.GetProperty("eventType").GetString());
        Assert.Equal("ranking-service", root.GetProperty("producer").GetString());
        Assert.Equal(tenant, root.GetProperty("tenantId").GetGuid());
        var data = root.GetProperty("data");
        Assert.Equal(
            ["averageRating", "changedAt", "lowReputation", "ratingCount", "technicianId"],
            data.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal));
        Assert.Equal(4.5m, data.GetProperty("averageRating").GetDecimal());
        Assert.Equal(2, data.GetProperty("ratingCount").GetInt32());
        Assert.False(data.GetProperty("lowReputation").GetBoolean());
    }

    [Fact]
    public async Task MismoEventId_NoVuelveASumar()
    {
        var envelope = Rated(5);

        await Handler().HandleAsync(envelope, CancellationToken.None);
        var outcome = await Handler().HandleAsync(envelope, CancellationToken.None);

        Assert.Equal(RatedHandlingOutcome.Duplicate, outcome);
        Assert.Equal(1, Assert.Single(store.Reputations).RatingCount);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task MismaSolicitudConOtroEventId_NoVuelveASumar()
    {
        var request = Guid.NewGuid();

        await Handler().HandleAsync(Rated(5, request), CancellationToken.None);
        var outcome = await Handler().HandleAsync(Rated(1, request), CancellationToken.None);

        Assert.Equal(RatedHandlingOutcome.AlreadyApplied, outcome);
        Assert.Equal(5.0m, Assert.Single(store.Reputations).AverageRating);
        Assert.Equal(2, store.ProcessedEvents.Count);
        Assert.Single(store.Outbox);
    }

    [Fact]
    public async Task CincoCalificacionesBajas_PublicanLaMarca()
    {
        foreach (var score in new[] { 2, 2, 3, 3, 4 })
        {
            await Handler().HandleAsync(Rated(score), CancellationToken.None);
        }

        Assert.True(Assert.Single(store.Reputations).LowReputation);
        var last = JsonDocument.Parse(store.Outbox[^1].Payload).RootElement.GetProperty("data");
        Assert.True(last.GetProperty("lowReputation").GetBoolean());
    }

    [Fact]
    public async Task OtroTenant_TieneSuPropiaReputacion()
    {
        await Handler().HandleAsync(Rated(5), CancellationToken.None);
        await Handler().HandleAsync(Rated(1, tenantId: Guid.NewGuid()), CancellationToken.None);

        Assert.Equal(2, store.Reputations.Count);
        Assert.Equal(5.0m, store.Reputations.Single(x => x.TenantId == tenant).AverageRating);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(6)]
    public async Task PuntajeFueraDeRango_SeDescarta_SinTransaccion(int score)
    {
        var outcome = await Handler().HandleAsync(Rated(score), CancellationToken.None);

        Assert.Equal(RatedHandlingOutcome.Invalid, outcome);
        Assert.Equal(0, store.Transactions);
        Assert.Empty(store.Outbox);
    }
}