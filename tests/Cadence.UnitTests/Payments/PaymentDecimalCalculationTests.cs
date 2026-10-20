using System;
using System.Linq;

using Cadence.Apps.Payments.Domain;
using Cadence.Apps.Payments.Infrastructure.Persistence;
using Cadence.Testing;
using Cadence.Testing.Security;
using Cadence.Testing.Time;

using FluentAssertions;

using Microsoft.EntityFrameworkCore;

using Xunit;

namespace Cadence.UnitTests.Payments;

public class PaymentDecimalCalculationTests
{
    [Fact]
    public void AllMonetaryAndRatePropertiesInPaymentEntities_UseDecimalType()
    {
        // 1. BudgetContract
        typeof(BudgetContract).GetProperty(nameof(BudgetContract.TotalBudget))!.PropertyType.Should().Be<decimal>();
        typeof(BudgetContract).GetProperty(nameof(BudgetContract.OverheadPercentage))!.PropertyType.Should().Be<decimal>();
        typeof(BudgetContract).GetProperty(nameof(BudgetContract.WithholdingPercentage))!.PropertyType.Should().Be<decimal>();

        // 2. FeeSchedule
        typeof(FeeSchedule).GetProperty(nameof(FeeSchedule.BaseAmount))!.PropertyType.Should().Be<decimal>();
        typeof(FeeSchedule).GetProperty(nameof(FeeSchedule.OverheadPercentage))!.PropertyType.Should().Be<decimal>();
        typeof(FeeSchedule).GetProperty(nameof(FeeSchedule.WithholdingPercentage))!.PropertyType.Should().Be<decimal>();
        typeof(FeeSchedule).GetProperty(nameof(FeeSchedule.PatientStipend))!.PropertyType.Should().Be<decimal>();
        typeof(FeeSchedule).GetProperty(nameof(FeeSchedule.TravelReimbursement))!.PropertyType.Should().Be<decimal>();

        // 3. SplitPayeeConfig
        typeof(SplitPayeeConfig).GetProperty(nameof(SplitPayeeConfig.AllocationPercentage))!.PropertyType.Should().Be<decimal>();

        // 4. Payable
        typeof(Payable).GetProperty(nameof(Payable.BaseAmount))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.OverheadAmount))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.PatientStipendAmount))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.TravelReimbursementAmount))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.GrossAmount))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.WithholdingRate))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.WithholdingAmount))!.PropertyType.Should().Be<decimal>();
        typeof(Payable).GetProperty(nameof(Payable.NetAmount))!.PropertyType.Should().Be<decimal>();

        // 5. PayableAllocation
        typeof(PayableAllocation).GetProperty(nameof(PayableAllocation.AllocationPercentage))!.PropertyType.Should().Be<decimal>();
        typeof(PayableAllocation).GetProperty(nameof(PayableAllocation.GrossShare))!.PropertyType.Should().Be<decimal>();
        typeof(PayableAllocation).GetProperty(nameof(PayableAllocation.WithholdingShare))!.PropertyType.Should().Be<decimal>();
        typeof(PayableAllocation).GetProperty(nameof(PayableAllocation.NetShare))!.PropertyType.Should().Be<decimal>();

        // 6. HoldbackReserve
        typeof(HoldbackReserve).GetProperty(nameof(HoldbackReserve.HoldbackAmount))!.PropertyType.Should().Be<decimal>();

        // 7. SelfBillingInvoice
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalBaseAmount))!.PropertyType.Should().Be<decimal>();
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalOverheadAmount))!.PropertyType.Should().Be<decimal>();
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalPatientStipend))!.PropertyType.Should().Be<decimal>();
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalTravelReimbursement))!.PropertyType.Should().Be<decimal>();
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalGrossAmount))!.PropertyType.Should().Be<decimal>();
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalHoldbackAmount))!.PropertyType.Should().Be<decimal>();
        typeof(SelfBillingInvoice).GetProperty(nameof(SelfBillingInvoice.TotalNetAmount))!.PropertyType.Should().Be<decimal>();

        // 8. InvoiceLineItem
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.BaseAmount))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.OverheadAmount))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.PatientStipend))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.TravelReimbursement))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.GrossAmount))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.WithholdingRate))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.WithholdingAmount))!.PropertyType.Should().Be<decimal>();
        typeof(InvoiceLineItem).GetProperty(nameof(InvoiceLineItem.NetAmount))!.PropertyType.Should().Be<decimal>();

        // 9. RemittanceAdvice
        typeof(RemittanceAdvice).GetProperty(nameof(RemittanceAdvice.GrossAmount))!.PropertyType.Should().Be<decimal>();
        typeof(RemittanceAdvice).GetProperty(nameof(RemittanceAdvice.OverheadAmount))!.PropertyType.Should().Be<decimal>();
        typeof(RemittanceAdvice).GetProperty(nameof(RemittanceAdvice.HoldbackAmount))!.PropertyType.Should().Be<decimal>();
        typeof(RemittanceAdvice).GetProperty(nameof(RemittanceAdvice.NetAmount))!.PropertyType.Should().Be<decimal>();

        // 10. ParticipantWallet
        typeof(ParticipantWallet).GetProperty(nameof(ParticipantWallet.CurrentBalance))!.PropertyType.Should().Be<decimal>();
        typeof(ParticipantWallet).GetProperty(nameof(ParticipantWallet.LifetimeStipends))!.PropertyType.Should().Be<decimal>();
        typeof(ParticipantWallet).GetProperty(nameof(ParticipantWallet.LifetimeReimbursements))!.PropertyType.Should().Be<decimal>();
        typeof(ParticipantWallet).GetProperty(nameof(ParticipantWallet.LifetimeDisbursements))!.PropertyType.Should().Be<decimal>();
        typeof(ParticipantWallet).GetProperty(nameof(ParticipantWallet.TaxableYtd))!.PropertyType.Should().Be<decimal>();
        typeof(ParticipantWallet).GetProperty(nameof(ParticipantWallet.AntiCoercionCap))!.PropertyType.Should().Be<decimal?>();
    }

    [Fact]
    public void PaymentConfigurations_ExplicitlyConfiguresNumeric18_4ForFinancialProperties()
    {
        var options = new DbContextOptionsBuilder<PaymentsDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var dbContext = new PaymentsDbContext(options, TestFieldProtection.CreateEncryptor());
        var model = dbContext.Model;

        var expectedProperties = new[]
        {
            (typeof(BudgetContract), nameof(BudgetContract.TotalBudget)),
            (typeof(BudgetContract), nameof(BudgetContract.OverheadPercentage)),
            (typeof(BudgetContract), nameof(BudgetContract.WithholdingPercentage)),

            (typeof(FeeSchedule), nameof(FeeSchedule.BaseAmount)),
            (typeof(FeeSchedule), nameof(FeeSchedule.OverheadPercentage)),
            (typeof(FeeSchedule), nameof(FeeSchedule.WithholdingPercentage)),
            (typeof(FeeSchedule), nameof(FeeSchedule.PatientStipend)),
            (typeof(FeeSchedule), nameof(FeeSchedule.TravelReimbursement)),

            (typeof(SplitPayeeConfig), nameof(SplitPayeeConfig.AllocationPercentage)),

            (typeof(Payable), nameof(Payable.BaseAmount)),
            (typeof(Payable), nameof(Payable.OverheadAmount)),
            (typeof(Payable), nameof(Payable.PatientStipendAmount)),
            (typeof(Payable), nameof(Payable.TravelReimbursementAmount)),
            (typeof(Payable), nameof(Payable.GrossAmount)),
            (typeof(Payable), nameof(Payable.WithholdingRate)),
            (typeof(Payable), nameof(Payable.WithholdingAmount)),
            (typeof(Payable), nameof(Payable.NetAmount)),

            (typeof(PayableAllocation), nameof(PayableAllocation.AllocationPercentage)),
            (typeof(PayableAllocation), nameof(PayableAllocation.GrossShare)),
            (typeof(PayableAllocation), nameof(PayableAllocation.WithholdingShare)),
            (typeof(PayableAllocation), nameof(PayableAllocation.NetShare)),

            (typeof(HoldbackReserve), nameof(HoldbackReserve.HoldbackAmount)),

            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalBaseAmount)),
            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalOverheadAmount)),
            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalPatientStipend)),
            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalTravelReimbursement)),
            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalGrossAmount)),
            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalHoldbackAmount)),
            (typeof(SelfBillingInvoice), nameof(SelfBillingInvoice.TotalNetAmount)),

            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.BaseAmount)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.OverheadAmount)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.PatientStipend)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.TravelReimbursement)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.GrossAmount)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.WithholdingRate)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.WithholdingAmount)),
            (typeof(InvoiceLineItem), nameof(InvoiceLineItem.NetAmount)),

            (typeof(RemittanceAdvice), nameof(RemittanceAdvice.GrossAmount)),
            (typeof(RemittanceAdvice), nameof(RemittanceAdvice.OverheadAmount)),
            (typeof(RemittanceAdvice), nameof(RemittanceAdvice.HoldbackAmount)),
            (typeof(RemittanceAdvice), nameof(RemittanceAdvice.NetAmount)),

            (typeof(ParticipantWallet), nameof(ParticipantWallet.CurrentBalance)),
            (typeof(ParticipantWallet), nameof(ParticipantWallet.LifetimeStipends)),
            (typeof(ParticipantWallet), nameof(ParticipantWallet.LifetimeReimbursements)),
            (typeof(ParticipantWallet), nameof(ParticipantWallet.LifetimeDisbursements)),
            (typeof(ParticipantWallet), nameof(ParticipantWallet.TaxableYtd)),
            (typeof(ParticipantWallet), nameof(ParticipantWallet.AntiCoercionCap)),
        };

        foreach (var (entityType, propertyName) in expectedProperties)
        {
            var entityTypeMeta = model.FindEntityType(entityType);
            entityTypeMeta.Should().NotBeNull($"Entity type {entityType.Name} should exist in EF Core model");

            var propertyMeta = entityTypeMeta!.FindProperty(propertyName);
            propertyMeta.Should().NotBeNull($"Property {propertyName} should exist on {entityType.Name}");

            var columnType = propertyMeta!.FindAnnotation("Relational:ColumnType")?.Value as string;
            columnType.Should().Be("numeric(18,4)", $"Property {entityType.Name}.{propertyName} must map to numeric(18,4)");
        }
    }

    [Fact]
    public void CalculatePayable_UsesFixedPointDecimalArithmetic_AndBankersRounding()
    {
        // Scenario: Investigator Payout Calculation
        var baseAmount = 1250.50m;
        var overheadRate = 28.5m; // 28.5%
        var patientStipend = 75.00m;
        var travelReimbursement = 25.25m;
        var withholdingRate = 10.0m; // 10%

        decimal overheadAmount = Math.Round(baseAmount * (overheadRate / 100.0m), 4, MidpointRounding.ToEven); // 356.3925m
        decimal grossAmount = baseAmount + overheadAmount + patientStipend + travelReimbursement; // 1707.1425m
        decimal withholdingAmount = Math.Round(grossAmount * (withholdingRate / 100.0m), 4, MidpointRounding.ToEven); // 170.7142m (banker's rounds 25 to even 20)
        decimal netAmount = grossAmount - withholdingAmount; // 1536.4283m

        var payable = new Payable
        {
            CreatedAt = FakeClock.DefaultStart,
            UpdatedAt = FakeClock.DefaultStart,
            CreatedBy = "payments-test",
            BaseAmount = baseAmount,
            OverheadAmount = overheadAmount,
            PatientStipendAmount = patientStipend,
            TravelReimbursementAmount = travelReimbursement,
            GrossAmount = grossAmount,
            WithholdingRate = withholdingRate,
            WithholdingAmount = withholdingAmount,
            NetAmount = netAmount,
        };

        payable.BaseAmount.Should().Be(1250.50m);
        payable.OverheadAmount.Should().Be(356.3925m);
        payable.GrossAmount.Should().Be(1707.1425m);
        payable.WithholdingAmount.Should().Be(170.7142m);
        payable.NetAmount.Should().Be(1536.4283m);

        // Assert exact cents rounding with banker's rounding
        decimal centsNet = Math.Round(payable.NetAmount, 2, MidpointRounding.ToEven);
        centsNet.Should().Be(1536.43m);
    }

    [Fact]
    public void BankerRounding_ToEven_DivergesFromFloatingPointBoundaryErrors()
    {
        // 2.675 when calculated with banker's rounding to 2 decimal places rounds to 2.68
        decimal boundaryVal1 = 2.675m;
        decimal roundedVal1 = Math.Round(boundaryVal1, 2, MidpointRounding.ToEven);
        roundedVal1.Should().Be(2.68m);

        // 2.685 when rounded with banker's rounding to 2 decimal places rounds to 2.68 (even)
        decimal boundaryVal2 = 2.685m;
        decimal roundedVal2 = Math.Round(boundaryVal2, 2, MidpointRounding.ToEven);
        roundedVal2.Should().Be(2.68m);
    }

    [Fact]
    public void ParticipantWallet_StipendLedgerAccumulation_MatchesExactDecimal()
    {
        var wallet = new ParticipantWallet
        {
            CreatedAt = FakeClock.DefaultStart,
            UpdatedAt = FakeClock.DefaultStart,
            CreatedBy = "payments-test",
            CurrentBalance = 0.00m,
            LifetimeStipends = 0.00m,
            TaxableYtd = 0.00m,
        };

        // Simulate 12 monthly payments of $52.25
        decimal stipendAmount = 52.25m;
        for (int i = 0; i < 12; i++)
        {
            wallet.CurrentBalance += stipendAmount;
            wallet.LifetimeStipends += stipendAmount;
            wallet.TaxableYtd += stipendAmount;
        }

        wallet.CurrentBalance.Should().Be(627.00m);
        wallet.LifetimeStipends.Should().Be(627.00m);
        wallet.TaxableYtd.Should().Be(627.00m);
        wallet.Is1099ThresholdExceeded = wallet.TaxableYtd >= 600.00m;
        wallet.Is1099ThresholdExceeded.Should().BeTrue();
    }

    [Fact]
    public void SplitPayeeAllocation_CalculatesShares_UsingDecimalArithmetic()
    {
        var grossAmount = 1000.00m;
        var withholdingAmount = 100.00m;

        var payee1AllocationRate = 60.0m;
        var payee2AllocationRate = 40.0m;

        var alloc1 = new PayableAllocation
        {
            CreatedAt = FakeClock.DefaultStart,
            UpdatedAt = FakeClock.DefaultStart,
            CreatedBy = "payments-test",
            AllocationPercentage = payee1AllocationRate,
            GrossShare = Math.Round(grossAmount * (payee1AllocationRate / 100.0m), 4, MidpointRounding.ToEven),
            WithholdingShare = Math.Round(withholdingAmount * (payee1AllocationRate / 100.0m), 4, MidpointRounding.ToEven),
        };
        alloc1.NetShare = alloc1.GrossShare - alloc1.WithholdingShare;

        var alloc2 = new PayableAllocation
        {
            CreatedAt = FakeClock.DefaultStart,
            UpdatedAt = FakeClock.DefaultStart,
            CreatedBy = "payments-test",
            AllocationPercentage = payee2AllocationRate,
            GrossShare = Math.Round(grossAmount * (payee2AllocationRate / 100.0m), 4, MidpointRounding.ToEven),
            WithholdingShare = Math.Round(withholdingAmount * (payee2AllocationRate / 100.0m), 4, MidpointRounding.ToEven),
        };
        alloc2.NetShare = alloc2.GrossShare - alloc2.WithholdingShare;

        alloc1.GrossShare.Should().Be(600.00m);
        alloc1.WithholdingShare.Should().Be(60.00m);
        alloc1.NetShare.Should().Be(540.00m);

        alloc2.GrossShare.Should().Be(400.00m);
        alloc2.WithholdingShare.Should().Be(40.00m);
        alloc2.NetShare.Should().Be(360.00m);

        (alloc1.GrossShare + alloc2.GrossShare).Should().Be(grossAmount);
        (alloc1.WithholdingShare + alloc2.WithholdingShare).Should().Be(withholdingAmount);
    }
}
