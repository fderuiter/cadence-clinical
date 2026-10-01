using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddDtpShipments : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "dtp_shipments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ShipmentId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TrackingNumber = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CourierName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectPseudonymId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DestinationAddressPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TemperatureZone = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                KitSerialsJson = table.Column<string>(type: "jsonb", nullable: false),
                TamperSealId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CurrentTempCelsius = table.Column<double>(type: "double precision", nullable: false),
                MinTempCelsius = table.Column<double>(type: "double precision", nullable: false),
                MaxTempCelsius = table.Column<double>(type: "double precision", nullable: false),
                MktCelsius = table.Column<double>(type: "double precision", nullable: false),
                ExcursionSeverity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CustodyHandoffsJson = table.Column<string>(type: "jsonb", nullable: false),
                TemperatureReadingsJson = table.Column<string>(type: "jsonb", nullable: false),
                DeliverySignatureJson = table.Column<string>(type: "jsonb", nullable: true),
                MinAllowedTemp = table.Column<double>(type: "double precision", nullable: false),
                MaxAllowedTemp = table.Column<double>(type: "double precision", nullable: false),
                ReplacementDispatchId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_dtp_shipments", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_dtp_shipments_ShipmentId",
            table: "dtp_shipments",
            column: "ShipmentId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_dtp_shipments_SiteId",
            table: "dtp_shipments",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_dtp_shipments_Status",
            table: "dtp_shipments",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_dtp_shipments_StudyId",
            table: "dtp_shipments",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_dtp_shipments_SubjectPseudonymId",
            table: "dtp_shipments",
            column: "SubjectPseudonymId");

        migrationBuilder.CreateIndex(
            name: "IX_dtp_shipments_TrackingNumber",
            table: "dtp_shipments",
            column: "TrackingNumber",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "dtp_shipments");
    }
}
