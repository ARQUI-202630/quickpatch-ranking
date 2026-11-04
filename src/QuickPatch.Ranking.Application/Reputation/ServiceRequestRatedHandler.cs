using Microsoft.Extensions.Logging;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Application.Events;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Application.Reputation;

public enum RatedHandlingOutcome
{
    /// <summary>La calificación se sumó y quedó <c>ranking.technician-reputation-changed</c> en el Outbox.</summary>
    Applied,

    /// <summary>El evento ya se había aplicado (mismo <c>eventId</c>, RN-EV1).</summary>
    Duplicate,

    /// <summary>La calificación de esa solicitud ya estaba sumada (llegó con otro <c>eventId</c>).</summary>
    AlreadyApplied,

    /// <summary>El puntaje no está entre 1 y 5: el mensaje no cumple el contrato.</summary>
    Invalid,
}

/// <summary>
/// Al consumir <c>service-request.rated</c> suma la calificación a la reputación del técnico bajo el tenant del evento y
/// publica <c>ranking.technician-reputation-changed</c> por Outbox en la misma transacción (ADR-027, decisiones 3 y 4;
/// ADR-007). Es idempotente por <c>eventId</c> y por solicitud: un evento repetido no vuelve a sumar.
/// </summary>
public sealed partial class ServiceRequestRatedHandler(
    ITenantUnitOfWork unitOfWork,
    IReputationRepository reputations,
    IProcessedEventRepository processedEvents,
    IOutbox outbox,
    TimeProvider clock,
    ILogger<ServiceRequestRatedHandler> logger)
{
    public Task<RatedHandlingOutcome> HandleAsync(EventEnvelope<ServiceRequestRatedData> envelope, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(envelope);
        var data = envelope.Data;
        if (data.Score is < TechnicianReputation.MinScore or > TechnicianReputation.MaxScore)
        {
            LogInvalidScore(logger, envelope.EventId);
            return Task.FromResult(RatedHandlingOutcome.Invalid);
        }

        return unitOfWork.ExecuteAsync(
            envelope.TenantId,
            async ct =>
            {
                if (await processedEvents.ExistsAsync(envelope.EventId, ct))
                {
                    return RatedHandlingOutcome.Duplicate;
                }

                var now = clock.GetUtcNow();
                processedEvents.Add(envelope.EventId, envelope.TenantId, envelope.EventType, now);
                if (await reputations.IsAppliedAsync(data.ServiceRequestId, ct))
                {
                    return RatedHandlingOutcome.AlreadyApplied;
                }

                var reputation = await reputations.FindForUpdateAsync(data.TechnicianId, ct);
                if (reputation is null)
                {
                    reputation = TechnicianReputation.Create(envelope.TenantId, data.TechnicianId, now);
                    reputations.Add(reputation);
                }

                reputation.Apply(data.Score, now);
                reputations.AddApplied(AppliedRating.Create(
                    envelope.TenantId, data.ServiceRequestId, data.TechnicianId, data.Score, envelope.EventId, now));
                outbox.Enqueue(RankingEvents.ReputationChanged(reputation, envelope.CorrelationId));
                LogApplied(logger, data.TechnicianId, reputation.RatingCount);
                return RatedHandlingOutcome.Applied;
            },
            cancellationToken);
    }

    [LoggerMessage(Level = LogLevel.Error, Message = "service-request.rated {EventId} trae un puntaje fuera de 1 a 5; se descarta.")]
    private static partial void LogInvalidScore(ILogger logger, Guid eventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Reputación del técnico {TechnicianId} actualizada con {RatingCount} calificaciones.")]
    private static partial void LogApplied(ILogger logger, Guid technicianId, int ratingCount);
}