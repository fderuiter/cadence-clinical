using System;

using Cadence.Kernel.Modules;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Modules.Execution.Infrastructure.Migrations;

/// <inheritdoc />
public partial class BitemporalFormQueries : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("CREATE EXTENSION IF NOT EXISTS btree_gist;");

        migrationBuilder.AddColumn<string>(
            name: "assignee",
            schema: "execution",
            table: "form_queries",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "due_at",
            schema: "execution",
            table: "form_queries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "escalated_at",
            schema: "execution",
            table: "form_queries",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "last_answered_by",
            schema: "execution",
            table: "form_queries",
            type: "character varying(256)",
            maxLength: 256,
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "opened_at",
            schema: "execution",
            table: "form_queries",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

        migrationBuilder.AddColumn<string>(
            name: "origin",
            schema: "execution",
            table: "form_queries",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Manual");

        migrationBuilder.AddColumn<string>(
            name: "owner_role",
            schema: "execution",
            table: "form_queries",
            type: "character varying(24)",
            maxLength: 24,
            nullable: false,
            defaultValue: "Site");

        migrationBuilder.AddColumn<string>(
            name: "priority",
            schema: "execution",
            table: "form_queries",
            type: "character varying(16)",
            maxLength: 16,
            nullable: false,
            defaultValue: "Normal");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "status_since",
            schema: "execution",
            table: "form_queries",
            type: "timestamp with time zone",
            nullable: false,
            defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

        migrationBuilder.AddUniqueConstraint(
            name: "AK_form_queries_tenant_id_id",
            schema: "execution",
            table: "form_queries",
            columns: new[] { "tenant_id", "id" });

        migrationBuilder.CreateTable(
            name: "form_query_aging_digests",
            schema: "execution",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_id = table.Column<Guid>(type: "uuid", nullable: false),
                study_site_id = table.Column<Guid>(type: "uuid", nullable: true),
                scope_key = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                recipient_role = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                digest_date = table.Column<DateOnly>(type: "date", nullable: false),
                generated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                study_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                site_code = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                escalated_count = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false),
                entries = table.Column<string>(type: "jsonb", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_query_aging_digests", x => x.id);
            });

        migrationBuilder.CreateTable(
            name: "form_query_messages",
            schema: "execution",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                query_id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<int>(type: "integer", nullable: false),
                kind = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                text = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                recorded_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_query_messages", x => x.id);
                table.ForeignKey(
                    name: "FK_form_query_messages_form_queries_tenant_id_query_id",
                    columns: x => new { x.tenant_id, x.query_id },
                    principalSchema: "execution",
                    principalTable: "form_queries",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "form_query_periods",
            schema: "execution",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                query_id = table.Column<Guid>(type: "uuid", nullable: false),
                status = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                owner_role = table.Column<string>(type: "character varying(24)", maxLength: 24, nullable: false),
                assignee = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                priority = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                status_since = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                due_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                escalated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                last_answered_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                kernel_query_id = table.Column<Guid>(type: "uuid", nullable: false),
                kernel_answer_id = table.Column<Guid>(type: "uuid", nullable: true),
                recorded_by = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_form_query_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_form_query_periods_form_queries_tenant_id_query_id",
                    columns: x => new { x.tenant_id, x.query_id },
                    principalSchema: "execution",
                    principalTable: "form_queries",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
            });

        // Backfill any pre-existing queries before dropping legacy jsonb messages column.
        migrationBuilder.Sql("""
            UPDATE execution.form_queries
            SET opened_at = created_at,
                status_since = created_at,
                due_at = CASE WHEN status = 'Open' THEN created_at + interval '14 days' ELSE NULL END,
                origin = CASE WHEN created_by = 'svc:execution-edit-checks' THEN 'System' ELSE 'Manual' END,
                owner_role = CASE WHEN status = 'Answered' THEN 'Monitor' ELSE 'Site' END,
                priority = 'Normal';

            INSERT INTO execution.form_query_periods (
                id, tenant_id, query_id, status, owner_role, assignee, priority, status_since, due_at, escalated_at,
                last_answered_by, kernel_query_id, kernel_answer_id, recorded_by, reason_for_change, valid_period, recorded_period
            )
            SELECT
                gen_random_uuid(),
                q.tenant_id,
                q.id,
                q.status,
                q.owner_role,
                q.assignee,
                q.priority,
                q.status_since,
                q.due_at,
                q.escalated_at,
                q.last_answered_by,
                q.kernel_query_id,
                q.kernel_answer_id,
                q.created_by,
                q.reason_for_change,
                tstzrange(q.status_since, NULL, '[)'),
                tstzrange(q.status_since, NULL, '[)')
            FROM execution.form_queries q;

            INSERT INTO execution.form_query_messages (
                id, tenant_id, query_id, sequence, kind, text, recorded_by, reason_for_change, recorded_at
            )
            SELECT
                gen_random_uuid(),
                q.tenant_id,
                q.id,
                msg.ord::int,
                COALESCE(msg.elem->>'Kind', msg.elem->>'kind', 'Raised'),
                COALESCE(msg.elem->>'Text', msg.elem->>'text', q.reason_for_change),
                COALESCE(msg.elem->>'By', msg.elem->>'by', q.created_by),
                q.reason_for_change,
                COALESCE((msg.elem->>'At')::timestamptz, (msg.elem->>'at')::timestamptz, q.created_at)
            FROM execution.form_queries q
            CROSS JOIN LATERAL jsonb_array_elements(COALESCE(q.messages, '[]'::jsonb)) WITH ORDINALITY AS msg(elem, ord);
            """);

        migrationBuilder.DropColumn(
            name: "messages",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.CreateIndex(
            name: "ix_form_queries_tenant_observation_status",
            schema: "execution",
            table: "form_queries",
            columns: new[] { "tenant_id", "observation_id", "status" });

        migrationBuilder.CreateIndex(
            name: "ux_form_query_aging_digests_study_date_scope",
            schema: "execution",
            table: "form_query_aging_digests",
            columns: new[] { "tenant_id", "study_id", "digest_date", "scope_key" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ux_form_query_messages_tenant_query_seq",
            schema: "execution",
            table: "form_query_messages",
            columns: new[] { "tenant_id", "query_id", "sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "ix_form_query_periods_tenant_query",
            schema: "execution",
            table: "form_query_periods",
            columns: new[] { "tenant_id", "query_id" });

        migrationBuilder.Sql(ModuleBitemporalSql.CreateAppendOnlyFunction(ExecutionModuleDbContext.Schema));
        migrationBuilder.Sql(ModuleBitemporalSql.CreateGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryPeriodsTable, "query_id"));

        migrationBuilder.Sql(ModuleInsertOnlySql.CreateFunction(ExecutionModuleDbContext.Schema));
        migrationBuilder.Sql(ModuleInsertOnlySql.CreateGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryMessagesTable));
        migrationBuilder.Sql(ModuleInsertOnlySql.CreateGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryAgingDigestsTable));

        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryPeriodsTable));
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryMessagesTable));
        migrationBuilder.Sql(ModuleTenantIsolationSql.EnableForTenantColumn(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryAgingDigestsTable));
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(ModuleInsertOnlySql.DropGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryAgingDigestsTable));
        migrationBuilder.Sql(ModuleInsertOnlySql.DropGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryMessagesTable));
        migrationBuilder.Sql(ModuleInsertOnlySql.DropFunction(ExecutionModuleDbContext.Schema));

        migrationBuilder.Sql(ModuleBitemporalSql.DropGuards(ExecutionModuleDbContext.Schema, ExecutionModuleDbContext.FormQueryPeriodsTable));
        migrationBuilder.Sql(ModuleBitemporalSql.DropAppendOnlyFunction(ExecutionModuleDbContext.Schema));

        migrationBuilder.DropTable(
            name: "form_query_aging_digests",
            schema: "execution");

        migrationBuilder.DropTable(
            name: "form_query_messages",
            schema: "execution");

        migrationBuilder.DropTable(
            name: "form_query_periods",
            schema: "execution");

        migrationBuilder.DropUniqueConstraint(
            name: "AK_form_queries_tenant_id_id",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropIndex(
            name: "ix_form_queries_tenant_observation_status",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "assignee",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "due_at",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "escalated_at",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "last_answered_by",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "opened_at",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "origin",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "owner_role",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "priority",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.DropColumn(
            name: "status_since",
            schema: "execution",
            table: "form_queries");

        migrationBuilder.AddColumn<string>(
            name: "messages",
            schema: "execution",
            table: "form_queries",
            type: "jsonb",
            nullable: true);
    }
}
