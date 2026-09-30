using System;

using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Tenant isolation in the database (K-ID-1, K-ID-2, ADR-2250): composite <c>(tenant_id, id)</c> keys and foreign
/// keys between tenant-scoped tables, the <c>tenant_memberships</c> table (K-ID-4), and Row-Level Security enabled
/// and forced on every kernel table with the policies in <see cref="TenantIsolationSql"/>.
/// </summary>
public partial class TenantIsolation : Migration
{
    // Named literally, never read from a shared list, so this migration cannot change once applied.
    private static readonly string[] TenantScopedTables =
    [
        "organizations", "sites", "studies", "study_sites", "tenant_memberships",
        "provenance_activities", "domain_events", "audit_entries", "audit_chain_heads", "audit_chain_seals",
        "outbox_messages", "idempotency_records",
    ];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropForeignKey(
            name: "FK_sites_organizations_institution_id",
            schema: "kernel",
            table: "sites");

        migrationBuilder.DropForeignKey(
            name: "FK_studies_organizations_sponsor_id",
            schema: "kernel",
            table: "studies");

        migrationBuilder.DropForeignKey(
            name: "FK_study_sites_sites_site_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropForeignKey(
            name: "FK_study_sites_studies_study_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropIndex(
            name: "IX_study_sites_site_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropIndex(
            name: "IX_study_sites_tenant_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropIndex(
            name: "IX_studies_sponsor_id",
            schema: "kernel",
            table: "studies");

        migrationBuilder.DropIndex(
            name: "IX_sites_tenant_id",
            schema: "kernel",
            table: "sites");

        migrationBuilder.AddUniqueConstraint(
            name: "AK_study_sites_tenant_id_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "tenant_id", "id" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_studies_tenant_id_id",
            schema: "kernel",
            table: "studies",
            columns: new[] { "tenant_id", "id" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_sites_tenant_id_id",
            schema: "kernel",
            table: "sites",
            columns: new[] { "tenant_id", "id" });

        migrationBuilder.AddUniqueConstraint(
            name: "AK_organizations_tenant_id_id",
            schema: "kernel",
            table: "organizations",
            columns: new[] { "tenant_id", "id" });

        migrationBuilder.CreateTable(
            name: "tenant_memberships",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                person_id = table.Column<Guid>(type: "uuid", nullable: false),
                is_active = table.Column<bool>(type: "boolean", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tenant_memberships", x => x.id);
                table.UniqueConstraint("AK_tenant_memberships_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_tenant_memberships_people_person_id",
                    column: x => x.person_id,
                    principalSchema: "kernel",
                    principalTable: "people",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_tenant_memberships_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_tenant_id_site_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "tenant_id", "site_id" });

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_tenant_id_study_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "tenant_id", "study_id" });

        migrationBuilder.CreateIndex(
            name: "IX_studies_tenant_id_sponsor_id",
            schema: "kernel",
            table: "studies",
            columns: new[] { "tenant_id", "sponsor_id" });

        migrationBuilder.CreateIndex(
            name: "IX_sites_tenant_id_institution_id",
            schema: "kernel",
            table: "sites",
            columns: new[] { "tenant_id", "institution_id" });

        migrationBuilder.CreateIndex(
            name: "IX_tenant_memberships_person_id",
            schema: "kernel",
            table: "tenant_memberships",
            column: "person_id");

        migrationBuilder.CreateIndex(
            name: "IX_tenant_memberships_tenant_id_person_id",
            schema: "kernel",
            table: "tenant_memberships",
            columns: new[] { "tenant_id", "person_id" },
            unique: true);

        migrationBuilder.AddForeignKey(
            name: "FK_sites_organizations_tenant_id_institution_id",
            schema: "kernel",
            table: "sites",
            columns: new[] { "tenant_id", "institution_id" },
            principalSchema: "kernel",
            principalTable: "organizations",
            principalColumns: new[] { "tenant_id", "id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_studies_organizations_tenant_id_sponsor_id",
            schema: "kernel",
            table: "studies",
            columns: new[] { "tenant_id", "sponsor_id" },
            principalSchema: "kernel",
            principalTable: "organizations",
            principalColumns: new[] { "tenant_id", "id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_study_sites_sites_tenant_id_site_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "tenant_id", "site_id" },
            principalSchema: "kernel",
            principalTable: "sites",
            principalColumns: new[] { "tenant_id", "id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_study_sites_studies_tenant_id_study_id",
            schema: "kernel",
            table: "study_sites",
            columns: new[] { "tenant_id", "study_id" },
            principalSchema: "kernel",
            principalTable: "studies",
            principalColumns: new[] { "tenant_id", "id" },
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateTenantIsolationFunctions("kernel");
        migrationBuilder.Sql(TenantIsolationSql.EnableForTenants("kernel"));
        migrationBuilder.Sql(TenantIsolationSql.EnableForPeople("kernel"));
        migrationBuilder.EnableTenantIsolation("kernel", TenantScopedTables);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantIsolation("kernel", TenantScopedTables);
        migrationBuilder.Sql(TenantIsolationSql.Disable("kernel", "people", TenantIsolationSql.PersonVisibilityPolicy));
        migrationBuilder.Sql(TenantIsolationSql.DropDirectory("kernel"));
        migrationBuilder.Sql(TenantIsolationSql.Disable("kernel", "tenants", TenantIsolationSql.IsolationPolicy));
        migrationBuilder.DropTenantIsolationFunctions("kernel");

        migrationBuilder.DropForeignKey(
            name: "FK_sites_organizations_tenant_id_institution_id",
            schema: "kernel",
            table: "sites");

        migrationBuilder.DropForeignKey(
            name: "FK_studies_organizations_tenant_id_sponsor_id",
            schema: "kernel",
            table: "studies");

        migrationBuilder.DropForeignKey(
            name: "FK_study_sites_sites_tenant_id_site_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropForeignKey(
            name: "FK_study_sites_studies_tenant_id_study_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropTable(
            name: "tenant_memberships",
            schema: "kernel");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_study_sites_tenant_id_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropIndex(
            name: "IX_study_sites_tenant_id_site_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropIndex(
            name: "IX_study_sites_tenant_id_study_id",
            schema: "kernel",
            table: "study_sites");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_studies_tenant_id_id",
            schema: "kernel",
            table: "studies");

        migrationBuilder.DropIndex(
            name: "IX_studies_tenant_id_sponsor_id",
            schema: "kernel",
            table: "studies");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_sites_tenant_id_id",
            schema: "kernel",
            table: "sites");

        migrationBuilder.DropIndex(
            name: "IX_sites_tenant_id_institution_id",
            schema: "kernel",
            table: "sites");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_organizations_tenant_id_id",
            schema: "kernel",
            table: "organizations");

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_site_id",
            schema: "kernel",
            table: "study_sites",
            column: "site_id");

        migrationBuilder.CreateIndex(
            name: "IX_study_sites_tenant_id",
            schema: "kernel",
            table: "study_sites",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_studies_sponsor_id",
            schema: "kernel",
            table: "studies",
            column: "sponsor_id");

        migrationBuilder.CreateIndex(
            name: "IX_sites_tenant_id",
            schema: "kernel",
            table: "sites",
            column: "tenant_id");

        migrationBuilder.AddForeignKey(
            name: "FK_sites_organizations_institution_id",
            schema: "kernel",
            table: "sites",
            column: "institution_id",
            principalSchema: "kernel",
            principalTable: "organizations",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_studies_organizations_sponsor_id",
            schema: "kernel",
            table: "studies",
            column: "sponsor_id",
            principalSchema: "kernel",
            principalTable: "organizations",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_study_sites_sites_site_id",
            schema: "kernel",
            table: "study_sites",
            column: "site_id",
            principalSchema: "kernel",
            principalTable: "sites",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.AddForeignKey(
            name: "FK_study_sites_studies_study_id",
            schema: "kernel",
            table: "study_sites",
            column: "study_id",
            principalSchema: "kernel",
            principalTable: "studies",
            principalColumn: "id",
            onDelete: ReferentialAction.Restrict);
    }
}
