using Cadence.Apps.Payments.Domain;
using Cadence.Security.FieldProtection;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Configurations;

public class BudgetContractConfiguration : IEntityTypeConfiguration<BudgetContract>
{
    public void Configure(EntityTypeBuilder<BudgetContract> builder)
    {
        builder.ToTable("payments_budget_contracts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.ContractName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.TierLevel).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TotalBudget).HasColumnType("numeric(18,4)");
        builder.Property(x => x.OverheadPercentage).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingPercentage).HasColumnType("numeric(18,4)");
        builder.Property(x => x.EffectiveDate).HasMaxLength(50);
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class FeeScheduleConfiguration : IEntityTypeConfiguration<FeeSchedule>
{
    public void Configure(EntityTypeBuilder<FeeSchedule> builder)
    {
        builder.ToTable("payments_fee_schedules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ContractId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ContractId);
        builder.Property(x => x.VisitCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.VisitCode);
        builder.Property(x => x.ActivityId).HasMaxLength(100);
        builder.Property(x => x.FormId).HasMaxLength(100);
        builder.HasIndex(x => x.FormId);
        builder.Property(x => x.ProcedureCode).HasMaxLength(100).IsRequired();
        builder.Property(x => x.ProcedureName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.BaseAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.OverheadPercentage).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingPercentage).HasColumnType("numeric(18,4)");
        builder.Property(x => x.PatientStipend).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TravelReimbursement).HasColumnType("numeric(18,4)");
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class SplitPayeeConfigConfiguration : IEntityTypeConfiguration<SplitPayeeConfig>
{
    public void Configure(EntityTypeBuilder<SplitPayeeConfig> builder)
    {
        builder.ToTable("payments_split_payees");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.ContractId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ContractId);
        builder.Property(x => x.PayeeId).HasMaxLength(100).IsRequired();
        builder.Property(x => x.PayeeName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PayeeRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AllocationPercentage).HasColumnType("numeric(18,4)");
        builder.Property(x => x.BankAccountReference).HasMaxLength(100);
        builder.Property(x => x.TaxIdentifier).HasMaxLength(100);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class PayableConfiguration : IEntityTypeConfiguration<Payable>
{
    public void Configure(EntityTypeBuilder<Payable> builder)
    {
        builder.ToTable("payments_payables");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.ContractId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ContractId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.VisitCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.VisitCode);
        builder.Property(x => x.FormId).HasMaxLength(100);
        builder.HasIndex(x => x.FormId);
        builder.Property(x => x.ProcedureCode).HasMaxLength(100);
        builder.Property(x => x.ProcedureName).HasMaxLength(255);
        builder.Property(x => x.TriggerType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TriggerType);
        builder.Property(x => x.TriggerEventId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.TriggerEventId).IsUnique();
        builder.Property(x => x.TriggerSource).HasMaxLength(100).IsRequired();
        builder.Property(x => x.BaseAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.OverheadAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.PatientStipendAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TravelReimbursementAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.GrossAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingRate).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.NetAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.HasIndex(x => x.IsSdvGated);
        builder.Property(x => x.SdvVerifiedBy).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class PayableAllocationConfiguration : IEntityTypeConfiguration<PayableAllocation>
{
    public void Configure(EntityTypeBuilder<PayableAllocation> builder)
    {
        builder.ToTable("payments_payable_allocations");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.PayableId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.PayableId);
        builder.Property(x => x.PayeeId).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.PayeeId);
        builder.Property(x => x.PayeeName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.PayeeRole).HasMaxLength(50).IsRequired();
        builder.Property(x => x.AllocationPercentage).HasColumnType("numeric(18,4)");
        builder.Property(x => x.GrossShare).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingShare).HasColumnType("numeric(18,4)");
        builder.Property(x => x.NetShare).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class HoldbackReserveConfiguration : IEntityTypeConfiguration<HoldbackReserve>
{
    public void Configure(EntityTypeBuilder<HoldbackReserve> builder)
    {
        builder.ToTable("payments_holdback_reserves");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.ContractId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ContractId);
        builder.Property(x => x.PayableId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.PayableId).IsUnique();
        builder.Property(x => x.HoldbackAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ReleaseAuthorizedBy).HasMaxLength(255);
        builder.Property(x => x.DmSignoffBy).HasMaxLength(255);
        builder.Property(x => x.QualitySignoffBy).HasMaxLength(255);
        builder.Property(x => x.ReleaseNotes).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class PaymentAuditLogConfiguration : IEntityTypeConfiguration<PaymentAuditLog>
{
    public void Configure(EntityTypeBuilder<PaymentAuditLog> builder)
    {
        builder.ToTable("payments_audit_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.EntityType).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.EntityType);
        builder.Property(x => x.EntityId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.EntityId);
        builder.Property(x => x.Action).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.Action);
        builder.Property(x => x.PreviousStateJson).HasColumnType("jsonb");
        builder.Property(x => x.NewStateJson).HasColumnType("jsonb");
        builder.Property(x => x.Details).HasMaxLength(2000);
        builder.Property(x => x.UserId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.UserId);
        builder.Property(x => x.UserRoles).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.CreatedAt);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class SelfBillingInvoiceConfiguration : IEntityTypeConfiguration<SelfBillingInvoice>
{
    public void Configure(EntityTypeBuilder<SelfBillingInvoice> builder)
    {
        builder.ToTable("payments_self_billing_invoices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.InvoiceNumber).IsUnique();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.ContractId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.ContractId);
        builder.Property(x => x.BillingCycle).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.BillingCycle);
        builder.Property(x => x.PeriodStart).HasMaxLength(50).IsRequired();
        builder.Property(x => x.PeriodEnd).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.TotalBaseAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalOverheadAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalPatientStipend).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalTravelReimbursement).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalGrossAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalHoldbackAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TotalNetAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ApprovedBy).HasMaxLength(255);
        builder.Property(x => x.ErpSystem).HasMaxLength(50);
        builder.Property(x => x.ErpReferenceId).HasMaxLength(100);
        builder.HasIndex(x => x.ErpReferenceId);
        builder.Property(x => x.ErpSyncStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.ErpSyncStatus);
        builder.Property(x => x.ReconciledBy).HasMaxLength(255);
        builder.Property(x => x.ReconciliationReference).HasMaxLength(255);
        builder.Property(x => x.ReconciliationNotes).HasMaxLength(1000);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class InvoiceLineItemConfiguration : IEntityTypeConfiguration<InvoiceLineItem>
{
    public void Configure(EntityTypeBuilder<InvoiceLineItem> builder)
    {
        builder.ToTable("payments_invoice_line_items");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.InvoiceId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.InvoiceId);
        builder.Property(x => x.PayableId).HasMaxLength(36);
        builder.HasIndex(x => x.PayableId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.VisitCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.VisitCode);
        builder.Property(x => x.ProcedureCode).HasMaxLength(100);
        builder.Property(x => x.ProcedureName).HasMaxLength(255);
        builder.Property(x => x.BaseAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.OverheadAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.PatientStipend).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TravelReimbursement).HasColumnType("numeric(18,4)");
        builder.Property(x => x.GrossAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingRate).HasColumnType("numeric(18,4)");
        builder.Property(x => x.WithholdingAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.NetAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class RemittanceAdviceConfiguration : IEntityTypeConfiguration<RemittanceAdvice>
{
    public void Configure(EntityTypeBuilder<RemittanceAdvice> builder)
    {
        builder.ToTable("payments_remittance_advices");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.RemittanceNumber).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.RemittanceNumber).IsUnique();
        builder.Property(x => x.InvoiceId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.InvoiceId).IsUnique();
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.GrossAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.OverheadAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.HoldbackAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.NetAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.PdfSha256Hash).HasMaxLength(64).IsRequired();
        builder.Property(x => x.SignedBy).HasMaxLength(255);
        builder.Property(x => x.SignerRole).HasMaxLength(100);
        builder.Property(x => x.SignatureManifestJson).HasColumnType("jsonb");
        builder.Property(x => x.DispatchedTo).HasMaxLength(255);
        builder.Property(x => x.DispatchStatus).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.DispatchStatus);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ErpSyncLogConfiguration : IEntityTypeConfiguration<ErpSyncLog>
{
    public void Configure(EntityTypeBuilder<ErpSyncLog> builder)
    {
        builder.ToTable("payments_erp_sync_logs");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.InvoiceId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.InvoiceId);
        builder.Property(x => x.ErpSystem).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.ErpSystem);
        builder.Property(x => x.SyncDirection).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.PayloadJson).HasColumnType("jsonb");
        builder.Property(x => x.ResponseDataJson).HasColumnType("jsonb");
        builder.Property(x => x.ExternalReference).HasMaxLength(100);
        builder.HasIndex(x => x.ExternalReference);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

/// <summary>
/// Participant wallet mapping. Processor references are encrypted at rest; bank routing and account numbers are
/// never stored, only their masks (#4770).
/// </summary>
public class ParticipantWalletConfiguration(IFieldEncryptor fieldEncryptor) : IEntityTypeConfiguration<ParticipantWallet>
{
    /// <summary>Encryption purpose (associated data) for <see cref="ParticipantWallet.VirtualCardProcessorReference"/>.</summary>
    public const string VirtualCardProcessorReferencePurpose = "payments.participant_wallets.virtual_card_processor_reference";

    /// <summary>Encryption purpose (associated data) for <see cref="ParticipantWallet.AchProcessorReference"/>.</summary>
    public const string AchProcessorReferencePurpose = "payments.participant_wallets.ach_processor_reference";

    /// <summary>Longest processor reference accepted, in characters.</summary>
    public const int MaxProcessorReferenceLength = 255;

    public void Configure(EntityTypeBuilder<ParticipantWallet> builder)
    {
        builder.ToTable("payments_participant_wallets");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.CurrentBalance).HasColumnType("numeric(18,4)");
        builder.Property(x => x.LifetimeStipends).HasColumnType("numeric(18,4)");
        builder.Property(x => x.LifetimeReimbursements).HasColumnType("numeric(18,4)");
        builder.Property(x => x.LifetimeDisbursements).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TaxableYtd).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AntiCoercionCap).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AutoDisbursementThreshold).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.W9Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.W9Status);
        builder.Property(x => x.TaxIdLastFour).HasMaxLength(4);
        builder.Property(x => x.CardType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.CardStatus).HasMaxLength(50).IsRequired();
        builder.Property(x => x.VirtualCardProcessorReference)
            .IsEncrypted(fieldEncryptor, VirtualCardProcessorReferencePurpose, MaxProcessorReferenceLength);
        builder.Property(x => x.VirtualCardMask).HasMaxLength(50);
        builder.Property(x => x.VirtualCardExpiry).HasMaxLength(20);
        builder.Property(x => x.AchProcessorReference)
            .IsEncrypted(fieldEncryptor, AchProcessorReferencePurpose, MaxProcessorReferenceLength);
        builder.Property(x => x.AchRoutingMask).HasMaxLength(20);
        builder.Property(x => x.AchAccountMask).HasMaxLength(50);
        builder.Property(x => x.AchAccountHolder).HasMaxLength(255);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ParticipantStipendRuleConfiguration : IEntityTypeConfiguration<ParticipantStipendRule>
{
    public void Configure(EntityTypeBuilder<ParticipantStipendRule> builder)
    {
        builder.ToTable("payments_participant_stipend_rules");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.VisitCode).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.VisitCode);
        builder.Property(x => x.VisitName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ProcedureCode).HasMaxLength(100);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Currency).HasMaxLength(10).IsRequired();
        builder.Property(x => x.AntiCoercionCap).HasColumnType("numeric(18,4)");
        builder.Property(x => x.AutoDisbursementThreshold).HasColumnType("numeric(18,4)");
        builder.Property(x => x.MilestoneCode).HasMaxLength(100);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ParticipantTransactionConfiguration : IEntityTypeConfiguration<ParticipantTransaction>
{
    public void Configure(EntityTypeBuilder<ParticipantTransaction> builder)
    {
        builder.ToTable("payments_participant_transactions");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.WalletId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.WalletId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.TransactionType).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.TransactionType);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.BalanceAfter).HasColumnType("numeric(18,4)");
        builder.Property(x => x.ReferenceId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.ReferenceId);
        builder.Property(x => x.Description).HasMaxLength(500).IsRequired();
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
    }
}

public class ParticipantExpenseClaimConfiguration : IEntityTypeConfiguration<ParticipantExpenseClaim>
{
    public void Configure(EntityTypeBuilder<ParticipantExpenseClaim> builder)
    {
        builder.ToTable("payments_participant_expense_claims");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.WalletId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.WalletId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.StudyId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.StudyId);
        builder.Property(x => x.SiteId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SiteId);
        builder.Property(x => x.ExpenseDate).HasMaxLength(50).IsRequired();
        builder.Property(x => x.Category).HasMaxLength(50).IsRequired();
        builder.Property(x => x.MerchantName).HasMaxLength(255).IsRequired();
        builder.Property(x => x.GrossAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.TaxDeduction).HasColumnType("numeric(18,4)");
        builder.Property(x => x.NetAmount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.ReceiptImageName).HasMaxLength(255);
        builder.Property(x => x.ReceiptOcrDataJson).HasColumnType("jsonb");
        builder.Property(x => x.ReviewedBy).HasMaxLength(255);
        builder.Property(x => x.ReviewNotes).HasMaxLength(1000);
        builder.Property(x => x.DisbursementReference).HasMaxLength(100);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
        builder.Property(x => x.ReasonForChange).HasMaxLength(1000);
    }
}

public class ParticipantDisbursementConfiguration : IEntityTypeConfiguration<ParticipantDisbursement>
{
    public void Configure(EntityTypeBuilder<ParticipantDisbursement> builder)
    {
        builder.ToTable("payments_participant_disbursements");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).HasMaxLength(36);
        builder.Property(x => x.WalletId).HasMaxLength(36).IsRequired();
        builder.HasIndex(x => x.WalletId);
        builder.Property(x => x.SubjectId).HasMaxLength(255).IsRequired();
        builder.HasIndex(x => x.SubjectId);
        builder.Property(x => x.Amount).HasColumnType("numeric(18,4)");
        builder.Property(x => x.DisbursementType).HasMaxLength(50).IsRequired();
        builder.Property(x => x.ReferenceNumber).HasMaxLength(100).IsRequired();
        builder.HasIndex(x => x.ReferenceNumber).IsUnique();
        builder.Property(x => x.Status).HasMaxLength(50).IsRequired();
        builder.HasIndex(x => x.Status);
        builder.Property(x => x.CreatedBy).HasMaxLength(255).IsRequired();
    }
}
