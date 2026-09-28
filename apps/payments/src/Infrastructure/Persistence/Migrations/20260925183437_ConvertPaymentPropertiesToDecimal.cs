using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Cadence.Apps.Payments.Infrastructure.Persistence.Migrations;

/// <inheritdoc />
public partial class ConvertPaymentPropertiesToDecimal : Migration
{
    /// <inheritdoc />
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<decimal>(
            name: "AllocationPercentage",
            table: "payments_split_payees",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalTravelReimbursement",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalPatientStipend",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalOverheadAmount",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalNetAmount",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalHoldbackAmount",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalGrossAmount",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalBaseAmount",
            table: "payments_self_billing_invoices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "OverheadAmount",
            table: "payments_remittance_advices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "NetAmount",
            table: "payments_remittance_advices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "HoldbackAmount",
            table: "payments_remittance_advices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "GrossAmount",
            table: "payments_remittance_advices",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingRate",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TravelReimbursementAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "PatientStipendAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "OverheadAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "NetAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "GrossAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseAmount",
            table: "payments_payables",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingShare",
            table: "payments_payable_allocations",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "NetShare",
            table: "payments_payable_allocations",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "GrossShare",
            table: "payments_payable_allocations",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "AllocationPercentage",
            table: "payments_payable_allocations",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TaxableYtd",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "LifetimeStipends",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "LifetimeReimbursements",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "LifetimeDisbursements",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "CurrentBalance",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "AntiCoercionCap",
            table: "payments_participant_wallets",
            type: "numeric(18,4)",
            nullable: true,
            oldClrType: typeof(double),
            oldType: "double precision",
            oldNullable: true);

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingRate",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingAmount",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TravelReimbursement",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "PatientStipend",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "OverheadAmount",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "NetAmount",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "GrossAmount",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseAmount",
            table: "payments_invoice_line_items",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "HoldbackAmount",
            table: "payments_holdback_reserves",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingPercentage",
            table: "payments_fee_schedules",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TravelReimbursement",
            table: "payments_fee_schedules",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "PatientStipend",
            table: "payments_fee_schedules",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "OverheadPercentage",
            table: "payments_fee_schedules",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "BaseAmount",
            table: "payments_fee_schedules",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "WithholdingPercentage",
            table: "payments_budget_contracts",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "TotalBudget",
            table: "payments_budget_contracts",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");

        migrationBuilder.AlterColumn<decimal>(
            name: "OverheadPercentage",
            table: "payments_budget_contracts",
            type: "numeric(18,4)",
            nullable: false,
            oldClrType: typeof(double),
            oldType: "double precision");
    }

    /// <inheritdoc />
    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AlterColumn<double>(
            name: "AllocationPercentage",
            table: "payments_split_payees",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalTravelReimbursement",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalPatientStipend",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalOverheadAmount",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalNetAmount",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalHoldbackAmount",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalGrossAmount",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalBaseAmount",
            table: "payments_self_billing_invoices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "OverheadAmount",
            table: "payments_remittance_advices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "NetAmount",
            table: "payments_remittance_advices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "HoldbackAmount",
            table: "payments_remittance_advices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "GrossAmount",
            table: "payments_remittance_advices",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingRate",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TravelReimbursementAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "PatientStipendAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "OverheadAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "NetAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "GrossAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "BaseAmount",
            table: "payments_payables",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingShare",
            table: "payments_payable_allocations",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "NetShare",
            table: "payments_payable_allocations",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "GrossShare",
            table: "payments_payable_allocations",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "AllocationPercentage",
            table: "payments_payable_allocations",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TaxableYtd",
            table: "payments_participant_wallets",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "LifetimeStipends",
            table: "payments_participant_wallets",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "LifetimeReimbursements",
            table: "payments_participant_wallets",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "LifetimeDisbursements",
            table: "payments_participant_wallets",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "CurrentBalance",
            table: "payments_participant_wallets",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "AntiCoercionCap",
            table: "payments_participant_wallets",
            type: "double precision",
            nullable: true,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)",
            oldNullable: true);

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingRate",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingAmount",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TravelReimbursement",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "PatientStipend",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "OverheadAmount",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "NetAmount",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "GrossAmount",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "BaseAmount",
            table: "payments_invoice_line_items",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "HoldbackAmount",
            table: "payments_holdback_reserves",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingPercentage",
            table: "payments_fee_schedules",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TravelReimbursement",
            table: "payments_fee_schedules",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "PatientStipend",
            table: "payments_fee_schedules",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "OverheadPercentage",
            table: "payments_fee_schedules",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "BaseAmount",
            table: "payments_fee_schedules",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "WithholdingPercentage",
            table: "payments_budget_contracts",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "TotalBudget",
            table: "payments_budget_contracts",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");

        migrationBuilder.AlterColumn<double>(
            name: "OverheadPercentage",
            table: "payments_budget_contracts",
            type: "double precision",
            nullable: false,
            oldClrType: typeof(decimal),
            oldType: "numeric(18,4)");
    }
}

