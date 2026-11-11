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