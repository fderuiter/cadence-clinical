using Cadence.Apps.Ctms.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Ctms.Infrastructure.Persistence.Configurations;

public sealed class CtmsAuditLogConfiguration : IEntityTypeConfiguration<CtmsAuditLog>
{
    public void Configure(EntityTypeBuilder<CtmsAuditLog> builder)
    {
        builder.ToTable("ctms_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Timestamp).IsRequired();
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.UserRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();

        builder.HasIndex(x => x.Timestamp);
        builder.HasIndex(x => x.UserId);
    }
}

public sealed class CtmsClinicalQueryConfiguration : IEntityTypeConfiguration<CtmsClinicalQuery>
{
    public void Configure(EntityTypeBuilder<CtmsClinicalQuery> builder)
    {
        builder.ToTable("ctms_clinical_queries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VisitId).HasMaxLength(255);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Explanation).HasMaxLength(1000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
        builder.HasIndex(x => x.VisitId);
    }
}

public sealed class CtmsCraAllocationConfiguration : IEntityTypeConfiguration<CtmsCraAllocation>
{
    public void Configure(EntityTypeBuilder<CtmsCraAllocation> builder)
    {
        builder.ToTable("ctms_cra_allocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.CraId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EffectiveStartDate).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.CraId);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CtmsDefeatedMonitoringVisitConfiguration : IEntityTypeConfiguration<CtmsDefeatedMonitoringVisit>
{
    public void Configure(EntityTypeBuilder<CtmsDefeatedMonitoringVisit> builder)
    {
        builder.ToTable("ctms_defeated_monitoring_visits");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.VisitId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DeviceTimestamp).IsRequired();
        builder.Property(x => x.OfflineSyncMarkers).HasColumnType("jsonb");
        builder.Property(x => x.Status).HasMaxLength(100).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255);
        builder.Property(x => x.ReasonForChange).HasMaxLength(500);
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.VisitId);
    }
}

public sealed class CtmsDelegationConfiguration : IEntityTypeConfiguration<CtmsDelegation>
{
    public void Configure(EntityTypeBuilder<CtmsDelegation> builder)
    {
        builder.ToTable("ctms_delegations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StaffUserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TaskCodes).HasColumnType("jsonb").IsRequired();
        builder.Property(x => x.StartDate).HasMaxLength(50).IsRequired();
        builder.Property(x => x.EndDate).HasMaxLength(50);
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.SignedOff).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StaffUserId);
    }
}

public sealed class CtmsGeneratedLetterConfiguration : IEntityTypeConfiguration<CtmsGeneratedLetter>
{
    public void Configure(EntityTypeBuilder<CtmsGeneratedLetter> builder)
    {
        builder.ToTable("ctms_generated_letters");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.VisitId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.LetterType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RenderedContent).HasMaxLength(100000).IsRequired();
        builder.Property(x => x.PdfObjectKey).HasMaxLength(1000);
        builder.Property(x => x.PdfSha256Hash).HasMaxLength(64);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.IsSigned).IsRequired();
        builder.Property(x => x.SignatureManifestation).HasColumnType("jsonb");
        builder.Property(x => x.EtmfDocumentId).HasMaxLength(100);
        builder.Property(x => x.RecipientEmail).HasMaxLength(255);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.VisitId);
    }
}

public sealed class CtmsInvestigatorGrantConfiguration : IEntityTypeConfiguration<CtmsInvestigatorGrant>
{
    public void Configure(EntityTypeBuilder<CtmsInvestigatorGrant> builder)
    {
        builder.ToTable("ctms_investigator_grants");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TotalBudget).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CtmsMonitoringVisitFindingConfiguration : IEntityTypeConfiguration<CtmsMonitoringVisitFinding>
{
    public void Configure(EntityTypeBuilder<CtmsMonitoringVisitFinding> builder)
    {
        builder.ToTable("ctms_monitoring_visit_findings", t =>
        {
            t.HasCheckConstraint(
                "ck_ctms_monitoring_visit_findings_resolution_status",
                "\"ResolutionStatus\" IN ('OPEN', 'UNDER_REVIEW', 'RESOLVED', 'CLOSED')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.VisitId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.Text).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Severity).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ResolutionStatus)
            .HasConversion(
                v => FindingResolutionStatusCodec.ToCode(v),
                v => FindingResolutionStatusCodec.Parse(v))
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.OfflineSyncMarkers).HasColumnType("jsonb");
        builder.Property(x => x.SyncStatus).HasMaxLength(50);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.VisitId);
    }
}

public sealed class CtmsMonitoringVisitConfiguration : IEntityTypeConfiguration<CtmsMonitoringVisit>
{
    public void Configure(EntityTypeBuilder<CtmsMonitoringVisit> builder)
    {
        builder.ToTable("ctms_monitoring_visits", t =>
        {
            t.HasCheckConstraint(
                "ck_ctms_monitoring_visits_status",
                "\"Status\" IN ('SCHEDULED', 'IN_PROGRESS', 'COMPLETED', 'REPORT_DRAFTED', 'APPROVED', 'SIGNED_OFF', 'CANCELLED')");
        });
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CraId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.VisitType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ScheduledDate).IsRequired();
        builder.Property(x => x.Status)
            .HasConversion(
                v => MonitoringVisitStatusCodec.ToCode(v),
                v => MonitoringVisitStatusCodec.Parse(v))
            .HasMaxLength(50)
            .IsRequired();
        builder.Property(x => x.SignatureManifestation).HasColumnType("jsonb");
        builder.Property(x => x.Signer).HasMaxLength(255);
        builder.Property(x => x.OfflineSyncMarkers).HasColumnType("jsonb");
        builder.Property(x => x.SyncStatus).HasMaxLength(50);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();
        builder.Ignore(x => x.DomainEvents);

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CtmsRecruitmentRecordConfiguration : IEntityTypeConfiguration<CtmsRecruitmentRecord>
{
    public void Configure(EntityTypeBuilder<CtmsRecruitmentRecord> builder)
    {
        builder.ToTable("ctms_recruitment_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ScreenedCount).IsRequired();
        builder.Property(x => x.EnrolledCount).IsRequired();
        builder.Property(x => x.TargetCount).IsRequired();
        builder.Property(x => x.AsOfDate).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CtmsRegulatoryFormConfiguration : IEntityTypeConfiguration<CtmsRegulatoryForm>
{
    public void Configure(EntityTypeBuilder<CtmsRegulatoryForm> builder)
    {
        builder.ToTable("ctms_regulatory_forms");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.FormType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.RenderedContent).HasMaxLength(100000).IsRequired();
        builder.Property(x => x.ApprovalStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SignatureManifestation).HasColumnType("jsonb");
        builder.Property(x => x.Signer).HasMaxLength(255);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}

public sealed class CtmsSiteMilestoneConfiguration : IEntityTypeConfiguration<CtmsSiteMilestone>
{
    public void Configure(EntityTypeBuilder<CtmsSiteMilestone> builder)
    {
        builder.ToTable("ctms_site_milestones");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.MilestoneType).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.MilestoneType);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StudyId);
    }
}


public sealed class CtmsBudgetLineItemConfiguration : IEntityTypeConfiguration<CtmsBudgetLineItem>
{
    public void Configure(EntityTypeBuilder<CtmsBudgetLineItem> builder)
    {
        builder.ToTable("ctms_budget_line_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.GrantId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Description).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Amount).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<CtmsInvestigatorGrant>()
            .WithMany()
            .HasForeignKey(x => x.GrantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.GrantId);
    }
}

public sealed class CtmsPaymentMilestoneConfiguration : IEntityTypeConfiguration<CtmsPaymentMilestone>
{
    public void Configure(EntityTypeBuilder<CtmsPaymentMilestone> builder)
    {
        builder.ToTable("ctms_payment_milestones");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.GrantId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.MilestoneName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TriggerCondition).HasMaxLength(100).IsRequired();
        builder.Property(x => x.Amount).IsRequired();
        builder.Property(x => x.IsTriggered).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<CtmsInvestigatorGrant>()
            .WithMany()
            .HasForeignKey(x => x.GrantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.GrantId);
    }
}

public sealed class DoaDelegationRecordConfiguration : IEntityTypeConfiguration<DoaDelegationRecord>
{
    public void Configure(EntityTypeBuilder<DoaDelegationRecord> builder)
    {
        builder.ToTable("doa_delegation_records");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.SiteId).IsRequired();
        builder.Property(x => x.StaffUserId).IsRequired();
        builder.Property(x => x.TaskCode).IsRequired();
        builder.Property(x => x.StartDate).IsRequired();
        builder.Property(x => x.Status).IsRequired();
        builder.Property(x => x.IsActive).IsRequired();
        builder.Property(x => x.IsDeleted).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).IsRequired();
        builder.Property(x => x.ReasonForChange).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.StaffUserId);
        builder.HasIndex(x => x.TaskCode);
    }
}

public sealed class CtmsInvestigatorPayableConfiguration : IEntityTypeConfiguration<CtmsInvestigatorPayable>
{
    public void Configure(EntityTypeBuilder<CtmsInvestigatorPayable> builder)
    {
        builder.ToTable("ctms_investigator_payables");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.GrantId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.MilestoneId).HasMaxLength(36);
        builder.Property(x => x.Amount).IsRequired();
        builder.Property(x => x.PaymentStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasOne<CtmsInvestigatorGrant>()
            .WithMany()
            .HasForeignKey(x => x.GrantId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<CtmsPaymentMilestone>()
            .WithMany()
            .HasForeignKey(x => x.MilestoneId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(x => x.GrantId);
        builder.HasIndex(x => x.MilestoneId);
    }
}

public sealed class CtmsSiteGreenlightGateConfiguration : IEntityTypeConfiguration<CtmsSiteGreenlightGate>
{
    public void Configure(EntityTypeBuilder<CtmsSiteGreenlightGate> builder)
    {
        builder.ToTable("ctms_site_greenlight_gates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CountryCode).HasMaxLength(10).IsRequired();
        builder.Property(x => x.OverallStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.GreenlightToken).HasMaxLength(1024);
        builder.Property(x => x.GreenlightCertifiedBy).HasMaxLength(255);
        builder.Property(x => x.OverrideSponsorUserId).HasMaxLength(255);
        builder.Property(x => x.OverrideMedicalUserId).HasMaxLength(255);
        builder.Property(x => x.OverrideJustification).HasMaxLength(2000);
        builder.Property(x => x.UnfulfilledReasons).HasMaxLength(2000);
        builder.Property(x => x.CreatedAt).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.VersionIndex).IsRequired();

        builder.HasIndex(x => x.StudyId);
        builder.HasIndex(x => x.SiteId);
        builder.HasIndex(x => x.CountryCode);
        builder.HasIndex(x => new { x.StudyId, x.CountryCode });
    }
}
