import { flushPromises, mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { createMemoryHistory, createRouter } from "vue-router";
import * as signatureVerification from "../../src/api/signatureVerification";
import { trainingApi } from "../../src/api/training";
import ProtocolTrainingConsole from "../../src/components/clinical/ProtocolTrainingConsole.vue";
import { useAuthStore } from "../../src/stores/auth";
import { signInAs } from "../helpers/signIn.js";

describe("ProtocolTrainingConsole.vue - Kernel Site-Grant Batch Assignment & Part 11 Qualification Filing", () => {
  const mockCurriculum = {
    id: "curriculum-v3-delta",
    title: "Protocol V3.0 Delta Amendment Curriculum",
    protocol_version: "3.0",
    protocol_version_number: 3,
    qualification_code: "protocol_training",
    passing_score: 0.8,
    study_id: "CARDIO-2026",
    description: "Delta overview...",
    is_active: true,
    created_at: "2026-10-01T10:00:00Z",
    modules: [
      {
        id: "mod-1",
        title: "Section 4.1 Eligibility Updates",
        section_ref: "Section 4.1",
        summary: "Updated baseline laboratory thresholds.",
        key_changes: ["Verify eGFR >= 45 mL/min prior to enrollment."],
        estimated_minutes: 10,
      },
    ],
    quiz_questions: [
      {
        id: "q-1",
        prompt: "What is the minimum baseline eGFR threshold?",
        options: [
          { id: "opt-a", label: "45 mL/min" },
          { id: "opt-b", label: "15 mL/min" },
        ],
        correct_option_id: "opt-a",
      },
    ],
  };

  const mockStudyStaff = [
    {
      user_id: "usr-01",
      user_name: "Marcus Vance",
      site_id: "SITE-101",
      role: "Sub-Investigator",
      grant_id: "grant-01",
    },
    {
      user_id: "usr-02",
      user_name: "Sarah Jenkins",
      site_id: "SITE-101",
      role: "Clinical Research Coordinator",
      grant_id: "grant-02",
    },
  ];

  function createTestRouter() {
    return createRouter({
      history: createMemoryHistory(),
      routes: [
        { path: "/studies/:studyCode?", name: "study", component: { template: "<div />" } },
      ],
    });
  }

  beforeEach(() => {
    setActivePinia(createPinia());
    signInAs(useAuthStore(), ["Data Manager"], { username: "sponsor_dm_01" });

    vi.spyOn(trainingApi, "listAssignments").mockResolvedValue([]);
    vi.spyOn(trainingApi, "listCurricula").mockResolvedValue([mockCurriculum]);
    vi.spyOn(trainingApi, "getCurriculum").mockResolvedValue(mockCurriculum);
    vi.spyOn(trainingApi, "listStudyStaff").mockResolvedValue(mockStudyStaff);
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  async function openBatchTab() {
    const router = createTestRouter();
    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "CARDIO-2026" },
      global: { plugins: [router] },
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

  it("offers only the sites and roles resolved from canonical Kernel study-staff grants, with nothing pre-selected (#5400)", async () => {
    const wrapper = await openBatchTab();

    expect(trainingApi.listStudyStaff).toHaveBeenCalledWith({
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

  it("counts the personnel holding active Kernel site grants reached by the selection", async () => {
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

  it("shows an empty state and offers no audience when no Kernel site grants exist", async () => {
    vi.mocked(trainingApi.listStudyStaff).mockResolvedValue([]);
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

  it("authors a protocol curriculum via POST /api/v1/training/curricula", async () => {
    /**
     * @req: PRD-TRN-001
     */
    const createdCurriculum = {
      ...mockCurriculum,
      id: "curriculum-v4",
      title: "Protocol V4.0 Core Qualification",
      protocol_version: "4.0",
      protocol_version_number: 4,
    };
    vi.spyOn(trainingApi, "createCurriculum").mockResolvedValue(
      createdCurriculum
    );

    const router = createTestRouter();
    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "CARDIO-2026" },
      global: { plugins: [router] },
    });
    await flushPromises();

    await wrapper.find("#btn-open-author-curriculum").trigger("click");
    await flushPromises();

    await wrapper
      .find("#curriculum-author-title")
      .setValue("Protocol V4.0 Core Qualification");
    await wrapper.find("#curriculum-author-version").setValue("4.0");
    await wrapper
      .find("#curriculum-author-mod-summary")
      .setValue("Updated eligibility and endpoint rules.");
    await wrapper.find("#btn-submit-curriculum").trigger("click");
    await flushPromises();

    expect(trainingApi.createCurriculum).toHaveBeenCalledWith(
      expect.objectContaining({
        study_id: "CARDIO-2026",
        title: "Protocol V4.0 Core Qualification",
        protocol_version: "4.0",
        protocol_version_number: 4,
        qualification_code: "protocol_training",
      })
    );
  });

  it("scores quiz answers and executes Part 11 signature to file TMF 05.02.04 certificate and Kernel Qualification", async () => {
    /**
     * @req: PRD-TRN-001
     * @req: PRD-TRN-004
     */
    signInAs(useAuthStore(), ["CRC"], { username: "crc_jenkins" });
    const pendingAssignment = {
      id: "asn-pending-01",
      curriculum_id: "curriculum-v3-delta",
      study_id: "CARDIO-2026",
      site_id: "SITE-101",
      user_id: "crc_jenkins",
      user_name: "Sarah Jenkins",
      user_role: "CRC",
      status: "PENDING",
      batch_transaction_id: null,
      quiz_score: null,
      time_spent_seconds: 0,
      completed_at: null,
      etmf_document_id: null,
      kernel_artifact_version_ref: null,
      kernel_qualification_ref: null,
      created_at: "2026-10-07T09:00:00Z",
    };
    vi.mocked(trainingApi.listAssignments).mockResolvedValue([
      pendingAssignment,
    ] as never);
    vi.spyOn(trainingApi, "getAssignmentDetail").mockResolvedValue({
      ...pendingAssignment,
      curriculum: mockCurriculum,
    } as never);
    vi.spyOn(signatureVerification, "requestStepUpSignature").mockResolvedValue(
      {
        sig_token: "step-up-trn-token",
      }
    );
    vi.spyOn(trainingApi, "completeAssignment").mockResolvedValue({
      assignment: {
        ...pendingAssignment,
        status: "COMPLETED",
        quiz_score: 1,
        completed_at: "2026-10-07T10:15:00Z",
        etmf_document_id: "00000000-0000-0000-0000-000000009401", // deid-ignore
        kernel_artifact_version_ref:
          "artifact_version:00000000-0000-0000-0000-000000009501", // deid-ignore
        kernel_qualification_ref:
          "qualification:00000000-0000-0000-0000-000000009601", // deid-ignore
      },
      quiz_score: 1,
      passing_score: 0.8,
      sha256_seal: "sha256-seal-050204",
      etmf_artifact_id: "05.02.04",
      etmf_document_id: "00000000-0000-0000-0000-000000009401", // deid-ignore
      kernel_artifact_version_ref:
        "artifact_version:00000000-0000-0000-0000-000000009501", // deid-ignore
      kernel_qualification_ref:
        "qualification:00000000-0000-0000-0000-000000009601", // deid-ignore
      executed_at: "2026-10-07T10:15:00Z",
    });

    const router = createTestRouter();
    const wrapper = mount(ProtocolTrainingConsole, {
      props: { studyId: "CARDIO-2026" },
      global: { plugins: [router] },
    });
    await flushPromises();

    // Retired duplicate Knowledge DoA sign-off tab is absent
    expect(
      wrapper
        .findAll(".tab-btn")
        .some((btn) => btn.text().includes("Delegation"))
    ).toBe(false);

    // Answer comprehension quiz question
    const quizRadio = wrapper.find(
      "[data-testid='training-quiz-card'] input[type='radio'][value='opt-a']"
    );
    expect(quizRadio.exists()).toBe(true);
    await quizRadio.setValue(true);
    await flushPromises();

    // Enter step-up credential and submit Part 11 attestation
    await wrapper
      .find("#training-attestation-credential")
      .setValue("SecurePassword123!"); // pragma: allowlist secret
    expect(
      wrapper
        .find("#btn-complete-training-assignment")
        .attributes("disabled")
    ).toBeUndefined();
    await wrapper.find("#training-attestation-form").trigger("submit");
    await flushPromises();

    expect(signatureVerification.requestStepUpSignature).toHaveBeenCalledWith(
      expect.objectContaining({
        password: "SecurePassword123!", // pragma: allowlist secret
        action: "/api/v1/training/assignments/asn-pending-01/complete",
        semanticAction: "training.assignment.complete",
      })
    );
    expect(trainingApi.completeAssignment).toHaveBeenCalledWith(
      "asn-pending-01",
      expect.objectContaining({
        quiz_answers: [{ question_id: "q-1", selected_option_id: "opt-a" }],
        signature_payload: expect.objectContaining({
          meaning: "I have read, understood, and completed protocol training",
        }),
      }),
      "step-up-trn-token"
    );

    const manifest = wrapper.find("[data-testid='training-completion-manifest']");
    expect(manifest.exists()).toBe(true);
    expect(manifest.text()).toContain("05.02.04");
    expect(manifest.text()).toContain("Document version");
    expect(manifest.text()).toContain("Qualification");
    const chipRefs = manifest
      .findAllComponents({ name: "NodeRefChip" })
      .map((chip: { props: (key: string) => unknown }) => chip.props("nodeRef"));
    expect(chipRefs).toEqual(
      expect.arrayContaining([
        "artifact_version:00000000-0000-0000-0000-000000009501", // deid-ignore
        "qualification:00000000-0000-0000-0000-000000009601", // deid-ignore
      ])
    );
  });
});
