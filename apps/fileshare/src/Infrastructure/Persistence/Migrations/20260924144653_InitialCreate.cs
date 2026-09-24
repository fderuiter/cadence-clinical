using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Fileshare.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class InitialCreate : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS audit_log_entries (
                    ""Id"" uuid NOT NULL,
                    ""EntityName"" character varying(255) NOT NULL,
                    ""EntityId"" character varying(255) NOT NULL,
                    ""Action"" character varying(50) NOT NULL,
                    ""Timestamp"" timestamp with time zone NOT NULL,
                    ""UserId"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    ""VersionIndex"" bigint NOT NULL,
                    ""ChangesJson"" text,
                    ""PredecessorHash"" character varying(64) NOT NULL,
                    ""BlockHash"" character varying(64) NOT NULL,
                    CONSTRAINT ""PK_audit_log_entries"" PRIMARY KEY (""Id"")
                );
                CREATE INDEX IF NOT EXISTS ""IX_audit_log_entries_EntityName_Timestamp"" ON audit_log_entries (""EntityName"", ""Timestamp"");
            ");

        migrationBuilder.CreateTable(
            name: "file_records",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Filename = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                MimeType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                UploadedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UploadedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsOnHold = table.Column<bool>(type: "boolean", nullable: false),
                HoldReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ScanStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ScanEngine = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ScanCompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ScanThreatName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TranscodeStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                HlsManifestKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                TranscodeError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                PdfRenditionKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ConversionStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ConversionError = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ApprovalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignatureManifestation = table.Column<string>(type: "jsonb", nullable: true),
                ApprovedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ApprovalReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_records", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "file_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                FileRecordId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Detail = table.Column<string>(type: "jsonb", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_file_audit_logs", x => x.Id);
                table.ForeignKey(
                    name: "FK_file_audit_logs_file_records_FileRecordId",
                    column: x => x.FileRecordId,
                    principalTable: "file_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "guest_links",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                FileRecordId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TokenHmac = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                LastAccessedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AccessCount = table.Column<int>(type: "integer", nullable: false),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_guest_links", x => x.Id);
                table.ForeignKey(
                    name: "FK_guest_links_file_records_FileRecordId",
                    column: x => x.FileRecordId,
                    principalTable: "file_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "playback_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                FileRecordId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Event = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Source = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SessionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_playback_audit_logs", x => x.Id);
                table.ForeignKey(
                    name: "FK_playback_audit_logs_file_records_FileRecordId",
                    column: x => x.FileRecordId,
                    principalTable: "file_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "share_grants",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                FileRecordId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                GrantedToUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                GrantedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Scope = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PermissionLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RevokedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsDeleted = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_share_grants", x => x.Id);
                table.ForeignKey(
                    name: "FK_share_grants_file_records_FileRecordId",
                    column: x => x.FileRecordId,
                    principalTable: "file_records",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_file_audit_logs_Action",
            table: "file_audit_logs",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_file_audit_logs_FileRecordId",
            table: "file_audit_logs",
            column: "FileRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_file_audit_logs_Timestamp",
            table: "file_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_file_audit_logs_UserId",
            table: "file_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_ApprovedBy",
            table: "file_records",
            column: "ApprovedBy");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_ChecksumSha256",
            table: "file_records",
            column: "ChecksumSha256");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_ConversionStatus",
            table: "file_records",
            column: "ConversionStatus");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_ObjectKey",
            table: "file_records",
            column: "ObjectKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_file_records_ScanStatus",
            table: "file_records",
            column: "ScanStatus");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_SiteId",
            table: "file_records",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_Status",
            table: "file_records",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_StudyId",
            table: "file_records",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_TranscodeStatus",
            table: "file_records",
            column: "TranscodeStatus");

        migrationBuilder.CreateIndex(
            name: "IX_file_records_UploadedBy",
            table: "file_records",
            column: "UploadedBy");

        migrationBuilder.CreateIndex(
            name: "IX_guest_links_FileRecordId",
            table: "guest_links",
            column: "FileRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_guest_links_TokenHmac",
            table: "guest_links",
            column: "TokenHmac",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_playback_audit_logs_Event",
            table: "playback_audit_logs",
            column: "Event");

        migrationBuilder.CreateIndex(
            name: "IX_playback_audit_logs_FileRecordId",
            table: "playback_audit_logs",
            column: "FileRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_playback_audit_logs_SessionId",
            table: "playback_audit_logs",
            column: "SessionId");

        migrationBuilder.CreateIndex(
            name: "IX_playback_audit_logs_UserId",
            table: "playback_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_share_grants_FileRecordId",
            table: "share_grants",
            column: "FileRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_share_grants_GrantedToUserId",
            table: "share_grants",
            column: "GrantedToUserId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "file_audit_logs");

        migrationBuilder.DropTable(
            name: "guest_links");

        migrationBuilder.DropTable(
            name: "playback_audit_logs");

        migrationBuilder.DropTable(
            name: "share_grants");

        migrationBuilder.DropTable(
            name: "file_records");
    }
}
