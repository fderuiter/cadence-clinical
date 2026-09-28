using Cadence.Apps.Etmf.Domain;
using Cadence.Apps.Etmf.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Etmf.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for eTMF microservice persistence gateway.
/// @req:PRD-SYS-053
/// </summary>
public sealed class EtmfDbContext(DbContextOptions<EtmfDbContext> options) : DbContext(options)
{
    public DbSet<TmfDocument> TmfDocuments => Set<TmfDocument>();
    public DbSet<TmfExpectedDocument> TmfExpectedDocuments => Set<TmfExpectedDocument>();
    public DbSet<TmfAuditLedgerSeal> TmfAuditLedgerSeals => Set<TmfAuditLedgerSeal>();
    public DbSet<TmfAuditLog> TmfAuditLogs => Set<TmfAuditLog>();
    public DbSet<TmfDocumentExpirationAlertState> TmfDocumentExpirationAlertStates => Set<TmfDocumentExpirationAlertState>();
    public DbSet<TmfDocumentQcTransition> TmfDocumentQcTransitions => Set<TmfDocumentQcTransition>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new TmfDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new TmfExpectedDocumentConfiguration());
        modelBuilder.ApplyConfiguration(new TmfAuditLedgerSealConfiguration());
        modelBuilder.ApplyConfiguration(new TmfAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new TmfDocumentExpirationAlertStateConfiguration());
        modelBuilder.ApplyConfiguration(new TmfDocumentQcTransitionConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
