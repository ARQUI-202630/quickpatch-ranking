using System.Net;

using Confluent.Kafka;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using QuickPatch.Ranking.Application;
using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Infrastructure;
using QuickPatch.Ranking.Infrastructure.Messaging;
using QuickPatch.Ranking.UnitTests.Support;

namespace QuickPatch.Ranking.UnitTests.Infrastructure;

public class ConsumerAndSetupTests
{
    private static ServiceProvider Services(InMemoryRanking store)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddSingleton<ITenantUnitOfWork>(store);
        services.AddSingleton<IReputationRepository>(store);
        services.AddSingleton<IProcessedEventRepository>(store);
        services.AddSingleton<IOutbox>(store);
        return services.BuildServiceProvider();
    }

    private static string Message(Guid tenant, Guid request, string score = "5", string type = "service-request.rated", Guid? technician = null) =>
        $$$"""
        {"eventId":"{{{Guid.NewGuid()}}}","eventType":"{{{type}}}","eventVersion":1,"occurredAt":"2026-10-27T15:00:00Z",
         "correlationId":"{{{Guid.NewGuid()}}}","tenantId":"{{{tenant}}}","producer":"service-request-service",
         "data":{"serviceRequestId":"{{{request}}}","technicianId":"{{{technician ?? Guid.NewGuid()}}}","score":{{{score}}},
                 "ratedAt":"2026-10-27T15:00:00Z"}}
        """;

    [Fact]
    public async Task Consumidor_AplicaElEvento_YDescartaLoQueNoCumpleElContrato()
    {
        var store = new InMemoryRanking();
        await using var services = Services(store);
        using var kafka = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = "localhost:1", GroupId = "pruebas" }).Build();
        var consumer = new ServiceRequestRatedConsumer(
            services.GetRequiredService<IServiceScopeFactory>(), kafka, NullLogger<ServiceRequestRatedConsumer>.Instance);
        var tenant = Guid.NewGuid();

        Assert.True(await consumer.TryProcessAsync(Message(tenant, Guid.NewGuid()), CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync("{no es json", CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync("null", CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync(Message(tenant, Guid.NewGuid(), type: "otro.evento"), CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync(Message(tenant, Guid.Empty), CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync(Message(Guid.Empty, Guid.NewGuid()), CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync(Message(tenant, Guid.NewGuid(), technician: Guid.Empty), CancellationToken.None));
        Assert.True(await consumer.TryProcessAsync(Message(tenant, Guid.NewGuid(), "9"), CancellationToken.None));

        Assert.Equal(1, Assert.Single(store.Reputations).RatingCount);
    }

    [Fact]
    public async Task Consumidor_SiFallaLaBase_PideReintentar()
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        await using var provider = services.BuildServiceProvider();
        using var kafka = new ConsumerBuilder<string, string>(new ConsumerConfig { BootstrapServers = "localhost:1", GroupId = "pruebas" }).Build();
        var consumer = new ServiceRequestRatedConsumer(
            provider.GetRequiredService<IServiceScopeFactory>(), kafka, NullLogger<ServiceRequestRatedConsumer>.Instance);

        Assert.False(await consumer.TryProcessAsync(Message(Guid.NewGuid(), Guid.NewGuid()), CancellationToken.None));
    }

    [Fact]
    public void SinKafka_NoRegistraElConsumidorNiElPublicador()
    {
        var services = new ServiceCollection();
        services.AddInfrastructure(new ConfigurationBuilder().Build());

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(OutboxPublisher));
        Assert.DoesNotContain(services, d => d.ServiceType == typeof(ServiceRequestRatedConsumer));
    }

    [Fact]
    public void ConKafka_RegistraElConsumidorYElPublicador()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Kafka:BootstrapServers"] = "localhost:9092" })
            .Build();
        var services = new ServiceCollection();
        services.AddInfrastructure(configuration);

        Assert.Contains(services, d => d.ServiceType == typeof(OutboxPublisher));
        Assert.Contains(services, d => d.ServiceType == typeof(ServiceRequestRatedConsumer));
    }

    [Fact]
    public async Task Salud_LiveResponde_YReadyFallaSinBase()
    {
        await using var factory = new WebApplicationFactory<Program>();
        using var client = factory.CreateClient();

        using var live = await client.GetAsync(new Uri("/health/live", UriKind.Relative));
        using var ready = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, live.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, ready.StatusCode);
    }
}