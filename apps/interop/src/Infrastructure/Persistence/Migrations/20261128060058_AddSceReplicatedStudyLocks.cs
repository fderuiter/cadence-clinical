using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSceReplicatedStudyLocks : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "CertificateFingerprint",
            table: "sce_database_lock_auth_tokens",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "CertificateId",
            table: "sce_database_lock_auth_tokens",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "LockCertificateJson",
            table: "sce_database_lock_auth_tokens",
            type: "jsonb",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "sce_replicated_study_locks",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                LockId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyRef = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ScopeKind = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LockKind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Milestone = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CertificateId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CertificateFingerprint = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                CertificateJson = table.Column<string>(type: "jsonb", nullable: true),
                AppliedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AppliedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RevocationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sce_replicated_study_locks", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sce_database_lock_auth_tokens_CertificateId",
            table: "sce_database_lock_auth_tokens",
            column: "CertificateId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_replicated_study_locks_CertificateId",
            table: "sce_replicated_study_locks",
            column: "CertificateId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_replicated_study_locks_LockId",
            table: "sce_replicated_study_locks",
            column: "LockId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_replicated_study_locks_StudyCode",
            table: "sce_replicated_study_locks",
            column: "StudyCode");

        migrationBuilder.CreateIndex(
            name: "IX_sce_replicated_study_locks_StudyRef",
            table: "sce_replicated_study_locks",
            column: "StudyRef");

        migrationBuilder.CreateIndex(
            name: "IX_sce_replicated_study_locks_tenant_id",
            table: "sce_replicated_study_locks",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("interop", ["sce_replicated_study_locks"]);
        migrationBuilder.Sql(StandaloneTenantIsolationSql.GrantAppRoleSql("interop"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("interop", ["sce_replicated_study_locks"]);

        migrationBuilder.DropTable(
            name: "sce_replicated_study_locks");

        migrationBuilder.DropIndex(
            name: "IX_sce_database_lock_auth_tokens_CertificateId",
            table: "sce_database_lock_auth_tokens");

        migrationBuilder.DropColumn(
            name: "CertificateFingerprint",
            table: "sce_database_lock_auth_tokens");

        migrationBuilder.DropColumn(
            name: "CertificateId",
            table: "sce_database_lock_auth_tokens");

        migrationBuilder.DropColumn(
            name: "LockCertificateJson",
            table: "sce_database_lock_auth_tokens");
    }
}
