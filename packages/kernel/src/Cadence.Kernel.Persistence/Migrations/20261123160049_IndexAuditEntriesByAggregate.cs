using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Composite B-tree index on <c>kernel.audit_entries (tenant_id, aggregate_type, aggregate_id, sequence)</c> for
/// ordered per-aggregate audit lookups under Row-Level Security.
/// </summary>
public partial class IndexAuditEntriesByAggregate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateIndex(
            name: "IX_audit_entries_tenant_id_aggregate_type_aggregate_id_sequence",
            schema: "kernel",
            table: "audit_entries",
            columns: ["tenant_id", "aggregate_type", "aggregate_id", "sequence"]);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_audit_entries_tenant_id_aggregate_type_aggregate_id_sequence",
            schema: "kernel",
            table: "audit_entries");
    }
}
