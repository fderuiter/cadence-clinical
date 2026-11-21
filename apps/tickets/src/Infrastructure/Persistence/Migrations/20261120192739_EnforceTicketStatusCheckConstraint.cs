using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Tickets.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceTicketStatusCheckConstraint : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "ck_tickets_status",
            table: "tickets",
            sql: "\"Status\" IN ('OPEN', 'IN_PROGRESS', 'WAITING_ON_SITE', 'WAITING_ON_SPONSOR', 'PENDING_REGULATORY_REVIEW', 'RESOLVED', 'CLOSED', 'REOPENED', 'CANCELLED')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_tickets_status",
            table: "tickets");
    }
}
