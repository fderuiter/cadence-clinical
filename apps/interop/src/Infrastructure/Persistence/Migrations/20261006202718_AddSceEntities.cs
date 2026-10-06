using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSceEntities : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "sce_biostat_export_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProgramId = table.Column<string>(type: "text", nullable: true),
                SnapshotId = table.Column<string>(type: "text", nullable: true),
                IsUnblinded = table.Column<bool>(type: "boolean", nullable: false),
                LockTokenId = table.Column<string>(type: "text", nullable: true),
                DetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sce_biostat_export_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sce_database_lock_auth_tokens",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TokenId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LockId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Milestone = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IssuedTo = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AuthorizedRolesJson = table.Column<string>(type: "jsonb", nullable: false),
                IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                SignatureHash = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                RevokedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RevokedBy = table.Column<string>(type: "text", nullable: true),
                RevocationReason = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sce_database_lock_auth_tokens", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sce_execution_runs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RunId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ManifestId = table.Column<string>(type: "text", nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProgramId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProgramName = table.Column<string>(type: "text", nullable: false),
                ProgramVersion = table.Column<string>(type: "text", nullable: false),
                SnapshotId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SnapshotVersionTag = table.Column<string>(type: "text", nullable: false),
                SnapshotDigest = table.Column<string>(type: "text", nullable: false),
                Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ScriptCodeDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LockfileDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ContainerImageDigest = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ExecutionStart = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExecutionEnd = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DurationMs = table.Column<double>(type: "double precision", nullable: false),
                ExitCode = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StdoutCapture = table.Column<string>(type: "text", nullable: false),
                StderrCapture = table.Column<string>(type: "text", nullable: false),
                OutputArtifactsJson = table.Column<string>(type: "jsonb", nullable: false),
                OutputArtifactsDigest = table.Column<string>(type: "text", nullable: false),
                IsUnblindedRun = table.Column<bool>(type: "boolean", nullable: false),
                DatabaseLockTokenId = table.Column<string>(type: "text", nullable: true),
                ExecutedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ExecutedByRole = table.Column<string>(type: "text", nullable: false),
                SignatureBlockJson = table.Column<string>(type: "jsonb", nullable: false),
                ManifestSha256Seal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sce_execution_runs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sce_statistical_programs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Language = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Version = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SourceCode = table.Column<string>(type: "text", nullable: false),
                CodeHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LockfileContent = table.Column<string>(type: "text", nullable: true),
                LockType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                LockDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TargetDomain = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sce_statistical_programs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_sce_biostat_export_audit_logs_StudyId",
            table: "sce_biostat_export_audit_logs",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_biostat_export_audit_logs_Timestamp",
            table: "sce_biostat_export_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_sce_biostat_export_audit_logs_UserId",
            table: "sce_biostat_export_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_database_lock_auth_tokens_StudyId",
            table: "sce_database_lock_auth_tokens",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_database_lock_auth_tokens_TokenId",
            table: "sce_database_lock_auth_tokens",
            column: "TokenId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sce_execution_runs_ProgramId",
            table: "sce_execution_runs",
            column: "ProgramId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_execution_runs_RunId",
            table: "sce_execution_runs",
            column: "RunId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_sce_execution_runs_SnapshotId",
            table: "sce_execution_runs",
            column: "SnapshotId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_execution_runs_StudyId",
            table: "sce_execution_runs",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sce_statistical_programs_StudyId",
            table: "sce_statistical_programs",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "sce_biostat_export_audit_logs");

        migrationBuilder.DropTable(
            name: "sce_database_lock_auth_tokens");

        migrationBuilder.DropTable(
            name: "sce_execution_runs");

        migrationBuilder.DropTable(
            name: "sce_statistical_programs");
    }
}
