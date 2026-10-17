import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { useAuthStore } from "../../src/stores/auth";
import ProtocolTrainingConsole from "../../src/components/clinical/ProtocolTrainingConsole.vue";
import { soaClient } from "../../src/api/soaClient";
import { trainingApi } from "../../src/api/training";
import { delegationBatchBindingId } from "../../src/utils/signatureBinding";

describe("ProtocolTrainingConsole.vue - Batch Assignment & Bulk Sign-Off Features", () => {
  const mockAssignment = {
    id: "asn-101",
    user_id: "site_investigator_01",
    user_name: "Dr. Eleanor Vance",
    user_role: "Principal Investigator",
    study_id: "CARDIO-2026",
    site_id: "SITE-101",
    status: "IN_PROGRESS",
    time_spent_seconds: 0,
    quiz_score: null,
    completed_at: null,
    curriculum: {
      id: "curriculum-v3-delta",
      title: "Protocol V3.0 Delta Amendment Curriculum",
      protocol_version: "3.0",
      study_id: "CARDIO-2026",
      modules: [
        {
          id: "mod-1",
          title: "Introduction",
          module_type: "PROCEDURE_DELTA",
          min_reading_time_seconds: 30,
          content_text: "Delta overview...",
          quiz_questions: [],
        },
      ],
    },
  };

  const mockDelegations = [
    {
      id: "del-01",
      study_id: "CARDIO-2026",
      site_id: "SITE-101",
      staff_user_id: "usr-01",
      staff_name: "Marcus Vance",
      role: "Sub-Investigator",
      task_code: "INFORMED_CONSENT",
      duties: "Informed Consent Administration",
      status: "PENDING",
      signed_off: false,
      pi_signature_hash: null,
      pi_approved_at: null,
      batch_transaction_id: null,
    },
    {
      id: "del-02",
      study_id: "CARDIO-2026",
      site_id: "SITE-101",
      staff_user_id: "usr-02",
      staff_name: "Sarah Jenkins",
      role: "Clinical Research Coordinator",
      task_code: "EDC_DATA_ENTRY",
      duties: "eCRF Data Entry",
      status: "PENDING",
      signed_off: false,
      pi_signature_hash: null,
      pi_approved_at: null,
      batch_transaction_id: null,
    },
  ];

  beforeEach(() => {
    setActivePinia(createPinia());
    useAuthStore().user = {
      sub: "site_investigator_01",
      username: "site_investigator_01",
    } as never;
    vi.useFakeTimers();

    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([
      mockAssignment,
    ]);
    vi.spyOn(trainingApi, "getAssignmentDetail").mockResolvedValue(
      mockAssignment
    );
    vi.spyOn(trainingApi, "recordProgress").mockResolvedValue({
      success: true,
    });
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue(mockDelegations);
  });

  afterEach(() => {
    vi.restoreAllMocks();
    vi.useRealTimers();
  });

  async function openBatchTab() {
    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();
    const batchTabBtn = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Batch Assignments"));
    expect(batchTabBtn).toBeDefined();
    await batchTabBtn!.trigger("click");
    return wrapper;
  }

  it("offers only the sites and roles on the study's delegation log, with nothing pre-selected (#5400)", async () => {
    const wrapper = await openBatchTab();

    expect(trainingApi.listDelegations).toHaveBeenCalledWith({
      study_id: "CARDIO-2026",
    });
    expect(wrapper.find(".batch-layout").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "Multi-Site & Role Batch Curriculum Assignment"
    );
    const options = wrapper
      .findAll(".batch-filter-grid .checkbox-label")
      .map((label) => label.text());
    expect(options).toEqual([
      "SITE-101",
      "Clinical Research Coordinator",
      "Sub-Investigator",
    ]);
    expect(
      wrapper
        .findAll(".batch-filter-grid input[type='checkbox']")
        .every((box) => !(box.element as HTMLInputElement).checked)
    ).toBe(true);
    expect(wrapper.text()).toContain("Target Scope: 0 personnel");
    expect(
      wrapper.find(".batch-preview-bar button").attributes("disabled")
    ).toBeDefined();
  });

  it("counts the people on the delegation log the selection reaches", async () => {
    const wrapper = await openBatchTab();

    await wrapper
      .find(".batch-filter-grid input[value='SITE-101']")
      .setValue(true);
    expect(wrapper.text()).toContain("Target Scope: 2 personnel");

    await wrapper
      .find(".batch-filter-grid input[value='Sub-Investigator']")
      .setValue(true);
    expect(wrapper.text()).toContain("Target Scope: 1 personnel");
  });

  it("shows an empty state and offers no audience when the delegation log is empty", async () => {
    vi.mocked(trainingApi.listDelegations).mockResolvedValue([]);
    const wrapper = await openBatchTab();

    expect(wrapper.find("[data-testid='batch-empty']").exists()).toBe(true);
    expect(wrapper.find(".batch-filter-grid").exists()).toBe(false);
    expect(
      wrapper.find(".batch-preview-bar button").attributes("disabled")
    ).toBeDefined();
  });

  it("shows the server's refusal when a batch is rejected", async () => {
    vi.spyOn(trainingApi, "batchAssignCurriculum").mockRejectedValue(
      new Error("Study 'CARDIO-2026' has no staff at: SITE-999.")
    );
    const wrapper = await openBatchTab();
    await wrapper
      .find(".batch-filter-grid input[value='SITE-101']")
      .setValue(true);
    await wrapper.find(".batch-preview-bar button").trigger("click");
    await wrapper.find("#batch-reason").setValue("Amendment 3 training");
    await wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Deploy Batch"))!
      .trigger("click");
    await vi.runOnlyPendingTimersAsync();

    expect(wrapper.find("[role='alert']").text()).toContain(
      "has no staff at: SITE-999"
    );
  });

  it("executes batch assignment and renders execution manifest with skipped duplicates", async () => {
    const mockBatchResponse = {
      batch_transaction_id: "tx-batch-999",
      curriculum_id: "curriculum-v3-delta",
      study_id: "CARDIO-2026",
      total_eligible: 6,
      total_assigned: 5,
      total_skipped: 1,
      assignments: [],
      skipped_items: [
        {
          user_id: "user_site-101_principal_investigator",
          site_id: "SITE-101",
          user_role: "Principal Investigator",
          reason:
            "Active training assignment already exists for this curriculum",
        },
      ],
      executed_at: "2026-10-07T10:00:00Z",
    };

    vi.spyOn(trainingApi, "batchAssignCurriculum").mockResolvedValue(
      mockBatchResponse
    );

    const wrapper = await openBatchTab();
    await wrapper
      .find(".batch-filter-grid input[value='SITE-101']")
      .setValue(true);

    // Open batch confirmation modal
    const deployBtn = wrapper.find(".batch-preview-bar button");
    await deployBtn.trigger("click");

    expect(wrapper.find(".modal-card").exists()).toBe(true);
    expect(wrapper.text()).toContain("Confirm Batch Curriculum Assignment");

    // The audit reason is required: there is no default.
    const confirmBtnBefore = wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Deploy Batch"));
    expect(confirmBtnBefore!.attributes("disabled")).toBeDefined();
    await wrapper.find("#batch-reason").setValue("Amendment 3 training");

    // Click confirm in modal
    const confirmBtn = wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Deploy Batch"));
    await confirmBtn!.trigger("click");

    expect(trainingApi.batchAssignCurriculum).toHaveBeenCalledWith({
      curriculum_id: "curriculum-v3-delta",
      study_id: "CARDIO-2026",
      site_ids: ["SITE-101"],
      roles: [],
      reason_for_change: "Amendment 3 training",
    });

    await wrapper.vm.$nextTick();
    expect(wrapper.text()).toContain("Tx ID: tx-batch-999");
    expect(wrapper.text()).toContain("Skipped Duplicates: 1");
    expect(wrapper.text()).toContain(
      "Active training assignment already exists"
    );
  });

  it("renders Delegation Bulk Sign-Off tab and executes Part 11 signature with digital seal", async () => {
    const mockBulkSignResponse = {
      batch_transaction_id: "tx-del-888",
      total_requested: 2,
      total_signed: 2,
      total_failed: 0,
      signed_delegations: [],
      failed_items: [],
      sha256_seal:
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      etmf_artifact_id: "05.02.04",
      executed_at: "2026-10-07T10:00:00Z",
    };

    vi.spyOn(trainingApi, "bulkSignOffDelegations").mockResolvedValue(
      mockBulkSignResponse
    );
    vi.spyOn(soaClient, "verifySignature").mockResolvedValue({
      sig_token: "step-up-token",
    });

    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    const delTabBtn = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Delegation Bulk Sign-Off"));
    await delTabBtn!.trigger("click");

    expect(trainingApi.listDelegations).toHaveBeenCalled();
    expect(wrapper.find(".delegations-layout").exists()).toBe(true);

    // Select all pending delegation records
    const selectAllCheckbox = wrapper.find("thead input[type='checkbox']");
    await selectAllCheckbox.setValue(true);

    expect(wrapper.text()).toContain("Selected for Sign-Off: 2 records");

    // Open Part 11 Bulk Signature modal
    const signBtn = wrapper.find(".delegation-actions-bar button");
    await signBtn.trigger("click");

    expect(wrapper.find(".modal-card").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "21 CFR Part 11 Bulk Delegation Log Sign-Off"
    );

    // Fill password in modal
    const passwordInput = wrapper.find("#bulk-password");
    await passwordInput.setValue("SecurePassword123!");

    // Confirm bulk signature
    const executeBtn = wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Execute Signature"));
    await executeBtn!.trigger("click");

    // The password only buys a step-up token bound to these ids; the signer is the signed-in user (ADR-2515).
    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        password: "SecurePassword123!", // pragma: allowlist secret
        action: "/api/v1/training/delegations/bulk-signoff",
        semanticAction: "knowledge.delegation.sign_off",
        batchId: await delegationBatchBindingId(["del-01", "del-02"]),
      })
    );
    expect(trainingApi.bulkSignOffDelegations).toHaveBeenCalledWith(
      {
        delegation_ids: ["del-01", "del-02"],
        study_id: "CARDIO-2026",
        site_id: "SITE-101",
        signature_payload: {
          meaning:
            "I confirm that I have reviewed and approved the delegated duties for the selected site staff personnel in accordance with 21 CFR Part 11 and ICH GCP E6(R2).",
          reason_for_change:
            "21 CFR Part 11 Principal Investigator bulk delegation sign-off",
        },
      },
      "step-up-token"
    );

    await wrapper.vm.$nextTick();
    expect(wrapper.text()).toContain(
      "Bulk Delegation Sign-Off Executed Successfully"
    );
    expect(wrapper.text()).toContain("eTMF Artifact: Zone 5 (05.02.04)");
    expect(wrapper.text()).toContain(
      "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"
    );
  });

  it("names no site when the selected delegations span several sites", async () => {
    vi.mocked(trainingApi.listDelegations).mockResolvedValue([
      mockDelegations[0],
      { ...mockDelegations[1], site_id: "SITE-102" },
    ]);
    vi.spyOn(trainingApi, "bulkSignOffDelegations").mockResolvedValue({
      batch_transaction_id: "tx-del-889",
      total_requested: 2,
      total_signed: 2,
      total_failed: 0,
      signed_delegations: [],
      failed_items: [],
      sha256_seal: "seal",
      etmf_artifact_id: "05.02.04",
      executed_at: "2026-10-07T10:00:00Z",
    });
    vi.spyOn(soaClient, "verifySignature").mockResolvedValue({
      sig_token: "step-up-token",
    });

    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();
    await wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Delegation Bulk Sign-Off"))!
      .trigger("click");
    await wrapper.find("thead input[type='checkbox']").setValue(true);
    await wrapper.find(".delegation-actions-bar button").trigger("click");
    await wrapper.find("#bulk-password").setValue("SecurePassword123!");
    await wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Execute Signature"))!
      .trigger("click");
    await vi.waitFor(() =>
      expect(trainingApi.bulkSignOffDelegations).toHaveBeenCalled()
    );

    expect(trainingApi.bulkSignOffDelegations).toHaveBeenCalledWith(
      expect.objectContaining({ study_id: "CARDIO-2026", site_id: null }),
      "step-up-token"
    );
  });
});
