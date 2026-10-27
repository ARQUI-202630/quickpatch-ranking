using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

/// <summary>Fábrica para <c>dotnet ef</c> (generar migraciones). No se conecta a ninguna base.</summary>
public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<RankingDbContext>
{
    public RankingDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<RankingDbContext>().UseNpgsql("Host=localhost;Database=db_ranking").Options);
}