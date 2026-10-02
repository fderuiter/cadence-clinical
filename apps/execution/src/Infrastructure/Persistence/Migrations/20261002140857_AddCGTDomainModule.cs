using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddCGTDomainModule : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "cgt_bedside_verifications",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Din = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BatchId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                PatientWristbandId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PrimaryVerifierId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PrimaryVerifierName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                PrimaryVerifierRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                PrimarySignatureToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                SecondaryVerifierId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SecondaryVerifierName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SecondaryVerifierRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                SecondarySignatureToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                UnlockToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                VerifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                IsMatch = table.Column<bool>(type: "boolean", nullable: false),
                Notes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cgt_bedside_verifications", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "cgt_chains_of_custody",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Din = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BatchId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CollectionSiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ManufacturingFacilityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CurrentStage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CryoTempExcursionCount = table.Column<int>(type: "integer", nullable: false),
                LowestTempCelsius = table.Column<double>(type: "double precision", nullable: true),
                UnlockToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: true),
                HarvestedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                InfusedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cgt_chains_of_custody", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "cgt_destruction_certificates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CertificateNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ChainOfCustodyId = table.Column<Guid>(type: "uuid", nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Din = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                LotNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                KitNumbersJson = table.Column<string>(type: "text", nullable: false),
                VialIdentifiersJson = table.Column<string>(type: "text", nullable: false),
                QuantityDestroyed = table.Column<int>(type: "integer", nullable: false),
                UnitOfMeasure = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ResidualVolumeMl = table.Column<double>(type: "double precision", nullable: true),
                DestructionReason = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                DestructionMethod = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DestructionFacility = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DestructionTimestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Witness1Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Witness1Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Witness1Role = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Witness1SignatureToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Witness2Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Witness2Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Witness2Role = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Witness2SignatureToken = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                CryptographicSeal = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ReconciliationNotes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cgt_destruction_certificates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "cgt_milestone_events",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ChainOfCustodyId = table.Column<Guid>(type: "uuid", nullable: false),
                Stage = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                MilestoneType = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                LocationName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FacilityId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Latitude = table.Column<double>(type: "double precision", nullable: true),
                Longitude = table.Column<double>(type: "double precision", nullable: true),
                TemperatureCelsius = table.Column<double>(type: "double precision", nullable: true),
                TemperatureStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CustodianId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CustodianName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CustodianRole = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Carrier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TrackingNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SealIntact = table.Column<bool>(type: "boolean", nullable: false),
                SealId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                DryShipperId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Notes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cgt_milestone_events", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "cgt_vial_return_reconciliations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                KitNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VialIdentifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReturnedQuantity = table.Column<int>(type: "integer", nullable: false),
                IntactVials = table.Column<int>(type: "integer", nullable: false),
                UsedVials = table.Column<int>(type: "integer", nullable: false),
                ResidualVolumeMl = table.Column<double>(type: "double precision", nullable: true),
                AdherencePercentage = table.Column<double>(type: "double precision", nullable: false),
                Disposition = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LoggedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LoggedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CertificateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Notes = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_cgt_vial_return_reconciliations", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_cgt_bedside_verifications_StudyId_SubjectId",
            table: "cgt_bedside_verifications",
            columns: new[] { "StudyId", "SubjectId" });

        migrationBuilder.CreateIndex(
            name: "IX_cgt_bedside_verifications_UnlockToken",
            table: "cgt_bedside_verifications",
            column: "UnlockToken");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_chains_of_custody_Din",
            table: "cgt_chains_of_custody",
            column: "Din");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_chains_of_custody_StudyId_SubjectId",
            table: "cgt_chains_of_custody",
            columns: new[] { "StudyId", "SubjectId" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_cgt_destruction_certificates_CertificateNumber",
            table: "cgt_destruction_certificates",
            column: "CertificateNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_cgt_destruction_certificates_StudyId",
            table: "cgt_destruction_certificates",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_milestone_events_ChainOfCustodyId",
            table: "cgt_milestone_events",
            column: "ChainOfCustodyId");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_milestone_events_StudyId",
            table: "cgt_milestone_events",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_vial_return_reconciliations_StudyId",
            table: "cgt_vial_return_reconciliations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_cgt_vial_return_reconciliations_VialIdentifier",
            table: "cgt_vial_return_reconciliations",
            column: "VialIdentifier");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "cgt_bedside_verifications");

        migrationBuilder.DropTable(
            name: "cgt_chains_of_custody");

        migrationBuilder.DropTable(
            name: "cgt_destruction_certificates");

        migrationBuilder.DropTable(
            name: "cgt_milestone_events");

        migrationBuilder.DropTable(
            name: "cgt_vial_return_reconciliations");
    }
}
