using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSubjectActivationTokens : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subject_activation_tokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ExpiresAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IssuedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                RedeemedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RedeemedByPrincipalHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                IsRevoked = table.Column<bool>(type: "boolean", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_activation_tokens", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_subject_activation_tokens_RedeemedByPrincipalHash",
            table: "subject_activation_tokens",
            column: "RedeemedByPrincipalHash");

        migrationBuilder.CreateIndex(
            name: "IX_subject_activation_tokens_SubjectCode",
            table: "subject_activation_tokens",
            column: "SubjectCode");

        migrationBuilder.CreateIndex(
            name: "IX_subject_activation_tokens_TokenHash",
            table: "subject_activation_tokens",
            column: "TokenHash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "subject_activation_tokens");
    }
}
