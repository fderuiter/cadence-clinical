using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Commands;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// The Clinical Kernel's single <see cref="DbContext"/>, mapping kernel truth into the <c>kernel</c> schema of the
/// shared <c>cadence</c> database (ADR-2244, ADR-2247). Internal: application modules never reference kernel
/// persistence (K-XS-2); they go through <c>Cadence.Kernel.Abstractions</c>.
/// @req:PRD-SYS-053
/// </summary>
internal sealed class KernelDbContext(DbContextOptions<KernelDbContext> options) : DbContext(options)
{
    /// <summary>PostgreSQL schema that holds every kernel table.</summary>
    public const string Schema = "kernel";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Study> Studies => Set<Study>();

    public DbSet<StudySite> StudySites => Set<StudySite>();

    public DbSet<ProvenanceActivityRecord> ProvenanceActivities => Set<ProvenanceActivityRecord>();

    public DbSet<DomainEventRecord> DomainEvents => Set<DomainEventRecord>();

    public DbSet<AuditEntryRecord> AuditEntries => Set<AuditEntryRecord>();

    public DbSet<AuditChainHead> AuditChainHeads => Set<AuditChainHead>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    private int _permittedWriters;

    /// <summary>
    /// Opens a window in which <c>SaveChanges</c> is allowed. Only the command pipeline's transaction (and test
    /// seeding, through InternalsVisibleTo) opens one: every other write is refused, so no kernel state changes
    /// without its provenance, event and audit link (K-TX-2).
    /// </summary>
    internal IDisposable PermitPipelineWrites()
    {
        Interlocked.Increment(ref _permittedWriters);
        return new WriteWindow(this);
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnsureWritesPermitted();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnsureWritesPermitted();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void EnsureWritesPermitted()
    {
        if (Volatile.Read(ref _permittedWriters) == 0)
        {
            throw new InvalidOperationException(
                "K-TX-2: kernel state is written only through the command pipeline (IKernelCommandSender).");
        }
    }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // TimeRange members translate to range operators however the context was built (DI, design time, tests).
        ((IDbContextOptionsBuilderInfrastructure)optionsBuilder).AddOrUpdateExtension(new KernelQueryTranslationOptionsExtension());
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.ConfigureKernelEntityIds();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);

        // btree_gist lets the K-TIME-2 exclusion constraints combine equality on keys with range overlap (ADR-2246).
        modelBuilder.HasPostgresExtension("btree_gist");
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KernelDbContext).Assembly);

        ApplySnakeCaseColumnNames(modelBuilder);
    }

    private static void ApplySnakeCaseColumnNames(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }
        }
    }

    internal static string ToSnakeCase(string name)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);

        var builder = new StringBuilder(name.Length + 8);
        for (int i = 0; i < name.Length; i++)
        {
            char c = name[i];
            if (char.IsUpper(c))
            {
                bool startsWord = i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (startsWord && builder.Length > 0 && builder[^1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private sealed class WriteWindow(KernelDbContext context) : IDisposable
    {
        private int _disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                Interlocked.Decrement(ref context._permittedWriters);
            }
        }
    }
}
