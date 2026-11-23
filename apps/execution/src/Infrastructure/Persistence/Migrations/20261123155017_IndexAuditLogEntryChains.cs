using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class IndexAuditLogEntryChains : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_EntityName_PredecessorHash",
            table: "AuditLogEntries",
            columns: new[] { "EntityName", "PredecessorHash" });

        migrationBuilder.CreateIndex(
            name: "IX_AuditLogEntries_EntityName_Timestamp_VersionIndex_Id",
            table: "AuditLogEntries",
            columns: new[] { "EntityName", "Timestamp", "VersionIndex", "Id" },
            descending: new[] { false, true, true, true });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_AuditLogEntries_EntityName_PredecessorHash",
            table: "AuditLogEntries");

        migrationBuilder.DropIndex(
            name: "IX_AuditLogEntries_EntityName_Timestamp_VersionIndex_Id",
            table: "AuditLogEntries");
    }
}
