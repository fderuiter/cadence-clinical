using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Imaging.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class IndexAuditLogEntryChains : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_EntityName_Timestamp",
            table: "audit_log_entries");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_EntityName_PredecessorHash",
            table: "audit_log_entries",
            columns: new[] { "EntityName", "PredecessorHash" });

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_EntityName_Timestamp_VersionIndex_Id",
            table: "audit_log_entries",
            columns: new[] { "EntityName", "Timestamp", "VersionIndex", "Id" },
            descending: new[] { false, true, true, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_EntityName_PredecessorHash",
            table: "audit_log_entries");

        migrationBuilder.DropIndex(
            name: "IX_audit_log_entries_EntityName_Timestamp_VersionIndex_Id",
            table: "audit_log_entries");

        migrationBuilder.CreateIndex(
            name: "IX_audit_log_entries_EntityName_Timestamp",
            table: "audit_log_entries",
            columns: new[] { "EntityName", "Timestamp" });
    }
}
