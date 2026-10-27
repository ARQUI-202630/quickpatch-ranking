using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

/// <summary>
/// Transacción por tenant (DD 10.2): fija <c>app.current_tenant</c> con <c>set_config(..., true)</c>,
/// equivalente a <c>SET LOCAL</c>, antes de ejecutar el trabajo; RLS filtra todo lo que se lea o escriba.
/// </summary>
public sealed class TenantUnitOfWork(RankingDbContext db) : ITenantUnitOfWork
{
    public async Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (tenantId == Guid.Empty)
        {
            throw new ArgumentException("El tenant es obligatorio.", nameof(tenantId));
        }

        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var tenant = tenantId.ToString();
        await db.Database.ExecuteSqlAsync($"SELECT set_config('app.current_tenant', {tenant}, true)", cancellationToken);

        var result = await work(cancellationToken);

        await db.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return result;
    }
}

public sealed class ReputationRepository(RankingDbContext db) : IReputationRepository
{
    public Task<TechnicianReputation?> FindForUpdateAsync(Guid technicianId, CancellationToken cancellationToken) =>
        db.Reputations
            .FromSql($"SELECT * FROM technician_reputations WHERE technician_id = {technicianId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);

    public void Add(TechnicianReputation reputation) => db.Reputations.Add(reputation);

    public Task<bool> IsAppliedAsync(Guid serviceRequestId, CancellationToken cancellationToken) =>
        db.AppliedRatings.AnyAsync(x => x.ServiceRequestId == serviceRequestId, cancellationToken);

    public void AddApplied(AppliedRating rating) => db.AppliedRatings.Add(rating);
}

public sealed class ProcessedEventRepository(RankingDbContext db) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.ProcessedEvents.AnyAsync(x => x.EventId == eventId, cancellationToken);

    public void Add(Guid eventId, Guid tenantId, string eventType, DateTimeOffset processedAt) =>
        db.ProcessedEvents.Add(new ProcessedEventRecord { EventId = eventId, TenantId = tenantId, EventType = eventType, ProcessedAt = processedAt });
}

public sealed class EfOutbox(RankingDbContext db, TimeProvider clock) : IOutbox
{
    public void Enqueue(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        db.OutboxEvents.Add(new OutboxEventRecord
        {
            Id = message.Id,
            TenantId = message.TenantId,
            AggregateId = message.AggregateId,
            EventType = message.EventType,
            Payload = message.Payload,
            CreatedAt = clock.GetUtcNow(),
            Attempts = 0,
        });
    }
}