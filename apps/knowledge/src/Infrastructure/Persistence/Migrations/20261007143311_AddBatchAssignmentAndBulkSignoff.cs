using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddBatchAssignmentAndBulkSignoff : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "DelegationRecords",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                StudyId = table.Column<string>(type: "text", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                StaffUserId = table.Column<string>(type: "text", nullable: false),
                StaffName = table.Column<string>(type: "text", nullable: false),
                Role = table.Column<string>(type: "text", nullable: false),
                TaskCode = table.Column<string>(type: "text", nullable: false),
                Duties = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                SignedOff = table.Column<bool>(type: "boolean", nullable: false),
                PiSignatureHash = table.Column<string>(type: "text", nullable: true),
                PiApprovedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                BatchTransactionId = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_DelegationRecords", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "OrgAuditLogs",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                Timestamp = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<string>(type: "text", nullable: false),
                ActorRole = table.Column<string>(type: "text", nullable: false),
                Action = table.Column<string>(type: "text", nullable: false),
                RecordId = table.Column<string>(type: "text", nullable: true),
                BatchTransactionId = table.Column<string>(type: "text", nullable: true),
                Details = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_OrgAuditLogs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TrainingAssignments",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                CurriculumId = table.Column<string>(type: "text", nullable: false),
                StudyId = table.Column<string>(type: "text", nullable: false),
                SiteId = table.Column<string>(type: "text", nullable: false),
                UserId = table.Column<string>(type: "text", nullable: false),
                UserName = table.Column<string>(type: "text", nullable: false),
                UserRole = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "text", nullable: false),
                BatchTransactionId = table.Column<string>(type: "text", nullable: true),
                QuizScore = table.Column<double>(type: "double precision", nullable: true),
                TimeSpentSeconds = table.Column<int>(type: "integer", nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TrainingAssignments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TrainingCertificates",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                AssignmentId = table.Column<string>(type: "text", nullable: false),
                BatchTransactionId = table.Column<string>(type: "text", nullable: true),
                SignerName = table.Column<string>(type: "text", nullable: false),
                SignerRole = table.Column<string>(type: "text", nullable: false),
                Meaning = table.Column<string>(type: "text", nullable: false),
                Sha256Seal = table.Column<string>(type: "text", nullable: false),
                EtmfArtifactId = table.Column<string>(type: "text", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TrainingCertificates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "TrainingCurricula",
            columns: table => new
            {
                Id = table.Column<string>(type: "text", nullable: false),
                StudyId = table.Column<string>(type: "text", nullable: false),
                ProtocolVersion = table.Column<string>(type: "text", nullable: false),
                Title = table.Column<string>(type: "text", nullable: false),
                Description = table.Column<string>(type: "text", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_TrainingCurricula", x => x.Id);
            });
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "DelegationRecords");

        migrationBuilder.DropTable(
            name: "OrgAuditLogs");

        migrationBuilder.DropTable(
            name: "TrainingAssignments");

        migrationBuilder.DropTable(
            name: "TrainingCertificates");

        migrationBuilder.DropTable(
            name: "TrainingCurricula");
    }
}
