using System;

using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <inheritdoc />
public partial class LineageStore : Migration
{
    private static readonly string[] LineageTables = ["lineage_nodes", "lineage_edges"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "lineage_edges",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                from_kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                from_id = table.Column<Guid>(type: "uuid", nullable: false),
                from_version = table.Column<long>(type: "bigint", nullable: false),
                to_kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                to_id = table.Column<Guid>(type: "uuid", nullable: false),
                to_version = table.Column<long>(type: "bigint", nullable: false),
                kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                valid_from = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lineage_edges", x => x.id);
                table.ForeignKey(
                    name: "FK_lineage_edges_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "lineage_nodes",
            schema: "kernel",
            columns: table => new
            {
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                kind = table.Column<string>(type: "character varying(48)", maxLength: 48, nullable: false),
                node_id = table.Column<Guid>(type: "uuid", nullable: false),
                version = table.Column<long>(type: "bigint", nullable: false),
                derived = table.Column<bool>(type: "boolean", nullable: false),
                activity_id = table.Column<Guid>(type: "uuid", nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_lineage_nodes", x => new { x.tenant_id, x.node_id, x.kind, x.version });
                table.ForeignKey(
                    name: "FK_lineage_nodes_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_lineage_edges_tenant_id_from_id_from_kind_from_version_to_i~",
            schema: "kernel",
            table: "lineage_edges",
            columns: new[] { "tenant_id", "from_id", "from_kind", "from_version", "to_id", "to_kind", "to_version", "kind" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_lineage_edges_tenant_id_to_id_to_kind_to_version",
            schema: "kernel",
            table: "lineage_edges",
            columns: new[] { "tenant_id", "to_id", "to_kind", "to_version" });

        migrationBuilder.CreateIndex(
            name: "IX_lineage_nodes_tenant_id_derived",
            schema: "kernel",
            table: "lineage_nodes",
            columns: new[] { "tenant_id", "derived" });

        // ADR-2250: forced RLS on every new table. ADR-2255 / ADR-2471: lineage is evidence and never changes once
        // written.
        migrationBuilder.EnableTenantIsolation("kernel", LineageTables);
        migrationBuilder.CreateInsertOnlyGuards("kernel", LineageTables);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", LineageTables);
        migrationBuilder.DisableTenantIsolation("kernel", LineageTables);

        migrationBuilder.DropTable(
            name: "lineage_edges",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "lineage_nodes",
            schema: "kernel");
    }
}
