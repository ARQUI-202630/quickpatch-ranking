using Confluent.Kafka;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Infrastructure.Messaging;
using QuickPatch.Ranking.Infrastructure.Options;
using QuickPatch.Ranking.Infrastructure.Persistence;

namespace QuickPatch.Ranking.Infrastructure;

/// <summary>
/// Capa de infraestructura: PostgreSQL (EF Core) y Kafka (SDD 6.3). Implementa las interfaces que definen Application y
/// Domain. Ranking no tiene REST hacia el gateway (ADR-025): recibe las calificaciones y devuelve la reputación por eventos
/// (ADR-027).
/// </summary>
public static class DependencyInjection
{
    public const string ConnectionStringName = "Ranking";
    public const string ReadyTag = "ready";

    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.Configure<KafkaOptions>(configuration.GetSection(KafkaOptions.Section));
        services.Configure<OutboxOptions>(configuration.GetSection(OutboxOptions.Section));

        services.AddDbContext<RankingDbContext>(options => options.UseNpgsql(configuration.GetConnectionString(ConnectionStringName)));

        services.AddScoped<ITenantUnitOfWork, TenantUnitOfWork>();
        services.AddScoped<IReputationRepository, ReputationRepository>();
        services.AddScoped<IProcessedEventRepository, ProcessedEventRepository>();
        services.AddScoped<IOutbox, EfOutbox>();

        services.AddHealthChecks().AddDbContextCheck<RankingDbContext>("postgresql", tags: [ReadyTag]);

        var kafka = configuration.GetSection(KafkaOptions.Section).Get<KafkaOptions>() ?? new KafkaOptions();
        if (kafka.Enabled)
        {
            services.AddSingleton(_ => new ProducerBuilder<string, string>(new ProducerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true,
            }).Build());
            services.AddSingleton(_ => new ConsumerBuilder<string, string>(new ConsumerConfig
            {
                BootstrapServers = kafka.BootstrapServers,
                GroupId = kafka.ConsumerGroupId,
                EnableAutoCommit = false,
                AutoOffsetReset = AutoOffsetReset.Earliest,
            }).Build());

            services.AddSingleton<OutboxPublisher>();
            services.AddHostedService(sp => sp.GetRequiredService<OutboxPublisher>());
            services.AddSingleton<ServiceRequestRatedConsumer>();
            services.AddHostedService(sp => sp.GetRequiredService<ServiceRequestRatedConsumer>());
        }

        return services;
    }
}