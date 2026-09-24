using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Migrations;

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
            name: "comprehension_checks",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                QuestionsJson = table.Column<string>(type: "jsonb", nullable: false),
                ExpectedAnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                ThresholdPolicyJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_comprehension_checks", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "comprehension_results",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                QuestionsJson = table.Column<string>(type: "jsonb", nullable: false),
                ExpectedAnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                ThresholdPolicyJson = table.Column<string>(type: "jsonb", nullable: false),
                SubmittedAnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                Passed = table.Column<bool>(type: "boolean", nullable: false),
                Score = table.Column<double>(type: "double precision", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_comprehension_results", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ActorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ActorRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                TenantId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PreviousDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                Sha256Digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_clauses",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ClauseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Text = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_clauses", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_documents",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DocumentName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Content = table.Column<string>(type: "text", nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_documents", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_signatures",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Role = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignerName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignerEmail = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Meaning = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SignatureData = table.Column<string>(type: "text", nullable: true),
                SignedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DigestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                LarRelationship = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                LarAuthorityBasis = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_signatures", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_templates",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TemplateName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ProtocolVersion = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                IsPublished = table.Column<bool>(type: "boolean", nullable: false),
                RequiresReconsent = table.Column<bool>(type: "boolean", nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                ClausesJson = table.Column<string>(type: "jsonb", nullable: false),
                WorkflowStepsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_templates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_translations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TranslationId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SourceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SourceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SourceVersionIndex = table.Column<int>(type: "integer", nullable: false),
                LanguageCode = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                TranslatedTitle = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TranslatedText = table.Column<string>(type: "text", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_translations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "consent_withdrawals",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                WithdrawalDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonCategory = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ReasonDetail = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                Scope = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AcknowledgedByInvestigator = table.Column<bool>(type: "boolean", nullable: false),
                InvestigatorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_consent_withdrawals", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "etmf_archival_deliveries",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Attempts = table.Column<int>(type: "integer", nullable: false),
                LastError = table.Column<string>(type: "text", nullable: true),
                NextRetryAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                RetryEligible = table.Column<bool>(type: "boolean", nullable: false),
                CorrelationId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ArtifactContent = table.Column<string>(type: "text", nullable: false),
                EtmfDocumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_etmf_archival_deliveries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "granular_consent_options",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                OptionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Title = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "text", nullable: false),
                Category = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsMandatory = table.Column<bool>(type: "boolean", nullable: false),
                DefaultSelected = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_granular_consent_options", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "reconsent_requirements",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TemplateId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PriorVersionIndex = table.Column<int>(type: "integer", nullable: false),
                NewVersionIndex = table.Column<int>(type: "integer", nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ChangeSummary = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                SubstantiveChangesJson = table.Column<string>(type: "jsonb", nullable: false),
                DeadlineAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CompletedConsentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_reconsent_requirements", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "subject_consent_option_selections",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ConsentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OptionCode = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Selected = table.Column<bool>(type: "boolean", nullable: false),
                SelectedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_consent_option_selections", x => x.Id);
            });

        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS subject_consents (
                    ""Id"" character varying(36) NOT NULL,
                    ""SubjectPseudonym"" character varying(255) NOT NULL,
                    ""StudyId"" character varying(255) NOT NULL,
                    ""SiteId"" character varying(255) NOT NULL,
                    ""TemplateId"" character varying(255) NOT NULL,
                    ""VersionIndex"" integer NOT NULL,
                    ""ProtocolVersion"" character varying(255) NOT NULL,
                    ""SourceContentIdentity"" text NOT NULL,
                    ""Status"" character varying(50) NOT NULL,
                    ""ServerTimestamp"" timestamp with time zone NOT NULL,
                    ""EncryptedSignaturePayload"" text,
                    ""ComprehensionPassed"" boolean NOT NULL,
                    ""SignedAt"" timestamp with time zone,
                    ""PaperConsentOverride"" boolean NOT NULL,
                    ""StorageReference"" text,
                    ""_version"" bigint NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""CreatedBy"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    CONSTRAINT ""PK_subject_consents"" PRIMARY KEY (""Id"")
                );
            ");

        migrationBuilder.CreateIndex(
            name: "IX_comprehension_checks_TemplateId",
            table: "comprehension_checks",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_comprehension_results_SubjectPseudonym",
            table: "comprehension_results",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_comprehension_results_TemplateId",
            table: "comprehension_results",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_audit_logs_ActorId",
            table: "consent_audit_logs",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_audit_logs_DocumentId",
            table: "consent_audit_logs",
            column: "DocumentId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_audit_logs_Timestamp",
            table: "consent_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_consent_clauses_ClauseId",
            table: "consent_clauses",
            column: "ClauseId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_clauses_StudyId",
            table: "consent_clauses",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_documents_SiteId",
            table: "consent_documents",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_documents_StudyId",
            table: "consent_documents",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_signatures_SubjectPseudonym",
            table: "consent_signatures",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_consent_signatures_TemplateId",
            table: "consent_signatures",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_templates_StudyId",
            table: "consent_templates",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_templates_TemplateId",
            table: "consent_templates",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_translations_LanguageCode",
            table: "consent_translations",
            column: "LanguageCode");

        migrationBuilder.CreateIndex(
            name: "IX_consent_translations_SourceId",
            table: "consent_translations",
            column: "SourceId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_translations_TranslationId",
            table: "consent_translations",
            column: "TranslationId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_withdrawals_SiteId",
            table: "consent_withdrawals",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_withdrawals_StudyId",
            table: "consent_withdrawals",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_consent_withdrawals_SubjectPseudonym",
            table: "consent_withdrawals",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_consent_withdrawals_TemplateId",
            table: "consent_withdrawals",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_etmf_archival_deliveries_CorrelationId",
            table: "etmf_archival_deliveries",
            column: "CorrelationId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_etmf_archival_deliveries_SubjectPseudonym",
            table: "etmf_archival_deliveries",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_etmf_archival_deliveries_TemplateId",
            table: "etmf_archival_deliveries",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_granular_consent_options_OptionCode",
            table: "granular_consent_options",
            column: "OptionCode");

        migrationBuilder.CreateIndex(
            name: "IX_granular_consent_options_TemplateId",
            table: "granular_consent_options",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_reconsent_requirements_SiteId",
            table: "reconsent_requirements",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_reconsent_requirements_StudyId",
            table: "reconsent_requirements",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_reconsent_requirements_SubjectPseudonym",
            table: "reconsent_requirements",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_reconsent_requirements_TemplateId",
            table: "reconsent_requirements",
            column: "TemplateId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consent_option_selections_ConsentId",
            table: "subject_consent_option_selections",
            column: "ConsentId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consent_option_selections_OptionCode",
            table: "subject_consent_option_selections",
            column: "OptionCode");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consent_option_selections_SubjectPseudonym",
            table: "subject_consent_option_selections",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consents_SiteId",
            table: "subject_consents",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consents_StudyId",
            table: "subject_consents",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consents_SubjectPseudonym",
            table: "subject_consents",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_subject_consents_TemplateId",
            table: "subject_consents",
            column: "TemplateId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "comprehension_checks");

        migrationBuilder.DropTable(
            name: "comprehension_results");

        migrationBuilder.DropTable(
            name: "consent_audit_logs");

        migrationBuilder.DropTable(
            name: "consent_clauses");

        migrationBuilder.DropTable(
            name: "consent_documents");

        migrationBuilder.DropTable(
            name: "consent_signatures");

        migrationBuilder.DropTable(
            name: "consent_templates");

        migrationBuilder.DropTable(
            name: "consent_translations");

        migrationBuilder.DropTable(
            name: "consent_withdrawals");

        migrationBuilder.DropTable(
            name: "etmf_archival_deliveries");

        migrationBuilder.DropTable(
            name: "granular_consent_options");

        migrationBuilder.DropTable(
            name: "reconsent_requirements");

        migrationBuilder.DropTable(
            name: "subject_consent_option_selections");

        migrationBuilder.DropTable(
            name: "subject_consents");
    }
}
