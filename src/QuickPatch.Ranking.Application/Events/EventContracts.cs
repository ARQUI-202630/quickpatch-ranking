using System.Text.Json;
using System.Text.Json.Serialization;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Events;

/// <summary>Sobre común de los eventos (DD 8.4.1; <c>quickpatch-kafka/events</c>).</summary>
public sealed record EventEnvelope<TData>(
    Guid EventId,
    string EventType,
    int EventVersion,
    DateTimeOffset OccurredAt,
    Guid CorrelationId,
    Guid TenantId,
    string Producer,
    TData Data);

public static class EventTypes
{
    public const string ServiceRequestRated = "service-request.rated";
    public const string TechnicianReputationChanged = "ranking.technician-reputation-changed";
}

/// <summary><c>data</c> de <c>service-request.rated</c> v1 (DD 8.4.2, ADR-027): sin el comentario.</summary>
public sealed record ServiceRequestRatedData(Guid ServiceRequestId, Guid TechnicianId, int Score, DateTimeOffset RatedAt);

/// <summary>
/// <c>data</c> de <c>ranking.technician-reputation-changed</c> v1 (ADR-027, decisión 4): Identity lo proyecta en
/// <c>technician_profiles.average_rating</c> y descarta los más viejos que el último aplicado por <see cref="ChangedAt"/>.
/// </summary>
public sealed record TechnicianReputationChangedData(
    Guid TechnicianId, decimal AverageRating, int RatingCount, bool LowReputation, DateTimeOffset ChangedAt);

public static class EventSerializer
{
    public const string Producer = "ranking-service";

    public static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.Never,
    };

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T? Deserialize<T>(string json) => JsonSerializer.Deserialize<T>(json, Options);
}

/// <summary>Eventos de Ranking listos para el Outbox. La clave del mensaje es el técnico.</summary>
public static class RankingEvents
{
    public static OutboxMessage ReputationChanged(TechnicianReputation reputation, Guid correlationId)
    {
        ArgumentNullException.ThrowIfNull(reputation);
        var eventId = Guid.CreateVersion7(reputation.UpdatedAt);
        var envelope = new EventEnvelope<TechnicianReputationChangedData>(
            eventId,
            EventTypes.TechnicianReputationChanged,
            1,
            reputation.UpdatedAt,
            correlationId,
            reputation.TenantId,
            EventSerializer.Producer,
            new TechnicianReputationChangedData(
                reputation.TechnicianId, reputation.AverageRating, reputation.RatingCount, reputation.LowReputation, reputation.UpdatedAt));
        return new OutboxMessage(
            eventId, reputation.TenantId, reputation.TechnicianId, EventTypes.TechnicianReputationChanged, EventSerializer.Serialize(envelope));
    }
}