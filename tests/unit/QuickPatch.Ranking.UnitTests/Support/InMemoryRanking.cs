using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.UnitTests.Support;

/// <summary>
/// Implementación en memoria de los puertos. Emula RLS: cada lectura solo ve las filas del tenant de la transacción en
/// curso, y fuera de una transacción no se ve nada (falla cerrado, como en el DD 10.2).
/// </summary>
public sealed class InMemoryRanking : ITenantUnitOfWork, IReputationRepository, IProcessedEventRepository, IOutbox
{
    private Guid? currentTenant;

    public List<TechnicianReputation> Reputations { get; } = [];

    public List<AppliedRating> Applied { get; } = [];

    public List<(Guid EventId, Guid TenantId, string EventType)> ProcessedEvents { get; } = [];

    public List<OutboxMessage> Outbox { get; } = [];

    public int Transactions { get; private set; }

    public async Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        Transactions++;
        currentTenant = tenantId;
        try
        {
            return await work(cancellationToken);
        }
        finally
        {
            currentTenant = null;
        }
    }

    public Task<TechnicianReputation?> FindForUpdateAsync(Guid technicianId, CancellationToken cancellationToken) =>
        Task.FromResult(Reputations.SingleOrDefault(x => x.TenantId == currentTenant && x.TechnicianId == technicianId));

    public void Add(TechnicianReputation reputation) => Reputations.Add(reputation);

    public Task<bool> IsAppliedAsync(Guid serviceRequestId, CancellationToken cancellationToken) =>
        Task.FromResult(Applied.Any(x => x.TenantId == currentTenant && x.ServiceRequestId == serviceRequestId));

    public void AddApplied(AppliedRating rating) => Applied.Add(rating);

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        Task.FromResult(ProcessedEvents.Any(x => x.TenantId == currentTenant && x.EventId == eventId));

    public void Add(Guid eventId, Guid tenantId, string eventType, DateTimeOffset processedAt) =>
        ProcessedEvents.Add((eventId, tenantId, eventType));

    public void Enqueue(OutboxMessage message) => Outbox.Add(message);
}

public sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}