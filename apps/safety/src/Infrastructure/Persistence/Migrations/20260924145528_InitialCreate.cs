using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Migrations;

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
            name: "device_safety_investigations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CaseNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                DeficiencyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceUdiDi = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                LotNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SerialNumber = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Classification = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Causality = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsUsade = table.Column<bool>(type: "boolean", nullable: false),
                VigilanceReportingRequired = table.Column<bool>(type: "boolean", nullable: false),
                VigilanceDeadlineDays = table.Column<int>(type: "integer", nullable: false),
                VigilanceDueDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                NotifiedBodyNotified = table.Column<bool>(type: "boolean", nullable: false),
                CompetentAuthorityNotified = table.Column<bool>(type: "boolean", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                InvestigationNotes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                RootCauseSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                CapaRequired = table.Column<bool>(type: "boolean", nullable: false),
                CapaReference = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                FscaRequired = table.Column<bool>(type: "boolean", nullable: false),
                SafetyPhysicianUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AdjudicatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_safety_investigations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "periodic_safety_reports",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                ReportType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CompoundName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PeriodStart = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                PeriodEnd = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TotalSubjectExposure = table.Column<int>(type: "integer", nullable: false),
                TotalSaes = table.Column<int>(type: "integer", nullable: false),
                ReportPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_periodic_safety_reports", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sae_discrepancies",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                RunId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Source = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CaseEventKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FieldName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ExpectedValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ActualValue = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                MeddraVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sae_discrepancies", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sae_reconciliation_jobs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                RunId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sae_reconciliation_jobs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sae_reconciliation_runs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                RunDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sae_reconciliation_runs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_as2_transmission_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TransmissionId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FromStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ToStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                Details = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_as2_transmission_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_as2_transmissions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                TransmissionId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CaseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                WorldwideUniqueCaseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                MessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                As2From = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                As2To = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TargetAuthority = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Environment = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                PayloadDigestSha256 = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                Mic = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                EncryptionAlgorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                SignatureAlgorithm = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                OutboundPayloadSize = table.Column<int>(type: "integer", nullable: false),
                OutboundXml = table.Column<string>(type: "text", nullable: true),
                MdnMessageId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                MdnDisposition = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                MdnMic = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                MdnStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                MdnReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AckAStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AckAReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AckBStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AckBReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AckBErrorsJson = table.Column<string>(type: "jsonb", nullable: false),
                AckCStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                AckCReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AckCDetailsJson = table.Column<string>(type: "jsonb", nullable: false),
                ErrorMessage = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                RetryCount = table.Column<int>(type: "integer", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_as2_transmissions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                VersionIndex = table.Column<int>(type: "integer", nullable: false),
                Action = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                RecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_cases",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                WorldwideUniqueCaseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PatientId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CaseDataJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_cases", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_export_jobs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                JobName = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Output = table.Column<string>(type: "text", nullable: true),
                Error = table.Column<string>(type: "text", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_export_jobs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_narratives",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CaseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SaeEventKey = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Title = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                SectionsJson = table.Column<string>(type: "jsonb", nullable: false),
                RawNarrativeText = table.Column<string>(type: "text", nullable: false),
                TimelineEventsJson = table.Column<string>(type: "jsonb", nullable: false),
                GroundedClaimsJson = table.Column<string>(type: "jsonb", nullable: false),
                ModelIdentifier = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PromptHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                ConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                ReviewStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_narratives", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_reporting_evaluations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                CaseId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReceiptDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                SeriousnessDeath = table.Column<bool>(type: "boolean", nullable: false),
                SeriousnessLifeThreatening = table.Column<bool>(type: "boolean", nullable: false),
                SeriousnessHospitalization = table.Column<bool>(type: "boolean", nullable: false),
                SeriousnessDisability = table.Column<bool>(type: "boolean", nullable: false),
                SeriousnessCongenitalAnomaly = table.Column<bool>(type: "boolean", nullable: false),
                SeriousnessOther = table.Column<bool>(type: "boolean", nullable: false),
                Causality = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Expectedness = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsSerious = table.Column<bool>(type: "boolean", nullable: false),
                IsUnexpected = table.Column<bool>(type: "boolean", nullable: false),
                IsCausal = table.Column<bool>(type: "boolean", nullable: false),
                IsSusar = table.Column<bool>(type: "boolean", nullable: false),
                AnyExpedited = table.Column<bool>(type: "boolean", nullable: false),
                ShortestTimeframeDays = table.Column<int>(type: "integer", nullable: true),
                EvaluationsJson = table.Column<string>(type: "jsonb", nullable: false),
                ApprovalStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ApprovedByUserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ApprovedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_reporting_evaluations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_signal_alerts",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                DetectionRunId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TargetDrug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                MeddraTerm = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                TermLevel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                MeddraSoc = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CaseCountA = table.Column<int>(type: "integer", nullable: false),
                ExpectedCountE = table.Column<double>(type: "double precision", nullable: false),
                Prr = table.Column<double>(type: "double precision", nullable: false),
                PrrCiLower = table.Column<double>(type: "double precision", nullable: false),
                PrrCiUpper = table.Column<double>(type: "double precision", nullable: false),
                ChiSquareYates = table.Column<double>(type: "double precision", nullable: false),
                Ror = table.Column<double>(type: "double precision", nullable: false),
                RorCiLower = table.Column<double>(type: "double precision", nullable: false),
                RorCiUpper = table.Column<double>(type: "double precision", nullable: false),
                Ebgm = table.Column<double>(type: "double precision", nullable: false),
                Eb05 = table.Column<double>(type: "double precision", nullable: false),
                Eb95 = table.Column<double>(type: "double precision", nullable: false),
                Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TriageSummary = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: false),
                AssignedSafetyOfficer = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReviewDecision = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                ReviewJustification = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                ClinicalSummary = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true),
                ReviewedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ReviewedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                EsignatureManifestId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_signal_alerts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "safety_signal_detection_runs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                TargetDrug = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                BenchmarkDataset = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                TotalCasesAnalyzed = table.Column<int>(type: "integer", nullable: false),
                SignalsDetectedCount = table.Column<int>(type: "integer", nullable: false),
                SummaryMetricsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_safety_signal_detection_runs", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_CaseNumber",
            table: "device_safety_investigations",
            column: "CaseNumber",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_Classification",
            table: "device_safety_investigations",
            column: "Classification");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_DeficiencyId",
            table: "device_safety_investigations",
            column: "DeficiencyId");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_DeviceUdiDi",
            table: "device_safety_investigations",
            column: "DeviceUdiDi");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_LotNumber",
            table: "device_safety_investigations",
            column: "LotNumber");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_SerialNumber",
            table: "device_safety_investigations",
            column: "SerialNumber");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_SiteId",
            table: "device_safety_investigations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_Status",
            table: "device_safety_investigations",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_StudyId",
            table: "device_safety_investigations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_device_safety_investigations_SubjectId",
            table: "device_safety_investigations",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_reports_CompoundName",
            table: "periodic_safety_reports",
            column: "CompoundName");

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_reports_ReportType",
            table: "periodic_safety_reports",
            column: "ReportType");

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_reports_Status",
            table: "periodic_safety_reports",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_periodic_safety_reports_StudyId",
            table: "periodic_safety_reports",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sae_discrepancies_CaseEventKey",
            table: "sae_discrepancies",
            column: "CaseEventKey");

        migrationBuilder.CreateIndex(
            name: "IX_sae_discrepancies_RunId",
            table: "sae_discrepancies",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_sae_reconciliation_jobs_RunId",
            table: "sae_reconciliation_jobs",
            column: "RunId");

        migrationBuilder.CreateIndex(
            name: "IX_sae_reconciliation_jobs_StudyId",
            table: "sae_reconciliation_jobs",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sae_reconciliation_runs_StudyId",
            table: "sae_reconciliation_runs",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmission_audit_logs_Action",
            table: "safety_as2_transmission_audit_logs",
            column: "Action");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmission_audit_logs_CreatedAt",
            table: "safety_as2_transmission_audit_logs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmission_audit_logs_CreatedBy",
            table: "safety_as2_transmission_audit_logs",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmission_audit_logs_TransmissionId",
            table: "safety_as2_transmission_audit_logs",
            column: "TransmissionId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_As2From",
            table: "safety_as2_transmissions",
            column: "As2From");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_As2To",
            table: "safety_as2_transmissions",
            column: "As2To");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_CaseId",
            table: "safety_as2_transmissions",
            column: "CaseId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_MessageId",
            table: "safety_as2_transmissions",
            column: "MessageId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_Status",
            table: "safety_as2_transmissions",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_StudyId",
            table: "safety_as2_transmissions",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_TargetAuthority",
            table: "safety_as2_transmissions",
            column: "TargetAuthority");

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_TransmissionId",
            table: "safety_as2_transmissions",
            column: "TransmissionId",
            unique: true);

        migrationBuilder.CreateIndex(
            name: "IX_safety_as2_transmissions_WorldwideUniqueCaseId",
            table: "safety_as2_transmissions",
            column: "WorldwideUniqueCaseId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_audit_logs_CreatedAt",
            table: "safety_audit_logs",
            column: "CreatedAt");

        migrationBuilder.CreateIndex(
            name: "IX_safety_audit_logs_CreatedBy",
            table: "safety_audit_logs",
            column: "CreatedBy");

        migrationBuilder.CreateIndex(
            name: "IX_safety_cases_PatientId",
            table: "safety_cases",
            column: "PatientId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_cases_WorldwideUniqueCaseId",
            table: "safety_cases",
            column: "WorldwideUniqueCaseId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narratives_CaseId",
            table: "safety_narratives",
            column: "CaseId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narratives_ReviewStatus",
            table: "safety_narratives",
            column: "ReviewStatus");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narratives_SaeEventKey",
            table: "safety_narratives",
            column: "SaeEventKey");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narratives_StudyId",
            table: "safety_narratives",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_narratives_SubjectId",
            table: "safety_narratives",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_reporting_evaluations_ApprovalStatus",
            table: "safety_reporting_evaluations",
            column: "ApprovalStatus");

        migrationBuilder.CreateIndex(
            name: "IX_safety_reporting_evaluations_CaseId",
            table: "safety_reporting_evaluations",
            column: "CaseId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_reporting_evaluations_ReceiptDate",
            table: "safety_reporting_evaluations",
            column: "ReceiptDate");

        migrationBuilder.CreateIndex(
            name: "IX_safety_reporting_evaluations_StudyId",
            table: "safety_reporting_evaluations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_reporting_evaluations_SubjectId",
            table: "safety_reporting_evaluations",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_DetectionRunId",
            table: "safety_signal_alerts",
            column: "DetectionRunId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_MeddraSoc",
            table: "safety_signal_alerts",
            column: "MeddraSoc");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_MeddraTerm",
            table: "safety_signal_alerts",
            column: "MeddraTerm");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_Severity",
            table: "safety_signal_alerts",
            column: "Severity");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_Status",
            table: "safety_signal_alerts",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_StudyId",
            table: "safety_signal_alerts",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_alerts_TargetDrug",
            table: "safety_signal_alerts",
            column: "TargetDrug");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_detection_runs_StudyId",
            table: "safety_signal_detection_runs",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_safety_signal_detection_runs_TargetDrug",
            table: "safety_signal_detection_runs",
            column: "TargetDrug");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "device_safety_investigations");

        migrationBuilder.DropTable(
            name: "periodic_safety_reports");

        migrationBuilder.DropTable(
            name: "sae_discrepancies");

        migrationBuilder.DropTable(
            name: "sae_reconciliation_jobs");

        migrationBuilder.DropTable(
            name: "sae_reconciliation_runs");

        migrationBuilder.DropTable(
            name: "safety_as2_transmission_audit_logs");

        migrationBuilder.DropTable(
            name: "safety_as2_transmissions");

        migrationBuilder.DropTable(
            name: "safety_audit_logs");

        migrationBuilder.DropTable(
            name: "safety_cases");

        migrationBuilder.DropTable(
            name: "safety_export_jobs");

        migrationBuilder.DropTable(
            name: "safety_narratives");

        migrationBuilder.DropTable(
            name: "safety_reporting_evaluations");

        migrationBuilder.DropTable(
            name: "safety_signal_alerts");

        migrationBuilder.DropTable(
            name: "safety_signal_detection_runs");
    }
}
