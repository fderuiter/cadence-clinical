using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class AddSubjectBatchImport : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "subject_batch_imports",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BatchId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyCode = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FileName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TotalRows = table.Column<int>(type: "integer", nullable: false),
                ValidRows = table.Column<int>(type: "integer", nullable: false),
                InvalidRows = table.Column<int>(type: "integer", nullable: false),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                CompletedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CommittedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_batch_imports", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "subject_batch_rows",
            columns: table => new
            {
                Id = table.Column<Guid>(type: "uuid", nullable: false),
                BatchImportId = table.Column<Guid>(type: "uuid", nullable: false),
                RowNumber = table.Column<int>(type: "integer", nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ArmId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Birthdate = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Gender = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Race = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_batch_rows", x => x.Id);
                table.ForeignKey(
                    name: "FK_subject_batch_rows_subject_batch_imports_BatchImportId",
                    column: x => x.BatchImportId,
                    principalTable: "subject_batch_imports",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_subject_batch_imports_BatchId",
            table: "subject_batch_imports",
            column: "BatchId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_subject_batch_rows_BatchImportId_RowNumber",
            table: "subject_batch_rows",
            columns: new[] { "BatchImportId", "RowNumber" },
            unique: true);
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "subject_batch_rows");

        migrationBuilder.DropTable(
            name: "subject_batch_imports");
    }
}
