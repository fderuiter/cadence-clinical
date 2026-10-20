using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddRandomizationAllocations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "TreatmentGroupCiphertext",
            table: "clinical_subjects");

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "AssignedAt",
            table: "ip_kit_inventory_items",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<Guid>(
            name: "AssignedRandomizationId",
            table: "ip_kit_inventory_items",
            type: "uuid",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "AssignedSubjectCode",
            table: "ip_kit_inventory_items",
            type: "character varying(255)",
            maxLength: 255,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "TreatmentArmCiphertext",
            table: "ip_kit_inventory_items",
            type: "text",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "randomization_schemes",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                Method = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ArmsJson = table.Column<string>(type: "jsonb", nullable: false),
                StratificationFactorsJson = table.Column<string>(type: "jsonb", nullable: false),
                BlockSizesJson = table.Column<string>(type: "jsonb", nullable: false),
                SlotsPerStratum = table.Column<int>(type: "integer", nullable: false),
                PreferredArmProbability = table.Column<double>(type: "double precision", nullable: false),
                RequiresKit = table.Column<bool>(type: "boolean", nullable: false),
                ConfigurationHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                DraftedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DraftedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ApprovedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ApprovalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                RetiredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                ListSize = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_randomization_schemes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "randomization_allocations",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SchemeId = table.Column<Guid>(type: "uuid", nullable: false),
                SchemeVersion = table.Column<int>(type: "integer", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "text", nullable: true),
                SubjectId = table.Column<Guid>(type: "uuid", nullable: false),
                StratumKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                StratificationFactorsJson = table.Column<string>(type: "jsonb", nullable: false),
                Sequence = table.Column<int>(type: "integer", nullable: false),
                ArmCiphertext = table.Column<string>(type: "text", nullable: false),
                ListSlotId = table.Column<Guid>(type: "uuid", nullable: true),
                KitNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AllocatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AllocatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_randomization_allocations", x => x.Id);
                table.ForeignKey(
                    name: "FK_randomization_allocations_clinical_subjects_SubjectId",
                    column: x => x.SubjectId,
                    principalTable: "clinical_subjects",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
                table.ForeignKey(
                    name: "FK_randomization_allocations_randomization_schemes_SchemeId",
                    column: x => x.SchemeId,
                    principalTable: "randomization_schemes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateTable(
            name: "randomization_list_slots",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SchemeId = table.Column<Guid>(type: "uuid", nullable: false),
                StratumKey = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: false),
                Sequence = table.Column<int>(type: "integer", nullable: false),
                ArmCiphertext = table.Column<string>(type: "text", nullable: false),
                AllocationId = table.Column<Guid>(type: "uuid", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_randomization_list_slots", x => x.Id);
                table.ForeignKey(
                    name: "FK_randomization_list_slots_randomization_schemes_SchemeId",
                    column: x => x.SchemeId,
                    principalTable: "randomization_schemes",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_ip_kit_inventory_items_AssignedRandomizationId",
            table: "ip_kit_inventory_items",
            column: "AssignedRandomizationId",
            unique: true,
            filter: "\"AssignedRandomizationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_randomization_allocations_SchemeId_StratumKey_Sequence",
            table: "randomization_allocations",
            columns: new[] { "SchemeId", "StratumKey", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_randomization_allocations_StudyCode_AllocatedAt",
            table: "randomization_allocations",
            columns: new[] { "StudyCode", "AllocatedAt" });

        migrationBuilder.CreateIndex(
            name: "IX_randomization_allocations_SubjectId",
            table: "randomization_allocations",
            column: "SubjectId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_randomization_list_slots_AllocationId",
            table: "randomization_list_slots",
            column: "AllocationId",
            unique: true,
            filter: "\"AllocationId\" IS NOT NULL");

        migrationBuilder.CreateIndex(
            name: "IX_randomization_list_slots_SchemeId_StratumKey_Sequence",
            table: "randomization_list_slots",
            columns: new[] { "SchemeId", "StratumKey", "Sequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_randomization_schemes_StudyCode_Approved",
            table: "randomization_schemes",
            column: "StudyCode",
            unique: true,
            filter: "\"Status\" = 'Approved'");

        migrationBuilder.CreateIndex(
            name: "IX_randomization_schemes_StudyCode_Version",
            table: "randomization_schemes",
            columns: new[] { "StudyCode", "Version" },
            unique: true);

        // Allocations are never changed or removed, and a list slot can only be consumed once (#5607): the
        // database refuses anything else, whichever role writes.
        migrationBuilder.Sql(RandomizationIntegritySql.Create);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(RandomizationIntegritySql.Drop);

        migrationBuilder.DropTable(
            name: "randomization_allocations");

        migrationBuilder.DropTable(
            name: "randomization_list_slots");

        migrationBuilder.DropTable(
            name: "randomization_schemes");

        migrationBuilder.DropIndex(
            name: "IX_ip_kit_inventory_items_AssignedRandomizationId",
            table: "ip_kit_inventory_items");

        migrationBuilder.DropColumn(
            name: "AssignedAt",
            table: "ip_kit_inventory_items");

        migrationBuilder.DropColumn(
            name: "AssignedRandomizationId",
            table: "ip_kit_inventory_items");

        migrationBuilder.DropColumn(
            name: "AssignedSubjectCode",
            table: "ip_kit_inventory_items");

        migrationBuilder.DropColumn(
            name: "TreatmentArmCiphertext",
            table: "ip_kit_inventory_items");

        migrationBuilder.AddColumn<string>(
            name: "TreatmentGroupCiphertext",
            table: "clinical_subjects",
            type: "text",
            nullable: true);
    }
}
