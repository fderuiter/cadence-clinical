using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class EnforceUniqueBiometricLivenessTokenHash : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_biometric_liveness_verifications_LivenessTokenHash",
            table: "biometric_liveness_verifications");

        migrationBuilder.CreateIndex(
            name: "IX_biometric_liveness_verifications_LivenessTokenHash",
            table: "biometric_liveness_verifications",
            column: "LivenessTokenHash",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "IX_biometric_liveness_verifications_LivenessTokenHash",
            table: "biometric_liveness_verifications");

        migrationBuilder.CreateIndex(
            name: "IX_biometric_liveness_verifications_LivenessTokenHash",
            table: "biometric_liveness_verifications",
            column: "LivenessTokenHash");
    }
}
