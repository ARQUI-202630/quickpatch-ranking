using System.Net;
using System.Text.Json;

using Confluent.Kafka;
using Confluent.Kafka.Admin;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;

using Npgsql;

using QuickPatch.Ranking.Infrastructure.Persistence;

using Testcontainers.Kafka;
using Testcontainers.PostgreSql;

namespace QuickPatch.Ranking.IntegrationTests;

/// <summary>
/// PostgreSQL 16 y Kafka reales, con migraciones, roles del DD 10.2 y el servicio como <c>ranking_app</c> (sin BYPASSRLS).
/// El Outbox publica cada 200 ms.
/// </summary>
public sealed class RankingEnvironment : IAsyncLifetime
{
    public static readonly string[] Topics = ["service-request.rated", "ranking.technician-reputation-changed"];

    private readonly PostgreSqlContainer postgres = new PostgreSqlBuilder("postgres:16-alpine").WithDatabase("db_ranking").Build();
    private readonly KafkaContainer kafka = new KafkaBuilder("confluentinc/cp-kafka:7.7.1").Build();

    public string AdminConnectionString => postgres.GetConnectionString();

    public string AppConnectionString => new NpgsqlConnectionStringBuilder(AdminConnectionString)
    {
        Username = "ranking_app",
        Password = "app-pruebas",
    }.ConnectionString;

    public string KafkaServers => kafka.GetBootstrapAddress();

    public WebApplicationFactory<Program> App { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        await Task.WhenAll(postgres.StartAsync(), kafka.StartAsync());
        await using (var db = new RankingDbContext(new DbContextOptionsBuilder<RankingDbContext>().UseNpgsql(AdminConnectionString).Options))
        {
            await db.Database.MigrateAsync();
        }

        await ExecuteAdminAsync(await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory, "db", "roles.sql")));
        await ExecuteAdminAsync("ALTER ROLE ranking_app LOGIN PASSWORD 'app-pruebas';");
        using (var admin = new AdminClientBuilder(new AdminClientConfig { BootstrapServers = KafkaServers }).Build())
        {
            await admin.CreateTopicsAsync(Topics.Select(t => new TopicSpecification { Name = t, NumPartitions = 3, ReplicationFactor = 1 }));
        }

        App = new WebApplicationFactory<Program>().WithWebHostBuilder(b =>
        {
            b.UseSetting("ConnectionStrings:Ranking", AppConnectionString);
            b.UseSetting("Kafka:BootstrapServers", KafkaServers);
            b.UseSetting("Kafka:ConsumerGroupId", "ranking-pruebas");
            b.UseSetting("Outbox:PublisherRole", "ranking_outbox");
            b.UseSetting("Outbox:PollInterval", "00:00:00.200");
        });
        _ = App.Server;
    }

    public async Task DisposeAsync()
    {
        if (App is not null)
        {
            await App.DisposeAsync();
        }

        await Task.WhenAll(postgres.DisposeAsync().AsTask(), kafka.DisposeAsync().AsTask());
    }

    public async Task ExecuteAdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    public async Task<T?> ScalarAdminAsync<T>(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(AdminConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters)
        {
            command.Parameters.AddWithValue(name, value);
        }

        var result = await command.ExecuteScalarAsync();
        return result is null or DBNull ? default : (T)result;
    }

    /// <summary>Publica <c>service-request.rated</c> como lo hace ServiceRequest (clave: la solicitud).</summary>
    public async Task<Guid> PublishRatedAsync(Guid tenant, Guid serviceRequest, Guid technician, int score, Guid? eventId = null)
    {
        var id = eventId ?? Guid.NewGuid();
        using var producer = new ProducerBuilder<string, string>(new ProducerConfig { BootstrapServers = KafkaServers }).Build();
        var value = JsonSerializer.Serialize(new
        {
            eventId = id,
            eventType = "service-request.rated",
            eventVersion = 1,
            occurredAt = DateTimeOffset.UtcNow,
            correlationId = Guid.NewGuid(),
            tenantId = tenant,
            producer = "service-request-service",
            data = new { serviceRequestId = serviceRequest, technicianId = technician, score, ratedAt = DateTimeOffset.UtcNow },
        });
        await producer.ProduceAsync("service-request.rated", new Message<string, string> { Key = serviceRequest.ToString(), Value = value });
        return id;
    }

    /// <summary>Espera hasta que la consulta devuelva el valor esperado o se acabe el tiempo.</summary>
    public static async Task<T?> EventuallyAsync<T>(Func<Task<T?>> read, Func<T?, bool> done, int seconds = 30)
    {
        ArgumentNullException.ThrowIfNull(read);
        ArgumentNullException.ThrowIfNull(done);
        var deadline = DateTime.UtcNow.AddSeconds(seconds);
        T? value;
        do
        {
            value = await read();
            if (done(value))
            {
                return value;
            }

            await Task.Delay(250);
        }
        while (DateTime.UtcNow < deadline);
        return value;
    }
}

[CollectionDefinition(Name)]
public sealed class EnvironmentDefinition : ICollectionFixture<RankingEnvironment>
{
    public const string Name = "ranking";
}

/// <summary>
/// SCRUM-498 de punta a punta (ADR-027): <c>service-request.rated</c> llega por Kafka, Ranking suma la calificación bajo
/// RLS, una vez por evento y por solicitud, y publica <c>ranking.technician-reputation-changed</c> por Outbox.
/// </summary>
[Collection(EnvironmentDefinition.Name)]
public class ReputationFlowTests(RankingEnvironment env)
{
    private Task<long> CountAsync(Guid tenant, Guid technician) =>
        env.ScalarAdminAsync<long>(
            "SELECT COALESCE((SELECT rating_count FROM technician_reputations WHERE tenant_id = @t AND technician_id = @tec), 0)::bigint",
            ("t", tenant), ("tec", technician));

    private Task<decimal> AverageAsync(Guid tenant, Guid technician) =>
        env.ScalarAdminAsync<decimal>(
            "SELECT average_rating FROM technician_reputations WHERE tenant_id = @t AND technician_id = @tec", ("t", tenant), ("tec", technician));

    [Fact]
    public async Task Calificaciones_ActualizanElPromedio_YPublicanElCambio()
    {
        var (tenant, technician) = (Guid.NewGuid(), Guid.NewGuid());
        using var listener = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = env.KafkaServers,
            GroupId = $"escucha-{Guid.NewGuid()}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
        }).Build();
        listener.Subscribe("ranking.technician-reputation-changed");

        await env.PublishRatedAsync(tenant, Guid.NewGuid(), technician, 5);
        await env.PublishRatedAsync(tenant, Guid.NewGuid(), technician, 4);

        Assert.Equal(2L, await RankingEnvironment.EventuallyAsync(() => CountAsync(tenant, technician), c => c == 2));
        Assert.Equal(4.5m, await AverageAsync(tenant, technician));

        JsonElement? last = null;
        var deadline = DateTime.UtcNow.AddSeconds(30);
        while (DateTime.UtcNow < deadline)
        {
            var result = listener.Consume(TimeSpan.FromMilliseconds(500));
            if (result is null || result.Message.Key != technician.ToString())
            {
                continue;
            }

            last = JsonDocument.Parse(result.Message.Value).RootElement.Clone();
            if (last.Value.GetProperty("data").GetProperty("ratingCount").GetInt32() == 2)
            {
                break;
            }
        }

        Assert.NotNull(last);
        Assert.Equal(tenant, last.Value.GetProperty("tenantId").GetGuid());
        var data = last.Value.GetProperty("data");
        Assert.Equal(2, data.GetProperty("ratingCount").GetInt32());
        Assert.Equal(4.5m, data.GetProperty("averageRating").GetDecimal());
        Assert.False(data.GetProperty("lowReputation").GetBoolean());
        listener.Close();
    }

    [Fact]
    public async Task EventoRepetido_YMismaSolicitudConOtroEventId_NoSumanDosVeces()
    {
        var (tenant, technician, request) = (Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid());

        var eventId = await env.PublishRatedAsync(tenant, request, technician, 5);
        await env.PublishRatedAsync(tenant, request, technician, 5, eventId);
        await env.PublishRatedAsync(tenant, request, technician, 1);
        var marker = await env.PublishRatedAsync(tenant, Guid.NewGuid(), technician, 3);

        // El último evento de la misma clave de técnico ya se aplicó: los anteriores también se procesaron.
        await RankingEnvironment.EventuallyAsync(
            () => env.ScalarAdminAsync<long>("SELECT count(*) FROM processed_events WHERE event_id = @e", ("e", marker)), c => c == 1);
        Assert.Equal(2L, await RankingEnvironment.EventuallyAsync(() => CountAsync(tenant, technician), c => c == 2));
        await Task.Delay(1000);
        Assert.Equal(2L, await CountAsync(tenant, technician));
        Assert.Equal(4.0m, await AverageAsync(tenant, technician));
        Assert.Equal(1L, await env.ScalarAdminAsync<long>(
            "SELECT count(*) FROM applied_ratings WHERE service_request_id = @r", ("r", request)));
    }

    [Fact]
    public async Task CincoCalificacionesBajas_MarcanReputacionBaja()
    {
        var (tenant, technician) = (Guid.NewGuid(), Guid.NewGuid());

        foreach (var score in new[] { 2, 2, 3, 3, 4 })
        {
            await env.PublishRatedAsync(tenant, Guid.NewGuid(), technician, score);
        }

        Assert.Equal(5L, await RankingEnvironment.EventuallyAsync(() => CountAsync(tenant, technician), c => c == 5));
        Assert.True(await env.ScalarAdminAsync<bool>(
            "SELECT low_reputation FROM technician_reputations WHERE tenant_id = @t AND technician_id = @tec", ("t", tenant), ("tec", technician)));
    }

    [Fact]
    public async Task Rls_LaAplicacionSoloVeLaReputacionDeSuTenant_YElOutboxNoSeLee()
    {
        var (tenant, technician) = (Guid.NewGuid(), Guid.NewGuid());
        await env.PublishRatedAsync(tenant, Guid.NewGuid(), technician, 4);
        Assert.Equal(1L, await RankingEnvironment.EventuallyAsync(() => CountAsync(tenant, technician), c => c == 1));

        await using var connection = new NpgsqlConnection(env.AppConnectionString);
        await connection.OpenAsync();
        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using (var set = new NpgsqlCommand($"SELECT set_config('app.current_tenant', '{Guid.NewGuid()}', true)", connection, transaction))
            {
                await set.ExecuteNonQueryAsync();
            }

            await using var other = new NpgsqlCommand(
                $"SELECT count(*) FROM technician_reputations WHERE technician_id = '{technician}'", connection, transaction);
            Assert.Equal(0L, (long)(await other.ExecuteScalarAsync())!);

            await using var outbox = new NpgsqlCommand("SELECT count(*) FROM outbox_events", connection, transaction);
            Assert.Equal("42501", (await Assert.ThrowsAsync<PostgresException>(() => outbox.ExecuteScalarAsync())).SqlState);
        }

        await using (var transaction = await connection.BeginTransactionAsync())
        {
            await using (var set = new NpgsqlCommand($"SELECT set_config('app.current_tenant', '{tenant}', true)", connection, transaction))
            {
                await set.ExecuteNonQueryAsync();
            }

            await using var own = new NpgsqlCommand(
                $"SELECT count(*) FROM technician_reputations WHERE technician_id = '{technician}'", connection, transaction);
            Assert.Equal(1L, (long)(await own.ExecuteScalarAsync())!);
        }
    }

    [Fact]
    public async Task Salud_ReadyRespondeConLaBase()
    {
        using var client = env.App.CreateClient();

        using var response = await client.GetAsync(new Uri("/health/ready", UriKind.Relative));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}