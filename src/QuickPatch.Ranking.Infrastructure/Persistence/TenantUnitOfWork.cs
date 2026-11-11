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