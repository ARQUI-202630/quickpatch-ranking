using QuickPatch.Ranking.Application.Abstractions;
using QuickPatch.Transversal.Persistence;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

/// <summary>
/// Transacción por tenant (DD 10.2) con la implementación común de ADR-020. Antes limpia el seguimiento de cambios: un
/// reintento del consumidor no debe arrastrar entidades de un intento que falló.
/// </summary>
public sealed class TenantUnitOfWork(RankingDbContext db) : ITenantUnitOfWork
{
    public Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> work, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        return TenantTransaction.ExecuteAsync(db, tenantId, work, cancellationToken);
    }
}