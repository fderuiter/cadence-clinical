using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Org.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS audit_log_entries (
                    ""Id"" uuid NOT NULL,
                    ""EntityName"" character varying(255) NOT NULL,
                    ""EntityId"" character varying(255) NOT NULL,
                    ""Action"" character varying(50) NOT NULL,
                    ""Timestamp"" timestamp with time zone NOT NULL,
                    ""UserId"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    ""VersionIndex"" bigint NOT NULL,
                    ""ChangesJson"" text,
                    ""PredecessorHash"" character varying(64) NOT NULL,
                    ""BlockHash"" character varying(64) NOT NULL,
                    CONSTRAINT ""PK_audit_log_entries"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_audit_log_entries_EntityName_Timestamp"" ON audit_log_entries (""EntityName"", ""Timestamp"");
            ");

        migrationBuilder.CreateTable(
            name: "delegations_of_authority",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegatorId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DelegateeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DutiesJson = table.Column<string>(type: "jsonb", nullable: false),
                StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                Signature = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                SignedPayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SignedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RevokedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RevocationReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delegations_of_authority", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "org_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ActorRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_org_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "org_training_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PersonnelId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TrainingTopic = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CompletionDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                SignatureManifestationJson = table.Column<string>(type: "jsonb", nullable: true),
                Signer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SigningTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_org_training_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "organizations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OrgType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_organizations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "personnel",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                KeycloakUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FirstName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                LastName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Email = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Role = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                OrganizationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_personnel", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "personnel_assignments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                PersonnelId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_personnel_assignments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sites",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OrganizationId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sites", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_delegations_of_authority_SiteId",
            table: "delegations_of_authority",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_delegations_of_authority_StudyId",
            table: "delegations_of_authority",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_org_audit_logs_ActorId",
            table: "org_audit_logs",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_org_audit_logs_RecordId",
            table: "org_audit_logs",
            column: "RecordId");

        migrationBuilder.CreateIndex(
            name: "IX_org_audit_logs_Timestamp",
            table: "org_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_org_training_logs_PersonnelId",
            table: "org_training_logs",
            column: "PersonnelId");

        migrationBuilder.CreateIndex(
            name: "IX_org_training_logs_SiteId",
            table: "org_training_logs",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_org_training_logs_StudyId",
            table: "org_training_logs",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_organizations_OrgType",
            table: "organizations",
            column: "OrgType");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_Email",
            table: "personnel",
            column: "Email");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_KeycloakUserId",
            table: "personnel",
            column: "KeycloakUserId");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_SiteId",
            table: "personnel",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_StudyId",
            table: "personnel",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_assignments_SiteId",
            table: "personnel_assignments",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_personnel_assignments_StudyId",
            table: "personnel_assignments",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sites_SiteId",
            table: "sites",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_sites_StudyId",
            table: "sites",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "delegations_of_authority");

        migrationBuilder.DropTable(
            name: "org_audit_logs");

        migrationBuilder.DropTable(
            name: "org_training_logs");

        migrationBuilder.DropTable(
            name: "organizations");

        migrationBuilder.DropTable(
            name: "personnel");

        migrationBuilder.DropTable(
            name: "personnel_assignments");

        migrationBuilder.DropTable(
            name: "sites");
    }
}
