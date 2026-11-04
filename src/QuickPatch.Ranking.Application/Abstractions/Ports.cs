using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Abstractions;

/// <summary>
/// Ejecuta un trabajo dentro de una transacción que primero fija el tenant de la sesión
/// (<c>SET LOCAL app.current_tenant</c>, DD 10.2), para que RLS aísle los datos. Al terminar guarda y confirma.
/// </summary>
public interface ITenantUnitOfWork
{
    Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken);
}

/// <summary>Reputación de los técnicos y calificaciones aplicadas, siempre bajo el tenant de la transacción.</summary>
public interface IReputationRepository
{
    /// <summary>
    /// Reputación del técnico, con su fila bloqueada hasta el final de la transacción (<c>SELECT ... FOR UPDATE</c>): dos
    /// calificaciones del mismo técnico que llegan por particiones distintas no se pisan. Null si todavía no tiene.
    /// </summary>
    Task<TechnicianReputation?> FindForUpdateAsync(Guid technicianId, CancellationToken cancellationToken);

    void Add(TechnicianReputation reputation);

    /// <summary>Si la calificación de esa solicitud ya se sumó (ADR-027: una por solicitud).</summary>
    Task<bool> IsAppliedAsync(Guid serviceRequestId, CancellationToken cancellationToken);

    void AddApplied(AppliedRating rating);
}

/// <summary>Registro de eventos ya aplicados (tabla <c>processed_events</c>, RN-EV1).</summary>
public interface IProcessedEventRepository
{
    Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken);

    void Add(Guid eventId, Guid tenantId, string eventType, DateTimeOffset processedAt);
}

/// <summary>Transactional Outbox (ADR-007): el evento se guarda en la misma transacción que el cambio.</summary>
public interface IOutbox
{
    void Enqueue(OutboxMessage message);
}

/// <summary>Mensaje pendiente de publicar en Kafka. <see cref="Id"/> se publica como <c>eventId</c>.</summary>
public sealed record OutboxMessage(Guid Id, Guid TenantId, Guid AggregateId, string EventType, string Payload);