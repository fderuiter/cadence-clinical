using System;

using Cadence.Kernel;
using Cadence.Kernel.Modules;
using Cadence.Modules.Execution.Domain;

using Microsoft.EntityFrameworkCore;

namespace Cadence.Modules.Execution.Infrastructure;

/// <summary>
/// The Execution module's records in its own <c>execution</c> schema (ADR-2601). The kernel command pipeline opens it
/// on each command's tenant-scoped connection and saves it in the command's transaction (ADR-2600); Row-Level Security
/// keeps every table to the connection's tenant.
/// </summary>
/// <param name="options">Options built by the module's command store.</param>
public sealed class ExecutionModuleDbContext(DbContextOptions<ExecutionModuleDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The module's name: its command store, audit prefix and service accounts.</summary>
    public const string Module = "execution";

    /// <summary>The module's schema, and the schema of its migrations history.</summary>
    public const string Schema = "execution";


    /// <summary>The form instances table.</summary>
    public const string FormInstancesTable = "form_instances";

    /// <summary>The field that holds a form's item order, mapped to <c>item_order</c>.</summary>
    public const string ItemOrderField = "_itemOrder";

    /// <summary>The Execution form instances, one per kernel form instance.</summary>
    public DbSet<FormInstance> FormInstances => Set<FormInstance>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<FormInstance>(form =>
        {
            form.ToTable(FormInstancesTable);
            form.HasKey(f => f.Id);
            form.Ignore(f => f.CollectionInstanceId);
            form.Property(f => f.Id).HasColumnName("id").ValueGeneratedNever();
            form.Property(f => f.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            form.Property(f => f.EnrollmentId).HasColumnName("enrollment_id").HasConversion(e => e.Value, v => EnrollmentId.FromGuid(v));
            form.Property(f => f.ParticipantId).HasColumnName("participant_id").HasConversion(p => p.Value, v => ParticipantId.FromGuid(v));
            form.Property(f => f.StudyId).HasColumnName("study_id").HasConversion(s => s.Value, v => StudyId.FromGuid(v));
            form.Property(f => f.ProtocolVersionId).HasColumnName("protocol_version_id").HasConversion(p => p.Value, v => ProtocolVersionId.FromGuid(v));
            form.Property(f => f.CollectionSpecId).HasColumnName("collection_spec_id").HasMaxLength(FormInstance.ElementIdMaxLength).IsRequired();
            form.Property(f => f.PlannedActivityId).HasColumnName("planned_activity_id").HasMaxLength(FormInstance.ElementIdMaxLength);
            form.Property(f => f.CollectedAt).HasColumnName("collected_at");
            form.Ignore(f => f.ItemOrder);
            form.Property<string[]>(ItemOrderField).HasColumnName("item_order").IsRequired();
            form.HasCommandAuditColumns();
            form.Property(f => f.CreatedAt).HasColumnName("created_at");
            form.Property(f => f.CreatedBy).HasColumnName("created_by");
            form.Property(f => f.ReasonForChange).HasColumnName("reason_for_change");
            form.Property(f => f.VersionIndex).HasColumnName("version_index");
            form.HasIndex(f => new { f.TenantId, f.EnrollmentId }).HasDatabaseName("ix_form_instances_tenant_enrollment");
        });
    }
}
