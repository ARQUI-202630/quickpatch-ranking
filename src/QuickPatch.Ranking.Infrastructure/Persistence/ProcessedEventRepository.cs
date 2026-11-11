using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

public sealed class ProcessedEventRepository(RankingDbContext db) : IProcessedEventRepository
{
    public Task<bool> ExistsAsync(Guid eventId, CancellationToken cancellationToken) =>
        db.ProcessedEvents.AnyAsync(x => x.EventId == eventId, cancellationToken);

    public void Add(Guid eventId, Guid tenantId, string eventType, DateTimeOffset processedAt) =>
        db.ProcessedEvents.Add(new ProcessedEventRecord { EventId = eventId, TenantId = tenantId, EventType = eventType, ProcessedAt = processedAt });
}