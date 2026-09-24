using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

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
            name: "ctms_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_clinical_queries",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Explanation = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_clinical_queries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_cra_allocations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CraId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                EffectiveStartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EffectiveEndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_cra_allocations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_defeated_monitoring_visits",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                VisitId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ActualDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Findings = table.Column<string>(type: "text", nullable: true),
                DeviceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OfflineSyncMarkers = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_defeated_monitoring_visits", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_delegations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCodes = table.Column<string>(type: "jsonb", nullable: false),
                StartDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                EndDate = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_delegations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_generated_letters",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                VisitId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                LetterType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RenderedContent = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_generated_letters", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_investigator_grants",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TotalBudget = table.Column<double>(type: "double precision", nullable: false),
                Currency = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_investigator_grants", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_monitoring_visit_findings",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                VisitId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Text = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ResolutionStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                OfflineSyncMarkers = table.Column<string>(type: "jsonb", nullable: true),
                SyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_monitoring_visit_findings", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_monitoring_visits",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CraId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VisitType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ScheduledDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActualDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignatureManifestation = table.Column<string>(type: "jsonb", nullable: true),
                Signer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SigningTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                OfflineSyncMarkers = table.Column<string>(type: "jsonb", nullable: true),
                SyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_monitoring_visits", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_recruitment_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ScreenedCount = table.Column<int>(type: "integer", nullable: false),
                EnrolledCount = table.Column<int>(type: "integer", nullable: false),
                TargetCount = table.Column<int>(type: "integer", nullable: false),
                AsOfDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_recruitment_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_regulatory_forms",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FormType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RenderedContent = table.Column<string>(type: "character varying(100000)", maxLength: 100000, nullable: false),
                ApprovalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignatureManifestation = table.Column<string>(type: "jsonb", nullable: true),
                Signer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SigningTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_regulatory_forms", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_site_milestones",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                MilestoneType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                PlannedDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ActualDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_site_milestones", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ctms_studies",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_studies", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "site_staff_members",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                FirstName = table.Column<string>(type: "text", nullable: false),
                LastName = table.Column<string>(type: "text", nullable: false),
                Email = table.Column<string>(type: "text", nullable: false),
                PrimaryRole = table.Column<string>(type: "text", nullable: false),
                LicenseNumber = table.Column<string>(type: "text", nullable: true),
                GcpCertified = table.Column<bool>(type: "boolean", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_site_staff_members", x => x.Id);
                table.UniqueConstraint("AK_site_staff_members_UserId", x => x.UserId);
            });

        migrationBuilder.CreateTable(
            name: "ctms_budget_line_items",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                GrantId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Category = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Description = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Amount = table.Column<double>(type: "double precision", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_budget_line_items", x => x.Id);
                table.ForeignKey(
                    name: "FK_ctms_budget_line_items_ctms_investigator_grants_GrantId",
                    column: x => x.GrantId,
                    principalTable: "ctms_investigator_grants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ctms_payment_milestones",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                GrantId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                MilestoneName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TriggerCondition = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Amount = table.Column<double>(type: "double precision", nullable: false),
                IsTriggered = table.Column<bool>(type: "boolean", nullable: false),
                TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_payment_milestones", x => x.Id);
                table.ForeignKey(
                    name: "FK_ctms_payment_milestones_ctms_investigator_grants_GrantId",
                    column: x => x.GrantId,
                    principalTable: "ctms_investigator_grants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "doa_delegation_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                StaffUserId = table.Column<string>(type: "text", nullable: false),
                TaskCode = table.Column<string>(type: "text", nullable: false),
                StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                Status = table.Column<string>(type: "text", nullable: false),
                PiSignatureHash = table.Column<string>(type: "text", nullable: true),
                PiApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_doa_delegation_records", x => x.Id);
                table.ForeignKey(
                    name: "FK_doa_delegation_records_site_staff_members_StaffUserId",
                    column: x => x.StaffUserId,
                    principalTable: "site_staff_members",
                    principalColumn: "UserId",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "ctms_investigator_payables",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                GrantId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                MilestoneId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Amount = table.Column<double>(type: "double precision", nullable: false),
                PaymentStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                PaidAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ctms_investigator_payables", x => x.Id);
                table.ForeignKey(
                    name: "FK_ctms_investigator_payables_ctms_investigator_grants_GrantId",
                    column: x => x.GrantId,
                    principalTable: "ctms_investigator_grants",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
                table.ForeignKey(
                    name: "FK_ctms_investigator_payables_ctms_payment_milestones_Mileston~",
                    column: x => x.MilestoneId,
                    principalTable: "ctms_payment_milestones",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.SetNull);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ctms_audit_logs_Timestamp",
            table: "ctms_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_audit_logs_UserId",
            table: "ctms_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_budget_line_items_GrantId",
            table: "ctms_budget_line_items",
            column: "GrantId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_clinical_queries_SiteId",
            table: "ctms_clinical_queries",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_clinical_queries_StudyId",
            table: "ctms_clinical_queries",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_clinical_queries_VisitId",
            table: "ctms_clinical_queries",
            column: "VisitId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_cra_allocations_CraId",
            table: "ctms_cra_allocations",
            column: "CraId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_cra_allocations_SiteId",
            table: "ctms_cra_allocations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_cra_allocations_StudyId",
            table: "ctms_cra_allocations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_defeated_monitoring_visits_VisitId",
            table: "ctms_defeated_monitoring_visits",
            column: "VisitId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_SiteId",
            table: "ctms_delegations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_delegations_StaffUserId",
            table: "ctms_delegations",
            column: "StaffUserId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_generated_letters_VisitId",
            table: "ctms_generated_letters",
            column: "VisitId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_investigator_grants_SiteId",
            table: "ctms_investigator_grants",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_investigator_grants_StudyId",
            table: "ctms_investigator_grants",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_investigator_payables_GrantId",
            table: "ctms_investigator_payables",
            column: "GrantId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_investigator_payables_MilestoneId",
            table: "ctms_investigator_payables",
            column: "MilestoneId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_monitoring_visit_findings_VisitId",
            table: "ctms_monitoring_visit_findings",
            column: "VisitId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_monitoring_visits_SiteId",
            table: "ctms_monitoring_visits",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_monitoring_visits_StudyId",
            table: "ctms_monitoring_visits",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_payment_milestones_GrantId",
            table: "ctms_payment_milestones",
            column: "GrantId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_recruitment_records_SiteId",
            table: "ctms_recruitment_records",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_recruitment_records_StudyId",
            table: "ctms_recruitment_records",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_regulatory_forms_SiteId",
            table: "ctms_regulatory_forms",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_regulatory_forms_StudyId",
            table: "ctms_regulatory_forms",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_site_milestones_MilestoneType",
            table: "ctms_site_milestones",
            column: "MilestoneType");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_site_milestones_SiteId",
            table: "ctms_site_milestones",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_site_milestones_StudyId",
            table: "ctms_site_milestones",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ctms_studies_StudyId",
            table: "ctms_studies",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_SiteId",
            table: "doa_delegation_records",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_StaffUserId",
            table: "doa_delegation_records",
            column: "StaffUserId");

        migrationBuilder.CreateIndex(
            name: "IX_doa_delegation_records_TaskCode",
            table: "doa_delegation_records",
            column: "TaskCode");

        migrationBuilder.CreateIndex(
            name: "IX_site_staff_members_SiteId",
            table: "site_staff_members",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_site_staff_members_UserId",
            table: "site_staff_members",
            column: "UserId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "ctms_audit_logs");

        migrationBuilder.DropTable(
            name: "ctms_budget_line_items");

        migrationBuilder.DropTable(
            name: "ctms_clinical_queries");

        migrationBuilder.DropTable(
            name: "ctms_cra_allocations");

        migrationBuilder.DropTable(
            name: "ctms_defeated_monitoring_visits");

        migrationBuilder.DropTable(
            name: "ctms_delegations");

        migrationBuilder.DropTable(
            name: "ctms_generated_letters");

        migrationBuilder.DropTable(
            name: "ctms_investigator_payables");

        migrationBuilder.DropTable(
            name: "ctms_monitoring_visit_findings");

        migrationBuilder.DropTable(
            name: "ctms_monitoring_visits");

        migrationBuilder.DropTable(
            name: "ctms_recruitment_records");

        migrationBuilder.DropTable(
            name: "ctms_regulatory_forms");

        migrationBuilder.DropTable(
            name: "ctms_site_milestones");

        migrationBuilder.DropTable(
            name: "ctms_studies");

        migrationBuilder.DropTable(
            name: "doa_delegation_records");

        migrationBuilder.DropTable(
            name: "ctms_payment_milestones");

        migrationBuilder.DropTable(
            name: "site_staff_members");

        migrationBuilder.DropTable(
            name: "ctms_investigator_grants");
    }
}
