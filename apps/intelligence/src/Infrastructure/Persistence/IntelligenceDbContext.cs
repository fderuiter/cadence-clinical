using Cadence.Kernel;
using Cadence.Kernel.Modules;
using Cadence.Modules.Intelligence.Domain;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cadence.Modules.Intelligence.Infrastructure.Persistence;

/// <summary>
/// The Intelligence module's records in its own schema (ADR-2600): the interaction ledger, the agent controls, the task configuration qualifications and the evaluation runs they rest on. Its
/// audited rows are written only by the module's commands through the kernel pipeline; reads go through a tenant-scoped
/// context (<see cref="IModuleTenantContextFactory{TContext}"/>) under Row-Level Security.
/// </summary>
/// <param name="options">Options from the command store, the tenant context factory or the migrator.</param>
public sealed class IntelligenceDbContext(DbContextOptions<IntelligenceDbContext> options) : ModuleDbContext(options)
{
    /// <summary>The module's name in the composition host.</summary>
    public const string Module = "intelligence";

    /// <summary>The module's schema.</summary>
    public const string Schema = "intelligence";

    /// <summary>The interaction ledger's table.</summary>
    public const string AgentRunsTable = "agent_runs";

    /// <summary>The agent controls' table.</summary>
    public const string AgentControlsTable = "agent_controls";

    /// <summary>The task configuration qualifications' table.</summary>
    public const string TaskQualificationsTable = "task_qualifications";

    /// <summary>The stored evaluation runs' table.</summary>
    public const string EvaluationRunsTable = "evaluation_runs";

    /// <summary>The interaction ledger.</summary>
    public DbSet<AgentRun> AgentRuns => Set<AgentRun>();

    /// <summary>The kill switches and budgets.</summary>
    public DbSet<AgentControl> AgentControls => Set<AgentControl>();

    /// <summary>The task configuration qualifications (ADR-2631).</summary>
    public DbSet<TaskQualification> TaskQualifications => Set<TaskQualification>();

    /// <summary>The stored evaluation runs qualifications rest on (ADR-2634 §4.4).</summary>
    public DbSet<EvaluationRun> EvaluationRuns => Set<EvaluationRun>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);
        modelBuilder.Entity<AgentRun>(run =>
        {
            run.ToTable(AgentRunsTable);
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            run.Property(r => r.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            run.Property(r => r.TaskKey).HasColumnName("task_key").HasMaxLength(96).IsRequired();
            run.Property(r => r.TaskVersion).HasColumnName("task_version");
            run.Property(r => r.ConfigurationHash).HasColumnName("configuration_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            run.Property(r => r.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
            run.Property(r => r.Model).HasColumnName("model").HasMaxLength(200).IsRequired();
            run.Property(r => r.RequestedBy).HasColumnName("requested_by").HasMaxLength(255).IsRequired();
            // text, not jsonb: jsonb rewrites the JSON, and the entry must read back exactly as it was hashed.
            run.Property(r => r.RequestJson).HasColumnName("request_json").IsRequired();
            run.Property(r => r.StartedAt).HasColumnName("started_at");
            run.Property(r => r.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            run.Property(r => r.ResponseText).HasColumnName("response_text");
            run.Property(r => r.FailureReason).HasColumnName("failure_reason").HasMaxLength(2000);
            run.Property(r => r.InputTokens).HasColumnName("input_tokens");
            run.Property(r => r.OutputTokens).HasColumnName("output_tokens");
            run.Property(r => r.CostUsd).HasColumnName("cost_usd").HasPrecision(18, 6);
            run.Property(r => r.CompletedAt).HasColumnName("completed_at");
            run.Property(r => r.ContentHash).HasColumnName("content_hash").HasMaxLength(64);
            run.Property(r => r.InferenceSupplier).HasColumnName("inference_supplier").HasMaxLength(64);
            run.Property(r => r.InferenceLocality).HasColumnName("inference_locality").HasMaxLength(16);
            run.Property(r => r.InferencePolicy).HasColumnName("inference_policy").HasMaxLength(16);
            run.Property(r => r.InferenceResidency).HasColumnName("inference_residency").HasMaxLength(8);
            // text, not jsonb, for the same reason as request_json: it must read back exactly as it was hashed.
            run.Property(r => r.ScrubCounts).HasColumnName("scrub_counts");
            run.HasCommandAuditColumns();
            run.Property(r => r.CreatedAt).HasColumnName("created_at");
            run.Property(r => r.CreatedBy).HasColumnName("created_by");
            run.Property(r => r.ReasonForChange).HasColumnName("reason_for_change");
            run.Property(r => r.VersionIndex).HasColumnName("version_index");
            run.HasIndex(r => new { r.TenantId, r.RequestedBy, r.StartedAt }).HasDatabaseName("ix_agent_runs_requester");
            run.HasIndex(r => new { r.TenantId, r.TaskKey, r.StartedAt }).HasDatabaseName("ix_agent_runs_task");
        });

        modelBuilder.Entity<AgentControl>(control =>
        {
            control.ToTable(AgentControlsTable);
            control.HasKey(c => c.Id);
            control.Property(c => c.Id).HasColumnName("id").ValueGeneratedNever();
            control.Property(c => c.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            control.Property(c => c.Scope).HasColumnName("scope").HasMaxLength(96).IsRequired();
            control.Property(c => c.Enabled).HasColumnName("enabled");
            control.Property(c => c.MonthlyTokenLimit).HasColumnName("monthly_token_limit");
            control.HasCommandAuditColumns();
            control.Property(c => c.CreatedAt).HasColumnName("created_at");
            control.Property(c => c.CreatedBy).HasColumnName("created_by");
            control.Property(c => c.ReasonForChange).HasColumnName("reason_for_change");
            control.Property(c => c.VersionIndex).HasColumnName("version_index");
            control.HasIndex(c => new { c.TenantId, c.Scope }).IsUnique().HasDatabaseName("ux_agent_controls_scope");
        });

        modelBuilder.Entity<TaskQualification>(qualification =>
        {
            qualification.ToTable(TaskQualificationsTable);
            qualification.HasKey(q => q.Id);
            qualification.Property(q => q.Id).HasColumnName("id").ValueGeneratedNever();
            qualification.Property(q => q.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            qualification.Property(q => q.TaskKey).HasColumnName("task_key").HasMaxLength(96).IsRequired();
            qualification.Property(q => q.TaskVersion).HasColumnName("task_version");
            qualification.Property(q => q.ConfigurationHash).HasColumnName("configuration_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            qualification.Property(q => q.Status).HasColumnName("status").HasConversion<string>().HasMaxLength(16);
            qualification.Property(q => q.RiskClass).HasColumnName("risk_class").HasConversion<string>().HasMaxLength(16);
            qualification.Property(q => q.EvaluationRunReference).HasColumnName("evaluation_run_reference").HasMaxLength(200).IsRequired();
            qualification.Property(q => q.EvaluationRunHash).HasColumnName("evaluation_run_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            qualification.Property(q => q.EvaluationSetHash).HasColumnName("evaluation_set_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            qualification.Property(q => q.EvaluationSummaryJson).HasColumnName("evaluation_summary_json").IsRequired();
            qualification.Property(q => q.AttestationId).HasColumnName("attestation_id");
            qualification.Property(q => q.QualifiedBy).HasColumnName("qualified_by").HasMaxLength(255).IsRequired();
            qualification.Property(q => q.QualifiedAt).HasColumnName("qualified_at");
            qualification.Property(q => q.SuspendedAt).HasColumnName("suspended_at");
            qualification.Property(q => q.SuspensionReason).HasColumnName("suspension_reason").HasMaxLength(1000);
            qualification.HasCommandAuditColumns();
            qualification.Property(q => q.CreatedAt).HasColumnName("created_at");
            qualification.Property(q => q.CreatedBy).HasColumnName("created_by");
            qualification.Property(q => q.ReasonForChange).HasColumnName("reason_for_change");
            qualification.Property(q => q.VersionIndex).HasColumnName("version_index");
            qualification.HasIndex(q => new { q.TenantId, q.TaskKey, q.TaskVersion }).IsUnique().HasDatabaseName("ux_task_qualifications_version");
        });

        modelBuilder.Entity<EvaluationRun>(run =>
        {
            run.ToTable(EvaluationRunsTable);
            run.HasKey(r => r.Id);
            run.Property(r => r.Id).HasColumnName("id").ValueGeneratedNever();
            run.Property(r => r.TenantId).HasColumnName("tenant_id").HasConversion(t => t.Value, v => new TenantId(v));
            run.Property(r => r.RunId).HasColumnName("run_id");
            run.Property(r => r.TaskKey).HasColumnName("task_key").HasMaxLength(96).IsRequired();
            run.Property(r => r.TaskVersion).HasColumnName("task_version");
            run.Property(r => r.ConfigurationHash).HasColumnName("configuration_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            run.Property(r => r.EvaluationSetId).HasColumnName("evaluation_set_id").HasMaxLength(96).IsRequired();
            run.Property(r => r.EvaluationSetVersion).HasColumnName("evaluation_set_version");
            run.Property(r => r.EvaluationSetHash).HasColumnName("evaluation_set_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            run.Property(r => r.Provider).HasColumnName("provider").HasMaxLength(64).IsRequired();
            run.Property(r => r.Model).HasColumnName("model").HasMaxLength(200).IsRequired();
            run.Property(r => r.Accepted).HasColumnName("accepted");
            run.Property(r => r.StartedAt).HasColumnName("started_at");
            run.Property(r => r.CompletedAt).HasColumnName("completed_at");
            // text, not jsonb: jsonb rewrites the JSON, and the record must read back exactly as it was hashed.
            run.Property(r => r.RecordJson).HasColumnName("record_json").IsRequired();
            run.Property(r => r.RecordHash).HasColumnName("record_hash").HasMaxLength(Cadence.Kernel.Trust.ContentHash.Length).IsRequired();
            run.Property(r => r.SummaryJson).HasColumnName("summary_json").IsRequired();
            run.HasCommandAuditColumns();
            run.Property(r => r.CreatedAt).HasColumnName("created_at");
            run.Property(r => r.CreatedBy).HasColumnName("created_by");
            run.Property(r => r.ReasonForChange).HasColumnName("reason_for_change");
            run.Property(r => r.VersionIndex).HasColumnName("version_index");
            run.HasIndex(r => new { r.TenantId, r.RunId }).IsUnique().HasDatabaseName("ux_evaluation_runs_run");
            run.HasIndex(r => new { r.TenantId, r.TaskKey, r.TaskVersion, r.CompletedAt }).HasDatabaseName("ix_evaluation_runs_task");
        });
    }
}

/// <summary>
/// Builds <see cref="IntelligenceDbContext"/> for <c>dotnet ef</c> using <c>--connection</c>,
/// <c>ConnectionStrings__cadence</c>, <c>POSTGRES_CONNECTION_STRING</c>, or a credential-free offline placeholder (ADR-2498).
/// </summary>
internal sealed class IntelligenceDbContextDesignTimeFactory : IDesignTimeDbContextFactory<IntelligenceDbContext>
{
    public IntelligenceDbContext CreateDbContext(string[] args) =>
        new(new DbContextOptionsBuilder<IntelligenceDbContext>()
            .UseNpgsql(
                ModuleMigrationServiceCollectionExtensions.ResolveDesignTimeConnectionString(args),
                npgsql => npgsql.MigrationsHistoryTable(ModuleMigrationServiceCollectionExtensions.MigrationsHistoryTable, IntelligenceDbContext.Schema))
            .Options);
}
