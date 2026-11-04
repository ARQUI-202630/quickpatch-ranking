using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace QuickPatch.Ranking.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Base de Ranking (ADR-027, SCRUM-498): reputación por técnico, calificaciones ya sumadas, eventos aplicados y Outbox,
    /// todo con RLS por tenant. El Outbox solo admite inserciones con el tenant de la sesión; lo lee y lo marca el rol del
    /// publicador, que tiene BYPASSRLS (DD 10.2).
    /// </summary>
    public partial class Inicial : Migration
    {
        private const string CurrentTenant = "NULLIF(current_setting('app.current_tenant', true), '')::uuid";

        private static readonly string SqlAdicional = $"""
            ALTER TABLE technician_reputations ENABLE ROW LEVEL SECURITY;
            ALTER TABLE technician_reputations FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON technician_reputations USING (tenant_id = {CurrentTenant}) WITH CHECK (tenant_id = {CurrentTenant});

            ALTER TABLE applied_ratings ENABLE ROW LEVEL SECURITY;
            ALTER TABLE applied_ratings FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON applied_ratings USING (tenant_id = {CurrentTenant}) WITH CHECK (tenant_id = {CurrentTenant});

            ALTER TABLE processed_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE processed_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY tenant_isolation ON processed_events USING (tenant_id = {CurrentTenant}) WITH CHECK (tenant_id = {CurrentTenant});

            ALTER TABLE outbox_events ALTER COLUMN tenant_id SET DEFAULT {CurrentTenant};
            ALTER TABLE outbox_events ENABLE ROW LEVEL SECURITY;
            ALTER TABLE outbox_events FORCE ROW LEVEL SECURITY;
            CREATE POLICY outbox_insert ON outbox_events FOR INSERT WITH CHECK (tenant_id = {CurrentTenant});
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "applied_ratings",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    service_request_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    score = table.Column<int>(type: "integer", nullable: false),
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    applied_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_applied_ratings", x => new { x.tenant_id, x.service_request_id });
                    table.CheckConstraint("ck_applied_ratings_score", "score BETWEEN 1 AND 5");
                });

            migrationBuilder.CreateTable(
                name: "outbox_events",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    aggregate_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    published_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_outbox_events", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "processed_events",
                columns: table => new
                {
                    event_id = table.Column<Guid>(type: "uuid", nullable: false),
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    event_type = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    processed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_processed_events", x => x.event_id);
                });

            migrationBuilder.CreateTable(
                name: "technician_reputations",
                columns: table => new
                {
                    tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                    technician_id = table.Column<Guid>(type: "uuid", nullable: false),
                    rating_count = table.Column<int>(type: "integer", nullable: false),
                    rating_sum = table.Column<int>(type: "integer", nullable: false),
                    average_rating = table.Column<decimal>(type: "numeric(2,1)", precision: 2, scale: 1, nullable: false),
                    low_reputation = table.Column<bool>(type: "boolean", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_technician_reputations", x => new { x.tenant_id, x.technician_id });
                    table.CheckConstraint("ck_technician_reputations_average", "average_rating BETWEEN 0 AND 5");
                    table.CheckConstraint("ck_technician_reputations_count", "rating_count >= 0");
                });

            migrationBuilder.CreateIndex(
                name: "ix_applied_ratings_technician",
                table: "applied_ratings",
                columns: new[] { "tenant_id", "technician_id" });

            migrationBuilder.CreateIndex(
                name: "ix_outbox_events_pending",
                table: "outbox_events",
                column: "created_at",
                filter: "published_at IS NULL");

            migrationBuilder.Sql(SqlAdicional);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "applied_ratings");

            migrationBuilder.DropTable(
                name: "outbox_events");

            migrationBuilder.DropTable(
                name: "processed_events");

            migrationBuilder.DropTable(
                name: "technician_reputations");
        }
    }
}
