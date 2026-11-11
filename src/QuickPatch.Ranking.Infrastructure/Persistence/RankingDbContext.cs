using Microsoft.EntityFrameworkCore;

using QuickPatch.Ranking.Domain.Reputation;
using QuickPatch.Transversal.Messaging;

namespace QuickPatch.Ranking.Infrastructure.Persistence;

/// <summary>
/// Base de Ranking Service (<c>db_ranking</c>, ADR-014 y ADR-027): reputación de cada técnico, calificaciones ya sumadas,
/// eventos aplicados y Outbox. RLS y los permisos los agrega la migración inicial.
/// </summary>
public sealed class RankingDbContext(DbContextOptions<RankingDbContext> options) : DbContext(options)
{
    public DbSet<TechnicianReputation> Reputations => Set<TechnicianReputation>();

    public DbSet<AppliedRating> AppliedRatings => Set<AppliedRating>();

    public DbSet<OutboxEventRecord> OutboxEvents => Set<OutboxEventRecord>();

    public DbSet<ProcessedEventRecord> ProcessedEvents => Set<ProcessedEventRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);

        // Una fila por técnico y tenant. El promedio es NUMERIC(2,1), como technician_profiles.average_rating (DD 5.3).
        modelBuilder.Entity<TechnicianReputation>(e =>
        {
            e.ToTable("technician_reputations", t =>
            {
                t.HasCheckConstraint("ck_technician_reputations_count", "rating_count >= 0");
                t.HasCheckConstraint("ck_technician_reputations_average", "average_rating BETWEEN 0 AND 5");
            });
            e.HasKey(x => new { x.TenantId, x.TechnicianId });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.TechnicianId).HasColumnName("technician_id");
            e.Property(x => x.RatingCount).HasColumnName("rating_count");
            e.Property(x => x.RatingSum).HasColumnName("rating_sum");
            e.Property(x => x.AverageRating).HasColumnName("average_rating").HasPrecision(2, 1);
            e.Property(x => x.LowReputation).HasColumnName("low_reputation");
            e.Property(x => x.UpdatedAt).HasColumnName("updated_at");
        });

        // Una calificación por solicitud: el evento repetido con otro eventId no vuelve a sumar (ADR-027).
        modelBuilder.Entity<AppliedRating>(e =>
        {
            e.ToTable("applied_ratings", t => t.HasCheckConstraint("ck_applied_ratings_score", "score BETWEEN 1 AND 5"));
            e.HasKey(x => new { x.TenantId, x.ServiceRequestId });
            e.Property(x => x.TenantId).HasColumnName("tenant_id");
            e.Property(x => x.ServiceRequestId).HasColumnName("service_request_id");
            e.Property(x => x.TechnicianId).HasColumnName("technician_id");
            e.Property(x => x.Score).HasColumnName("score");
            e.Property(x => x.EventId).HasColumnName("event_id");
            e.Property(x => x.AppliedAt).HasColumnName("applied_at");
            e.HasIndex(x => new { x.TenantId, x.TechnicianId }, "ix_applied_ratings_technician");
        });

        // Tablas transversales con el mapeo común (ADR-020); las migraciones siguen siendo de este servicio.
        modelBuilder.MapOutboxEvents().MapProcessedEvents();
    }
}