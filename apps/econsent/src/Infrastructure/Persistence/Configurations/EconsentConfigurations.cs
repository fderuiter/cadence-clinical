using Cadence.Apps.Econsent.Domain;
using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Econsent.Infrastructure.Persistence.Configurations;

public class ConsentDocumentConfiguration : IEntityTypeConfiguration<ConsentDocument>
{
    public void Configure(EntityTypeBuilder<ConsentDocument> builder)
    {
        builder.ToTable("consent_documents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.DocumentName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public sealed class ConsentDocumentRevisionConfiguration : IEntityTypeConfiguration<ConsentDocumentRevision>
{
    public void Configure(EntityTypeBuilder<ConsentDocumentRevision> builder)
    {
        builder.ToTable("consent_document_revisions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DocumentId).HasMaxLength(36).IsRequired();
        builder.Property(x => x.RevisionNumber).IsRequired();
        builder.Property(x => x.StudyCode).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteCode).HasMaxLength(255).IsRequired();
        builder.Property(x => x.DocumentName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Content).IsRequired();
        builder.Property(x => x.RecordedAt).IsRequired();
        builder.Property(x => x.RecordedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();

        builder.HasOne<ConsentDocument>()
            .WithMany()
            .HasForeignKey(x => x.DocumentId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => x.DocumentId);
        builder.HasIndex(x => new { x.DocumentId, x.RevisionNumber }).IsUnique();
    }
}

public class EtmfArchivalDeliveryConfiguration : IEntityTypeConfiguration<EtmfArchivalDelivery>
{
    public void Configure(EntityTypeBuilder<EtmfArchivalDelivery> builder)
    {
        builder.ToTable("etmf_archival_deliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CorrelationId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CorrelationId).IsUnique();
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.Property(x => x.EtmfDocumentId).HasMaxLength(36);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SubjectConsentConfiguration : IEntityTypeConfiguration<SubjectConsent>
{
    public void Configure(EntityTypeBuilder<SubjectConsent> builder)
    {
        builder.ToTable("subject_consents");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.ProtocolVersion).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SignatureManifestJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ComprehensionCheckConfiguration : IEntityTypeConfiguration<ComprehensionCheck>
{
    public void Configure(EntityTypeBuilder<ComprehensionCheck> builder)
    {
        builder.ToTable("comprehension_checks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.QuestionsJson).HasColumnType("jsonb");
        builder.Property(x => x.ExpectedAnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.ThresholdPolicyJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ComprehensionResultConfiguration : IEntityTypeConfiguration<ComprehensionResult>
{
    public void Configure(EntityTypeBuilder<ComprehensionResult> builder)
    {
        builder.ToTable("comprehension_results");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.QuestionsJson).HasColumnType("jsonb");
        builder.Property(x => x.ExpectedAnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.ThresholdPolicyJson).HasColumnType("jsonb");
        builder.Property(x => x.SubmittedAnswersJson).HasColumnType("jsonb");
        builder.Property(x => x.Score).HasPrecision(5, 2);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

/// <summary>
/// Consent signature mapping. The signer e-mail is encrypted at rest and the raw signature capture is replaced by a
/// SHA-256 binding to the signed content (#4770).
/// </summary>
public class ConsentSignatureConfiguration(IFieldEncryptor fieldEncryptor) : IEntityTypeConfiguration<ConsentSignature>
{
    /// <summary>Encryption purpose (associated data) for <see cref="ConsentSignature.SignerEmail"/>.</summary>
    public const string SignerEmailPurpose = "econsent.consent_signatures.signer_email";

    /// <summary>Longest signer e-mail accepted, in characters.</summary>
    public const int MaxSignerEmailLength = 255;

    public void Configure(EntityTypeBuilder<ConsentSignature> builder)
    {
        builder.ToTable("consent_signatures");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.Role).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SignerName).HasMaxLength(255);
        builder.Property(x => x.SignerEmail).IsEncrypted(fieldEncryptor, SignerEmailPurpose, MaxSignerEmailLength);
        builder.Property(x => x.Meaning).HasMaxLength(255);
        builder.Property(x => x.DigestSha256).HasMaxLength(64);
        builder.Property(x => x.SignatureBindingSha256).HasMaxLength(64);
        builder.Property(x => x.LarRelationship).HasMaxLength(100);
        builder.Property(x => x.LarAuthorityBasis).HasMaxLength(500);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ConsentClauseConfiguration : IEntityTypeConfiguration<ConsentClause>
{
    public void Configure(EntityTypeBuilder<ConsentClause> builder)
    {
        builder.ToTable("consent_clauses");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ClauseId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ClauseId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ConsentTemplateConfiguration : IEntityTypeConfiguration<ConsentTemplate>
{
    public void Configure(EntityTypeBuilder<ConsentTemplate> builder)
    {
        builder.ToTable("consent_templates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.TemplateName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProtocolVersion).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ClausesJson).HasColumnType("jsonb");
        builder.Property(x => x.WorkflowStepsJson).HasColumnType("jsonb");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ConsentAuditLogConfiguration : IEntityTypeConfiguration<ConsentAuditLog>
{
    public void Configure(EntityTypeBuilder<ConsentAuditLog> builder)
    {
        builder.ToTable("consent_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.HasIndex(x => x.Timestamp);
        builder.Property(x => x.ActorId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ActorId);
        builder.Property(x => x.ActorRole).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Action).HasMaxLength(50).IsRequired();
        builder.Property(x => x.DocumentId).HasMaxLength(36);
        builder.HasIndex(x => x.DocumentId);
        builder.Property(x => x.Details).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.TenantId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PreviousDigest).HasMaxLength(64);
        builder.Property(x => x.Sha256Digest).HasMaxLength(64);
    }
}

public class ConsentTranslationConfiguration : IEntityTypeConfiguration<ConsentTranslation>
{
    public void Configure(EntityTypeBuilder<ConsentTranslation> builder)
    {
        builder.ToTable("consent_translations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TranslationId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TranslationId);
        builder.Property(x => x.SourceId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SourceId);
        builder.Property(x => x.SourceType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.LanguageCode).HasMaxLength(10).IsRequired();
        builder.HasIndex(x => x.LanguageCode);
        builder.Property(x => x.TranslatedTitle).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class GranularConsentOptionConfiguration : IEntityTypeConfiguration<GranularConsentOption>
{
    public void Configure(EntityTypeBuilder<GranularConsentOption> builder)
    {
        builder.ToTable("granular_consent_options");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.OptionCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.OptionCode);
        builder.Property(x => x.Title).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class SubjectConsentOptionSelectionConfiguration : IEntityTypeConfiguration<SubjectConsentOptionSelection>
{
    public void Configure(EntityTypeBuilder<SubjectConsentOptionSelection> builder)
    {
        builder.ToTable("subject_consent_option_selections");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ConsentId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ConsentId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.OptionCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.OptionCode);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ReconsentRequirementConfiguration : IEntityTypeConfiguration<ReconsentRequirement>
{
    public void Configure(EntityTypeBuilder<ReconsentRequirement> builder)
    {
        builder.ToTable("reconsent_requirements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255);
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ChangeSummary).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.SubstantiveChangesJson).HasColumnType("jsonb");
        builder.Property(x => x.CompletedConsentId).HasMaxLength(36);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ConsentWithdrawalConfiguration : IEntityTypeConfiguration<ConsentWithdrawal>
{
    public void Configure(EntityTypeBuilder<ConsentWithdrawal> builder)
    {
        builder.ToTable("consent_withdrawals");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.TemplateId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TemplateId);
        builder.Property(x => x.ReasonCategory).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ReasonDetail).HasMaxLength(1000).IsRequired();
        builder.Property(x => x.Scope).HasMaxLength(100).IsRequired();
        builder.Property(x => x.InvestigatorId).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class BiometricLivenessVerificationConfiguration : IEntityTypeConfiguration<BiometricLivenessVerification>
{
    public void Configure(EntityTypeBuilder<BiometricLivenessVerification> builder)
    {
        builder.ToTable("biometric_liveness_verifications");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ConsentId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ConsentId);
        builder.Property(x => x.SubjectPseudonym).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectPseudonym);
        builder.Property(x => x.LivenessToken).IsRequired();
        builder.Property(x => x.LivenessTokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.LivenessTokenHash).IsUnique();
        builder.Property(x => x.AntiSpoofScore).HasColumnType("numeric(5,4)").IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ReplicatedScopeLockConfiguration : IEntityTypeConfiguration<ReplicatedScopeLock>
{
    public void Configure(EntityTypeBuilder<ReplicatedScopeLock> builder)
    {
        builder.ToTable("replicated_scope_locks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.LockId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.LockId);
        builder.Property(x => x.StudyRef).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyRef);
        builder.Property(x => x.StudyCode).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyCode);
        builder.Property(x => x.SiteCode).HasMaxLength(255);
        builder.HasIndex(x => x.SiteCode);
        builder.Property(x => x.SubjectCode).HasMaxLength(255);
        builder.HasIndex(x => x.SubjectCode);
        builder.Property(x => x.ScopeKind).HasMaxLength(64).IsRequired();
        builder.Property(x => x.ScopeRef).HasMaxLength(255).IsRequired();
        builder.Property(x => x.LockKind).HasMaxLength(32).IsRequired();
        builder.Property(x => x.Milestone).HasMaxLength(64).IsRequired();
        builder.HasIndex(x => x.IsActive);
        builder.Property(x => x.AppliedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReleasedBy).HasMaxLength(255);
        builder.Property(x => x.ReleaseReason).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}
