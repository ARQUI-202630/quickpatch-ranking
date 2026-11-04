namespace QuickPatch.Ranking.Infrastructure.Options;

/// <summary>Conexión a Kafka (VM6; VM7 en QA). Sección <c>Kafka</c>; sin servidores, el Outbox y el consumidor no arrancan.</summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;

    public string ConsumerGroupId { get; set; } = "ranking-service";

    public bool Enabled => !string.IsNullOrWhiteSpace(BootstrapServers);
}

/// <summary>Publicador del Outbox (ADR-007). Sección <c>Outbox</c>.</summary>
public sealed class OutboxOptions
{
    public const string Section = "Outbox";

    /// <summary>
    /// Rol <c>BYPASSRLS</c> que adopta el publicador con <c>SET LOCAL ROLE</c> para leer los eventos de todos
    /// los tenants (DD, sección 10.2). Vacío solo en desarrollo local.
    /// </summary>
    public string PublisherRole { get; set; } = string.Empty;

    public int BatchSize { get; set; } = 50;

    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(1);
}