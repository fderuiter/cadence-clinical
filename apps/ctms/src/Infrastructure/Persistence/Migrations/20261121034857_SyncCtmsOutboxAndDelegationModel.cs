using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class SyncCtmsOutboxAndDelegationModel : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "transactional_outbox",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EventId = table.Column<Guid>(type: "uuid", nullable: false),
                EventType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AggregateType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AggregateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Payload = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                DispatchedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transactional_outbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_transactional_outbox_CreatedAt_DispatchedAt",
            table: "transactional_outbox",
            columns: new[] { "CreatedAt", "DispatchedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_transactional_outbox_EventId",
            table: "transactional_outbox",
            column: "EventId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "transactional_outbox");
    }
}
