using System;

using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Migrations;

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

        migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS clinical_queries (
                    ""Id"" character varying(36) NOT NULL,
                    ""QueryId"" character varying(255) NOT NULL,
                    ""SubjectCode"" character varying(255) NOT NULL,
                    ""StudyCode"" character varying(255) NOT NULL,
                    ""SiteCode"" character varying(255) NOT NULL,
                    ""ObservationId"" character varying(255),
                    ""QueryText"" text NOT NULL,
                    ""Status"" character varying(50) NOT NULL,
                    ""ResolutionText"" text,
                    ""_version"" bigint NOT NULL DEFAULT 0,
                    ""CreatedAt"" timestamp with time zone NOT NULL,
                    ""CreatedBy"" character varying(255) NOT NULL,
                    ""ReasonForChange"" character varying(1000) NOT NULL,
                    CONSTRAINT ""PK_clinical_queries"" PRIMARY KEY (""Id"")
                );
            ");

        migrationBuilder.CreateTable(
            name: "device_registrations",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Vendor = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ModelName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                UdiDi = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                UdiPiSerial = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                UdiPiLot = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                MacAddressMasked = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                DeviceToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FirmwareVersion = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CalibrationStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CalibrationDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_device_registrations", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ehr_endpoints",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SystemType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                FhirBaseUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                TokenUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                ClientId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                PublicKeyJwksUrl = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: true),
                ScopesJson = table.Column<string>(type: "jsonb", nullable: false),
                PollingIntervalSec = table.Column<int>(type: "integer", nullable: false),
                IsActive = table.Column<bool>(type: "boolean", nullable: false),
                LastPolledAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ehr_endpoints", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "ehr_reconciliation_candidates",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SubjectPseudonym = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                EhrSystemId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                FhirResourceType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                FhirResourceId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SourceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                TargetDomain = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TargetVariable = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                CdashTestcd = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                CdashTest = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CandidateValueJson = table.Column<string>(type: "jsonb", nullable: false),
                CandidateUnit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                CurrentEcrfValueJson = table.Column<string>(type: "jsonb", nullable: true),
                CurrentEcrfUpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                DiscrepancyType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ConfidenceScore = table.Column<double>(type: "double precision", nullable: false),
                MappingProvenance = table.Column<string>(type: "character varying(500)", maxLength: 500, nullable: false),
                RawFhirResourceJson = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ResolutionAction = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                ResolutionReason = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ResolvedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                ResolvedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                ElectronicSignatureJson = table.Column<string>(type: "jsonb", nullable: true),
                ClinicalQueryId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_ehr_reconciliation_candidates", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "epro_defeated_submissions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DiaryId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                OfflineSyncMarkersJson = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_epro_defeated_submissions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "epro_quarantined_submissions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DiaryId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                OriginalAnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                OfflineSyncMarkersJson = table.Column<string>(type: "jsonb", nullable: false),
                ValidationErrorsJson = table.Column<string>(type: "jsonb", nullable: false),
                Status = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TriageHistoryJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UpdatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_epro_quarantined_submissions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "epro_submissions",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DiaryId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ServerTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                AnswersJson = table.Column<string>(type: "jsonb", nullable: false),
                OfflineSyncMarkersJson = table.Column<string>(type: "jsonb", nullable: false),
                SyncStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_epro_submissions", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "instruments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Name = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Description = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                ItemsJson = table.Column<string>(type: "jsonb", nullable: false),
                ResponseTypesJson = table.Column<string>(type: "jsonb", nullable: false),
                ScoringMetadataJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_instruments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "interop_audit_logs",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                Timestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                UserId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                UserRole = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                Action = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Details = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                ChangeReason = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_interop_audit_logs", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sensor_daily_summaries",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SummaryDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                DeviceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DeviceToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ReadingsCount = table.Column<int>(type: "integer", nullable: false),
                ActiveWearHours = table.Column<double>(type: "double precision", nullable: false),
                TirPercentage = table.Column<double>(type: "double precision", nullable: true),
                TbrLevel1Percentage = table.Column<double>(type: "double precision", nullable: true),
                TbrLevel2Percentage = table.Column<double>(type: "double precision", nullable: true),
                TarLevel1Percentage = table.Column<double>(type: "double precision", nullable: true),
                TarLevel2Percentage = table.Column<double>(type: "double precision", nullable: true),
                MeanGlucoseMgDl = table.Column<double>(type: "double precision", nullable: true),
                GlucoseSd = table.Column<double>(type: "double precision", nullable: true),
                GlucoseCvPercent = table.Column<double>(type: "double precision", nullable: true),
                GlucoseManagementIndicator = table.Column<double>(type: "double precision", nullable: true),
                TotalSteps = table.Column<int>(type: "integer", nullable: true),
                MeanCadenceSpm = table.Column<double>(type: "double precision", nullable: true),
                PeakCadenceSpm = table.Column<double>(type: "double precision", nullable: true),
                ActiveMinutes = table.Column<double>(type: "double precision", nullable: true),
                SedentaryMinutes = table.Column<double>(type: "double precision", nullable: true),
                SleepDurationMinutes = table.Column<double>(type: "double precision", nullable: true),
                SleepEfficiencyPercent = table.Column<double>(type: "double precision", nullable: true),
                RestingHeartRateBpm = table.Column<double>(type: "double precision", nullable: true),
                BestFev1Liters = table.Column<double>(type: "double precision", nullable: true),
                BestFvcLiters = table.Column<double>(type: "double precision", nullable: true),
                Fev1FvcRatio = table.Column<double>(type: "double precision", nullable: true),
                Fev1PercentPredicted = table.Column<double>(type: "double precision", nullable: true),
                PeakExpiratoryFlowLMin = table.Column<double>(type: "double precision", nullable: true),
                MeanSystolicMmhg = table.Column<double>(type: "double precision", nullable: true),
                MeanDiastolicMmhg = table.Column<double>(type: "double precision", nullable: true),
                MeanArterialPressureMmhg = table.Column<double>(type: "double precision", nullable: true),
                SummaryManifestJson = table.Column<string>(type: "jsonb", nullable: false),
                LakehouseRecordId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                Sha256Hash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sensor_daily_summaries", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sensor_safety_alerts",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                AlertId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DeviceToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                AlertType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                Severity = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                ParameterName = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ObservedValue = table.Column<double>(type: "double precision", nullable: false),
                ThresholdValue = table.Column<double>(type: "double precision", nullable: false),
                Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                TriggeredAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                IsAcknowledged = table.Column<bool>(type: "boolean", nullable: false),
                AcknowledgedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                AcknowledgedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                AcknowledgmentNotes = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: true),
                AuditSignature = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sensor_safety_alerts", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "sensor_telemetry_streams",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StudyId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                SiteId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: true),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                DeviceToken = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                DeviceType = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                BatchId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                ReceivedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                ClockDriftOffsetMs = table.Column<double>(type: "double precision", nullable: false),
                ReadingCount = table.Column<int>(type: "integer", nullable: false),
                StartTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndTimestamp = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                OmhPayloadJson = table.Column<string>(type: "jsonb", nullable: false),
                FhirObservationsJson = table.Column<string>(type: "jsonb", nullable: false),
                RawDataHash = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                FeedLakehouseRecordIdsJson = table.Column<string>(type: "jsonb", nullable: false),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_sensor_telemetry_streams", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "subject_assignments",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                InstrumentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                StartDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                EndDate = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                RecurrencePattern = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true),
                DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastMissedAlertAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                LastMissedAlertNotifiedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_assignments", x => x.Id);
            });

        migrationBuilder.CreateTable(
            name: "subject_notifications",
            columns: table => new
            {
                Id = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: false),
                SubjectId = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                AssignmentId = table.Column<string>(type: "character varying(36)", maxLength: 36, nullable: true),
                DueAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                Channel = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                DeliveryStatus = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                IsRead = table.Column<bool>(type: "boolean", nullable: false),
                ReadAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                CreatedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                CreatedBy = table.Column<string>(type: "character varying(255)", maxLength: 255, nullable: false),
                ReasonForChange = table.Column<string>(type: "character varying(1000)", maxLength: 1000, nullable: false),
                VersionIndex = table.Column<int>(type: "integer", nullable: false)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_subject_notifications", x => x.Id);
            });

        migrationBuilder.CreateIndex(
            name: "IX_clinical_queries_StudyId",
            table: "clinical_queries",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_queries_SubjectId",
            table: "clinical_queries",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_clinical_queries_TestCode",
            table: "clinical_queries",
            column: "TestCode");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_DeviceToken",
            table: "device_registrations",
            column: "DeviceToken");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_DeviceType",
            table: "device_registrations",
            column: "DeviceType");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_IsActive",
            table: "device_registrations",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_SiteId",
            table: "device_registrations",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_StudyId",
            table: "device_registrations",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_SubjectId",
            table: "device_registrations",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_device_registrations_UdiDi",
            table: "device_registrations",
            column: "UdiDi");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_endpoints_ClientId",
            table: "ehr_endpoints",
            column: "ClientId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_endpoints_IsActive",
            table: "ehr_endpoints",
            column: "IsActive");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_endpoints_SystemType",
            table: "ehr_endpoints",
            column: "SystemType");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_ClinicalQueryId",
            table: "ehr_reconciliation_candidates",
            column: "ClinicalQueryId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_EhrSystemId",
            table: "ehr_reconciliation_candidates",
            column: "EhrSystemId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_FhirResourceId",
            table: "ehr_reconciliation_candidates",
            column: "FhirResourceId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_FhirResourceType",
            table: "ehr_reconciliation_candidates",
            column: "FhirResourceType");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_SiteId",
            table: "ehr_reconciliation_candidates",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_SourceTimestamp",
            table: "ehr_reconciliation_candidates",
            column: "SourceTimestamp");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_Status",
            table: "ehr_reconciliation_candidates",
            column: "Status");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_StudyId",
            table: "ehr_reconciliation_candidates",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_SubjectId",
            table: "ehr_reconciliation_candidates",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_SubjectPseudonym",
            table: "ehr_reconciliation_candidates",
            column: "SubjectPseudonym");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_TargetDomain",
            table: "ehr_reconciliation_candidates",
            column: "TargetDomain");

        migrationBuilder.CreateIndex(
            name: "IX_ehr_reconciliation_candidates_TargetVariable",
            table: "ehr_reconciliation_candidates",
            column: "TargetVariable");

        migrationBuilder.CreateIndex(
            name: "IX_epro_defeated_submissions_DiaryId",
            table: "epro_defeated_submissions",
            column: "DiaryId");

        migrationBuilder.CreateIndex(
            name: "IX_epro_defeated_submissions_SubjectId",
            table: "epro_defeated_submissions",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_epro_quarantined_submissions_DeviceTimestamp",
            table: "epro_quarantined_submissions",
            column: "DeviceTimestamp");

        migrationBuilder.CreateIndex(
            name: "IX_epro_quarantined_submissions_DiaryId",
            table: "epro_quarantined_submissions",
            column: "DiaryId");

        migrationBuilder.CreateIndex(
            name: "IX_epro_quarantined_submissions_SubjectId",
            table: "epro_quarantined_submissions",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_epro_submissions_DeviceTimestamp",
            table: "epro_submissions",
            column: "DeviceTimestamp");

        migrationBuilder.CreateIndex(
            name: "IX_epro_submissions_DiaryId",
            table: "epro_submissions",
            column: "DiaryId");

        migrationBuilder.CreateIndex(
            name: "IX_epro_submissions_SubjectId",
            table: "epro_submissions",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_interop_audit_logs_Timestamp",
            table: "interop_audit_logs",
            column: "Timestamp");

        migrationBuilder.CreateIndex(
            name: "IX_interop_audit_logs_UserId",
            table: "interop_audit_logs",
            column: "UserId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_DeviceToken",
            table: "sensor_daily_summaries",
            column: "DeviceToken");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_DeviceType",
            table: "sensor_daily_summaries",
            column: "DeviceType");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_LakehouseRecordId",
            table: "sensor_daily_summaries",
            column: "LakehouseRecordId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_Sha256Hash",
            table: "sensor_daily_summaries",
            column: "Sha256Hash");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_SiteId",
            table: "sensor_daily_summaries",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_StudyId",
            table: "sensor_daily_summaries",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_SubjectId",
            table: "sensor_daily_summaries",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_daily_summaries_SummaryDate",
            table: "sensor_daily_summaries",
            column: "SummaryDate");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_AlertId",
            table: "sensor_safety_alerts",
            column: "AlertId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_AlertType",
            table: "sensor_safety_alerts",
            column: "AlertType");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_DeviceToken",
            table: "sensor_safety_alerts",
            column: "DeviceToken");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_DeviceType",
            table: "sensor_safety_alerts",
            column: "DeviceType");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_IsAcknowledged",
            table: "sensor_safety_alerts",
            column: "IsAcknowledged");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_Severity",
            table: "sensor_safety_alerts",
            column: "Severity");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_SiteId",
            table: "sensor_safety_alerts",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_StudyId",
            table: "sensor_safety_alerts",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_SubjectId",
            table: "sensor_safety_alerts",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_safety_alerts_TriggeredAt",
            table: "sensor_safety_alerts",
            column: "TriggeredAt");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_BatchId",
            table: "sensor_telemetry_streams",
            column: "BatchId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_DeviceToken",
            table: "sensor_telemetry_streams",
            column: "DeviceToken");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_DeviceType",
            table: "sensor_telemetry_streams",
            column: "DeviceType");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_RawDataHash",
            table: "sensor_telemetry_streams",
            column: "RawDataHash");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_ReceivedAt",
            table: "sensor_telemetry_streams",
            column: "ReceivedAt");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_SiteId",
            table: "sensor_telemetry_streams",
            column: "SiteId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_StudyId",
            table: "sensor_telemetry_streams",
            column: "StudyId");

        migrationBuilder.CreateIndex(
            name: "IX_sensor_telemetry_streams_SubjectId",
            table: "sensor_telemetry_streams",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_assignments_InstrumentId",
            table: "subject_assignments",
            column: "InstrumentId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_assignments_SubjectId",
            table: "subject_assignments",
            column: "SubjectId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_notifications_AssignmentId",
            table: "subject_notifications",
            column: "AssignmentId");

        migrationBuilder.CreateIndex(
            name: "IX_subject_notifications_DueAt",
            table: "subject_notifications",
            column: "DueAt");

        migrationBuilder.CreateIndex(
            name: "IX_subject_notifications_SubjectId",
            table: "subject_notifications",
            column: "SubjectId");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable(
            name: "audit_log_entries");

        migrationBuilder.DropTable(
            name: "clinical_queries");

        migrationBuilder.DropTable(
            name: "device_registrations");

        migrationBuilder.DropTable(
            name: "ehr_endpoints");

        migrationBuilder.DropTable(
            name: "ehr_reconciliation_candidates");

        migrationBuilder.DropTable(
            name: "epro_defeated_submissions");

        migrationBuilder.DropTable(
            name: "epro_quarantined_submissions");

        migrationBuilder.DropTable(
            name: "epro_submissions");

        migrationBuilder.DropTable(
            name: "instruments");

        migrationBuilder.DropTable(
            name: "interop_audit_logs");

        migrationBuilder.DropTable(
            name: "sensor_daily_summaries");

        migrationBuilder.DropTable(
            name: "sensor_safety_alerts");

        migrationBuilder.DropTable(
            name: "sensor_telemetry_streams");

        migrationBuilder.DropTable(
            name: "subject_assignments");

        migrationBuilder.DropTable(
            name: "subject_notifications");
    }
}
