import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { soaClient } from "../../src/api/soaClient";
import { trainingApi } from "../../src/api/training";
import ProtocolTrainingConsole from "../../src/components/clinical/ProtocolTrainingConsole.vue";
import { useAuthStore } from "../../src/stores/auth";
import { delegationBatchBindingId } from "../../src/utils/signatureBinding";
import { signInAs } from "../helpers/signIn.js";

describe("ProtocolTrainingConsole.vue - Batch Assignment & PI Delegation Sign-Off", () => {
  const mockCurriculum = {
    id: "curriculum-v3-delta",
    title: "Protocol V3.0 Delta Amendment Curriculum",
    protocol_version: "3.0",
    study_id: "CARDIO-2026",
    description: "Delta overview...",
    is_active: true,
    created_at: "2026-10-01T10:00:00Z",
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
    signInAs(useAuthStore(), ["Data Manager"], { username: "sponsor_dm_01" });

    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([mockCurriculum]);
    vi.spyOn(trainingApi, "getCurriculum").mockResolvedValue(mockCurriculum);
    vi.spyOn(trainingApi, "listDelegations").mockResolvedValue(mockDelegations);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function openBatchTab() {
    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "CARDIO-2026" },
    });
    await flushPromises();
    const batchTabBtn = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Batch Assignments"));
    expect(batchTabBtn).toBeDefined();
    await batchTabBtn!.trigger("click");
    await flushPromises();
    return wrapper;
  }

  it("offers only the sites and roles on the study's delegation log, with nothing pre-selected (#5400)", async () => {
    const wrapper = await openBatchTab();

    expect(trainingApi.listDelegations).toHaveBeenCalledWith({
      study_id: "CARDIO-2026",
    });
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

  it("shows the server's refusal when batchAssignCurriculum is rejected", async () => {
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
    await flushPromises();

    expect(wrapper.find("[role='alert']").text()).toContain(
      "has no staff at: SITE-999"
    );
  });

  it("executes batchAssignCurriculum and renders execution manifest with skipped duplicates", async () => {
    vi.spyOn(trainingApi, "batchAssignCurriculum").mockResolvedValue({
      batch_transaction_id: "tx-batch-999",
      curriculum_id: "curriculum-v3-delta",
      study_id: "CARDIO-2026",
      total_eligible: 2,
      total_assigned: 1,
      total_skipped: 1,
      assignments: [],
      skipped_items: [
        {
          user_id: "usr-01",
          site_id: "SITE-101",
          user_role: "Sub-Investigator",
          reason:
            "Active training assignment already exists for this curriculum",
        },
      ],
      executed_at: "2026-10-07T10:00:00Z",
    });

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
    await flushPromises();

    expect(trainingApi.batchAssignCurriculum).toHaveBeenCalledWith({
      curriculum_id: "curriculum-v3-delta",
      study_id: "CARDIO-2026",
      site_ids: ["SITE-101"],
      roles: [],
      reason_for_change: "Amendment 3 training",
    });
    expect(wrapper.text()).toContain("Tx ID: tx-batch-999");
    expect(wrapper.text()).toContain("Skipped Duplicates: 1");
  });

  it("renders Delegation Bulk Sign-Off tab and executes Part 11 signature via POST /api/v1/training/delegations/bulk-signoff", async () => {
    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi_vance",
    });
    vi.spyOn(soaClient, "verifySignature").mockResolvedValue({
      sig_token: "step-up-token",
    });
    vi.spyOn(trainingApi, "bulkSignOffDelegations").mockResolvedValue({
      batch_transaction_id: "tx-del-888",
      total_requested: 2,
      total_signed: 2,
      total_failed: 0,
      signed_delegations: [],
      failed_items: [],
      sha256_seal:
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
      etmf_artifact_id: "05.02.04",
      executed_at: "2026-10-07T10:00:00Z",
    });

    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "CARDIO-2026" },
    });
    await flushPromises();

    const delTabBtn = wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Delegation Bulk Sign-Off"));
    await delTabBtn!.trigger("click");
    await flushPromises();

    await wrapper.find("thead input[type='checkbox']").setValue(true);
    await wrapper.find(".delegation-actions-bar button").trigger("click");
    await wrapper.find("#bulk-password").setValue("SecurePassword123!");
    await wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Execute Signature"))!
      .trigger("click");
    await flushPromises();

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
    expect(wrapper.text()).toContain(
      "Bulk Delegation Sign-Off Executed Successfully"
    );
    expect(wrapper.text()).toContain("eTMF Artifact: Zone 5 (05.02.04)");
    const delegationEntityChip = wrapper.find(
      ".bulk-sign-result .clinical-entity-link"
    );
    expect(delegationEntityChip.exists()).toBe(true);
    await delegationEntityChip.trigger("click");
    await flushPromises();
    expect(wrapper.find(".entity-peek-body").exists()).toBe(true);
    expect(wrapper.find(".entity-peek-body").text()).toContain(
      "Delegation of Authority Log"
    );
  });

  it("names no site when the selected delegations span several sites", async () => {
    signInAs(useAuthStore(), ["Principal Investigator"], {
      username: "pi_vance",
    });
    vi.mocked(trainingApi.listDelegations).mockResolvedValue([
      mockDelegations[0],
      { ...mockDelegations[1], site_id: "SITE-102" },
    ]);
    vi.spyOn(soaClient, "verifySignature").mockResolvedValue({
      sig_token: "step-up-token",
    });
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

    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "CARDIO-2026" },
    });
    await flushPromises();
    await wrapper
      .findAll(".tab-btn")
      .find((btn) => btn.text().includes("Delegation Bulk Sign-Off"))!
      .trigger("click");
    await flushPromises();
    await wrapper.find("thead input[type='checkbox']").setValue(true);
    await wrapper.find(".delegation-actions-bar button").trigger("click");
    await wrapper.find("#bulk-password").setValue("SecurePassword123!");
    await wrapper
      .findAll(".modal-footer button")
      .find((btn) => btn.text().includes("Confirm & Execute Signature"))!
      .trigger("click");

    await vi.waitFor(() => {
      expect(trainingApi.bulkSignOffDelegations).toHaveBeenCalledWith(
        expect.objectContaining({ study_id: "CARDIO-2026", site_id: null }),
        "step-up-token"
      );
    });
  });
});
