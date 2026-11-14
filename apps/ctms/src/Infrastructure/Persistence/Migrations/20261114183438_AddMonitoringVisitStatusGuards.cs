using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddMonitoringVisitStatusGuards : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddCheckConstraint(
            name: "ck_ctms_monitoring_visits_status",
            table: "ctms_monitoring_visits",
            sql: "\"Status\" IN ('SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'REPORT_DRAFTED', 'APPROVED', 'SIGNED_OFF', 'CANCELLED')");

        migrationBuilder.AddCheckConstraint(
            name: "ck_ctms_monitoring_visit_findings_resolution_status",
            table: "ctms_monitoring_visit_findings",
            sql: "\"ResolutionStatus\" IN ('OPEN', 'UNDER_REVIEW', 'RESOLVED', 'CLOSED')");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropCheckConstraint(
            name: "ck_ctms_monitoring_visits_status",
            table: "ctms_monitoring_visits");

        migrationBuilder.DropCheckConstraint(
            name: "ck_ctms_monitoring_visit_findings_resolution_status",
            table: "ctms_monitoring_visit_findings");
    }
}
