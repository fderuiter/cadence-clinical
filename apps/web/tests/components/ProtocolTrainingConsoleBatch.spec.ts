import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
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

  it("renders Batch Assignment tab and calculates target scope count correctly", async () => {
    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    const batchTabBtn = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Batch Assignments"));
    expect(batchTabBtn).toBeDefined();
    await batchTabBtn!.trigger("click");

    expect(wrapper.find(".batch-layout").exists()).toBe(true);
    expect(wrapper.text()).toContain(
      "Multi-Site & Role Batch Curriculum Assignment"
    );
    expect(wrapper.text()).toContain("Target Scope: 6 personnel"); // 2 sites * 3 roles default selection
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

    const wrapper = mount(ProtocolTrainingConsole);
    await vi.runOnlyPendingTimersAsync();

    const batchTabBtn = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Batch Assignments"));
    await batchTabBtn!.trigger("click");

    // Open batch confirmation modal
    const deployBtn = wrapper.find(".batch-preview-bar button");
    await deployBtn.trigger("click");

    expect(wrapper.find(".modal-card").exists()).toBe(true);
    expect(wrapper.text()).toContain("Confirm Batch Curriculum Assignment");

    // Click confirm in modal
    const confirmBtn = wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Deploy Batch"));
    await confirmBtn!.trigger("click");

    expect(trainingApi.batchAssignCurriculum).toHaveBeenCalledWith({
      curriculum_id: "curriculum-v3-delta",
      study_id: "CARDIO-2026",
      site_ids: ["SITE-101", "SITE-102"],
      roles: [
        "Principal Investigator",
        "Sub-Investigator",
        "Clinical Research Coordinator",
      ],
      reason_for_change: "Protocol Amendment V3.0 Delta Deployment",
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
});
