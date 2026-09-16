using Cadence.Apps.Quality.Domain;
using Cadence.Apps.Quality.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Quality.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Quality microservice persistence gateway.
/// @req:PRD-SYS-053
/// </summary>
public sealed class QualityDbContext(DbContextOptions<QualityDbContext> options) : DbContext(options)
{
    public DbSet<QualityDeviation> QualityDeviations => Set<QualityDeviation>();
    public DbSet<QualityRootCauseAnalysis> QualityRootCauseAnalyses => Set<QualityRootCauseAnalysis>();
    public DbSet<QualityCapaRecord> QualityCapaRecords => Set<QualityCapaRecord>();
    public DbSet<QualityAuditLog> QualityAuditLogs => Set<QualityAuditLog>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new QualityDeviationConfiguration());
        modelBuilder.ApplyConfiguration(new QualityRootCauseAnalysisConfiguration());
        modelBuilder.ApplyConfiguration(new QualityCapaRecordConfiguration());
        modelBuilder.ApplyConfiguration(new QualityAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
    }
}
