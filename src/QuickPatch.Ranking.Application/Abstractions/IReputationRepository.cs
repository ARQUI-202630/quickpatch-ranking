using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Abstractions;

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