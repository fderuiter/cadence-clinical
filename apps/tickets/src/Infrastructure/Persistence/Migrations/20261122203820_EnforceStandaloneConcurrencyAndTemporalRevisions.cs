using System;

using Cadence.Database.Temporal;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Tickets.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceStandaloneConcurrencyAndTemporalRevisions : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ticket_revisions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TicketId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RevisionNumber = table.Column<int>(type: "integer", nullable: false),
                Reference = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Priority = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                GxpSeverity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AssigneeUser = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AssigneeRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                RootCauseCategory = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                RootCauseSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ResolutionCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                SlaTargetAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ContextPayload = table.Column<string>(type: "text", nullable: true),
                RecordedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RecordedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ticket_revisions", x => x.Id);
                table.ForeignKey(
                    name: "FK_ticket_revisions_tickets_TicketId",
                    column: x => x.TicketId,
                    principalTable: "tickets",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ticket_revisions_tenant_id",
            table: "ticket_revisions",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_revisions_TicketId",
            table: "ticket_revisions",
            column: "TicketId");

        migrationBuilder.CreateIndex(
            name: "IX_ticket_revisions_TicketId_RevisionNumber",
            table: "ticket_revisions",
            columns: new[] { "TicketId", "RevisionNumber" },
            unique: true);

        migrationBuilder.EnableStandaloneTenantIsolation("tickets", ["ticket_revisions"]);
        migrationBuilder.EnableStandaloneInsertOnlyGuards("tickets", ["audit_log_entries", "ticket_audit_logs", "ticket_revisions"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("tickets", ["ticket_revisions"]);
        migrationBuilder.DisableStandaloneInsertOnlyGuards("tickets", ["audit_log_entries", "ticket_audit_logs", "ticket_revisions"]);

        migrationBuilder.DropTable(
            name: "ticket_revisions");
    }
}
