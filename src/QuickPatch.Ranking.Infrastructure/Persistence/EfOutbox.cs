using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

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