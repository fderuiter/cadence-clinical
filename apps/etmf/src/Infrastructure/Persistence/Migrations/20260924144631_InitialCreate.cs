using System;

using Microsoft.EntityFrameworkCore.Migrations;

using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Cadence.Apps.Etmf.Infrastructure.Persistence.Migrations;

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
            name: "tmf_audit_ledger_seals",
            columns: table => new
            {
                BlockIndex = table.Column<int>(type: "integer", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                PreviousBlockHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CurrentBlockHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SealedRecordCount = table.Column<int>(type: "integer", nullable: false),
                MerkleRootHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_audit_ledger_seals", x => x.BlockIndex);
            });

        migrationBuilder.CreateTable(
            name: "tmf_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                CryptographicSeal = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "text", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "tmf_documents",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IdempotencyKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Zone = table.Column<int>(type: "integer", nullable: false),
                Section = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ArtifactType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Filename = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                MimeType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TaxonomyVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ArtifactCode = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                IssueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ExpirationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DocumentOwnerId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ProtocolVersionTag = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ProtocolVersionIndex = table.Column<int>(type: "integer", nullable: true),
                ProtocolVersionStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                DocumentType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ApprovalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignatureManifestation = table.Column<string>(type: "jsonb", nullable: true),
                Signer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SigningTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsRedacted = table.Column<bool>(type: "boolean", nullable: false),
                RedactionSourceId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                RedactionManifestJson = table.Column<string>(type: "jsonb", nullable: true),
                CorrelationKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ContentChecksum = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                SourceSystem = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_documents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "tmf_expected_documents",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Milestone = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ArtifactType = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Zone = table.Column<int>(type: "integer", nullable: true),
                Section = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                MetadataJson = table.Column<string>(type: "jsonb", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_expected_documents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "tmf_document_expiration_alert_states",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                WarningWindow = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AlertedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Dispatched = table.Column<bool>(type: "boolean", nullable: false),
                NotificationId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<long>(type: "bigint", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_document_expiration_alert_states", x => x.Id);
                table.ForeignKey(
                    name: "FK_tmf_document_expiration_alert_states_tmf_documents_Document~",
                    column: x => x.DocumentId,
                    principalTable: "tmf_documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "tmf_document_qc_transitions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TransitionSequence = table.Column<int>(type: "integer", nullable: false),
                FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ActorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ActorRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_tmf_document_qc_transitions", x => x.Id);
                table.ForeignKey(
                    name: "FK_tmf_document_qc_transitions_tmf_documents_DocumentId",
                    column: x => x.DocumentId,
                    principalTable: "tmf_documents",
                    principalColumn: "Id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_tmf_audit_logs_DocumentId",
            table: "tmf_audit_logs",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_audit_logs_Timestamp",
            table: "tmf_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_audit_logs_UserId",
            table: "tmf_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_expiration_alert_states_Dispatched",
            table: "tmf_document_expiration_alert_states",
            column: "Dispatched");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_expiration_alert_states_DocumentId",
            table: "tmf_document_expiration_alert_states",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_expiration_alert_states_DocumentId_WarningWind~",
            table: "tmf_document_expiration_alert_states",
            columns: new[] { "DocumentId", "WarningWindow" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_expiration_alert_states_NotificationId",
            table: "tmf_document_expiration_alert_states",
            column: "NotificationId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_expiration_alert_states_WarningWindow",
            table: "tmf_document_expiration_alert_states",
            column: "WarningWindow");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_qc_transitions_DocumentId",
            table: "tmf_document_qc_transitions",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_qc_transitions_DocumentId_TransitionSequence",
            table: "tmf_document_qc_transitions",
            columns: new[] { "DocumentId", "TransitionSequence" },
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_tmf_document_qc_transitions_Timestamp",
            table: "tmf_document_qc_transitions",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_ArtifactCode",
            table: "tmf_documents",
            column: "ArtifactCode");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_ArtifactType",
            table: "tmf_documents",
            column: "ArtifactType");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_CorrelationKey",
            table: "tmf_documents",
            column: "CorrelationKey");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_DocumentOwnerId",
            table: "tmf_documents",
            column: "DocumentOwnerId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_DocumentType",
            table: "tmf_documents",
            column: "DocumentType");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_ExpirationDate",
            table: "tmf_documents",
            column: "ExpirationDate");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_IdempotencyKey",
            table: "tmf_documents",
            column: "IdempotencyKey");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_IssueDate",
            table: "tmf_documents",
            column: "IssueDate");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_ObjectKey",
            table: "tmf_documents",
            column: "ObjectKey");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_RedactionSourceId",
            table: "tmf_documents",
            column: "RedactionSourceId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_SiteId",
            table: "tmf_documents",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_StudyId",
            table: "tmf_documents",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_documents_Zone",
            table: "tmf_documents",
            column: "Zone");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_expected_documents_ArtifactType",
            table: "tmf_expected_documents",
            column: "ArtifactType");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_expected_documents_Milestone",
            table: "tmf_expected_documents",
            column: "Milestone");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_expected_documents_SiteId",
            table: "tmf_expected_documents",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_tmf_expected_documents_StudyId",
            table: "tmf_expected_documents",
            column: "StudyId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "tmf_audit_ledger_seals");

        migrationBuilder.DropTable(
            name: "tmf_audit_logs");

        migrationBuilder.DropTable(
            name: "tmf_document_expiration_alert_states");

        migrationBuilder.DropTable(
            name: "tmf_document_qc_transitions");

        migrationBuilder.DropTable(
            name: "tmf_expected_documents");

        migrationBuilder.DropTable(
            name: "tmf_documents");
    }
}
