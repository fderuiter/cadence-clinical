using Cadence.Apps.Imaging.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Imaging.Infrastructure.Persistence.Configurations;

public class ImagingStudyEnvelopeConfiguration : IEntityTypeConfiguration<ImagingStudyEnvelope>
{
    public void Configure(EntityTypeBuilder<ImagingStudyEnvelope> builder)
    {
        builder.ToTable("imaging_study_envelopes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyInstanceUid).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.StudyInstanceUid).IsUnique();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.ModalitiesJson).HasColumnType("jsonb");
        builder.Property(x => x.StudyDate).HasMaxLength(8);
        builder.Property(x => x.StudyDescription).HasMaxLength(500);
        builder.Property(x => x.StoragePrefix).HasMaxLength(1000);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.AdjudicationStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.AdjudicationStatus);
        builder.Property(x => x.TenantId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TenantId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CreatedBy);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ImagingSeriesEnvelopeConfiguration : IEntityTypeConfiguration<ImagingSeriesEnvelope>
{
    public void Configure(EntityTypeBuilder<ImagingSeriesEnvelope> builder)
    {
        builder.ToTable("imaging_series_envelopes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyEnvelopeId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.StudyEnvelopeId);
        builder.Property(x => x.SeriesInstanceUid).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.SeriesInstanceUid);
        builder.Property(x => x.Modality).HasMaxLength(20).IsRequired();
        builder.HasIndex(x => x.Modality);
        builder.Property(x => x.BodyPartExamined).HasMaxLength(100);
        builder.Property(x => x.SeriesDescription).HasMaxLength(500);
        builder.Property(x => x.ProtocolName).HasMaxLength(255);
        builder.Property(x => x.FrameOfReferenceUid).HasMaxLength(64);
    }
}

public class ImagingInstanceEnvelopeConfiguration : IEntityTypeConfiguration<ImagingInstanceEnvelope>
{
    public void Configure(EntityTypeBuilder<ImagingInstanceEnvelope> builder)
    {
        builder.ToTable("imaging_instance_envelopes");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SeriesEnvelopeId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.SeriesEnvelopeId);
        builder.Property(x => x.SopInstanceUid).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.SopInstanceUid).IsUnique();
        builder.Property(x => x.SopClassUid).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.SopClassUid);
        builder.Property(x => x.PixelSpacingJson).HasColumnType("jsonb");
        builder.Property(x => x.ObjectKey).HasMaxLength(1000).IsRequired();
        builder.HasIndex(x => x.ObjectKey).IsUnique();
        builder.Property(x => x.ChecksumSha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.ChecksumSha256);
        builder.Property(x => x.CertificateId).HasMaxLength(36);
        builder.HasIndex(x => x.CertificateId);
    }
}

public class DeidentificationCertificateConfiguration : IEntityTypeConfiguration<DeidentificationCertificate>
{
    public void Configure(EntityTypeBuilder<DeidentificationCertificate> builder)
    {
        builder.ToTable("imaging_deid_certificates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyEnvelopeId).HasMaxLength(36);
        builder.HasIndex(x => x.StudyEnvelopeId);
        builder.Property(x => x.SourceSha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.SourceSha256);
        builder.Property(x => x.TargetSha256).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.TargetSha256);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.OriginalStudyInstanceUidHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.OriginalSopInstanceUidHash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.AnonymizedStudyInstanceUid).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.AnonymizedStudyInstanceUid);
        builder.Property(x => x.AnonymizedSopInstanceUid).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.AnonymizedSopInstanceUid);
        builder.Property(x => x.ScrubbedTagsJson).HasColumnType("jsonb");
        builder.Property(x => x.PixelMasksJson).HasColumnType("jsonb");
        builder.Property(x => x.OperatorIdentity).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Timestamp).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Signature).HasMaxLength(255).IsRequired();
    }
}

public class ImagingAuditLogConfiguration : IEntityTypeConfiguration<ImagingAuditLog>
{
    public void Configure(EntityTypeBuilder<ImagingAuditLog> builder)
    {
        builder.ToTable("imaging_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyEnvelopeId).HasMaxLength(36);
        builder.HasIndex(x => x.StudyEnvelopeId);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Action);
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ActorId);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.DetailsJson).HasColumnType("jsonb");
    }
}

public class ImagingAnnotationConfiguration : IEntityTypeConfiguration<ImagingAnnotation>
{
    public void Configure(EntityTypeBuilder<ImagingAnnotation> builder)
    {
        builder.ToTable("imaging_caliper_annotations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyEnvelopeId).HasMaxLength(36);
        builder.HasIndex(x => x.StudyEnvelopeId);
        builder.Property(x => x.SeriesEnvelopeId).HasMaxLength(36);
        builder.HasIndex(x => x.SeriesEnvelopeId);
        builder.Property(x => x.SopInstanceUid).HasMaxLength(64);
        builder.HasIndex(x => x.SopInstanceUid);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.TimepointName).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.TimepointName);
        builder.Property(x => x.LesionId).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.LesionId);
        builder.Property(x => x.LesionType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OrganSite).HasMaxLength(100).IsRequired();
        builder.Property(x => x.MeasurementCriteria).HasMaxLength(50).IsRequired();
        builder.Property(x => x.MeasurementType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PointsJson).HasColumnType("jsonb");
        builder.Property(x => x.HounsfieldStatsJson).HasColumnType("jsonb");
        builder.Property(x => x.ReaderId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ReaderId);
        builder.Property(x => x.Notes).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class TumorResponseAssessmentConfiguration : IEntityTypeConfiguration<TumorResponseAssessment>
{
    public void Configure(EntityTypeBuilder<TumorResponseAssessment> builder)
    {
        builder.ToTable("imaging_tumor_response_assessments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.TimepointName).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.TimepointName);
        builder.Property(x => x.AssessmentCriteria).HasMaxLength(50).IsRequired();
        builder.Property(x => x.TargetResponse).HasMaxLength(50).IsRequired();
        builder.Property(x => x.NonTargetStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.OverallResponse).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.OverallResponse);
        builder.Property(x => x.LockedBy).HasMaxLength(255);
        builder.Property(x => x.ReaderId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ReaderId);
        builder.Property(x => x.ManifestSha256).HasMaxLength(64).IsRequired();
        builder.Property(x => x.Signature).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ImagingAdjudicationSessionConfiguration : IEntityTypeConfiguration<ImagingAdjudicationSession>
{
    public void Configure(EntityTypeBuilder<ImagingAdjudicationSession> builder)
    {
        builder.ToTable("imaging_adjudication_sessions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyEnvelopeId).HasMaxLength(36);
        builder.HasIndex(x => x.StudyEnvelopeId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.TimepointName).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.TimepointName);
        builder.Property(x => x.AssessmentCriteria).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reader1Id).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.Reader1Id);
        builder.Property(x => x.Reader1Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Reader1Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reader1AssessmentJson).HasColumnType("jsonb");
        builder.Property(x => x.Reader2Id).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.Reader2Id);
        builder.Property(x => x.Reader2Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Reader2Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Reader2AssessmentJson).HasColumnType("jsonb");
        builder.Property(x => x.AdjudicatorId).HasMaxLength(255);
        builder.HasIndex(x => x.AdjudicatorId);
        builder.Property(x => x.AdjudicatorName).HasMaxLength(255);
        builder.Property(x => x.AdjudicatorAssessmentJson).HasColumnType("jsonb");
        builder.Property(x => x.DiscordanceReasonsJson).HasColumnType("jsonb");
        builder.Property(x => x.WorkflowStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.WorkflowStatus);
        builder.Property(x => x.FinalConsensusResponse).HasMaxLength(50);
        builder.Property(x => x.BindingReader).HasMaxLength(50);
        builder.Property(x => x.AdjudicationRationale).HasMaxLength(2000);
        builder.Property(x => x.ElectronicSignatureJson).HasColumnType("jsonb");
        builder.Property(x => x.LockedBy).HasMaxLength(255);
        builder.Property(x => x.SessionDigest).HasMaxLength(64).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TenantId);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}
