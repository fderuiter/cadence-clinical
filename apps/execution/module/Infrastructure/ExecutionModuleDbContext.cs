using System;

using Cadence.Database.Signatures;
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

    /// <summary>The queries table.</summary>
    public const string FormQueriesTable = "form_queries";

    /// <summary>The append-only bitemporal query periods table (<c>K-TIME-1</c>, <c>K-TIME-2</c>).</summary>
    public const string FormQueryPeriodsTable = "form_query_periods";

    /// <summary>The insert-only query messages table (<c>KA001</c>).</summary>
    public const string FormQueryMessagesTable = "form_query_messages";

    /// <summary>The insert-only daily query aging digests table (<c>KA001</c>).</summary>
    public const string FormQueryAgingDigestsTable = "form_query_aging_digests";

    /// <summary>The field that holds a form's item order, mapped to <c>item_order</c>.</summary>
    public const string ItemOrderField = "_itemOrder";

    /// <summary>The Execution form instances, one per kernel form instance.</summary>
    public DbSet<FormInstance> FormInstances => Set<FormInstance>();

    /// <summary>The queries about form values.</summary>
    public DbSet<FormQuery> FormQueries => Set<FormQuery>();

    /// <summary>The bitemporal state history rows of <see cref="FormQueries"/>.</summary>
    public DbSet<FormQueryPeriod> FormQueryPeriods => Set<FormQueryPeriod>();

    /// <summary>The insert-only conversation messages of <see cref="FormQueries"/>.</summary>
    public DbSet<FormQueryMessageRecord> FormQueryMessages => Set<FormQueryMessageRecord>();

    /// <summary>The insert-only daily aging digests emitted for overdue open queries.</summary>
    public DbSet<FormQueryAgingDigest> FormQueryAgingDigests => Set<FormQueryAgingDigest>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        ArgumentNullException.ThrowIfNull(modelBuilder);
        modelBuilder.HasDefaultSchema(Schema);

        // Used e-signature step-up tokens (ADR-2515): not tenant data, so no Row-Level Security; read and written only by
        // ExecutionSignatureTokenDbContext.
        modelBuilder.ApplyConfiguration(new ConsumedSignatureTokenConfiguration());

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

        modelBuilder.Entity<FormQuery>(query =>
        {
            query.ToTable(FormQueriesTable);
            query.HasKey(q => q.Id);
            query.HasAlternateKey(q => new { q.TenantId, q.Id });
            query.Ignore(q => q.Messages);
            query.Property(q => q.Id).HasColumnName("id")
                .HasConversion(id => id.Value, v => FormQueryId.FromGuid(v))
                .ValueGeneratedNever();
            query.Property(q => q.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            query.Property(q => q.FormId).HasColumnName("form_id").HasConversion(f => f.Value, v => CollectionInstanceId.FromGuid(v));
            query.Property(q => q.EnrollmentId).HasColumnName("enrollment_id").HasConversion(e => e.Value, v => EnrollmentId.FromGuid(v));
            query.Property(q => q.ItemId).HasColumnName("item_id").HasMaxLength(FormInstance.ElementIdMaxLength).IsRequired();
            query.Property(q => q.ObservationId).HasColumnName("observation_id").HasConversion(o => o.Value, v => ObservationId.FromGuid(v));
            query.Property(q => q.Revision).HasColumnName("revision");
            query.Property(q => q.Origin).HasColumnName("origin").HasConversion<string>().HasMaxLength(16);
            query.Property(q => q.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            query.Property(q => q.OwnerRole).HasColumnName("owner_role").HasConversion<string>().HasMaxLength(24);
            query.Property(q => q.Assignee).HasColumnName("assignee").HasMaxLength(FormQuery.AssigneeMaxLength);
            query.Property(q => q.Priority).HasColumnName("priority").HasConversion<string>().HasMaxLength(16);
            query.Property(q => q.OpenedAt).HasColumnName("opened_at");
            query.Property(q => q.StatusSince).HasColumnName("status_since");
            query.Property(q => q.DueAt).HasColumnName("due_at");
            query.Property(q => q.EscalatedAt).HasColumnName("escalated_at");
            query.Property(q => q.LastAnsweredBy).HasColumnName("last_answered_by").HasMaxLength(256);
            query.Property(q => q.KernelQueryId).HasColumnName("kernel_query_id").HasConversion(a => a.Value, v => AssertionId.FromGuid(v));
            query.Property(q => q.KernelAnswerId).HasColumnName("kernel_answer_id")
                .HasConversion(a => a == null ? (Guid?)null : a.Value.Value, v => v == null ? null : AssertionId.FromGuid(v.Value));

            query.HasMany(q => q.Periods).WithOne()
                .HasForeignKey(p => new { p.TenantId, p.QueryId })
                .HasPrincipalKey(q => new { q.TenantId, q.Id })
                .OnDelete(DeleteBehavior.Restrict);
            query.Navigation(q => q.Periods).HasField("_periods").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

            query.HasMany(q => q.MessageRecords).WithOne()
                .HasForeignKey(m => new { m.TenantId, m.QueryId })
                .HasPrincipalKey(q => new { q.TenantId, q.Id })
                .OnDelete(DeleteBehavior.Restrict);
            query.Navigation(q => q.MessageRecords).HasField("_messageRecords").UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();

            query.HasCommandAuditColumns();
            query.Property(q => q.CreatedAt).HasColumnName("created_at");
            query.Property(q => q.CreatedBy).HasColumnName("created_by");
            query.Property(q => q.ReasonForChange).HasColumnName("reason_for_change");
            query.Property(q => q.VersionIndex).HasColumnName("version_index");
            query.HasIndex(q => new { q.TenantId, q.FormId }).HasDatabaseName("ix_form_queries_tenant_form");
            query.HasIndex(q => new { q.TenantId, q.EnrollmentId, q.Status }).HasDatabaseName("ix_form_queries_tenant_enrollment_status");
            query.HasIndex(q => new { q.TenantId, q.ObservationId, q.Status }).HasDatabaseName("ix_form_queries_tenant_observation_status");
        });

        modelBuilder.Entity<FormQueryPeriod>(period =>
        {
            period.ToTable(FormQueryPeriodsTable);
            period.HasKey(p => p.Id);
            period.Property(p => p.Id).HasColumnName("id").ValueGeneratedNever();
            period.Property(p => p.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            period.Property(p => p.QueryId).HasColumnName("query_id").HasConversion(id => id.Value, v => FormQueryId.FromGuid(v));
            period.Property(p => p.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16).IsRequired();
            period.Property(p => p.OwnerRole).HasColumnName("owner_role").HasConversion<string>().HasMaxLength(24).IsRequired();
            period.Property(p => p.Assignee).HasColumnName("assignee").HasMaxLength(FormQuery.AssigneeMaxLength);
            period.Property(p => p.Priority).HasColumnName("priority").HasConversion<string>().HasMaxLength(16).IsRequired();
            period.Property(p => p.StatusSince).HasColumnName("status_since");
            period.Property(p => p.DueAt).HasColumnName("due_at");
            period.Property(p => p.EscalatedAt).HasColumnName("escalated_at");
            period.Property(p => p.LastAnsweredBy).HasColumnName("last_answered_by").HasMaxLength(256);
            period.Property(p => p.KernelQueryId).HasColumnName("kernel_query_id").HasConversion(a => a.Value, v => AssertionId.FromGuid(v));
            period.Property(p => p.KernelAnswerId).HasColumnName("kernel_answer_id")
                .HasConversion(a => a == null ? (Guid?)null : a.Value.Value, v => v == null ? null : AssertionId.FromGuid(v.Value));
            period.Property(p => p.RecordedBy).HasColumnName("recorded_by").HasMaxLength(256).IsRequired();
            period.Property(p => p.ReasonForChange).HasColumnName("reason_for_change").HasMaxLength(4000).IsRequired();
            period.HasModuleBitemporalPeriod();
            period.HasIndex(p => new { p.TenantId, p.QueryId }).HasDatabaseName("ix_form_query_periods_tenant_query");
        });

        modelBuilder.Entity<FormQueryMessageRecord>(message =>
        {
            message.ToTable(FormQueryMessagesTable);
            message.HasKey(m => m.Id);
            message.Property(m => m.Id).HasColumnName("id").ValueGeneratedNever();
            message.Property(m => m.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            message.Property(m => m.QueryId).HasColumnName("query_id").HasConversion(id => id.Value, v => FormQueryId.FromGuid(v));
            message.Property(m => m.Sequence).HasColumnName("sequence");
            message.Property(m => m.Kind).HasColumnName("kind").HasConversion<string>().HasMaxLength(24).IsRequired();
            message.Property(m => m.Text).HasColumnName("text").HasMaxLength(4000).IsRequired();
            message.Property(m => m.RecordedBy).HasColumnName("recorded_by").HasMaxLength(256).IsRequired();
            message.Property(m => m.ReasonForChange).HasColumnName("reason_for_change").HasMaxLength(4000).IsRequired();
            message.Property(m => m.RecordedAt).HasColumnName("recorded_at");
            message.HasIndex(m => new { m.TenantId, m.QueryId, m.Sequence })
                .IsUnique()
                .HasDatabaseName("ux_form_query_messages_tenant_query_seq");
        });

        modelBuilder.Entity<FormQueryAgingDigest>(digest =>
        {
            digest.ToTable(FormQueryAgingDigestsTable);
            digest.HasKey(d => d.Id);
            digest.Property(d => d.Id).HasColumnName("id").ValueGeneratedNever();
            digest.Property(d => d.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            digest.Property(d => d.StudyId).HasColumnName("study_id").HasConversion(s => s.Value, v => StudyId.FromGuid(v));
            digest.Property(d => d.StudySiteId).HasColumnName("study_site_id")
                .HasConversion(s => s == null ? (Guid?)null : s.Value.Value, v => v == null ? null : StudySiteId.FromGuid(v.Value));
            digest.Property(d => d.ScopeKey).HasColumnName("scope_key").HasMaxLength(128).IsRequired();
            digest.Property(d => d.RecipientRole).HasColumnName("recipient_role").HasMaxLength(64).IsRequired();
            digest.Property(d => d.DigestDate).HasColumnName("digest_date");
            digest.Property(d => d.GeneratedAt).HasColumnName("generated_at");
            digest.Property(d => d.StudyCode).HasColumnName("study_code").HasMaxLength(128).IsRequired();
            digest.Property(d => d.SiteCode).HasColumnName("site_code").HasMaxLength(128);
            digest.Property(d => d.EscalatedCount).HasColumnName("escalated_count");
            digest.OwnsMany(d => d.Entries, entries =>
            {
                entries.ToJson("entries");
            });
            digest.Navigation(d => d.Entries).HasField("_entries");
            digest.HasCommandAuditColumns();
            digest.Property(d => d.CreatedAt).HasColumnName("created_at");
            digest.Property(d => d.CreatedBy).HasColumnName("created_by");
            digest.Property(d => d.ReasonForChange).HasColumnName("reason_for_change");
            digest.Property(d => d.VersionIndex).HasColumnName("version_index");
            digest.HasIndex(d => new { d.TenantId, d.StudyId, d.DigestDate, d.ScopeKey })
                .IsUnique()
                .HasDatabaseName("ux_form_query_aging_digests_study_date_scope");
        });
    }
}
