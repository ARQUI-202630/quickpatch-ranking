using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Ranking.Domain.Reputation;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

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