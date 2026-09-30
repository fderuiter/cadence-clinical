using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <summary>
/// #4770: participant wallets stop storing payment secrets. <c>VirtualCardToken</c> and <c>AchRoutingNumber</c> are
/// replaced by encrypted processor references (<c>VirtualCardProcessorReference</c>, <c>AchProcessorReference</c>)
/// and a routing-number mask (<c>AchRoutingMask</c>).
/// <para>
/// Existing rows: the routing number is transformed into its mask (last four digits); both plaintext columns are
/// then blanked (so live row versions no longer carry them) and dropped. Stored card tokens and routing numbers are
/// not carried into the new encrypted columns because the key is not available to SQL migrations and must never be
/// embedded in one; the processor remains the system of record, so a wallet's references are re-linked from it.
/// No code path wrote these columns before this change (the service exposed only health endpoints). After applying,
/// run <c>VACUUM FULL payments_participant_wallets</c> to purge dead row versions; see the ADR.
/// </para>
/// @req:PRD-SYS-053
/// </summary>
public partial class StoreProcessorReferencesInsteadOfPaymentSecrets : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "AchProcessorReference",
            table: "payments_participant_wallets",
            type: "character varying(1424)",
            maxLength: 1424,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AchRoutingMask",
            table: "payments_participant_wallets",
            type: "character varying(20)",
            maxLength: 20,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VirtualCardProcessorReference",
            table: "payments_participant_wallets",
            type: "character varying(1424)",
            maxLength: 1424,
            nullable: true);

        // Transform: keep only a display mask of the routing number.
        migrationBuilder.Sql(
            """
            UPDATE payments_participant_wallets
            SET "AchRoutingMask" = '*****' || right(regexp_replace("AchRoutingNumber", '[^0-9]', '', 'g'), 4)
            WHERE "AchRoutingNumber" IS NOT NULL
              AND length(regexp_replace("AchRoutingNumber", '[^0-9]', '', 'g')) >= 4;
            """);

        // Clear: blank the plaintext before dropping it, so no live row version still holds it.
        migrationBuilder.Sql(
            """
            UPDATE payments_participant_wallets
            SET "AchRoutingNumber" = NULL, "VirtualCardToken" = NULL
            WHERE "AchRoutingNumber" IS NOT NULL OR "VirtualCardToken" IS NOT NULL;
            """);

        migrationBuilder.DropColumn(
            name: "AchRoutingNumber",
            table: "payments_participant_wallets");

        migrationBuilder.DropColumn(
            name: "VirtualCardToken",
            table: "payments_participant_wallets");
    }

    /// <inheritdoc />
    /// <remarks>Restores the old columns empty: the cleared secrets are not recoverable by design.</remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "AchProcessorReference",
            table: "payments_participant_wallets");

        migrationBuilder.DropColumn(
            name: "AchRoutingMask",
            table: "payments_participant_wallets");

        migrationBuilder.DropColumn(
            name: "VirtualCardProcessorReference",
            table: "payments_participant_wallets");

        migrationBuilder.AddColumn<string>(
            name: "AchRoutingNumber",
            table: "payments_participant_wallets",
            type: "character varying(50)",
            maxLength: 50,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "VirtualCardToken",
            table: "payments_participant_wallets",
            type: "character varying(100)",
            maxLength: 100,
            nullable: true);
    }
}
