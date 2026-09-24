using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Imaging.Infrastructure.Persistence.Migrations;

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
            name: "imaging_adjudication_sessions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TimepointName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AssessmentCriteria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reader1Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Reader1Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Reader1Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reader1AssessmentJson = table.Column<string>(type: "jsonb", nullable: true),
                Reader2Id = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Reader2Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Reader2Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Reader2AssessmentJson = table.Column<string>(type: "jsonb", nullable: true),
                AdjudicatorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AdjudicatorName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AdjudicatorAssessmentJson = table.Column<string>(type: "jsonb", nullable: true),
                SldDiscordanceDelta = table.Column<double>(type: "double precision", nullable: false),
                IsResponseDiscordant = table.Column<bool>(type: "boolean", nullable: false),
                DiscordanceReasonsJson = table.Column<string>(type: "jsonb", nullable: false),
                WorkflowStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                FinalConsensusResponse = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                FinalConsensusSldMm = table.Column<double>(type: "double precision", nullable: true),
                BindingReader = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AdjudicationRationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ElectronicSignatureJson = table.Column<string>(type: "jsonb", nullable: true),
                LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SessionDigest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TenantId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_adjudication_sessions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ActorId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                DetailsJson = table.Column<string>(type: "jsonb", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_caliper_annotations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                SeriesEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                SopInstanceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TimepointName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                LesionId = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                LesionType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                OrganSite = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                MeasurementCriteria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                MeasurementType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                LongestDiameterMm = table.Column<double>(type: "double precision", nullable: false),
                PerpendicularDiameterMm = table.Column<double>(type: "double precision", nullable: true),
                ProductPerpendicularMm2 = table.Column<double>(type: "double precision", nullable: true),
                VolumeMm3 = table.Column<double>(type: "double precision", nullable: true),
                SliceIndex = table.Column<int>(type: "integer", nullable: true),
                SliceLocation = table.Column<double>(type: "double precision", nullable: true),
                DeauvilleScore = table.Column<int>(type: "integer", nullable: true),
                PointsJson = table.Column<string>(type: "jsonb", nullable: false),
                HounsfieldStatsJson = table.Column<string>(type: "jsonb", nullable: true),
                ReaderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Notes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_caliper_annotations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_deid_certificates",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                SourceSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                TargetSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                OriginalStudyInstanceUidHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                OriginalSopInstanceUidHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AnonymizedStudyInstanceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                AnonymizedSopInstanceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ScrubbedTagsCount = table.Column<int>(type: "integer", nullable: false),
                ScrubbedTagsJson = table.Column<string>(type: "jsonb", nullable: false),
                PixelMaskApplied = table.Column<bool>(type: "boolean", nullable: false),
                PixelMasksJson = table.Column<string>(type: "jsonb", nullable: false),
                OperatorIdentity = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Timestamp = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Signature = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_deid_certificates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_instance_envelopes",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SeriesEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SopInstanceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SopClassUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                InstanceNumber = table.Column<int>(type: "integer", nullable: true),
                Rows = table.Column<int>(type: "integer", nullable: false),
                Columns = table.Column<int>(type: "integer", nullable: false),
                BitsAllocated = table.Column<int>(type: "integer", nullable: false),
                PixelSpacingJson = table.Column<string>(type: "jsonb", nullable: true),
                SliceThickness = table.Column<double>(type: "double precision", nullable: true),
                SliceLocation = table.Column<double>(type: "double precision", nullable: true),
                WindowCenter = table.Column<double>(type: "double precision", nullable: true),
                WindowWidth = table.Column<double>(type: "double precision", nullable: true),
                ObjectKey = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ChecksumSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SizeBytes = table.Column<long>(type: "bigint", nullable: false),
                BurnedInPhiMasked = table.Column<bool>(type: "boolean", nullable: false),
                CertificateId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_instance_envelopes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_series_envelopes",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyEnvelopeId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SeriesInstanceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                SeriesNumber = table.Column<int>(type: "integer", nullable: true),
                Modality = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: false),
                BodyPartExamined = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                SeriesDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                InstanceCount = table.Column<int>(type: "integer", nullable: false),
                ProtocolName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FrameOfReferenceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_series_envelopes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_study_envelopes",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyInstanceUid = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ModalitiesJson = table.Column<string>(type: "jsonb", nullable: false),
                StudyDate = table.Column<string>(type: "character varying(8)", maxLength: 8, nullable: true),
                StudyDescription = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                SeriesCount = table.Column<int>(type: "integer", nullable: false),
                InstanceCount = table.Column<int>(type: "integer", nullable: false),
                StoragePrefix = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                AdjudicationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TenantId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_study_envelopes", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "imaging_tumor_response_assessments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TimepointName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AssessmentCriteria = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                BaselineSldMm = table.Column<double>(type: "double precision", nullable: true),
                NadirSldMm = table.Column<double>(type: "double precision", nullable: true),
                CurrentSldMm = table.Column<double>(type: "double precision", nullable: true),
                PctChangeBaseline = table.Column<double>(type: "double precision", nullable: true),
                PctChangeNadir = table.Column<double>(type: "double precision", nullable: true),
                AbsoluteChangeNadirMm = table.Column<double>(type: "double precision", nullable: true),
                BaselineSpdMm2 = table.Column<double>(type: "double precision", nullable: true),
                NadirSpdMm2 = table.Column<double>(type: "double precision", nullable: true),
                CurrentSpdMm2 = table.Column<double>(type: "double precision", nullable: true),
                PctChangeSpdBaseline = table.Column<double>(type: "double precision", nullable: true),
                PctChangeSpdNadir = table.Column<double>(type: "double precision", nullable: true),
                TargetResponse = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                NonTargetStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                NewLesionsPresent = table.Column<bool>(type: "boolean", nullable: false),
                NewLesionsCount = table.Column<int>(type: "integer", nullable: false),
                MaxDeauvilleScore = table.Column<int>(type: "integer", nullable: true),
                OverallResponse = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsLocked = table.Column<bool>(type: "boolean", nullable: false),
                LockedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LockedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReaderId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ManifestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Signature = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_imaging_tumor_response_assessments", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_AdjudicatorId",
            table: "imaging_adjudication_sessions",
            column: "AdjudicatorId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_Reader1Id",
            table: "imaging_adjudication_sessions",
            column: "Reader1Id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_Reader2Id",
            table: "imaging_adjudication_sessions",
            column: "Reader2Id");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_SiteId",
            table: "imaging_adjudication_sessions",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_StudyEnvelopeId",
            table: "imaging_adjudication_sessions",
            column: "StudyEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_StudyId",
            table: "imaging_adjudication_sessions",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_SubjectId",
            table: "imaging_adjudication_sessions",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_TenantId",
            table: "imaging_adjudication_sessions",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_TimepointName",
            table: "imaging_adjudication_sessions",
            column: "TimepointName");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_adjudication_sessions_WorkflowStatus",
            table: "imaging_adjudication_sessions",
            column: "WorkflowStatus");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_audit_logs_Action",
            table: "imaging_audit_logs",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_audit_logs_ActorId",
            table: "imaging_audit_logs",
            column: "ActorId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_audit_logs_StudyEnvelopeId",
            table: "imaging_audit_logs",
            column: "StudyEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_LesionId",
            table: "imaging_caliper_annotations",
            column: "LesionId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_ReaderId",
            table: "imaging_caliper_annotations",
            column: "ReaderId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_SeriesEnvelopeId",
            table: "imaging_caliper_annotations",
            column: "SeriesEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_SiteId",
            table: "imaging_caliper_annotations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_SopInstanceUid",
            table: "imaging_caliper_annotations",
            column: "SopInstanceUid");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_StudyEnvelopeId",
            table: "imaging_caliper_annotations",
            column: "StudyEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_StudyId",
            table: "imaging_caliper_annotations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_SubjectId",
            table: "imaging_caliper_annotations",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_caliper_annotations_TimepointName",
            table: "imaging_caliper_annotations",
            column: "TimepointName");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_AnonymizedSopInstanceUid",
            table: "imaging_deid_certificates",
            column: "AnonymizedSopInstanceUid");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_AnonymizedStudyInstanceUid",
            table: "imaging_deid_certificates",
            column: "AnonymizedStudyInstanceUid");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_SiteId",
            table: "imaging_deid_certificates",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_SourceSha256",
            table: "imaging_deid_certificates",
            column: "SourceSha256");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_StudyEnvelopeId",
            table: "imaging_deid_certificates",
            column: "StudyEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_StudyId",
            table: "imaging_deid_certificates",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_SubjectId",
            table: "imaging_deid_certificates",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_deid_certificates_TargetSha256",
            table: "imaging_deid_certificates",
            column: "TargetSha256");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_CertificateId",
            table: "imaging_instance_envelopes",
            column: "CertificateId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_ChecksumSha256",
            table: "imaging_instance_envelopes",
            column: "ChecksumSha256");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_ObjectKey",
            table: "imaging_instance_envelopes",
            column: "ObjectKey",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_SeriesEnvelopeId",
            table: "imaging_instance_envelopes",
            column: "SeriesEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_SopClassUid",
            table: "imaging_instance_envelopes",
            column: "SopClassUid");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_instance_envelopes_SopInstanceUid",
            table: "imaging_instance_envelopes",
            column: "SopInstanceUid",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_imaging_series_envelopes_Modality",
            table: "imaging_series_envelopes",
            column: "Modality");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_series_envelopes_SeriesInstanceUid",
            table: "imaging_series_envelopes",
            column: "SeriesInstanceUid");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_series_envelopes_StudyEnvelopeId",
            table: "imaging_series_envelopes",
            column: "StudyEnvelopeId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_AdjudicationStatus",
            table: "imaging_study_envelopes",
            column: "AdjudicationStatus");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_CreatedBy",
            table: "imaging_study_envelopes",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_SiteId",
            table: "imaging_study_envelopes",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_Status",
            table: "imaging_study_envelopes",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_StudyId",
            table: "imaging_study_envelopes",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_StudyInstanceUid",
            table: "imaging_study_envelopes",
            column: "StudyInstanceUid",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_SubjectId",
            table: "imaging_study_envelopes",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_study_envelopes_TenantId",
            table: "imaging_study_envelopes",
            column: "TenantId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_OverallResponse",
            table: "imaging_tumor_response_assessments",
            column: "OverallResponse");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_ReaderId",
            table: "imaging_tumor_response_assessments",
            column: "ReaderId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_SiteId",
            table: "imaging_tumor_response_assessments",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_StudyId",
            table: "imaging_tumor_response_assessments",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_SubjectId",
            table: "imaging_tumor_response_assessments",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_imaging_tumor_response_assessments_TimepointName",
            table: "imaging_tumor_response_assessments",
            column: "TimepointName");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "imaging_adjudication_sessions");

        migrationBuilder.DropTable(
            name: "imaging_audit_logs");

        migrationBuilder.DropTable(
            name: "imaging_caliper_annotations");

        migrationBuilder.DropTable(
            name: "imaging_deid_certificates");

        migrationBuilder.DropTable(
            name: "imaging_instance_envelopes");

        migrationBuilder.DropTable(
            name: "imaging_series_envelopes");

        migrationBuilder.DropTable(
            name: "imaging_study_envelopes");

        migrationBuilder.DropTable(
            name: "imaging_tumor_response_assessments");
    }
}
