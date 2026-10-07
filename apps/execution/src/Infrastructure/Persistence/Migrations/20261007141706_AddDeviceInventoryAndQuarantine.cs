using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDeviceInventoryAndQuarantine : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "device_deficiencies",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                DeviceInventoryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                UdiDi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                LotNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SerialNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                DeviceName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                DeficiencyType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DeficiencyDescription = table.Column<string>(type: "character varying(4096)", maxLength: 4096, nullable: false),
                IncidentDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                AwarenessDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CouldLeadToSade = table.Column<bool>(type: "boolean", nullable: false),
                PatientInvolved = table.Column<bool>(type: "boolean", nullable: false),
                HealthImpact = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ActionTakenDevice = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DeviceReturnedToSponsor = table.Column<bool>(type: "boolean", nullable: false),
                ReturnTrackingNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                RootCauseAnalysis = table.Column<string>(type: "text", nullable: true),
                SponsorEvaluation = table.Column<string>(type: "text", nullable: true),
                QuarantineTriggered = table.Column<bool>(type: "boolean", nullable: false),
                QuarantineScope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SafetyCaseId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                SafetyClassification = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                InvestigationStatus = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_deficiencies", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "device_inventory_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DeviceIdentifier = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                BrandName = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                DeviceModel = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                DeviceType = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DeviceClassification = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                UdiDi = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                UdiPi = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                UdiCarrierFormat = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LotNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SerialNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ExpirationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ManufacturingDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                SoftwareVersion = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                IsDispensationBlocked = table.Column<bool>(type: "boolean", nullable: false),
                AllocatedSubjectId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                DispensedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                DispensedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                QuarantinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                QuarantinedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                QuarantineReason = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_inventory_items", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "device_quarantine_records",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                SiteId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                DeficiencyId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                DeviceInventoryId = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Scope = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                LotNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                SerialNumber = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                Reason = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: false),
                Status = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                QuarantinedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                QuarantinedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ReleasedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ReleasedBy = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: true),
                ReleaseJustification = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_quarantine_records", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_device_deficiencies_SafetyCaseId",
            table: "device_deficiencies",
            column: "SafetyCaseId");

        migrationBuilder.CreateIndex(
            name: "IX_device_deficiencies_StudyId_SiteId",
            table: "device_deficiencies",
            columns: new[] { "StudyId", "SiteId" });

        migrationBuilder.CreateIndex(
            name: "IX_device_inventory_items_SerialNumber",
            table: "device_inventory_items",
            column: "SerialNumber");

        migrationBuilder.CreateIndex(
            name: "IX_device_inventory_items_StudyId_SiteId",
            table: "device_inventory_items",
            columns: new[] { "StudyId", "SiteId" });

        migrationBuilder.CreateIndex(
            name: "IX_device_inventory_items_StudyId_Status",
            table: "device_inventory_items",
            columns: new[] { "StudyId", "Status" });

        migrationBuilder.CreateIndex(
            name: "IX_device_inventory_items_UdiDi_LotNumber",
            table: "device_inventory_items",
            columns: new[] { "UdiDi", "LotNumber" });

        migrationBuilder.CreateIndex(
            name: "IX_device_quarantine_records_DeviceInventoryId",
            table: "device_quarantine_records",
            column: "DeviceInventoryId");

        migrationBuilder.CreateIndex(
            name: "IX_device_quarantine_records_StudyId_Status",
            table: "device_quarantine_records",
            columns: new[] { "StudyId", "Status" });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "device_deficiencies");

        migrationBuilder.DropTable(
            name: "device_inventory_items");

        migrationBuilder.DropTable(
            name: "device_quarantine_records");
    }
}
