using Cadence.Apps.Knowledge.Domain;
using Cadence.Apps.Knowledge.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Knowledge.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Knowledge microservice persistence gateway.
/// @req:PRD-SYS-053
/// </summary>
public sealed class KnowledgeDbContext(DbContextOptions<KnowledgeDbContext> options) : DbContext(options)
{
    public DbSet<KnowledgeCategory> KnowledgeCategories => Set<KnowledgeCategory>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<KnowledgeArticleVersion> KnowledgeArticleVersions => Set<KnowledgeArticleVersion>();
    public DbSet<KnowledgeArticleAuditLog> KnowledgeArticleAuditLogs => Set<KnowledgeArticleAuditLog>();
    public DbSet<ContextualHelpMapping> ContextualHelpMappings => Set<ContextualHelpMapping>();
    public DbSet<ProtocolKnowledgeChunk> ProtocolKnowledgeChunks => Set<ProtocolKnowledgeChunk>();
    public DbSet<TrainingCurriculum> TrainingCurricula => Set<TrainingCurriculum>();
    public DbSet<TrainingAssignment> TrainingAssignments => Set<TrainingAssignment>();
    public DbSet<DelegationRecord> DelegationRecords => Set<DelegationRecord>();
    public DbSet<OrgAuditLog> OrgAuditLogs => Set<OrgAuditLog>();
    public DbSet<TrainingCertificate> TrainingCertificates => Set<TrainingCertificate>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new KnowledgeCategoryConfiguration());
        modelBuilder.ApplyConfiguration(new KnowledgeArticleConfiguration());
        modelBuilder.ApplyConfiguration(new KnowledgeArticleVersionConfiguration());
        modelBuilder.ApplyConfiguration(new KnowledgeArticleAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new ContextualHelpMappingConfiguration());
        modelBuilder.ApplyConfiguration(new ProtocolKnowledgeChunkConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
