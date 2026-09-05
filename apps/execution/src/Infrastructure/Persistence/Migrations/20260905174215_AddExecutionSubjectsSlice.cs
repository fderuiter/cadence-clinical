using System;
using System.Collections.Generic;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddExecutionSubjectsSlice : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "AuditLogEntries",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                EntityName = table.Column<string>(type: "text", nullable: false),
                EntityId = table.Column<string>(type: "text", nullable: false),
                Action = table.Column<string>(type: "text", nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false),
                ChangesJson = table.Column<string>(type: "text", nullable: true),
                PredecessorHash = table.Column<string>(type: "text", nullable: false),
                BlockHash = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_AuditLogEntries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "clinical_subjects",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "text", nullable: true),
                TreatmentGroupCiphertext = table.Column<string>(type: "text", nullable: true),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ActiveProtocolVersion = table.Column<string>(type: "text", nullable: false),
                EncryptedDemographics = table.Column<string>(type: "text", maxLength: 2147483647, nullable: true),
                EnrollmentIndex = table.Column<int>(type: "integer", nullable: false),
                IsUnblinded = table.Column<bool>(type: "boolean", nullable: false),
                UnblindedBy = table.Column<string>(type: "text", nullable: true),
                UnblindedReason = table.Column<string>(type: "text", nullable: true),
                UnblindedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                WithdrawalReason = table.Column<string>(type: "text", nullable: true),
                WithdrawnAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RandomizationId = table.Column<Guid>(type: "uuid", nullable: true),
                KitReference = table.Column<string>(type: "text", nullable: true),
                strat_factors = table.Column<string>(type: "jsonb", nullable: false),
                _version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_clinical_subjects", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "site_compliance_caches",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Milestone = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IsComplete = table.Column<bool>(type: "boolean", nullable: false),
                Version = table.Column<int>(type: "integer", nullable: false),
                missing_documents = table.Column<List<string>>(type: "text[]", nullable: false),
                _version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 0L),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_site_compliance_caches", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "subject_consents",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                SubjectCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IcfSigned = table.Column<bool>(type: "boolean", nullable: false),
                IcfSignedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                RequiresReconsent = table.Column<bool>(type: "boolean", nullable: false),
                IsPaperOverride = table.Column<bool>(type: "boolean", nullable: false),
                _version = table.Column<long>(type: "bigint", rowVersion: true, nullable: false, defaultValue: 0L),
                ProtocolVersion_StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProtocolVersion_VersionIndex = table.Column<int>(type: "integer", nullable: false),
                ProtocolVersion_VersionTag = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_consents", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_StudyCode_EnrollmentIndex",
            table: "clinical_subjects",
            columns: new[] { "StudyCode", "EnrollmentIndex" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_clinical_subjects_SubjectCode",
            table: "clinical_subjects",
            column: "SubjectCode",
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "AuditLogEntries");

        migrationBuilder.DropTable(
            name: "clinical_subjects");

        migrationBuilder.DropTable(
            name: "site_compliance_caches");

        migrationBuilder.DropTable(
            name: "subject_consents");
    }
}
