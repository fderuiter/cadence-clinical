using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddEconsentReplicatedScopeLocks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "replicated_scope_locks",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                LockId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ScopeKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScopeRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LockKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Milestone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AppliedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReleasedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReleaseReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_replicated_scope_locks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_IsActive",
            table: "replicated_scope_locks",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_LockId",
            table: "replicated_scope_locks",
            column: "LockId");

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_SiteCode",
            table: "replicated_scope_locks",
            column: "SiteCode");

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_StudyCode",
            table: "replicated_scope_locks",
            column: "StudyCode");

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_StudyRef",
            table: "replicated_scope_locks",
            column: "StudyRef");

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_SubjectCode",
            table: "replicated_scope_locks",
            column: "SubjectCode");

        migrationBuilder.CreateIndex(
            name: "IX_replicated_scope_locks_tenant_id",
            table: "replicated_scope_locks",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("econsent", ["replicated_scope_locks"]);
        migrationBuilder.Sql(StandaloneTenantIsolationSql.GrantAppRoleSql("econsent"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("econsent", ["replicated_scope_locks"]);

        migrationBuilder.DropTable(
            name: "replicated_scope_locks");
    }
}
