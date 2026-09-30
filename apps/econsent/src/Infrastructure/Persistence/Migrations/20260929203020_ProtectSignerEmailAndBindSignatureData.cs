using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <summary>
/// #4770: consent signatures stop storing the raw signature capture and the signer e-mail in plaintext.
/// <para>
/// Existing rows: <c>SignatureData</c> is transformed into <c>SignatureBindingSha256</c> with exactly the expression
/// <see cref="Domain.ConsentSignatureBinding.Compute"/> uses (SHA-256 over
/// <c>'cadence.econsent.signature-binding.v1' \n DigestSha256 \n SignatureData</c>), so a retained copy of a capture can
/// still be verified; the raw capture is then blanked and dropped. <c>SignerEmail</c> is widened for ciphertext and
/// existing plaintext values are cleared: they cannot be encrypted in SQL without embedding the key, and reading a
/// plaintext value through the encrypting converter would fail. The e-mail is contact data, not part of the Part 11
/// signature manifest (printed name, date/time and meaning are kept). No code path wrote these columns before this
/// change (the service exposed only health endpoints). After applying, run <c>VACUUM FULL consent_signatures</c>
/// to purge dead row versions; see the ADR.
/// </para>
/// @req:PRD-SYS-053
/// </summary>
public partial class ProtectSignerEmailAndBindSignatureData : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "SignatureBindingSha256",
            table: "consent_signatures",
            type: "character varying(64)",
            maxLength: 64,
            nullable: true);

        // Transform: bind each existing capture to the signed document digest (built-in sha256(), PostgreSQL 11+).
        migrationBuilder.Sql(
            """
            UPDATE consent_signatures
            SET "SignatureBindingSha256" = encode(sha256(convert_to(
                'cadence.econsent.signature-binding.v1' || chr(10) || coalesce("DigestSha256", '') || chr(10) || "SignatureData",
                'UTF8')), 'hex')
            WHERE "SignatureData" IS NOT NULL;
            """);

        // Clear: blank plaintext captures and e-mail addresses before the column changes.
        migrationBuilder.Sql(
            """
            UPDATE consent_signatures
            SET "SignatureData" = NULL, "SignerEmail" = NULL
            WHERE "SignatureData" IS NOT NULL OR "SignerEmail" IS NOT NULL;
            """);

        migrationBuilder.DropColumn(
            name: "SignatureData",
            table: "consent_signatures");

        migrationBuilder.AlterColumn<string>(
            name: "SignerEmail",
            table: "consent_signatures",
            type: "character varying(1424)",
            maxLength: 1424,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(255)",
            oldMaxLength: 255,
            oldNullable: true);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Restores the old columns empty: raw captures are not recoverable by design, and encrypted e-mail values would
    /// not fit the old column, so they are cleared.
    /// </remarks>
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "SignatureBindingSha256",
            table: "consent_signatures");

        migrationBuilder.Sql("""UPDATE consent_signatures SET "SignerEmail" = NULL WHERE "SignerEmail" IS NOT NULL;""");

        migrationBuilder.AlterColumn<string>(
            name: "SignerEmail",
            table: "consent_signatures",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true,
            oldClrType: typeof(string),
            oldType: "character varying(1424)",
            oldMaxLength: 1424,
            oldNullable: true);

        migrationBuilder.AddColumn<string>(
            name: "SignatureData",
            table: "consent_signatures",
            type: "text",
            nullable: true);
    }
}
