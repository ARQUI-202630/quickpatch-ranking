using System.Text.Json;
using System.Text.Json.Serialization;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Events;

/// <summary>
/// <c>data</c> de <c>ranking.technician-reputation-changed</c> v1 (ADR-027, decisión 4): Identity lo proyecta en
/// <c>technician_profiles.average_rating</c> y descarta los más viejos que el último aplicado por <see cref="ChangedAt"/>.
/// </summary>
public sealed record TechnicianReputationChangedData(
    Guid TechnicianId, decimal AverageRating, int RatingCount, bool LowReputation, DateTimeOffset ChangedAt);

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