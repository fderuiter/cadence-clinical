using System;

using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Converts <c>kernel.subscriptions</c> lifecycle state into append-only bitemporal <c>kernel.subscription_periods</c>
/// rows and upgrades <c>kernel.product_entitlements</c> to append-only bitemporal period rows guarded by <c>KT001</c>
/// triggers and deferred GiST exclusion constraints.
/// </summary>
public partial class BitemporalSubscriptionsAndEntitlements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subscription_periods",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                activated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                study_phase = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                study_duration_months = table.Column<int>(type: "integer", nullable: true),
                countries_csv = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                site_count = table.Column<int>(type: "integer", nullable: true),
                participant_count = table.Column<int>(type: "integer", nullable: true),
                active_study_count = table.Column<int>(type: "integer", nullable: true),
                billing_confirmation = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<int>(type: "integer", nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subscription_periods", x => x.id);
                table.ForeignKey(
                    name: "FK_subscription_periods_subscriptions_tenant_id_subscription_id",
                    columns: x => new { x.tenant_id, x.subscription_id },
                    principalSchema: "kernel",
                    principalTable: "subscriptions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_subscription_periods_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_subscription_periods_tenant_id_subscription_id",
            schema: "kernel",
            table: "subscription_periods",
            columns: new[] { "tenant_id", "subscription_id" });

        migrationBuilder.EnableTenantIsolation("kernel", "subscription_periods");

        migrationBuilder.Sql("""
            INSERT INTO kernel.subscription_periods (
                id,
                tenant_id,
                subscription_id,
                tier,
                status,
                started_at,
                trial_ends_at,
                expired_at,
                activated_at,
                study_phase,
                study_duration_months,
                countries_csv,
                site_count,
                participant_count,
                active_study_count,
                billing_confirmation,
                created_at,
                created_by,
                reason_for_change,
                version_index,
                recorded_period,
                valid_period)
            SELECT
                gen_random_uuid(),
                tenant_id,
                id,
                tier,
                status,
                started_at,
                trial_ends_at,
                expired_at,
                activated_at,
                study_phase,
                study_duration_months,
                countries_csv,
                site_count,
                participant_count,
                active_study_count,
                billing_confirmation,
                created_at,
                created_by,
                reason_for_change,
                1,
                tstzrange(started_at, NULL, '[)'),
                tstzrange(started_at, NULL, '[)')
            FROM kernel.subscriptions;
            """);

        migrationBuilder.CreateBitemporalGuards("kernel", "subscription_periods", "subscription_id");

        migrationBuilder.DropColumn(name: "activated_at", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "active_study_count", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "billing_confirmation", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "countries_csv", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "expired_at", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "participant_count", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "site_count", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "started_at", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "status", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "study_duration_months", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "study_phase", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "tier", schema: "kernel", table: "subscriptions");
        migrationBuilder.DropColumn(name: "trial_ends_at", schema: "kernel", table: "subscriptions");

        migrationBuilder.DropIndex(
            name: "IX_product_entitlements_tenant_id_family",
            schema: "kernel",
            table: "product_entitlements");

        migrationBuilder.DropIndex(
            name: "IX_product_entitlements_tenant_id_subscription_id",
            schema: "kernel",
            table: "product_entitlements");

        migrationBuilder.Sql("""
            ALTER TABLE kernel.product_entitlements
                ADD COLUMN recorded_period tstzrange,
                ADD COLUMN valid_period tstzrange;

            UPDATE kernel.product_entitlements
            SET recorded_period = tstzrange(granted_at, NULL, '[)'),
                valid_period = tstzrange(granted_at, NULL, '[)');

            ALTER TABLE kernel.product_entitlements
                ALTER COLUMN recorded_period SET NOT NULL,
                ALTER COLUMN valid_period SET NOT NULL;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_product_entitlements_tenant_id_subscription_id_family",
            schema: "kernel",
            table: "product_entitlements",
            columns: new[] { "tenant_id", "subscription_id", "family" });

        migrationBuilder.CreateBitemporalGuards("kernel", "product_entitlements", "subscription_id", "family");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropBitemporalGuards("kernel", "product_entitlements");

        migrationBuilder.DropIndex(
            name: "IX_product_entitlements_tenant_id_subscription_id_family",
            schema: "kernel",
            table: "product_entitlements");

        migrationBuilder.Sql("""
            DELETE FROM kernel.product_entitlements
            WHERE NOT upper_inf(recorded_period);

            ALTER TABLE kernel.product_entitlements
                DROP COLUMN recorded_period,
                DROP COLUMN valid_period;
            """);

        migrationBuilder.CreateIndex(
            name: "IX_product_entitlements_tenant_id_family",
            schema: "kernel",
            table: "product_entitlements",
            columns: new[] { "tenant_id", "family" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_product_entitlements_tenant_id_subscription_id",
            schema: "kernel",
            table: "product_entitlements",
            columns: new[] { "tenant_id", "subscription_id" });

        migrationBuilder.AddColumn<string>(
            name: "tier",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Starter");

        migrationBuilder.AddColumn<string>(
            name: "status",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(32)",
            maxLength: 32,
            nullable: false,
            defaultValue: "Trialing");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "started_at",
            schema: "kernel",
            table: "subscriptions",
            type: "timestamp with time zone",
            nullable: false,
            defaultValueSql: "CURRENT_TIMESTAMP");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "trial_ends_at",
            schema: "kernel",
            table: "subscriptions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "expired_at",
            schema: "kernel",
            table: "subscriptions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "activated_at",
            schema: "kernel",
            table: "subscriptions",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "study_phase",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "study_duration_months",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "countries_csv",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(1024)",
            maxLength: 1024,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "site_count",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "participant_count",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "active_study_count",
            schema: "kernel",
            table: "subscriptions",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "billing_confirmation",
            schema: "kernel",
            table: "subscriptions",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.DropBitemporalGuards("kernel", "subscription_periods");
        migrationBuilder.DisableTenantIsolation("kernel", "subscription_periods");
        migrationBuilder.DropTable(name: "subscription_periods", schema: "kernel");
    }
}
