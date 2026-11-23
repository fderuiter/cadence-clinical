using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Imaging.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddTransactionalOutboxAndSignatureTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "consumed_signature_tokens",
            columns: table => new
            {
                TokenId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ConsumedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                RetainUntil = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consumed_signature_tokens", x => x.TokenId);
            });

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
                LastError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_transactional_outbox", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_consumed_signature_tokens_RetainUntil",
            table: "consumed_signature_tokens",
            column: "RetainUntil");

        migrationBuilder.CreateIndex(
            name: "IX_transactional_outbox_CreatedAt_DispatchedAt",
            table: "transactional_outbox",
            columns: new[] { "CreatedAt", "DispatchedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_transactional_outbox_EventId",
            table: "transactional_outbox",
            column: "EventId");

        migrationBuilder.CreateIndex(
            name: "IX_transactional_outbox_tenant_id",
            table: "transactional_outbox",
            column: "tenant_id");

        migrationBuilder.EnableStandaloneTenantIsolation("imaging", ["transactional_outbox"]);
        migrationBuilder.Sql(StandaloneTenantIsolationSql.GrantAppRoleSql("imaging"));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("imaging", ["transactional_outbox"]);

        migrationBuilder.DropTable(
            name: "consumed_signature_tokens");

        migrationBuilder.DropTable(
            name: "transactional_outbox");
    }
}
