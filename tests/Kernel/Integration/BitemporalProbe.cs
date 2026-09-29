using System;

using Cadence.Kernel.Domain.Bitemporal;
using Cadence.Kernel.Persistence;
using Cadence.Kernel.Persistence.Bitemporal;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.UnitTests.Kernel.Integration;

/// <summary>
/// A minimal bitemporal fact used to exercise the kernel's bitemporal persistence (tstzrange mapping, range-operator
/// translation, K-TIME-1 trigger, K-TIME-2 exclusion constraint) before the first production bitemporal table lands.
/// Test-only; never mapped by <see cref="KernelDbContext"/>.
/// </summary>
internal sealed class ProbeFact : IBitemporalEntity
{
    private ProbeFact()
    {
    }

    public ProbeFact(Guid tenantId, string subjectKey, string value, TemporalPeriod period)
    {
        Id = Guid.CreateVersion7();
        TenantId = tenantId;
        SubjectKey = subjectKey;
        Value = value;
        Period = period;
    }

    public Guid Id { get; private set; }

    public Guid TenantId { get; private set; }

    public string SubjectKey { get; private set; } = string.Empty;

    public string Value { get; private set; } = string.Empty;

    public TemporalPeriod Period { get; private set; }

    /// <summary>Closes this fact's recorded time: the only update K-TIME-1 allows.</summary>
    public void Supersede(DateTimeOffset at) => Period = Period.Supersede(at);

    /// <summary>Illegal under K-TIME-1; used to prove the trigger rejects it.</summary>
    public void OverwriteValue(string value) => Value = value;

    /// <summary>Illegal under K-TIME-1; used to prove the trigger rejects it.</summary>
    public void ReplacePeriod(TemporalPeriod period) => Period = period;
}

/// <summary>Maps <see cref="ProbeFact"/> to <c>kernel.probe_facts</c> the way a kernel bitemporal table is mapped.</summary>
internal sealed class ProbeDbContext(DbContextOptions<ProbeDbContext> options) : DbContext(options)
{
    public const string Table = "probe_facts";

    public const string CreateTableSql = $"""
        CREATE TABLE {KernelDbContext.Schema}.{Table} (
            id uuid PRIMARY KEY,
            tenant_id uuid NOT NULL,
            subject_key text NOT NULL,
            value text NOT NULL,
            valid_period tstzrange NOT NULL,
            recorded_period tstzrange NOT NULL);
        """;

    public DbSet<ProbeFact> Facts => Set<ProbeFact>();

    public static ProbeDbContext Create(string connectionString) =>
        new(new DbContextOptionsBuilder<ProbeDbContext>().UseNpgsql(connectionString).Options);

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder) =>
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new KernelQueryTranslationOptionsExtension());

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ProbeFact>(entity =>
        {
            entity.ToTable(Table, KernelDbContext.Schema);
            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.TenantId).HasColumnName("tenant_id");
            entity.Property(e => e.SubjectKey).HasColumnName("subject_key");
            entity.Property(e => e.Value).HasColumnName("value");
            entity.HasBitemporalPeriod();
        });
    }
}
