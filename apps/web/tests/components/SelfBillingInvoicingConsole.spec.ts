import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SelfBillingInvoicingConsole from "../../src/components/payments/SelfBillingInvoicingConsole.vue";

describe("SelfBillingInvoicingConsole.vue - Batch Milestone Invoicing Features", () => {
  const mockInvoices = [
    {
      id: "INV-101",
      invoice_number: "SBI-202610-0001",
      study_id: "STUDY-ONC-201",
      site_id: "SITE-101",
      contract_id: "CONTRACT-001",
      billing_cycle: "MONTHLY",
      period_start: "2026-08-01",
      period_end: "2026-08-31",
      currency: "USD",
      total_base_amount: 3000,
      total_overhead_amount: 750,
      total_patient_stipend: 0,
      total_travel_reimbursement: 0,
      total_gross_amount: 3750,
      total_holdback_amount: 375,
      total_net_amount: 3375,
      payable_count: 2,
      status: "APPROVED",
      approved_by: "system_batch",
      approved_at: "2026-10-12T12:00:00Z",
      erp_system: "SAP",
      erp_reference_id: null,
      erp_sync_status: "PENDING",
      erp_synced_at: null,
      reconciled_by: null,
      reconciled_at: null,
      reconciliation_reference: null,
      reconciliation_notes: null,
      created_at: "2026-10-12T12:00:00Z",
      created_by: "batch_process",
      reason_for_change: "Batch compile run BATCH-EXEC-20261012-0001",
      version_index: 1,
      line_items: [
        {
          id: "LINE-1",
          invoice_id: "INV-101",
          payable_id: "PAY-101-01",
          subject_id: "SUBJ-101",
          visit_code: "V1_SCREENING",
          procedure_code: "PROC-01",
          procedure_name: "Screening Panel",
          base_amount: 1000,
          overhead_amount: 250,
          patient_stipend: 0,
          travel_reimbursement: 0,
          gross_amount: 1250,
          withholding_rate: 10,
          withholding_amount: 125,
          net_amount: 1125,
          currency: "USD",
          created_at: "2026-10-12T12:00:00Z",
          version_index: 1,
        },
      ],
      remittance_advice: {
        id: "REM-101",
        remittance_number: "REM-INV-101",
        invoice_id: "INV-101",
        study_id: "STUDY-ONC-201",
        site_id: "SITE-101",
        currency: "USD",
        gross_amount: 3750,
        overhead_amount: 750,
        holdback_amount: 375,
        net_amount: 3375,
        pdf_sha256_hash: "abcdef1234567890",
        is_sealed: true,
        is_signed: false,
        signed_by: null,
        signed_at: null,
        signer_role: null,
        dispatch_status: "PENDING",
        created_at: "2026-10-12T12:00:00Z",
        version_index: 1,
      },
    },
  ];

  const mockBatchResponse = {
    batch_execution_id: "BATCH-EXEC-20261012-0001",
    study_id: "STUDY-ONC-201",
    billing_cycle: "MONTHLY",
    period_start: "2026-08-01",
    period_end: "2026-08-31",
    compiled_at: "2026-10-12T12:00:00Z",
    compiled_by: "system_batch",
    total_sites_processed: 2,
    successful_sites_count: 2,
    failed_sites_count: 0,
    total_invoices_generated: 2,
    total_payables_compiled: 3,
    total_gross_amount: 5550,
    total_holdback_amount: 555,
    total_net_amount: 4995,
    site_results: [
      {
        site_id: "SITE-101",
        invoice_id: "INV-101",
        invoice_number: "SBI-202610-0001",
        success: true,
        error_message: null,
        payable_count: 2,
        gross_amount: 3750,
        holdback_amount: 375,
        net_amount: 3375,
      },
      {
        site_id: "SITE-102",
        invoice_id: "INV-102",
        invoice_number: "SBI-202610-0002",
        success: true,
        error_message: null,
        payable_count: 1,
        gross_amount: 1800,
        holdback_amount: 180,
        net_amount: 1620,
      },
    ],
  };

  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubGlobal(
      "fetch",
      vi.fn().mockImplementation((url, options) => {
        if (typeof url === "string" && url.includes("/api/v1/payments/invoices/batch-compile")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(mockBatchResponse),
          });
        }
        if (typeof url === "string" && url.includes("/api/v1/payments/invoices")) {
          return Promise.resolve({
            ok: true,
            json: () => Promise.resolve(mockInvoices),
          });
        }
        return Promise.resolve({
          ok: true,
          json: () => Promise.resolve({}),
        });
      })
    );
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("renders header actions including Study Batch Compilation button and filter input for Batch Execution ID", async () => {
    const wrapper = mount(SelfBillingInvoicingConsole);
    await wrapper.vm.$nextTick();

    const batchBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Study Batch Compilation"));
    expect(batchBtn).toBeDefined();

    const batchFilterInput = wrapper.find("input#filter-batch");
    expect(batchFilterInput.exists()).toBe(true);
  });

  it("opens Study Batch Compilation modal on button click and submits batch compile request", async () => {
    const wrapper = mount(SelfBillingInvoicingConsole);
    await wrapper.vm.$nextTick();

    const batchBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Study Batch Compilation"));
    await batchBtn!.trigger("click");

    expect(wrapper.find("input#batch-study").exists()).toBe(true);

    // Set study ID
    await wrapper.find("input#batch-study").setValue("STUDY-ONC-201");

    const executeBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Execute Study Batch"));
    expect(executeBtn).toBeDefined();

    await executeBtn!.trigger("click");
    await wrapper.vm.$nextTick();

    expect(fetch).toHaveBeenCalledWith(
      "/api/v1/payments/invoices/batch-compile",
      expect.objectContaining({
        method: "POST",
        body: expect.stringContaining("STUDY-ONC-201"),
      })
    );

    // Verify batch summary modal appears
    await wrapper.vm.$nextTick();
    expect(wrapper.text()).toContain("Batch Execution Summary: BATCH-EXEC-20261012-0001");
    expect(wrapper.text()).toContain("2 / 2 Passed");
  });
});
