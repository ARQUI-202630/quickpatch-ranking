using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;
using QuickPatch.Transversal.Messaging;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

public sealed class EfOutbox(RankingDbContext db, TimeProvider clock) : IOutbox
{
    public void Enqueue(OutboxMessage message)
    {
        ArgumentNullException.ThrowIfNull(message);
        db.OutboxEvents.Add(OutboxEventRecord.Pending(message.Id, message.TenantId, message.AggregateId, message.EventType, message.Payload, clock.GetUtcNow()));
    }
}