using System;
using System.Text;

using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Kernel.Domain.Entities;

using Microsoft.EntityFrameworkCore;

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

    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        base.ConfigureConventions(configurationBuilder);
        configurationBuilder.ConfigureKernelEntityIds();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(KernelDbContext).Assembly);

        // Interim: the shared per-entity-type audit chain written by GxPAuditInterceptor. Replaced by the kernel
        // AuditEntry with one chain per tenant in the command-pipeline slice (#4779, spec §5.3).
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());

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
}
