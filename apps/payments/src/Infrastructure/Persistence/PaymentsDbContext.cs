using System;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence.Configurations;
using Cadence.Core.Domain.Audit;
using Cadence.Database.Configurations;
using Cadence.Database.Signatures;
using Cadence.Database.Tenancy;
using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Cadence.Apps.Payments.Infrastructure.Persistence;

/// <summary>
/// EF Core 10 DbContext for Payments microservice persistence gateway. Payment processor references are encrypted
/// at rest with <paramref name="fieldEncryptor"/> (#4770).
/// </summary>
public sealed class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options, IFieldEncryptor fieldEncryptor)
    : DbContext(options), IFieldEncryptingDbContext, ITenantScopedDbContext
{
    public Guid? ActiveInMemoryTenantFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantFilter(this);

    public string? ActiveInMemoryTenantStringFilter => StandaloneTenantModelExtensions.ResolveInMemoryTenantStringFilter(this);
    /// <inheritdoc />
    public IFieldEncryptor FieldEncryptor { get; } = fieldEncryptor;

    public DbSet<BudgetContract> BudgetContracts => Set<BudgetContract>();
    public DbSet<FeeSchedule> FeeSchedules => Set<FeeSchedule>();
    public DbSet<SplitPayeeConfig> SplitPayeeConfigs => Set<SplitPayeeConfig>();
    public DbSet<Payable> Payables => Set<Payable>();
    public DbSet<PayableAllocation> PayableAllocations => Set<PayableAllocation>();
    public DbSet<HoldbackReserve> HoldbackReserves => Set<HoldbackReserve>();
    public DbSet<PaymentAuditLog> PaymentAuditLogs => Set<PaymentAuditLog>();
    public DbSet<SelfBillingInvoice> SelfBillingInvoices => Set<SelfBillingInvoice>();
    public DbSet<InvoiceLineItem> InvoiceLineItems => Set<InvoiceLineItem>();
    public DbSet<RemittanceAdvice> RemittanceAdvices => Set<RemittanceAdvice>();
    public DbSet<ErpSyncLog> ErpSyncLogs => Set<ErpSyncLog>();
    public DbSet<ParticipantWallet> ParticipantWallets => Set<ParticipantWallet>();
    public DbSet<ParticipantStipendRule> ParticipantStipendRules => Set<ParticipantStipendRule>();
    public DbSet<ParticipantTransaction> ParticipantTransactions => Set<ParticipantTransaction>();
    public DbSet<ParticipantExpenseClaim> ParticipantExpenseClaims => Set<ParticipantExpenseClaim>();
    public DbSet<ParticipantDisbursement> ParticipantDisbursements => Set<ParticipantDisbursement>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<ConsumedSignatureToken> ConsumedSignatureTokens => Set<ConsumedSignatureToken>();

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);
        optionsBuilder.ReplaceService<IModelCacheKeyFactory, FieldEncryptionModelCacheKeyFactory>();
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfiguration(new BudgetContractConfiguration());
        modelBuilder.ApplyConfiguration(new FeeScheduleConfiguration());
        modelBuilder.ApplyConfiguration(new SplitPayeeConfigConfiguration());
        modelBuilder.ApplyConfiguration(new PayableConfiguration());
        modelBuilder.ApplyConfiguration(new PayableAllocationConfiguration());
        modelBuilder.ApplyConfiguration(new HoldbackReserveConfiguration());
        modelBuilder.ApplyConfiguration(new PaymentAuditLogConfiguration());
        modelBuilder.ApplyConfiguration(new SelfBillingInvoiceConfiguration());
        modelBuilder.ApplyConfiguration(new InvoiceLineItemConfiguration());
        modelBuilder.ApplyConfiguration(new RemittanceAdviceConfiguration());
        modelBuilder.ApplyConfiguration(new ErpSyncLogConfiguration());
        modelBuilder.ApplyConfiguration(new ParticipantWalletConfiguration(FieldEncryptor));
        modelBuilder.ApplyConfiguration(new ParticipantStipendRuleConfiguration());
        modelBuilder.ApplyConfiguration(new ParticipantTransactionConfiguration());
        modelBuilder.ApplyConfiguration(new ParticipantExpenseClaimConfiguration());
        modelBuilder.ApplyConfiguration(new ParticipantDisbursementConfiguration());
        modelBuilder.ApplyConfiguration(new AuditLogEntryConfiguration());
        modelBuilder.ApplyConfiguration(new ConsumedSignatureTokenConfiguration());
        modelBuilder.ConfigureStandaloneTenancy(this);
    }
}
