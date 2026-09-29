using System;

using Microsoft.EntityFrameworkCore;

namespace Cadence.ArchitectureTests.Architecture.Fixtures;

// Deliberate kernel-boundary violations (spec §14 scenario 7). They live only in the test assembly, which the
// production scans exclude, and are fed to the rule functions explicitly to prove that K-XS-1 and K-MOD-1 bite.
// Do not reuse these types anywhere else.

/// <summary>Synthetic duplicate of the kernel Site aggregate (forbidden type name + string StudyId).</summary>
public sealed class Site
{
    public Guid Id { get; set; }

    public string StudyId { get; set; } = string.Empty;

    public Guid OwningSiteId { get; set; }
}

/// <summary>Synthetic module-local audit table (forbidden <c>*AuditLog</c> type).</summary>
public sealed class SyntheticModuleAuditLog
{
    public Guid Id { get; set; }
}

/// <summary>Synthetic module entity carrying kernel identity as primitives.</summary>
public sealed class SyntheticSiteRegistration
{
    public Guid Id { get; set; }

    public string StudyId { get; set; } = string.Empty;

    public string? PrimarySiteId;

    public Guid TypedStudyId { get; set; }

    internal string InternalStudyId { get; set; } = string.Empty;

    /// <summary>Public string field, also forbidden.</summary>
    public string ParticipantId = string.Empty;

    /// <summary>A const is a name (e.g. a header), not an ID value, so it is allowed.</summary>
    public const string HeaderSiteId = "X-Site-Id";
}

/// <summary>Synthetic module DbContext mapping the fixtures above (K-MOD-1).</summary>
public sealed class SyntheticModuleDbContext(DbContextOptions<SyntheticModuleDbContext> options) : DbContext(options)
{
    public DbSet<Site> Sites => Set<Site>();

    public DbSet<SyntheticSiteRegistration> Registrations => Set<SyntheticSiteRegistration>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SyntheticSiteRegistration>(entity =>
        {
            entity.Ignore(e => e.TypedStudyId);
            entity.Property(e => e.PrimarySiteId);
            entity.Property(e => e.ParticipantId);
        });

        // Shadow column: no CLR member, still a duplicated kernel identity column.
        modelBuilder.Entity<Site>().Property<Guid>("LegacySiteId");
    }
}
