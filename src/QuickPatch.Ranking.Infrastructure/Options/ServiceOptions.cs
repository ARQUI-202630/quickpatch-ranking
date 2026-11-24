namespace QuickPatch.Ranking.Infrastructure.Options;

/// <summary>Conexión a Kafka (VM6; VM7 en QA). Sección <c>Kafka</c>; sin servidores, el Outbox y el consumidor no arrancan.</summary>
public sealed class KafkaOptions
{
    public const string Section = "Kafka";

    public string BootstrapServers { get; set; } = string.Empty;

    public string ConsumerGroupId { get; set; } = "ranking-service";

    public bool Enabled => !string.IsNullOrWhiteSpace(BootstrapServers);
}