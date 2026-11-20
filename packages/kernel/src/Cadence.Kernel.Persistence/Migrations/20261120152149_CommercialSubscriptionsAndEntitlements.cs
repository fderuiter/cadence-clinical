using System;

using Cadence.Kernel.Persistence.Tenancy;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Kernel.Persistence.Migrations;

/// <summary>
/// Commercial subscriptions and product-family entitlements per tenant, isolated by PostgreSQL RLS while keeping
/// <c>kernel.tenants</c> strictly focused on isolation and data residency.
/// </summary>
public partial class CommercialSubscriptionsAndEntitlements : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subscriptions",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                tier = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                trial_ends_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                expired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subscriptions", x => x.id);
                table.UniqueConstraint("AK_subscriptions_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_subscriptions_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "product_entitlements",
            schema: "kernel",
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                subscription_id = table.Column<Guid>(type: "uuid", nullable: false),
                family = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                is_enabled = table.Column<bool>(type: "boolean", nullable: false),
                granted_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                created_by = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                reason_for_change = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                version_index = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_product_entitlements", x => x.id);
                table.UniqueConstraint("AK_product_entitlements_tenant_id_id", x => new { x.tenant_id, x.id });
                table.ForeignKey(
                    name: "FK_product_entitlements_subscriptions_tenant_id_subscription_id",
                    columns: x => new { x.tenant_id, x.subscription_id },
                    principalSchema: "kernel",
                    principalTable: "subscriptions",
                    principalColumns: new[] { "tenant_id", "id" },
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_product_entitlements_tenants_tenant_id",
                    column: x => x.tenant_id,
                    principalSchema: "kernel",
                    principalTable: "tenants",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Restrict);
            });

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

        migrationBuilder.CreateIndex(
            name: "IX_subscriptions_tenant_id",
            schema: "kernel",
            table: "subscriptions",
            column: "tenant_id",
            unique: true);

        migrationBuilder.EnableTenantIsolation("kernel", "subscriptions", "product_entitlements");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DisableTenantIsolation("kernel", "product_entitlements", "subscriptions");

        migrationBuilder.DropTable(
            name: "product_entitlements",
            schema: "kernel");

        migrationBuilder.DropTable(
            name: "subscriptions",
            schema: "kernel");
    }
}
