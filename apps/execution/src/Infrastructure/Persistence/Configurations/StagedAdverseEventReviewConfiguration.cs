using Cadence.Apps.Execution.Domain.Observations;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Domain.Toxicity;
using Cadence.Kernel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;

public sealed class StagedAdverseEventReviewConfiguration : IEntityTypeConfiguration<StagedAdverseEventReview>
{
    public void Configure(EntityTypeBuilder<StagedAdverseEventReview> builder)
    {
        builder.ToTable("staged_adverse_event_reviews");

        builder.HasKey(r => r.Id);

        builder.Property(r => r.Id)
            .HasColumnName("id");

        builder.Property(r => r.StudyId)
            .HasColumnName("study_id")
            .HasConversion(v => v.Value, v => new StudyCode(v))
            .IsRequired();

        builder.Property(r => r.SiteId)
            .HasColumnName("site_id")
            .HasConversion(c => c.HasValue ? c.Value.Value : null, v => v == null ? null : (SiteCode?)new SiteCode(v));

        builder.Property(r => r.SubjectId)
            .HasColumnName("subject_id")
            .HasConversion(v => v.Value, v => new SubjectCode(v))
            .IsRequired();

        builder.Property(r => r.VisitId)
            .HasColumnName("visit_id");

        builder.Property(r => r.ObservationId)
            .HasColumnName("observation_id")
            .HasConversion(c => c.HasValue ? c.Value.Value.ToString() : null, v => string.IsNullOrEmpty(v) ? (ObservationId?)null : ObservationId.Parse(v));

        builder.Property(r => r.TestCode)
            .HasColumnName("test_code")
            .HasConversion(v => v.Value, v => new TestCode(v))
            .IsRequired();

        builder.Property(r => r.TestName)
            .HasColumnName("test_name")
            .IsRequired();

        builder.Property(r => r.LabValue)
            .HasColumnName("lab_value");

        builder.Property(r => r.Unit)
            .HasColumnName("unit");

        builder.Property(r => r.CtcaeGrade)
            .HasColumnName("ctcae_grade");

        builder.Property(r => r.GradingScale)
            .HasColumnName("grading_scale");

        builder.Property(r => r.ToxicityTerm)
            .HasColumnName("toxicity_term");

        builder.Property(r => r.ClinicalDescription)
            .HasColumnName("clinical_description");

        builder.Property(r => r.IsHysLawWarning)
            .HasColumnName("is_hys_law_warning");

        builder.Property(r => r.IsDeltaCheckAlert)
            .HasColumnName("is_delta_check_alert");

        builder.Property(r => r.DeltaPercentage)
            .HasColumnName("delta_percentage");

        builder.Property(r => r.ReviewStatus)
            .HasColumnName("review_status")
            .IsRequired();

        builder.Property(r => r.ReviewedBy)
            .HasColumnName("reviewed_by");

        builder.Property(r => r.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(r => r.InvestigatorNotes)
            .HasColumnName("investigator_notes");

        builder.Property(r => r.Version)
            .HasColumnName("version");

        builder.Property(r => r.IsDeleted)
            .HasColumnName("is_deleted");

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at");

        builder.Property(r => r.CreatedBy)
            .HasColumnName("created_by");

        builder.Property(r => r.ReasonForChange)
            .HasColumnName("reason_for_change");

        builder.HasIndex(r => new { r.StudyId, r.ReviewStatus });
        builder.HasIndex(r => r.SubjectId);
    }
}
