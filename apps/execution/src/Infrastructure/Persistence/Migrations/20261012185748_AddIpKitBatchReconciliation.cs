using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddIpKitBatchReconciliation : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "ip_kit_inventory_items",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                KitNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LotNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ExpirationDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ResidualVolumeMl = table.Column<double>(type: "double precision", nullable: true),
                ResidualVolumePercentage = table.Column<double>(type: "double precision", nullable: true),
                DispositionReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                LastReconciledBatchId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                LastReconciledAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                LastReconciledBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ip_kit_inventory_items", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ip_kit_reconciliation_batches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BatchNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProcessedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProcessedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                TotalKits = table.Column<int>(type: "integer", nullable: false),
                QuarantinedKits = table.Column<int>(type: "integer", nullable: false),
                CertificateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Notes = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ItemsJson = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ip_kit_reconciliation_batches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ip_kit_reconciliation_certificates",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                CertificateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BatchId = table.Column<Guid>(type: "uuid", nullable: false),
                BatchNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IssuedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IssuedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                KitNumbersJson = table.Column<string>(type: "text", nullable: false),
                SummaryJson = table.Column<string>(type: "text", nullable: false),
                DigitalSignature = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ip_kit_reconciliation_certificates", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_inventory_items_StudyId_SiteId_KitNumber",
            table: "ip_kit_inventory_items",
            columns: new[] { "StudyId", "SiteId", "KitNumber" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_reconciliation_batches_BatchNumber",
            table: "ip_kit_reconciliation_batches",
            column: "BatchNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_reconciliation_certificates_CertificateId",
            table: "ip_kit_reconciliation_certificates",
            column: "CertificateId",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "ip_kit_inventory_items");

        migrationBuilder.DropTable(
            name: "ip_kit_reconciliation_batches");

        migrationBuilder.DropTable(
            name: "ip_kit_reconciliation_certificates");
    }
}
