using Cadence.Apps.Econsent.Domain;
using Cadence.Apps.Econsent.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Security.FieldProtection;

using MassTransit;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Apps.Econsent.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for eConsent microservice persistence gateway. Signer e-mail addresses are encrypted at rest
/// with <paramref name="fieldEncryptor"/> (#4770).
/// @req:PRD-SYS-053
/// </summary>
public sealed class EconsentDbContext(DbContextOptions<EconsentDbContext> options, IFieldEncryptor fieldEncryptor)
    : DbContext(options), IFieldEncryptingDbContext
{
    /// <inheritdoc />
    public IFieldEncryptor FieldEncryptor { get; } = fieldEncryptor;

    public DbSet<ConsentDocument> ConsentDocuments => Set<ConsentDocument>();
    public DbSet<EtmfArchivalDelivery> EtmfArchivalDeliveries => Set<EtmfArchivalDelivery>();
    public DbSet<SubjectConsent> SubjectConsents => Set<SubjectConsent>();
    public DbSet<ComprehensionCheck> ComprehensionChecks => Set<ComprehensionCheck>();
    public DbSet<ComprehensionResult> ComprehensionResults => Set<ComprehensionResult>();
    public DbSet<ConsentSignature> ConsentSignatures => Set<ConsentSignature>();
    public DbSet<ConsentClause> ConsentClauses => Set<ConsentClause>();
    public DbSet<ConsentTemplate> ConsentTemplates => Set<ConsentTemplate>();
    public DbSet<ConsentAuditLog> ConsentAuditLogs => Set<ConsentAuditLog>();
    public DbSet<ConsentTranslation> ConsentTranslations => Set<ConsentTranslation>();
    public DbSet<GranularConsentOption> GranularConsentOptions => Set<GranularConsentOption>();
    public DbSet<SubjectConsentOptionSelection> SubjectConsentOptionSelections => Set<SubjectConsentOptionSelection>();
    public DbSet<ReconsentRequirement> ReconsentRequirements => Set<ReconsentRequirement>();
    public DbSet<ConsentWithdrawal> ConsentWithdrawals => Set<ConsentWithdrawal>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, FieldEncryptionModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ConsentDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new EtmfArchivalDeliveryConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConsentConfiguration());
        modelBuilder.ApplyConfiguration(new ComprehensionCheckConfiguration());
        modelBuilder.ApplyConfiguration(new ComprehensionResultConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentSignatureConfiguration(FieldEncryptor));
        modelBuilder.ApplyConfiguration(new ConsentClauseConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentTemplateConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentTranslationConfiguration());
        modelBuilder.ApplyConfiguration(new GranularConsentOptionConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConsentOptionSelectionConfiguration());
        modelBuilder.ApplyConfiguration(new ReconsentRequirementConfiguration());
        modelBuilder.ApplyConfiguration(new ConsentWithdrawalConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());

        modelBuilder.AddTransactionalOutboxEntities();
    }
}
