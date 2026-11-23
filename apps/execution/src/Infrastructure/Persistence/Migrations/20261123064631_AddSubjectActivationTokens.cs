using System;

using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSubjectActivationTokens : Migration
{
    private static readonly string[] TenantTables = ["subject_activation_tokens"];

    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subject_activation_tokens",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
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
            name: "IX_subject_activation_tokens_tenant_id",
            table: "subject_activation_tokens",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_subject_activation_tokens_tenant_id_RedeemedByPrincipalHash",
            table: "subject_activation_tokens",
            columns: ["tenant_id", "RedeemedByPrincipalHash"]);

        migrationBuilder.CreateIndex(
            name: "IX_subject_activation_tokens_tenant_id_SubjectCode",
            table: "subject_activation_tokens",
            columns: ["tenant_id", "SubjectCode"]);

        migrationBuilder.CreateIndex(
            name: "IX_subject_activation_tokens_TokenHash",
            table: "subject_activation_tokens",
            column: "TokenHash",
            unique: true);

        migrationBuilder.EnableStandaloneTenantIsolation("edc", TenantTables);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableStandaloneTenantIsolation("edc", TenantTables);

        migrationBuilder.DropTable(
            name: "subject_activation_tokens");
    }
}
