using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBiometricLiveness : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "biometric_liveness_verifications",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ConsentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LivenessToken = table.Column<string>(type: "text", nullable: false),
                LivenessTokenHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AntiSpoofScore = table.Column<decimal>(type: "numeric(5,4)", nullable: false),
                IsLive = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_biometric_liveness_verifications", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_biometric_liveness_verifications_ConsentId",
            table: "biometric_liveness_verifications",
            column: "ConsentId");

        migrationBuilder.CreateIndex(
            name: "IX_biometric_liveness_verifications_LivenessTokenHash",
            table: "biometric_liveness_verifications",
            column: "LivenessTokenHash");

        migrationBuilder.CreateIndex(
            name: "IX_biometric_liveness_verifications_SubjectPseudonym",
            table: "biometric_liveness_verifications",
            column: "SubjectPseudonym");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "biometric_liveness_verifications");
    }
}
