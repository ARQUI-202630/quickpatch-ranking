using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using System.Text.Json.Serialization;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Events;

/// <summary><c>data</c> de <c>service-request.rated</c> v1 (DD 8.4.2, ADR-027): sin el comentario.</summary>
public sealed record ServiceRequestRatedData(Guid ServiceRequestId, Guid TechnicianId, int Score, DateTimeOffset RatedAt);

/// <summary>
/// Contrato de <c>service-request.rated</c> v1: lo que el mensaje debe traer para aplicarse. Un mensaje que no lo cumple
/// se descarta sin reintentar. El puntaje fuera de 1 a 5 lo rechaza el caso de uso (<c>RatedHandlingOutcome.Invalid</c>).
/// </summary>
public static class ServiceRequestRatedContract
{
    public static bool IsWellFormed([NotNullWhen(true)] EventEnvelope<ServiceRequestRatedData>? envelope) =>
        envelope?.Data is { } data
        && envelope.EventType == EventTypes.ServiceRequestRated
        && envelope.EventVersion == 1
        && envelope.EventId != Guid.Empty
        && envelope.TenantId != Guid.Empty
        && data.ServiceRequestId != Guid.Empty
        && data.TechnicianId != Guid.Empty;
}