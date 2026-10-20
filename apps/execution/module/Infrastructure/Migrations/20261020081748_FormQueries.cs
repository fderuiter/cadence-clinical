using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <inheritdoc />
public partial class FormQueries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "form_queries",
            schema: "execution",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                form_id = table.Column<Guid>(type: "uuid", nullable: false),
                enrollment_id = table.Column<Guid>(type: "uuid", nullable: false),
                item_id = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                observation_id = table.Column<Guid>(type: "uuid", nullable: false),
                revision = table.Column<int>(type: "integer", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                kernel_query_id = table.Column<Guid>(type: "uuid", nullable: false),
                kernel_answer_id = table.Column<Guid>(type: "uuid", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false),
                messages = table.Column<string>(type: "jsonb", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_queries", x => x.id);
            });

        migrationBuilder.CreateIndex(
            name: "ix_form_queries_tenant_enrollment_status",
            schema: "execution",
            table: "form_queries",
            columns: new[] { "tenant_id", "enrollment_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ix_form_queries_tenant_form",
            schema: "execution",
            table: "form_queries",
            columns: new[] { "tenant_id", "form_id" });

        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueriesTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "form_queries",
            schema: "execution");
    }
}
