using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Cadence.Kernel.Domain.Entities;
using Cadence.Kernel.Domain.Knowledge;
using Cadence.Kernel.Persistence.Bitemporal;
using Cadence.Kernel.Persistence.Commands;
using Cadence.Kernel.Persistence.Lineage;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Kernel.Persistence;

/// <summary>
/// The Clinical Kernel's single <see cref="DbContext"/>, mapping kernel truth into the <c>kernel</c> schema of the
/// shared <c>cadence</c> database (ADR-2244, ADR-2247). Internal: application modules never reference kernel
/// persistence (K-XS-2); they go through <c>Cadence.Kernel.Abstractions</c>.
/// </summary>
internal sealed class KernelDbContext(DbContextOptions<KernelDbContext> options) : DbContext(options)
{
    /// <summary>PostgreSQL schema that holds every kernel table.</summary>
    public const string Schema = "kernel";

    public DbSet<Tenant> Tenants => Set<Tenant>();

    public DbSet<Organization> Organizations => Set<Organization>();

    public DbSet<Person> People => Set<Person>();

    public DbSet<TenantMembership> TenantMemberships => Set<TenantMembership>();

    public DbSet<Site> Sites => Set<Site>();

    public DbSet<Study> Studies => Set<Study>();

    public DbSet<StudySite> StudySites => Set<StudySite>();

    public DbSet<CodeSystem> CodeSystems => Set<CodeSystem>();

    public DbSet<CodeSystemVersion> CodeSystemVersions => Set<CodeSystemVersion>();

    public DbSet<CodeSystemEntry> CodeSystemEntries => Set<CodeSystemEntry>();

    public DbSet<Concept> Concepts => Set<Concept>();

    public DbSet<ConceptRevision> ConceptRevisions => Set<ConceptRevision>();

    public DbSet<ConceptMapping> ConceptMappings => Set<ConceptMapping>();

    public DbSet<ProtocolVersion> ProtocolVersions => Set<ProtocolVersion>();

    public DbSet<FormVersionSnapshot> FormVersionSnapshots => Set<FormVersionSnapshot>();

    public DbSet<Attestation> Attestations => Set<Attestation>();

    public DbSet<Participant> Participants => Set<Participant>();

    public DbSet<Enrollment> Enrollments => Set<Enrollment>();

    public DbSet<EnrollmentStatusPeriod> EnrollmentStatusPeriods => Set<EnrollmentStatusPeriod>();

    public DbSet<Artifact> Artifacts => Set<Artifact>();

    public DbSet<ArtifactVersion> ArtifactVersions => Set<ArtifactVersion>();

    public DbSet<Observation> Observations => Set<Observation>();

    public DbSet<ObservationRevision> ObservationRevisions => Set<ObservationRevision>();

    public DbSet<Assertion> Assertions => Set<Assertion>();

    public DbSet<CollectionInstance> CollectionInstances => Set<CollectionInstance>();

    public DbSet<Role> Roles => Set<Role>();

    public DbSet<Grant> Grants => Set<Grant>();

    public DbSet<GrantPeriod> GrantPeriods => Set<GrantPeriod>();

    public DbSet<Qualification> Qualifications => Set<Qualification>();

    public DbSet<QualificationPeriod> QualificationPeriods => Set<QualificationPeriod>();

    public DbSet<QualificationRequirement> QualificationRequirements => Set<QualificationRequirement>();

    public DbSet<QualificationRequirementPeriod> QualificationRequirementPeriods => Set<QualificationRequirementPeriod>();

    public DbSet<WorkQueueDefinition> WorkQueueDefinitions => Set<WorkQueueDefinition>();
    public DbSet<Effectivity> Effectivities => Set<Effectivity>();

    public DbSet<EffectivityStatusPeriod> EffectivityStatusPeriods => Set<EffectivityStatusPeriod>();

    public DbSet<Proposal> Proposals => Set<Proposal>();

    public DbSet<Subscription> Subscriptions => Set<Subscription>();

    public DbSet<SubscriptionPeriod> SubscriptionPeriods => Set<SubscriptionPeriod>();

    public DbSet<ProductEntitlement> ProductEntitlements => Set<ProductEntitlement>();

    public DbSet<ProvenanceActivityRecord> ProvenanceActivities => Set<ProvenanceActivityRecord>();

    public DbSet<DomainEventRecord> DomainEvents => Set<DomainEventRecord>();

    public DbSet<AuditEntryRecord> AuditEntries => Set<AuditEntryRecord>();

    public DbSet<AuditChainHead> AuditChainHeads => Set<AuditChainHead>();

    public DbSet<AuditChainSeal> AuditChainSeals => Set<AuditChainSeal>();

    public DbSet<OutboxMessageRecord> OutboxMessages => Set<OutboxMessageRecord>();

    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();

    public DbSet<BiostatExportAuditLog> BiostatExportAuditLogs => Set<BiostatExportAuditLog>();

    public DbSet<LineageNodeRecord> LineageNodes => Set<LineageNodeRecord>();

    public DbSet<LineageEdgeRecord> LineageEdges => Set<LineageEdgeRecord>();

    private int _permittedWriters;

    /// <summary>
    /// The tenant every connection this context opens is scoped to (K-ID-2). Set once, before first use, by
    /// <see cref="Tenancy.KernelTenantDbContextFactory"/>; <see cref="Tenancy.KernelTenantConnectionInterceptor"/>
    /// refuses to open a connection for a context that has neither a tenant nor <see cref="IsTenantDirectory"/>.
    /// </summary>
    internal TenantId? Tenant { get; private set; }

    /// <summary>True for a context that may only list tenant ids; it sees no tenant's rows.</summary>
    internal bool IsTenantDirectory { get; private set; }

    internal void BindTenant(TenantId tenant)
    {
        if (tenant.IsEmpty)
        {
            throw new ArgumentException("K-ID-2: a kernel context needs a tenant.", nameof(tenant));
        }

        if (Tenant is not null || IsTenantDirectory)
        {
            throw new InvalidOperationException("K-ID-2: a kernel context is bound to one tenant, once.");
        }

        Tenant = tenant;
    }

    internal void BindTenantDirectory()
    {
        if (Tenant is not null || IsTenantDirectory)
        {
            throw new InvalidOperationException("K-ID-2: a kernel context is bound to one tenant, once.");
        }

        IsTenantDirectory = true;
    }

    /// <summary>
    /// Opens a window in which <c>SaveChanges</c> is allowed. Only the command pipeline's transaction, the outbox relay
    /// (delivery bookkeeping only), the activity projector (a derived read model, ADR-2485) and test seeding open one:
    /// every other write is refused, so no kernel state changes without its provenance, event and audit link (K-TX-2).
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
