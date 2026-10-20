using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <inheritdoc />
public partial class FormSignatureTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "consumed_signature_tokens",
            schema: "execution",
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

        migrationBuilder.CreateIndex(
            name: "IX_consumed_signature_tokens_RetainUntil",
            schema: "execution",
            table: "consumed_signature_tokens",
            column: "RetainUntil");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "consumed_signature_tokens",
            schema: "execution");
    }
}
