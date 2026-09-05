using Cadence.Apps.Execution.Domain.Compliance;
using Cadence.Apps.Execution.Domain.Subjects;
using Cadence.Apps.Execution.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Apps.Execution.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 persistence gateway for the <c>execution-subjects</c> slice. Constructor takes only
/// <see cref="DbContextOptions{TContext}"/> — the ordinary EF Core shape — so callers register it via the
/// standard <c>services.AddDbContext&lt;ExecutionDbContext&gt;(...)</c> ASP.NET Core DI path and consumers
/// inject <see cref="ExecutionDbContext"/> with the usual scoped lifetime.
///
/// <para>
/// <see cref="AuditLogEntry"/> is exposed here as a <see cref="DbSet{TEntity}"/> purely so
/// <c>GxPAuditInterceptor</c> (<c>packages/database/src/GxPAuditInterceptor.cs</c>, MIG-SUBJ-007) can resolve
/// <c>context.Set&lt;AuditLogEntry&gt;()</c> when it is registered against this context — without an entity
/// type reachable from the model, that call throws at <c>SaveChangesAsync</c> time for every mutation.
/// </para>
///
/// <para>
/// This context deliberately does <b>not</b> call <c>optionsBuilder.AddInterceptors(new GxPAuditInterceptor(...))</c>
/// from an <c>OnConfiguring</c> override. <c>02_technical_design.md</c> §4.4's intended source for the
/// interceptor's current-user delegate, <c>ICurrentAuditContextAccessor</c>, is not implemented anywhere in
/// this codebase yet (it is scoped to a later ticket, MIG-SUBJ-017/018 per the interceptor's own
/// implementation comment) — hardcoding a placeholder delegate here instead would risk the interceptor being
/// registered a second time once a later ticket wires the real one into the composition root
/// (<c>Program.cs</c>, not a target file of this ticket), silently double-writing <see cref="AuditLogEntry"/>
/// rows and double-incrementing <c>VersionIndex</c> per save — a correctness bug in a 21 CFR Part 11 audit
/// trail. Registration therefore happens once, at the single call site that builds this context's
/// <see cref="DbContextOptions{TContext}"/> (tests do this directly via
/// <c>DbContextOptionsBuilder&lt;ExecutionDbContext&gt;.AddInterceptors(...)</c>, matching MIG-SUBJ-007's own
/// test convention; the production call site is Program.cs's future <c>AddDbContext</c> registration).
/// </para>
/// @req:PRD-SUB-001
/// </summary>
public sealed class ExecutionDbContext(DbContextOptions<ExecutionDbContext> options) : DbContext(options)
{
    public DbSet<ClinicalSubject> ClinicalSubjects => Set<ClinicalSubject>();

    public DbSet<SubjectConsent> SubjectConsents => Set<SubjectConsent>();

    public DbSet<SiteComplianceCache> SiteComplianceCaches => Set<SiteComplianceCache>();

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new ClinicalSubjectConfiguration());
        modelBuilder.ApplyConfiguration(new SubjectConsentConfiguration());
        modelBuilder.ApplyConfiguration(new SiteComplianceCacheConfiguration());
    }
}
