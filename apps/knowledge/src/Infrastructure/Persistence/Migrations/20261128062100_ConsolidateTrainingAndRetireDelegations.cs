using System;

using Microsoft.EntityFrameworkCore.Migrations;

using NpgsqlTypes;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ConsolidateTrainingAndRetireDelegations : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "delegation_record_periods");

        migrationBuilder.DropTable(
            name: "DelegationRecords");

        migrationBuilder.AddColumn<string>(
            name: "ModulesJson",
            table: "TrainingCurricula",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<double>(
            name: "PassingScore",
            table: "TrainingCurricula",
            type: "double precision",
            nullable: false,
            defaultValue: 0.80000000000000004);

        migrationBuilder.AddColumn<int>(
            name: "ProtocolVersionNumber",
            table: "TrainingCurricula",
            type: "integer",
            nullable: false,
            defaultValue: 1);

        migrationBuilder.AddColumn<string>(
            name: "QualificationCode",
            table: "TrainingCurricula",
            type: "character varying(128)",
            maxLength: 128,
            nullable: false,
            defaultValue: "protocol_training");

        migrationBuilder.AddColumn<string>(
            name: "QuizQuestionsJson",
            table: "TrainingCurricula",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EtmfDocumentId",
            table: "TrainingCertificates",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "KernelArtifactVersionRef",
            table: "TrainingCertificates",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "KernelQualificationRef",
            table: "TrainingCertificates",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "EtmfDocumentId",
            table: "TrainingAssignments",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "KernelArtifactVersionRef",
            table: "TrainingAssignments",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "KernelQualificationRef",
            table: "TrainingAssignments",
            type: "character varying(128)",
            maxLength: 128,
            nullable: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(
            name: "ModulesJson",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "PassingScore",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "ProtocolVersionNumber",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "QualificationCode",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "QuizQuestionsJson",
            table: "TrainingCurricula");

        migrationBuilder.DropColumn(
            name: "EtmfDocumentId",
            table: "TrainingCertificates");

        migrationBuilder.DropColumn(
            name: "KernelArtifactVersionRef",
            table: "TrainingCertificates");

        migrationBuilder.DropColumn(
            name: "KernelQualificationRef",
            table: "TrainingCertificates");

        migrationBuilder.DropColumn(
            name: "EtmfDocumentId",
            table: "TrainingAssignments");

        migrationBuilder.DropColumn(
            name: "KernelArtifactVersionRef",
            table: "TrainingAssignments");

        migrationBuilder.DropColumn(
            name: "KernelQualificationRef",
            table: "TrainingAssignments");

        migrationBuilder.CreateTable(
            name: "DelegationRecords",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                BatchTransactionId = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                Duties = table.Column<string>(type: "text", nullable: false),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PiSignatureHash = table.Column<string>(type: "text", nullable: true),
                Role = table.Column<string>(type: "text", nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                StaffName = table.Column<string>(type: "text", nullable: false),
                StaffUserId = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                StudyId = table.Column<string>(type: "text", nullable: false),
                TaskCode = table.Column<string>(type: "text", nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DelegationRecords", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "delegation_record_periods",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                BatchTransactionId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DelegationRecordId = table.Column<string>(type: "text", nullable: false),
                Duties = table.Column<string>(type: "text", nullable: false),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                PiSignatureHash = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                recorded_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                Role = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StaffUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TaskCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                tenant_id = table.Column<Guid>(type: "uuid", nullable: false),
                valid_period = table.Column<NpgsqlRange<DateTimeOffset>>(type: "tstzrange", nullable: false),
                _version = table.Column<long>(type: "bigint", nullable: false, defaultValue: 0L)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_delegation_record_periods", x => x.Id);
                table.ForeignKey(
                    name: "FK_delegation_record_periods_DelegationRecords_DelegationRecor~",
                    column: x => x.DelegationRecordId,
                    principalTable: "DelegationRecords",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Restrict);
            });

        migrationBuilder.CreateIndex(
            name: "IX_delegation_record_periods_DelegationRecordId",
            table: "delegation_record_periods",
            column: "DelegationRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_delegation_record_periods_StudyCode_SiteCode",
            table: "delegation_record_periods",
            columns: new[] { "StudyCode", "SiteCode" });

        migrationBuilder.CreateIndex(
            name: "IX_delegation_record_periods_tenant_id",
            table: "delegation_record_periods",
            column: "tenant_id");

        migrationBuilder.CreateIndex(
            name: "IX_DelegationRecords_tenant_id",
            table: "DelegationRecords",
            column: "tenant_id");
    }
}
