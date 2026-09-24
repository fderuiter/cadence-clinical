using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Tickets.Infrastructure.Persistence.Migrations;

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
            name: "ticket_attachments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TicketId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FileSizeBytes = table.Column<int>(type: "integer", nullable: false),
                MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StorageUri = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UploadedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeidScrubbed = table.Column<bool>(type: "boolean", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ticket_attachments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ticket_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TicketId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                RecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ticket_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ticket_comments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TicketId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Body = table.Column<string>(type: "text", nullable: false),
                Visibility = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ticket_comments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "tickets",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reporter = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AssigneeUser = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AssigneeRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                OrgId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RelatedEntityType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                RelatedEntityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ContextPayload = table.Column<string>(type: "text", nullable: true),
                GxpSeverity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RootCauseCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                RootCauseSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ResolutionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                DueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SlaTargetAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SlaPausedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SlaTotalPausedSeconds = table.Column<int>(type: "integer", nullable: false),
                SlaBreached = table.Column<bool>(type: "boolean", nullable: false),
                SlaAmberWarned = table.Column<bool>(type: "boolean", nullable: false),
                LastEscalatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastEscalationNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EscalationCount = table.Column<int>(type: "integer", nullable: false),
                WorkflowType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ActionType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SignatureAction = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignatureToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                SignatureMeaning = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignatureTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                SignatureUser = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tickets", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ticket_attachments_TicketId",
            table: "ticket_attachments",
            column: "TicketId");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_audit_logs_CreatedAt",
            table: "ticket_audit_logs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_audit_logs_CreatedBy",
            table: "ticket_audit_logs",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_audit_logs_TicketId",
            table: "ticket_audit_logs",
            column: "TicketId");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_comments_TicketId",
            table: "ticket_comments",
            column: "TicketId");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_OrgId",
            table: "tickets",
            column: "OrgId");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_Reference",
            table: "tickets",
            column: "Reference",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tickets_SiteId",
            table: "tickets",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_tickets_StudyId",
            table: "tickets",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "ticket_attachments");

        migrationBuilder.DropTable(
            name: "ticket_audit_logs");

        migrationBuilder.DropTable(
            name: "ticket_comments");

        migrationBuilder.DropTable(
            name: "tickets");
    }
}
