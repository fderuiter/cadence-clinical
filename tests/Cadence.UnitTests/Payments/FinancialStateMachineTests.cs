using System;
using System.Collections.Generic;
using System.Data.Common;
using System.Linq;
using System.Threading.Tasks;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Endpoints;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Kernel.Time;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

using Xunit;

namespace Cadence.UnitTests.Payments;

public sealed class FinancialStateMachineTests : IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private readonly DateTimeOffset _now = new(2026, 11, 14, 10, 0, 0, TimeSpan.Zero);

    public FinancialStateMachineTests()
    {
        _connection.Open();
        using var context = CreateContext();
        context.Database.EnsureCreated();
    }

    public void Dispose() => _connection.Dispose();

    private PaymentsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseSqlite(_connection)
            .Options;

        return new PaymentsDbContext(options, TestFieldProtection.CreateEncryptor());
    }

    #region 1. Payable State Machine Tests

    [Fact]
    public void Payable_InitialStatus_DefaultsToTriggered()
    {
        var payable = new Payable
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        payable.Status.Should().Be("TRIGGERED");
    }

    [Fact]
    public void Payable_WithValidDeclaredStatus_Succeeds()
    {
        var payable = new Payable
        {
            Status = "VERIFIED",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        payable.Status.Should().Be("VERIFIED");
    }

    [Fact]
    public void Payable_WithInvalidStatus_ThrowsInvalidStateTransitionException()
    {
        var act = () => new Payable
        {
            Status = "UNKNOWN_STATUS",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*not a valid declared status*");
    }

    [Fact]
    public void Payable_VerifySourceData_FromTriggered_TransitionsToVerified()
    {
        var payable = new Payable
        {
            IsSdvGated = true,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        payable.VerifySourceData("verifier-1", _now);

        payable.Status.Should().Be("VERIFIED");
        payable.SdvVerifiedBy.Should().Be("verifier-1");
        payable.SdvVerifiedAt.Should().Be(_now);
        payable.VersionIndex.Should().Be(2);
    }

    [Fact]
    public void Payable_VerifySourceData_WhenAlreadyInvoiced_ThrowsInvalidStateTransitionException()
    {
        var payable = new Payable
        {
            IsSdvGated = false,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        payable.MarkInvoiced(_now);

        var act = () => payable.VerifySourceData("verifier-1", _now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*already been invoiced*");
    }

    [Fact]
    public void Payable_VerifySourceData_WhenCancelled_ThrowsInvalidStateTransitionException()
    {
        var payable = new Payable
        {
            IsSdvGated = true,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        payable.Cancel(_now);

        var act = () => payable.VerifySourceData("verifier-1", _now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cancelled*");
    }

    [Fact]
    public void Payable_VerifySourceData_WithEmptyVerifier_ThrowsArgumentException()
    {
        var payable = new Payable
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        var act = () => payable.VerifySourceData("", _now);

        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Payable_MarkInvoiced_WhenNotSdvGated_TransitionsToInvoiced()
    {
        var payable = new Payable
        {
            IsSdvGated = false,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        payable.MarkInvoiced(_now);

        payable.Status.Should().Be("INVOICED");
        payable.UpdatedAt.Should().Be(_now);
        payable.VersionIndex.Should().Be(2);
    }

    [Fact]
    public void Payable_MarkInvoiced_WhenSdvGatedAndVerified_TransitionsToInvoiced()
    {
        var payable = new Payable
        {
            IsSdvGated = true,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        payable.VerifySourceData("cra-1", _now);
        payable.MarkInvoiced(_now);

        payable.Status.Should().Be("INVOICED");
    }

    [Fact]
    public void Payable_MarkInvoiced_WhenSdvGatedAndUnverified_RejectsTransitionToInvoiced()
    {
        // Criterion 1: The Payable entity rejects transition to invoiced when source data verification is pending.
        var payable = new Payable
        {
            IsSdvGated = true,
            SdvVerifiedAt = null,
            SdvVerifiedBy = null,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        var act = () => payable.MarkInvoiced(_now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*source data verification (SDV) is pending*");
        payable.Status.Should().Be("TRIGGERED");
    }

    [Fact]
    public void Payable_MarkInvoiced_WhenAlreadyInvoiced_PreventsDuplicateInvoicing()
    {
        var payable = new Payable
        {
            IsSdvGated = false,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        payable.MarkInvoiced(_now);

        var act = () => payable.MarkInvoiced(_now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*already invoiced*");
    }

    [Fact]
    public void Payable_Cancel_FromTriggeredOrVerified_TransitionsToCancelled()
    {
        var payable = new Payable
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        payable.Cancel(_now, "Protocol amendment removed procedure");

        payable.Status.Should().Be("CANCELLED");
        payable.ReasonForChange.Should().Be("Protocol amendment removed procedure");
    }

    [Fact]
    public void Payable_Cancel_WhenAlreadyInvoiced_ThrowsInvalidStateTransitionException()
    {
        var payable = new Payable
        {
            IsSdvGated = false,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        payable.MarkInvoiced(_now);

        var act = () => payable.Cancel(_now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*already been invoiced*");
    }

    #endregion

    #region 2. SelfBillingInvoice State Machine Tests

    [Fact]
    public void SelfBillingInvoice_InitialStatus_DefaultsToDraft()
    {
        var invoice = new SelfBillingInvoice
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        invoice.Status.Should().Be("DRAFT");
    }

    [Fact]
    public void SelfBillingInvoice_WithValidDeclaredStatus_Succeeds()
    {
        var invoice = new SelfBillingInvoice
        {
            Status = "APPROVED",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        invoice.Status.Should().Be("APPROVED");
    }

    [Fact]
    public void SelfBillingInvoice_WithInvalidStatus_ThrowsInvalidStateTransitionException()
    {
        var act = () => new SelfBillingInvoice
        {
            Status = "INVALID",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*not a valid declared status*");
    }

    [Fact]
    public void SelfBillingInvoice_Approve_FromDraft_TransitionsToApproved()
    {
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        invoice.Approve("finance-mgr", _now);

        invoice.Status.Should().Be("APPROVED");
        invoice.ApprovedBy.Should().Be("finance-mgr");
        invoice.ApprovedAt.Should().Be(_now);
        invoice.VersionIndex.Should().Be(2);
    }

    [Fact]
    public void SelfBillingInvoice_Approve_WhenAlreadyApproved_ThrowsInvalidStateTransitionException()
    {
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        invoice.Approve("finance-mgr", _now);

        var act = () => invoice.Approve("finance-mgr-2", _now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cannot be approved because it is in status 'APPROVED'*");
    }

    [Fact]
    public void SelfBillingInvoice_ExportToErp_WhenApproved_Succeeds()
    {
        // Criterion 2: The SelfBillingInvoice entity blocks ERP export until authorized users approve the invoice.
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        invoice.Approve("finance-mgr", _now);

        invoice.ExportToErp("SAP", "ERP-SAP-98765", _now);

        invoice.ErpSystem.Should().Be("SAP");
        invoice.ErpReferenceId.Should().Be("ERP-SAP-98765");
        invoice.ErpSyncStatus.Should().Be("EXPORTED");
        invoice.ErpSyncedAt.Should().Be(_now);
    }

    [Fact]
    public void SelfBillingInvoice_ExportToErp_WhenDraft_BlocksExport()
    {
        // Criterion 2: The SelfBillingInvoice entity blocks ERP export until authorized users approve the invoice.
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        var act = () => invoice.ExportToErp("SAP", "ERP-SAP-12345", _now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*must be in 'APPROVED' status before ERP export, but is currently in 'DRAFT'*");
        invoice.ErpSyncStatus.Should().Be("PENDING");
    }

    [Fact]
    public void SelfBillingInvoice_ExportToErp_WhenCancelled_BlocksExport()
    {
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        invoice.Cancel(_now);

        var act = () => invoice.ExportToErp("SAP", "ERP-SAP-12345", _now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*must be in 'APPROVED' status before ERP export, but is currently in 'CANCELLED'*");
    }

    [Fact]
    public void SelfBillingInvoice_Reconcile_WhenApproved_TransitionsToReconciled()
    {
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        invoice.Approve("finance-mgr", _now);

        invoice.Reconcile("treasury-1", _now, "WIRE-REF-999", "Cleared in full", "SAP");

        invoice.Status.Should().Be("RECONCILED");
        invoice.ReconciledBy.Should().Be("treasury-1");
        invoice.ReconciledAt.Should().Be(_now);
        invoice.ReconciliationReference.Should().Be("WIRE-REF-999");
    }

    [Fact]
    public void SelfBillingInvoice_Reconcile_WhenDraft_ThrowsInvalidStateTransitionException()
    {
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        var act = () => invoice.Reconcile("treasury-1", _now, "WIRE-REF-999", null);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*must be in 'APPROVED' status before settlement reconciliation, but is currently in 'DRAFT'*");
    }

    [Fact]
    public void SelfBillingInvoice_Cancel_WhenAlreadyReconciled_ThrowsInvalidStateTransitionException()
    {
        var invoice = new SelfBillingInvoice
        {
            InvoiceNumber = "INV-001",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        invoice.Approve("finance-mgr", _now);
        invoice.Reconcile("treasury-1", _now, "WIRE-REF-999", null);

        var act = () => invoice.Cancel(_now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*already been reconciled*");
    }

    #endregion

    #region 3. ParticipantExpenseClaim State Machine Tests

    [Fact]
    public void ParticipantExpenseClaim_InitialStatus_DefaultsToPendingReview()
    {
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        claim.Status.Should().Be("PENDING_REVIEW");
    }

    [Fact]
    public void ParticipantExpenseClaim_WithValidDeclaredStatus_Succeeds()
    {
        var claim = new ParticipantExpenseClaim
        {
            Status = "APPROVED",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        claim.Status.Should().Be("APPROVED");
    }

    [Fact]
    public void ParticipantExpenseClaim_WithInvalidStatus_ThrowsInvalidStateTransitionException()
    {
        var act = () => new ParticipantExpenseClaim
        {
            Status = "BOGUS",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*not a valid declared status*");
    }

    [Fact]
    public void ParticipantExpenseClaim_Approve_FromPendingReview_TransitionsToApproved()
    {
        var claim = new ParticipantExpenseClaim
        {
            GrossAmount = 100.00m,
            TaxDeduction = 10.00m,
            NetAmount = 90.00m,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        claim.Approve("site-crc-1", _now, "Approved receipt verified");

        claim.Status.Should().Be("APPROVED");
        claim.ReviewedBy.Should().Be("site-crc-1");
        claim.ReviewedAt.Should().Be(_now);
        claim.ReviewNotes.Should().Be("Approved receipt verified");
        claim.VersionIndex.Should().Be(2);
    }

    [Fact]
    public void ParticipantExpenseClaim_Approve_WithAdjustedAmount_RecalculatesNet()
    {
        var claim = new ParticipantExpenseClaim
        {
            GrossAmount = 100.00m,
            TaxDeduction = 10.00m,
            NetAmount = 90.00m,
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        claim.Approve("site-crc-1", _now, "Cap applied", adjustedGrossAmount: 80.00m);

        claim.GrossAmount.Should().Be(80.00m);
        claim.NetAmount.Should().Be(70.00m);
    }

    [Fact]
    public void ParticipantExpenseClaim_Approve_WhenAlreadyApproved_ForbidsMultipleApprovals()
    {
        // Criterion 3: The ParticipantExpenseClaim entity forbids multiple approvals or wallet credit operations.
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        claim.Approve("site-crc-1", _now);

        var act = () => claim.Approve("site-crc-2", _now);

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cannot be approved because it is in status 'APPROVED', but 'PENDING_REVIEW' is required*");
    }

    [Fact]
    public void ParticipantExpenseClaim_Reject_FromPendingReview_TransitionsToRejected()
    {
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        claim.Reject("site-crc-1", _now, "Unallowable meal expense");

        claim.Status.Should().Be("REJECTED");
        claim.ReviewedBy.Should().Be("site-crc-1");
        claim.ReviewNotes.Should().Be("Unallowable meal expense");
    }

    [Fact]
    public void ParticipantExpenseClaim_ReviewActions_WhenRejected_ThrowsInvalidStateTransitionException()
    {
        // Requirement 4: Expense claims must permit review actions only from pending review status.
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        claim.Reject("site-crc-1", _now);

        var actApprove = () => claim.Approve("site-crc-2", _now);
        var actReject = () => claim.Reject("site-crc-2", _now);

        actApprove.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cannot be approved because it is in status 'REJECTED'*");
        actReject.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cannot be rejected because it is in status 'REJECTED'*");
    }

    [Fact]
    public void ParticipantExpenseClaim_RecordWalletCredit_ForbidsMultipleWalletCredits()
    {
        // Criterion 3: The ParticipantExpenseClaim entity forbids multiple approvals or wallet credit operations.
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        claim.Approve("site-crc-1", _now);

        claim.RecordWalletCredit();
        claim.IsWalletCredited.Should().BeTrue();

        var actDuplicate = () => claim.RecordWalletCredit();
        actDuplicate.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*Duplicate wallet credits are prohibited*");
    }

    [Fact]
    public void ParticipantExpenseClaim_RecordWalletCredit_WhenNotApproved_ThrowsInvalidStateTransitionException()
    {
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };

        var act = () => claim.RecordWalletCredit();
        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*must be approved before crediting wallet*");
    }

    [Fact]
    public void ParticipantExpenseClaim_RecordDisbursement_ForbidsDuplicateDisbursementReference()
    {
        var claim = new ParticipantExpenseClaim
        {
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        claim.Approve("site-crc-1", _now);

        claim.RecordDisbursement("DISB-ACH-001");
        claim.DisbursementReference.Should().Be("DISB-ACH-001");

        var actDuplicate = () => claim.RecordDisbursement("DISB-ACH-002");
        actDuplicate.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*already has a disbursement reference 'DISB-ACH-001'. Duplicate disbursement is prohibited*");
    }

    #endregion

    #region 4. ParticipantDisbursement State Machine Tests

    [Fact]
    public void ParticipantDisbursement_InitialStatus_DefaultsToDispatched()
    {
        var disb = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-001",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };

        disb.Status.Should().Be("DISPATCHED");
    }

    [Fact]
    public void ParticipantDisbursement_WithValidDeclaredStatus_Succeeds()
    {
        var disb = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-001",
            Status = "SETTLED",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };

        disb.Status.Should().Be("SETTLED");
    }

    [Fact]
    public void ParticipantDisbursement_WithInvalidStatus_ThrowsInvalidStateTransitionException()
    {
        var act = () => new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-001",
            Status = "NOT_A_STATUS",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };

        act.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*not a valid declared status*");
    }

    [Fact]
    public void ParticipantDisbursement_Settle_FromDispatched_TransitionsToSettled()
    {
        // Criterion 4: The ParticipantDisbursement entity enforces valid lifecycle progression from dispatched to settled.
        var disb = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-001",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };

        disb.Settle(_now);

        disb.Status.Should().Be("SETTLED");
        disb.VersionIndex.Should().Be(2);
    }

    [Fact]
    public void ParticipantDisbursement_Settle_WhenAlreadySettled_ThrowsInvalidStateTransitionException()
    {
        // Requirement 5: Disbursements must prevent duplicate dispatch or settlement actions on existing payments.
        var disb = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-001",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };
        disb.Settle(_now);

        var actDuplicate = () => disb.Settle(_now);

        actDuplicate.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*already settled. Duplicate settlement is prohibited*");
    }

    [Fact]
    public void ParticipantDisbursement_Settle_WhenFailedOrCancelled_ThrowsInvalidStateTransitionException()
    {
        var disbFailed = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-FAIL",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };
        disbFailed.Fail(_now, "Bank routing failed");

        var actFailed = () => disbFailed.Settle(_now);
        actFailed.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cannot be settled. Only dispatched disbursements can be settled*");

        var disbCancelled = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-CANCEL",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };
        disbCancelled.Cancel(_now, "Stop payment");

        var actCancelled = () => disbCancelled.Settle(_now);
        actCancelled.Should().Throw<InvalidStateTransitionException>()
            .WithMessage("*cannot be settled. Only dispatched disbursements can be settled*");
    }

    [Fact]
    public void ParticipantDisbursement_FailAndCancel_WhenSettled_ThrowsInvalidStateTransitionException()
    {
        var disb = new ParticipantDisbursement
        {
            ReferenceNumber = "DISB-001",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };
        disb.Settle(_now);

        var actFail = () => disb.Fail(_now);
        var actCancel = () => disb.Cancel(_now);

        actFail.Should().Throw<InvalidStateTransitionException>();
        actCancel.Should().Throw<InvalidStateTransitionException>();
    }

    #endregion

    #region 5. Persistence Check Constraint Tests (Criterion 5)

    [Fact]
    public void PersistenceConfigurations_HaveCheckConstraintsForDeclaredDomainEnumerations()
    {
        // Criterion 5: Persistence configurations restrict status columns to declared domain enumeration values.
        var builder = new ModelBuilder();
        builder.ApplyConfigurationsFromAssembly(typeof(PaymentsDbContext).Assembly);
        var model = builder.Model;

        // 1. Payable check constraint
        var payableType = model.FindEntityType(typeof(Payable));
        payableType.Should().NotBeNull();
        var payableConstraints = payableType!.GetCheckConstraints().ToList();
        payableConstraints.Should().Contain(c => c.Name == "ck_payments_payables_status");
        var payableSql = payableConstraints.First(c => c.Name == "ck_payments_payables_status").Sql;
        payableSql.Should().Contain("TRIGGERED").And.Contain("VERIFIED").And.Contain("INVOICED").And.Contain("CANCELLED");

        // 2. SelfBillingInvoice check constraint
        var invoiceType = model.FindEntityType(typeof(SelfBillingInvoice));
        invoiceType.Should().NotBeNull();
        var invoiceConstraints = invoiceType!.GetCheckConstraints().ToList();
        invoiceConstraints.Should().Contain(c => c.Name == "ck_payments_self_billing_invoices_status");
        var invoiceSql = invoiceConstraints.First(c => c.Name == "ck_payments_self_billing_invoices_status").Sql;
        invoiceSql.Should().Contain("DRAFT").And.Contain("APPROVED").And.Contain("RECONCILED").And.Contain("CANCELLED");

        // 3. ParticipantExpenseClaim check constraint
        var claimType = model.FindEntityType(typeof(ParticipantExpenseClaim));
        claimType.Should().NotBeNull();
        var claimConstraints = claimType!.GetCheckConstraints().ToList();
        claimConstraints.Should().Contain(c => c.Name == "ck_payments_participant_expense_claims_status");
        var claimSql = claimConstraints.First(c => c.Name == "ck_payments_participant_expense_claims_status").Sql;
        claimSql.Should().Contain("PENDING_REVIEW").And.Contain("APPROVED").And.Contain("REJECTED");

        // 4. ParticipantDisbursement check constraint
        var disbType = model.FindEntityType(typeof(ParticipantDisbursement));
        disbType.Should().NotBeNull();
        var disbConstraints = disbType!.GetCheckConstraints().ToList();
        disbConstraints.Should().Contain(c => c.Name == "ck_payments_participant_disbursements_status");
        var disbSql = disbConstraints.First(c => c.Name == "ck_payments_participant_disbursements_status").Sql;
        disbSql.Should().Contain("DISPATCHED").And.Contain("SETTLED").And.Contain("FAILED").And.Contain("CANCELLED");
    }

    [Fact]
    public async Task Persistence_RawSqlInsert_WithInvalidStatus_ViolatesCheckConstraint()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();
        await db.Database.EnsureCreatedAsync(ct);

        // Attempting to bypass domain model with raw SQL insert having invalid status
        var sql = @"
            INSERT INTO payments_payables (
                ""Id"", ""StudyId"", ""SiteId"", ""ContractId"", ""SubjectId"", ""VisitCode"", ""TriggerType"",
                ""TriggerEventId"", ""TriggerSource"", ""BaseAmount"", ""OverheadAmount"", ""PatientStipendAmount"",
                ""TravelReimbursementAmount"", ""GrossAmount"", ""WithholdingRate"", ""WithholdingAmount"", ""NetAmount"",
                ""Currency"", ""Status"", ""IsSdvGated"", ""CreatedAt"", ""CreatedBy"", ""UpdatedAt"", ""VersionIndex""
            ) VALUES (
                'PAY-INVALID', 'S1', 'SITE1', 'C1', 'SUB1', 'V1', 'VISIT',
                'EVT-INV', 'EDC', 100, 20, 0,
                0, 120, 10, 12, 108,
                'USD', 'INVALID_STATUS', 0, '2026-11-14', 'test', '2026-11-14', 1
            )";

        var act = async () => await db.Database.ExecuteSqlRawAsync(sql, ct);
        await act.Should().ThrowAsync<DbException>();
    }

    #endregion

    #region 6. Endpoints HTTP 409 Conflict Response Tests

    [Fact]
    public async Task ReviewExpenseClaim_WhenAlreadyApproved_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        // Constraint: Endpoints must return standard HTTP 409 Conflict errors when an illegal state transition occurs.
        await using var db = CreateContext();

        var claim = new ParticipantExpenseClaim
        {
            Id = "claim-001",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            SubjectId = "SUBJ-01",
            WalletId = "WALLET-01",
            GrossAmount = 50.00m,
            NetAmount = 50.00m,
            CreatedAt = _now,
            CreatedBy = "sub-1",
            UpdatedAt = _now
        };
        claim.Approve("crc-1", _now);
        db.ParticipantExpenseClaims.Add(claim);
        await db.SaveChangesAsync(ct);

        var caller = TestSecurityContext.CreateScopedPrincipal("crc-1", ["STUDY-01"], ["SITE-01"], "crc");
        var req = new ReviewExpenseClaimRequestDto(Status: "APPROVED", ReviewNotes: "Second approval");

        var method = typeof(PaymentEndpoints).GetMethod("ReviewExpenseClaim",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, ["claim-001", req, db, new DefaultHttpContext { User = caller }])!;
        var result = await task;

        var conflict = result as IStatusCodeHttpResult;
        conflict.Should().NotBeNull("Method must return standard 409 Conflict on illegal state transition");
        conflict!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ExportInvoiceToErp_WhenInvoiceIsDraft_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var invoice = new SelfBillingInvoice
        {
            Id = "inv-001",
            InvoiceNumber = "INV-001",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            ContractId = "C-1",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        db.SelfBillingInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        var caller = TestSecurityContext.CreateScopedPrincipal("finance-1", ["STUDY-01"], ["SITE-01"], "Grants Manager");
        var clock = new FakeClock();

        var method = typeof(PaymentEndpoints).GetMethod("ExportInvoiceToErp",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, ["inv-001", db, clock, new DefaultHttpContext { User = caller }, "SAP"])!;
        var result = await task;

        var conflict = result as IStatusCodeHttpResult;
        conflict.Should().NotBeNull("Method must return 409 Conflict when attempting to export unapproved invoice");
        conflict!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ReconcileInvoice_WhenInvoiceIsDraft_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var invoice = new SelfBillingInvoice
        {
            Id = "inv-002",
            InvoiceNumber = "INV-002",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            ContractId = "C-1",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        db.SelfBillingInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        var caller = TestSecurityContext.CreateScopedPrincipal("finance-1", ["STUDY-01"], ["SITE-01"], "Grants Manager");
        var clock = new FakeClock();
        var req = new ReconcileInvoiceRequest(PaymentReference: "WIRE-123");

        var method = typeof(PaymentEndpoints).GetMethod("ReconcileInvoice",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, ["inv-002", req, db, clock, new DefaultHttpContext { User = caller }])!;
        var result = await task;

        var conflict = result as IStatusCodeHttpResult;
        conflict.Should().NotBeNull("Method must return 409 Conflict when attempting to reconcile unapproved invoice");
        conflict!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task ApproveInvoice_WhenAlreadyApproved_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var invoice = new SelfBillingInvoice
        {
            Id = "inv-003",
            InvoiceNumber = "INV-003",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            ContractId = "C-1",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        invoice.Approve("finance-mgr", _now);
        db.SelfBillingInvoices.Add(invoice);
        await db.SaveChangesAsync(ct);

        var caller = TestSecurityContext.CreateScopedPrincipal("finance-1", ["STUDY-01"], ["SITE-01"], "Grants Manager");
        var clock = new FakeClock();

        var method = typeof(PaymentEndpoints).GetMethod("ApproveInvoice",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, ["inv-003", db, clock, new DefaultHttpContext { User = caller }])!;
        var result = await task;

        var conflict = result as IStatusCodeHttpResult;
        conflict.Should().NotBeNull("Method must return 409 Conflict on redundant approval");
        conflict!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task SettleDisbursement_FromDispatched_SucceedsAndRecordsAudit()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var db = CreateContext();

        var wallet = new ParticipantWallet
        {
            Id = "w-001",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            SubjectId = "SUBJ-01",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        db.ParticipantWallets.Add(wallet);

        var disb = new ParticipantDisbursement
        {
            Id = "disb-001",
            WalletId = "w-001",
            SubjectId = "SUBJ-01",
            Amount = 100.00m,
            ReferenceNumber = "DISB-ACH-12345",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };
        db.ParticipantDisbursements.Add(disb);
        await db.SaveChangesAsync(ct);

        var caller = TestSecurityContext.CreateScopedPrincipal("finance-1", ["STUDY-01"], ["SITE-01"], "Grants Manager");
        var clock = new FakeClock();

        var method = typeof(PaymentEndpoints).GetMethod("SettleDisbursement",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, ["disb-001", db, clock, new DefaultHttpContext { User = caller }])!;
        var result = await task;

        var okResult = result as Ok<ParticipantDisbursement>;
        okResult.Should().NotBeNull();
        okResult!.Value!.Status.Should().Be("SETTLED");

        var audit = await db.PaymentAuditLogs.FirstOrDefaultAsync(a => a.EntityId == "disb-001" && a.Action == "DIGITAL_DISBURSEMENT_SETTLED", ct);
        audit.Should().NotBeNull();
    }

    [Fact]
    public async Task SettleDisbursement_WhenAlreadySettled_Returns409Conflict()
    {
        var ct = TestContext.Current.CancellationToken;
        // Requirement 5 & Criterion 4: Duplicate settlement action is prevented
        await using var db = CreateContext();

        var wallet = new ParticipantWallet
        {
            Id = "w-002",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            SubjectId = "SUBJ-02",
            CreatedAt = _now,
            CreatedBy = "user-1",
            UpdatedAt = _now
        };
        db.ParticipantWallets.Add(wallet);

        var disb = new ParticipantDisbursement
        {
            Id = "disb-002",
            WalletId = "w-002",
            SubjectId = "SUBJ-02",
            Amount = 100.00m,
            ReferenceNumber = "DISB-ACH-22222",
            CreatedAt = _now,
            CreatedBy = "user-1"
        };
        disb.Settle(_now);
        db.ParticipantDisbursements.Add(disb);
        await db.SaveChangesAsync(ct);

        var caller = TestSecurityContext.CreateScopedPrincipal("finance-1", ["STUDY-01"], ["SITE-01"], "Grants Manager");
        var clock = new FakeClock();

        var method = typeof(PaymentEndpoints).GetMethod("SettleDisbursement",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, ["disb-002", db, clock, new DefaultHttpContext { User = caller }])!;
        var result = await task;

        var conflict = result as IStatusCodeHttpResult;
        conflict.Should().NotBeNull("Method must return 409 Conflict on redundant settlement attempt");
        conflict!.StatusCode.Should().Be(409);
    }

    [Fact]
    public async Task CompileBillingCycle_WhenPayablesAreUnverifiedSdvGated_ExcludesThemAndAvoidsPrematureInvoicing()
    {
        var ct = TestContext.Current.CancellationToken;
        // Success metric 3: Zero premature invoices created from unverified clinical payables during billing cycles.
        await using var db = CreateContext();

        // 1. Unverified SDV-gated payable
        db.Payables.Add(new Payable
        {
            Id = "PAY-UNVERIFIED",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            ContractId = "C-1",
            SubjectId = "SUBJ-01",
            VisitCode = "V1",
            TriggerType = "VISIT_COMPLETED",
            TriggerEventId = "EVT-UNVERIFIED",
            BaseAmount = 1000m,
            GrossAmount = 1000m,
            NetAmount = 900m,
            WithholdingAmount = 100m,
            IsSdvGated = true,
            SdvVerifiedAt = null,
            SdvVerifiedBy = null,
            CreatedAt = _now,
            CreatedBy = "edc",
            UpdatedAt = _now
        });

        // 2. Verified SDV-gated payable
        db.Payables.Add(new Payable
        {
            Id = "PAY-VERIFIED",
            StudyId = "STUDY-01",
            SiteId = "SITE-01",
            ContractId = "C-1",
            SubjectId = "SUBJ-02",
            VisitCode = "V1",
            TriggerType = "VISIT_COMPLETED",
            TriggerEventId = "EVT-VERIFIED",
            BaseAmount = 500m,
            GrossAmount = 500m,
            NetAmount = 450m,
            WithholdingAmount = 50m,
            IsSdvGated = true,
            SdvVerifiedAt = _now,
            SdvVerifiedBy = "cra-auditor",
            CreatedAt = _now,
            CreatedBy = "edc",
            UpdatedAt = _now
        });

        await db.SaveChangesAsync(ct);

        var req = new BillingCycleCompilationRequest(
            StudyId: "STUDY-01",
            SiteId: "SITE-01",
            BillingCycle: "MONTHLY",
            PeriodStart: "2026-08-01",
            PeriodEnd: "2026-08-31",
            AutoApprove: false
        );

        var clock = new FakeClock();
        var caller = PaymentEndpointsTestInvoker.FinanceOfficerContext();

        var method = typeof(PaymentEndpoints).GetMethod("CompileBillingCycle",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)!;

        var task = (Task<Microsoft.AspNetCore.Http.IResult>)method.Invoke(null, [req, db, clock, caller])!;
        var result = await task;

        var created = result as Created<SelfBillingInvoiceResponse>;
        created.Should().NotBeNull();
        created!.Value!.PayableCount.Should().Be(1, "Only verified payable must be invoiced");
        created.Value.TotalGrossAmount.Should().Be(500m);

        // Verify unverified payable remained in TRIGGERED status (zero premature invoice)
        var unverifiedPayable = await db.Payables.FirstAsync(p => p.Id == "PAY-UNVERIFIED", ct);
        unverifiedPayable.Status.Should().Be("TRIGGERED");

        // Verify verified payable transitioned to INVOICED
        var verifiedPayable = await db.Payables.FirstAsync(p => p.Id == "PAY-VERIFIED", ct);
        verifiedPayable.Status.Should().Be("INVOICED");
    }

    #endregion
}
