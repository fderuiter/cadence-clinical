using System;

using Cadence.Apps.Imaging.Domain;
using Cadence.Apps.Imaging.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Tenancy;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Imaging.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Imaging microservice persistence gateway.
/// </summary>
public sealed class ImagingDbContext(DbContextOptions<ImagingDbContext> options) : DbContext(options), ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);

    public DbSet<ImagingStudyEnvelope> ImagingStudyEnvelopes => Set<ImagingStudyEnvelope>();
    public DbSet<ImagingSeriesEnvelope> ImagingSeriesEnvelopes => Set<ImagingSeriesEnvelope>();
    public DbSet<ImagingInstanceEnvelope> ImagingInstanceEnvelopes => Set<ImagingInstanceEnvelope>();
    public DbSet<DeidentificationCertificate> DeidentificationCertificates => Set<DeidentificationCertificate>();
    public DbSet<ImagingAuditLog> ImagingAuditLogs => Set<ImagingAuditLog>();
    public DbSet<ImagingAnnotation> ImagingAnnotations => Set<ImagingAnnotation>();
    public DbSet<TumorResponseAssessment> TumorResponseAssessments => Set<TumorResponseAssessment>();
    public DbSet<ImagingAdjudicationSession> ImagingAdjudicationSessions => Set<ImagingAdjudicationSession>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ImagingStudyEnvelopeConfiguration());
        modelBuilder.ApplyConfiguration(new ImagingSeriesEnvelopeConfiguration());
        modelBuilder.ApplyConfiguration(new ImagingInstanceEnvelopeConfiguration());
        modelBuilder.ApplyConfiguration(new DeidentificationCertificateConfiguration());
        modelBuilder.ApplyConfiguration(new ImagingAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new ImagingAnnotationConfiguration());
        modelBuilder.ApplyConfiguration(new TumorResponseAssessmentConfiguration());
        modelBuilder.ApplyConfiguration(new ImagingAdjudicationSessionConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
