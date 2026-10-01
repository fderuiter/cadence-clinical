using Cadence.Apps.Interop.Domain;
using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Interop.Infrastructure.Persistence.Configurations;

public class EproSubmissionConfiguration : IEntityTypeConfiguration<EproSubmission>
{
    public void Configure(EntityTypeBuilder<EproSubmission> builder)
    {
        builder.ToTable("epro_submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DiaryId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.DiaryId);
        builder.HasIndex(x => x.DeviceTimestamp);
        builder.Property(x => x.AnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.OfflineSyncMarkersJson).HasColumnType("jsonb");
        builder.Property(x => x.SyncStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class InteropAuditLogConfiguration : IEntityTypeConfiguration<InteropAuditLog>
{
    public void Configure(EntityTypeBuilder<InteropAuditLog> builder)
    {
        builder.ToTable("interop_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.UserRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ChangeReason).HasMaxLength(255);
    }
}

public class InstrumentConfiguration : IEntityTypeConfiguration<Instrument>
{
    public void Configure(EntityTypeBuilder<Instrument> builder)
    {
        builder.ToTable("instruments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(1000);
        builder.Property(x => x.ItemsJson).HasColumnType("jsonb");
        builder.Property(x => x.ResponseTypesJson).HasColumnType("jsonb");
        builder.Property(x => x.ScoringMetadataJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SubjectAssignmentConfiguration : IEntityTypeConfiguration<SubjectAssignment>
{
    public void Configure(EntityTypeBuilder<SubjectAssignment> builder)
    {
        builder.ToTable("subject_assignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.InstrumentId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.InstrumentId);
        builder.Property(x => x.RecurrencePattern).HasMaxLength(50);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SubjectNotificationConfiguration : IEntityTypeConfiguration<SubjectNotification>
{
    public void Configure(EntityTypeBuilder<SubjectNotification> builder)
    {
        builder.ToTable("subject_notifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.AssignmentId).HasMaxLength(36);
        builder.HasIndex(x => x.AssignmentId);
        builder.HasIndex(x => x.DueAt);
        builder.Property(x => x.Channel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DeliveryStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class EproSubmissionDefeatedConfiguration : IEntityTypeConfiguration<EproSubmissionDefeated>
{
    public void Configure(EntityTypeBuilder<EproSubmissionDefeated> builder)
    {
        builder.ToTable("epro_defeated_submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DiaryId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.DiaryId);
        builder.Property(x => x.AnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.OfflineSyncMarkersJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(100).IsRequired();
    }
}

public class ClinicalQueryConfiguration : IEntityTypeConfiguration<ClinicalQuery>
{
    public void Configure(EntityTypeBuilder<ClinicalQuery> builder)
    {
        builder.ToTable("clinical_queries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.VisitId).HasMaxLength(255);
        builder.Property(x => x.Domain).HasMaxLength(50);
        builder.Property(x => x.TestCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.TestCode);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Explanation).HasMaxLength(1000);
    }
}

public class EproSubmissionQuarantineConfiguration : IEntityTypeConfiguration<EproSubmissionQuarantine>
{
    public void Configure(EntityTypeBuilder<EproSubmissionQuarantine> builder)
    {
        builder.ToTable("epro_quarantined_submissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DiaryId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.DiaryId);
        builder.HasIndex(x => x.DeviceTimestamp);
        builder.Property(x => x.AnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.OriginalAnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.OfflineSyncMarkersJson).HasColumnType("jsonb");
        builder.Property(x => x.ValidationErrorsJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TriageHistoryJson).HasColumnType("jsonb");
    }
}

public class EhrEndpointRecordConfiguration : IEntityTypeConfiguration<EhrEndpointRecord>
{
    public void Configure(EntityTypeBuilder<EhrEndpointRecord> builder)
    {
        builder.ToTable("ehr_endpoints");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SystemType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.SystemType);
        builder.Property(x => x.FhirBaseUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.TokenUrl).HasMaxLength(500).IsRequired();
        builder.Property(x => x.ClientId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ClientId);
        builder.Property(x => x.PublicKeyJwksUrl).HasMaxLength(500);
        builder.Property(x => x.ScopesJson).HasColumnType("jsonb");
        builder.HasIndex(x => x.IsActive);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class EhrReconciliationCandidateRecordConfiguration : IEntityTypeConfiguration<EhrReconciliationCandidateRecord>
{
    public void Configure(EntityTypeBuilder<EhrReconciliationCandidateRecord> builder)
    {
        builder.ToTable("ehr_reconciliation_candidates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.EhrSystemId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.EhrSystemId);
        builder.Property(x => x.FhirResourceType).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.FhirResourceType);
        builder.Property(x => x.FhirResourceId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.FhirResourceId);
        builder.HasIndex(x => x.SourceTimestamp);
        builder.Property(x => x.TargetDomain).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TargetDomain);
        builder.Property(x => x.TargetVariable).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TargetVariable);
        builder.Property(x => x.CdashTestcd).HasMaxLength(100);
        builder.Property(x => x.CdashTest).HasMaxLength(255);
        builder.Property(x => x.CandidateValueJson).HasColumnType("jsonb");
        builder.Property(x => x.CandidateUnit).HasMaxLength(50);
        builder.Property(x => x.CurrentEcrfValueJson).HasColumnType("jsonb");
        builder.Property(x => x.DiscrepancyType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.MappingProvenance).HasMaxLength(500).IsRequired();
        builder.Property(x => x.RawFhirResourceJson).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ResolutionAction).HasMaxLength(50);
        builder.Property(x => x.ResolutionReason).HasMaxLength(1000);
        builder.Property(x => x.ResolvedBy).HasMaxLength(255);
        builder.Property(x => x.ElectronicSignatureJson).HasColumnType("jsonb");
        builder.Property(x => x.ClinicalQueryId).HasMaxLength(36);
        builder.HasIndex(x => x.ClinicalQueryId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class DeviceRegistrationRecordConfiguration : IEntityTypeConfiguration<DeviceRegistrationRecord>
{
    public void Configure(EntityTypeBuilder<DeviceRegistrationRecord> builder)
    {
        builder.ToTable("device_registrations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DeviceType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.DeviceType);
        builder.Property(x => x.Vendor).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ModelName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.UdiDi).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.UdiDi);
        builder.Property(x => x.UdiPiSerial).HasMaxLength(100);
        builder.Property(x => x.UdiPiLot).HasMaxLength(100);
        builder.Property(x => x.MacAddressMasked).HasMaxLength(50);
        builder.Property(x => x.DeviceTokenHash).HasMaxLength(ILookupHasher.HashLength).IsRequired();
        builder.HasIndex(x => x.DeviceTokenHash);
        builder.Property(x => x.FirmwareVersion).HasMaxLength(50);
        builder.Property(x => x.CalibrationStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.IsActive);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SensorTelemetryStreamRecordConfiguration : IEntityTypeConfiguration<SensorTelemetryStreamRecord>
{
    public void Configure(EntityTypeBuilder<SensorTelemetryStreamRecord> builder)
    {
        builder.ToTable("sensor_telemetry_streams");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DeviceTokenHash).HasMaxLength(ILookupHasher.HashLength).IsRequired();
        builder.HasIndex(x => x.DeviceTokenHash);
        builder.Property(x => x.DeviceType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.DeviceType);
        builder.Property(x => x.BatchId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.BatchId);
        builder.HasIndex(x => x.ReceivedAt);
        builder.Property(x => x.OmhPayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.FhirObservationsJson).HasColumnType("jsonb");
        builder.Property(x => x.RawDataHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.RawDataHash);
        builder.Property(x => x.FeedLakehouseRecordIdsJson).HasColumnType("jsonb");
    }
}

public class SensorDailySummaryRecordConfiguration : IEntityTypeConfiguration<SensorDailySummaryRecord>
{
    public void Configure(EntityTypeBuilder<SensorDailySummaryRecord> builder)
    {
        builder.ToTable("sensor_daily_summaries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.SummaryDate);
        builder.Property(x => x.DeviceType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.DeviceType);
        builder.Property(x => x.DeviceTokenHash).HasMaxLength(ILookupHasher.HashLength).IsRequired();
        builder.HasIndex(x => x.DeviceTokenHash);
        builder.Property(x => x.SummaryManifestJson).HasColumnType("jsonb");
        builder.Property(x => x.LakehouseRecordId).HasMaxLength(255);
        builder.HasIndex(x => x.LakehouseRecordId);
        builder.Property(x => x.Sha256Hash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.Sha256Hash);
    }
}

public class SensorSafetyAlertRecordConfiguration : IEntityTypeConfiguration<SensorSafetyAlertRecord>
{
    public void Configure(EntityTypeBuilder<SensorSafetyAlertRecord> builder)
    {
        builder.ToTable("sensor_safety_alerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.AlertId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.AlertId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DeviceType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.DeviceType);
        builder.Property(x => x.DeviceTokenHash).HasMaxLength(ILookupHasher.HashLength).IsRequired();
        builder.HasIndex(x => x.DeviceTokenHash);
        builder.Property(x => x.AlertType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.AlertType);
        builder.Property(x => x.Severity).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Severity);
        builder.Property(x => x.ParameterName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Unit).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TriggeredAt);
        builder.HasIndex(x => x.IsAcknowledged);
        builder.Property(x => x.AcknowledgedBy).HasMaxLength(255);
        builder.Property(x => x.AcknowledgmentNotes).HasMaxLength(1000);
        builder.Property(x => x.AuditSignature).HasMaxLength(255);
    }
}

public class DtpShipmentRecordConfiguration : IEntityTypeConfiguration<DtpShipmentRecord>
{
    public void Configure(EntityTypeBuilder<DtpShipmentRecord> builder)
    {
        builder.ToTable("dtp_shipments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ShipmentId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.ShipmentId).IsUnique();
        builder.Property(x => x.TrackingNumber).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.TrackingNumber).IsUnique();
        builder.Property(x => x.CourierName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectPseudonymId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonymId);
        builder.Property(x => x.DestinationAddressPseudonym).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TemperatureZone).HasMaxLength(50).IsRequired();
        builder.Property(x => x.KitSerialsJson).HasColumnType("jsonb");
        builder.Property(x => x.TamperSealId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ExcursionSeverity).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CustodyHandoffsJson).HasColumnType("jsonb");
        builder.Property(x => x.TemperatureReadingsJson).HasColumnType("jsonb");
        builder.Property(x => x.DeliverySignatureJson).HasColumnType("jsonb");
        builder.Property(x => x.ReplacementDispatchId).HasMaxLength(100);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}
