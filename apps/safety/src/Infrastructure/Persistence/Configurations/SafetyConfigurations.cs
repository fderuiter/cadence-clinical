using Cadence.Apps.Safety.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Safety.Infrastructure.Persistence.Configurations;

public class IcsrReportingEvaluationConfiguration : IEntityTypeConfiguration<IcsrReportingEvaluation>
{
    public void Configure(EntityTypeBuilder<IcsrReportingEvaluation> builder)
    {
        builder.ToTable("safety_reporting_evaluations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.CaseId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CaseId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.HasIndex(x => x.ReceiptDate);
        builder.Property(x => x.Causality).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Expectedness).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EvaluationsJson).HasColumnType("jsonb");
        builder.Property(x => x.ApprovalStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.ApprovalStatus);
        builder.Property(x => x.ApprovedByUserId).HasMaxLength(255);
        builder.Property(x => x.EsignatureManifestId).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SafetyCaseIcsrConfiguration : IEntityTypeConfiguration<SafetyCaseIcsr>
{
    public void Configure(EntityTypeBuilder<SafetyCaseIcsr> builder)
    {
        builder.ToTable("safety_cases");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.WorldwideUniqueCaseId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.WorldwideUniqueCaseId);
        builder.Property(x => x.PatientId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.PatientId);
        builder.Property(x => x.CaseDataJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ExportJobConfiguration : IEntityTypeConfiguration<ExportJob>
{
    public void Configure(EntityTypeBuilder<ExportJob> builder)
    {
        builder.ToTable("safety_export_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.JobName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SaeReconciliationRunConfiguration : IEntityTypeConfiguration<SaeReconciliationRun>
{
    public void Configure(EntityTypeBuilder<SaeReconciliationRun> builder)
    {
        builder.ToTable("sae_reconciliation_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SaeDiscrepancyConfiguration : IEntityTypeConfiguration<SaeDiscrepancy>
{
    public void Configure(EntityTypeBuilder<SaeDiscrepancy> builder)
    {
        builder.ToTable("sae_discrepancies");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.RunId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.RunId);
        builder.Property(x => x.Source).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CaseEventKey).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CaseEventKey);
        builder.Property(x => x.FieldName).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ExpectedValue).HasMaxLength(1000);
        builder.Property(x => x.ActualValue).HasMaxLength(1000);
        builder.Property(x => x.MeddraVersion).HasMaxLength(50);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SaeReconciliationJobConfiguration : IEntityTypeConfiguration<SaeReconciliationJob>
{
    public void Configure(EntityTypeBuilder<SaeReconciliationJob> builder)
    {
        builder.ToTable("sae_reconciliation_jobs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RunId).HasMaxLength(36);
        builder.HasIndex(x => x.RunId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SafetyNarrativeConfiguration : IEntityTypeConfiguration<SafetyNarrative>
{
    public void Configure(EntityTypeBuilder<SafetyNarrative> builder)
    {
        builder.ToTable("safety_narratives");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.CaseId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CaseId);
        builder.Property(x => x.SaeEventKey).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SaeEventKey);
        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.SectionsJson).HasColumnType("jsonb");
        builder.Property(x => x.TimelineEventsJson).HasColumnType("jsonb");
        builder.Property(x => x.GroundedClaimsJson).HasColumnType("jsonb");
        builder.Property(x => x.ModelIdentifier).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PromptHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ReviewStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.ReviewStatus);
        builder.Property(x => x.ApprovedByUserId).HasMaxLength(255);
        builder.Property(x => x.EsignatureManifestId).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SafetyAuditLogConfiguration : IEntityTypeConfiguration<SafetyAuditLog>
{
    public void Configure(EntityTypeBuilder<SafetyAuditLog> builder)
    {
        builder.ToTable("safety_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.CreatedAt);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CreatedBy);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.RecordId).HasMaxLength(255);
    }
}

public class As2TransmissionConfiguration : IEntityTypeConfiguration<As2Transmission>
{
    public void Configure(EntityTypeBuilder<As2Transmission> builder)
    {
        builder.ToTable("safety_as2_transmissions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TransmissionId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TransmissionId).IsUnique();
        builder.Property(x => x.CaseId).HasMaxLength(255);
        builder.HasIndex(x => x.CaseId);
        builder.Property(x => x.StudyId).HasMaxLength(255);
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.WorldwideUniqueCaseId).HasMaxLength(255);
        builder.HasIndex(x => x.WorldwideUniqueCaseId);
        builder.Property(x => x.MessageId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.MessageId).IsUnique();
        builder.Property(x => x.As2From).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.As2From);
        builder.Property(x => x.As2To).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.As2To);
        builder.Property(x => x.TargetAuthority).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.TargetAuthority);
        builder.Property(x => x.Environment).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.PayloadDigestSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Mic).HasMaxLength(255).IsRequired();
        builder.Property(x => x.EncryptionAlgorithm).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SignatureAlgorithm).HasMaxLength(50).IsRequired();
        builder.Property(x => x.MdnMessageId).HasMaxLength(255);
        builder.Property(x => x.MdnDisposition).HasMaxLength(255);
        builder.Property(x => x.MdnMic).HasMaxLength(255);
        builder.Property(x => x.MdnStatus).HasMaxLength(50);
        builder.Property(x => x.AckAStatus).HasMaxLength(50);
        builder.Property(x => x.AckBStatus).HasMaxLength(50);
        builder.Property(x => x.AckBErrorsJson).HasColumnType("jsonb");
        builder.Property(x => x.AckCStatus).HasMaxLength(50);
        builder.Property(x => x.AckCDetailsJson).HasColumnType("jsonb");
        builder.Property(x => x.ErrorMessage).HasMaxLength(2000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class As2TransmissionAuditLogConfiguration : IEntityTypeConfiguration<As2TransmissionAuditLog>
{
    public void Configure(EntityTypeBuilder<As2TransmissionAuditLog> builder)
    {
        builder.ToTable("safety_as2_transmission_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TransmissionId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.TransmissionId);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Action);
        builder.Property(x => x.FromStatus).HasMaxLength(50);
        builder.Property(x => x.ToStatus).HasMaxLength(50);
        builder.Property(x => x.Details).HasMaxLength(2000).IsRequired();
        builder.HasIndex(x => x.CreatedAt);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CreatedBy);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class SafetySignalDetectionRunConfiguration : IEntityTypeConfiguration<SafetySignalDetectionRun>
{
    public void Configure(EntityTypeBuilder<SafetySignalDetectionRun> builder)
    {
        builder.ToTable("safety_signal_detection_runs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255);
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.TargetDrug).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TargetDrug);
        builder.Property(x => x.BenchmarkDataset).HasMaxLength(100).IsRequired();
        builder.Property(x => x.SummaryMetricsJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SafetySignalAlertConfiguration : IEntityTypeConfiguration<SafetySignalAlert>
{
    public void Configure(EntityTypeBuilder<SafetySignalAlert> builder)
    {
        builder.ToTable("safety_signal_alerts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DetectionRunId).HasMaxLength(36);
        builder.HasIndex(x => x.DetectionRunId);
        builder.Property(x => x.StudyId).HasMaxLength(255);
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.TargetDrug).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TargetDrug);
        builder.Property(x => x.MeddraTerm).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.MeddraTerm);
        builder.Property(x => x.TermLevel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.MeddraSoc).HasMaxLength(255);
        builder.HasIndex(x => x.MeddraSoc);
        builder.Property(x => x.Severity).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Severity);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.TriageSummary).HasMaxLength(2000).IsRequired();
        builder.Property(x => x.AssignedSafetyOfficer).HasMaxLength(255);
        builder.Property(x => x.ReviewDecision).HasMaxLength(100);
        builder.Property(x => x.ReviewJustification).HasMaxLength(2000);
        builder.Property(x => x.ClinicalSummary).HasMaxLength(4000);
        builder.Property(x => x.ReviewedBy).HasMaxLength(255);
        builder.Property(x => x.EsignatureManifestId).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class PeriodicSafetyReportRecordConfiguration : IEntityTypeConfiguration<PeriodicSafetyReportRecord>
{
    public void Configure(EntityTypeBuilder<PeriodicSafetyReportRecord> builder)
    {
        builder.ToTable("periodic_safety_reports");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ReportType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.ReportType);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.CompoundName).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CompoundName);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ReportPayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.ApprovedByUserId).HasMaxLength(255);
        builder.Property(x => x.EsignatureManifestId).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class DeviceSafetyInvestigationConfiguration : IEntityTypeConfiguration<DeviceSafetyInvestigation>
{
    public void Configure(EntityTypeBuilder<DeviceSafetyInvestigation> builder)
    {
        builder.ToTable("device_safety_investigations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.CaseNumber).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CaseNumber).IsUnique();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255);
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.DeficiencyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.DeficiencyId);
        builder.Property(x => x.DeviceName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DeviceUdiDi).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.DeviceUdiDi);
        builder.Property(x => x.LotNumber).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.LotNumber);
        builder.Property(x => x.SerialNumber).HasMaxLength(255);
        builder.HasIndex(x => x.SerialNumber);
        builder.Property(x => x.Classification).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Classification);
        builder.Property(x => x.Causality).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.InvestigationNotes).HasMaxLength(4000);
        builder.Property(x => x.RootCauseSummary).HasMaxLength(4000);
        builder.Property(x => x.CapaReference).HasMaxLength(255);
        builder.Property(x => x.SafetyPhysicianUserId).HasMaxLength(255);
        builder.Property(x => x.EsignatureManifestId).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}
