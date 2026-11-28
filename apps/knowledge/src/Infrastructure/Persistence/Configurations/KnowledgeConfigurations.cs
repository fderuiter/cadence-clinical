using Cadence.Apps.Knowledge.Domain;
using Cadence.Kernel.Tenancy;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence.Configurations;

public class KnowledgeCategoryConfiguration : IEntityTypeConfiguration<KnowledgeCategory>
{
    public void Configure(EntityTypeBuilder<KnowledgeCategory> builder)
    {
        builder.ToTable("knowledge_categories");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Name).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Slug).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ParentId).HasMaxLength(36);
        builder.HasIndex(x => x.ParentId);
        builder.Property(x => x.PersonaVisibility).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class KnowledgeArticleConfiguration : IEntityTypeConfiguration<KnowledgeArticle>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticle> builder)
    {
        builder.ToTable("knowledge_articles");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.Slug).HasMaxLength(255).IsRequired();
        builder.Property(x => x.Title).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CategoryId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.CategoryId);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.VersionLabel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CurrentPublishedVersionId).HasMaxLength(36);
        builder.HasIndex(x => x.CurrentPublishedVersionId);
        builder.Property(x => x.AuthorUserId).HasMaxLength(255).IsRequired();
        builder.Property(x => x.LastEditedBy).HasMaxLength(255);
        builder.Property(x => x.ApprovedBy).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class KnowledgeArticleVersionConfiguration : IEntityTypeConfiguration<KnowledgeArticleVersion>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticleVersion> builder)
    {
        builder.ToTable("knowledge_article_versions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ArticleId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ArticleId);
        builder.HasIndex(x => x.VersionIndex);
        builder.HasIndex(x => new { x.ArticleId, x.VersionIndex }).IsUnique();
        builder.HasOne<KnowledgeArticle>()
            .WithMany()
            .HasForeignKey(x => x.ArticleId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.Property(x => x.VersionLabel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.StatusAtSnapshot).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class KnowledgeArticleAuditLogConfiguration : IEntityTypeConfiguration<KnowledgeArticleAuditLog>
{
    public void Configure(EntityTypeBuilder<KnowledgeArticleAuditLog> builder)
    {
        builder.ToTable("knowledge_article_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ArticleId).HasMaxLength(36);
        builder.HasIndex(x => x.ArticleId);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Action);
        builder.Property(x => x.PreviousStatus).HasMaxLength(50);
        builder.Property(x => x.NewStatus).HasMaxLength(50);
        builder.Property(x => x.Details).HasMaxLength(2000);
        builder.Property(x => x.PreviousDigest).HasMaxLength(128);
        builder.Property(x => x.Sha256Digest).HasMaxLength(128);
        builder.HasIndex(x => x.CreatedAt);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CreatedBy);
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ContextualHelpMappingConfiguration : IEntityTypeConfiguration<ContextualHelpMapping>
{
    public void Configure(EntityTypeBuilder<ContextualHelpMapping> builder)
    {
        builder.ToTable("knowledge_contextual_help_mappings");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.RoutePattern).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.RoutePattern);
        builder.Property(x => x.Persona).HasMaxLength(100);
        builder.HasIndex(x => x.Persona);
        builder.Property(x => x.ArticleId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ArticleId);
        builder.Property(x => x.SectionAnchor).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public class ProtocolKnowledgeChunkConfiguration : IEntityTypeConfiguration<ProtocolKnowledgeChunk>
{
    public void Configure(EntityTypeBuilder<ProtocolKnowledgeChunk> builder)
    {
        builder.ToTable("protocol_knowledge_chunks");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.DocumentId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.DocumentId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.ProtocolVersion).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.ProtocolVersion);
        builder.Property(x => x.DocumentType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.SectionNumber).HasMaxLength(50);
        builder.Property(x => x.SectionTitle).HasMaxLength(500).IsRequired();
        builder.HasIndex(x => x.PageNumber);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000).IsRequired();
    }
}

public sealed class TrainingCurriculumConfiguration : IEntityTypeConfiguration<TrainingCurriculum>
{
    public void Configure(EntityTypeBuilder<TrainingCurriculum> builder)
    {
        builder.ToTable("TrainingCurricula");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudyId)
            .HasConversion(v => v.Value, raw => new KernelStudyCode(raw))
            .IsRequired();
        builder.Property(x => x.QualificationCode).HasMaxLength(128).HasDefaultValue("protocol_training").IsRequired();
        builder.Property(x => x.ProtocolVersionNumber).HasDefaultValue(1);
        builder.Property(x => x.PassingScore).HasDefaultValue(0.8);
    }
}

public sealed class TrainingAssignmentConfiguration : IEntityTypeConfiguration<TrainingAssignment>
{
    public void Configure(EntityTypeBuilder<TrainingAssignment> builder)
    {
        builder.ToTable("TrainingAssignments");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.StudyId)
            .HasConversion(v => v.Value, raw => new KernelStudyCode(raw))
            .IsRequired();
        builder.Property(x => x.SiteId)
            .HasConversion(v => v.Value, raw => new KernelSiteCode(raw))
            .IsRequired();
        builder.Property(x => x.EtmfDocumentId).HasMaxLength(128);
        builder.Property(x => x.KernelArtifactVersionRef).HasMaxLength(128);
        builder.Property(x => x.KernelQualificationRef).HasMaxLength(128);
    }
}

public sealed class TrainingCertificateConfiguration : IEntityTypeConfiguration<TrainingCertificate>
{
    public void Configure(EntityTypeBuilder<TrainingCertificate> builder)
    {
        builder.ToTable("TrainingCertificates");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.EtmfDocumentId).HasMaxLength(128);
        builder.Property(x => x.KernelArtifactVersionRef).HasMaxLength(128);
        builder.Property(x => x.KernelQualificationRef).HasMaxLength(128);
    }
}
