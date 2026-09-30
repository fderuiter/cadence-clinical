using System;

using Cadence.Kernel.Persistence.Commands;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Outbox relay bookkeeping (<c>next_attempt_at</c>, <c>last_error</c>, a per-tenant index over undelivered rows) and
/// the insert-only <c>audit_chain_seals</c> table for periodic root sealing (spec §5.3, ADR-2449).
/// </summary>
public partial class OutboxRelayAndChainSeals : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "last_error",
            schema: "kernel",
            table: "outbox_messages",
            type: "character varying(2000)",
            maxLength: 2000,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "next_attempt_at",
            schema: "kernel",
            table: "outbox_messages",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "audit_chain_seals",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false),
                hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                sealed_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                sealed_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_audit_chain_seals", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_outbox_messages_tenant_id_enqueued_at",
            schema: "kernel",
            table: "outbox_messages",
            columns: new[] { "tenant_id", "enqueued_at" },
            filter: "dispatched_at IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_audit_chain_seals_tenant_id_sequence",
            schema: "kernel",
            table: "audit_chain_seals",
            columns: new[] { "tenant_id", "sequence" },
            unique: true);

        migrationBuilder.CreateInsertOnlyGuards("kernel", "audit_chain_seals");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropInsertOnlyGuards("kernel", "audit_chain_seals");

        migrationBuilder.DropTable(
            name: "audit_chain_seals",
            schema: "kernel");

        migrationBuilder.DropIndex(
            name: "IX_outbox_messages_tenant_id_enqueued_at",
            schema: "kernel",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "last_error",
            schema: "kernel",
            table: "outbox_messages");

        migrationBuilder.DropColumn(
            name: "next_attempt_at",
            schema: "kernel",
            table: "outbox_messages");
    }
}
