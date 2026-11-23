import { describe, it, expect, vi, beforeEach } from "vitest";
import { flushPromises, mount } from "@vue/test-utils";
import EhrReconciliationDrawer from "../../src/components/clinical/EhrReconciliationDrawer.vue";
import { interopService } from "../../src/api/interop";
import { soaClient } from "../../src/api/soaClient";
import { computeBatchBindingId } from "../../src/utils/signatureBinding";

vi.mock("../../src/api/interop", () => ({
  interopService: {
    listEhrCandidates: vi.fn(),
    acceptEhrCandidate: vi.fn(),
    batchAcceptEhrCandidates: vi.fn(),
    rejectEhrCandidate: vi.fn(),
    batchRejectEhrCandidates: vi.fn(),
    raiseEhrClinicalQuery: vi.fn(),
  },
}));

vi.mock("../../src/api/soaClient", () => ({
  soaClient: {
    verifySignature: vi.fn(),
  },
}));

describe("EhrReconciliationDrawer.vue", () => {
  const getMockCandidates = () => [
    {
      id: "cand-1",
      study_id: "STUDY-101",
      site_id: "SITE-01",
      subject_id: "PAT-001",
      subject_pseudonym: "PAT-001",
      ehr_system_id: "EPIC-HEALTH",
      fhir_resource_type: "Observation",
      fhir_resource_id: "obs-sysbp-1",
      source_timestamp: "2026-10-01T10:00:00Z",
      target_domain: "VS",
      target_variable: "SYSBP",
      cdash_testcd: "SYSBP",
      cdash_test: "Systolic Blood Pressure",
      candidate_value: 128,
      candidate_unit: "mmHg",
      current_ecrf_value: 140,
      current_ecrf_updated_at: "2026-09-30T10:00:00Z",
      discrepancy_type: "VALUE_MISMATCH",
      confidence_score: 0.98,
      mapping_provenance: "LOINC 8480-6 -> VS.SYSBP",
      raw_fhir_resource: {
        resourceType: "Observation",
        valueQuantity: { value: 128, unit: "mmHg" },
      },
      status: "PENDING_REVIEW",
      version_index: 1,
      created_at: "2026-10-01T10:00:00Z",
    },
    {
      id: "cand-2",
      study_id: "STUDY-101",
      site_id: "SITE-01",
      subject_id: "PAT-001",
      subject_pseudonym: "PAT-001",
      ehr_system_id: "EPIC-HEALTH",
      fhir_resource_type: "Observation",
      fhir_resource_id: "obs-gluc-2",
      source_timestamp: "2026-10-01T10:00:00Z",
      target_domain: "LB",
      target_variable: "GLUC",
      cdash_testcd: "GLUC",
      cdash_test: "Glucose",
      candidate_value: 105,
      candidate_unit: "mg/dL",
      current_ecrf_value: null,
      current_ecrf_updated_at: null,
      discrepancy_type: "NEW_FIELD",
      confidence_score: 0.95,
      mapping_provenance: "LOINC 2339-0 -> LB.GLUC",
      raw_fhir_resource: {
        resourceType: "Observation",
        valueQuantity: { value: 105, unit: "mg/dL" },
      },
      status: "PENDING_REVIEW",
      version_index: 1,
      created_at: "2026-10-01T10:00:00Z",
    },
    {
      id: "cand-3",
      study_id: "STUDY-101",
      site_id: "SITE-01",
      subject_id: "PAT-001",
      subject_pseudonym: "PAT-001",
      ehr_system_id: "EPIC-HEALTH",
      fhir_resource_type: "Observation",
      fhir_resource_id: "obs-temp-3",
      source_timestamp: "2026-10-01T10:00:00Z",
      target_domain: "VS",
      target_variable: "TEMP",
      cdash_testcd: "TEMP",
      cdash_test: "Body Temperature",
      candidate_value: 37.0,
      candidate_unit: "C",
      current_ecrf_value: 98.6,
      current_ecrf_updated_at: "2026-09-30T10:00:00Z",
      discrepancy_type: "UNIT_MISMATCH",
      confidence_score: 0.92,
      mapping_provenance: "LOINC 8310-5 -> VS.TEMP",
      raw_fhir_resource: {
        resourceType: "Observation",
        valueQuantity: { value: 37.0, unit: "C" },
      },
      status: "PENDING_REVIEW",
      version_index: 1,
      created_at: "2026-10-01T10:00:00Z",
    },
  ];

  beforeEach(() => {
    vi.clearAllMocks();
    vi.mocked(interopService.listEhrCandidates).mockResolvedValue({
      candidates: getMockCandidates() as any,
      total_count: 3,
    });
    vi.mocked(soaClient.verifySignature).mockResolvedValue({
      sig_token: "sig-esource-001",
    } as any);
  });

  const mountDrawer = () =>
    mount(EhrReconciliationDrawer, {
      props: {
        isOpen: true,
        studyId: "STUDY-101",
        siteId: "SITE-01",
        subjectId: "PAT-001",
      },
      global: {
        stubs: {
          Teleport: true,
        },
      },
    });

  it("renders side-by-side reconciliation drawer header, badges, and metrics", async () => {
    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(wrapper.text()).toContain("eSource EHR Reconciliation");
    expect(wrapper.text()).toContain("SMART on FHIR");
    expect(wrapper.text()).toContain("21 CFR Part 11");
    expect(wrapper.text()).toContain("PAT-001");
  });

  it("displays candidate records side-by-side with discrepancy labels", async () => {
    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(wrapper.text()).toContain("SYSBP");
    expect(wrapper.text()).toContain("GLUC");
    expect(wrapper.text()).toContain("TEMP");
    expect(wrapper.text()).toContain("⚠️ Value Conflict");
    expect(wrapper.text()).toContain("✨ New Field");
    expect(wrapper.text()).toContain("🔄 Unit Mismatch");
  });

  it("dispatches acceptEhrCandidate API call when Accept & Commit is clicked", async () => {
    const cand = getMockCandidates()[0];
    vi.mocked(interopService.acceptEhrCandidate).mockResolvedValue({
      success: true,
      candidate: {
        ...cand,
        status: "ACCEPTED",
        resolution_action: "ACCEPT_COMMIT",
      } as any,
      message: "Accepted candidate",
    });

    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    const acceptButtons = wrapper.findAll(".btn-accept");
    expect(acceptButtons.length).toBeGreaterThan(0);

    await acceptButtons[0].trigger("click");
    await wrapper.vm.$nextTick();

    const confirmBtn = wrapper.find("#btn-confirm-accept");
    expect((confirmBtn.element as HTMLButtonElement).disabled).toBe(true);

    await wrapper.find("#accept-sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.vm.$nextTick();
    expect((confirmBtn.element as HTMLButtonElement).disabled).toBe(false);

    await confirmBtn.trigger("click");
    await flushPromises();

    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: "/api/v1/interop/esource/reconcile/accept",
        semanticAction: "interop.esource.accept",
      })
    );
    expect(interopService.acceptEhrCandidate).toHaveBeenCalledWith(
      {
        candidate_id: "cand-1",
        commit_reason: "CRC accepted candidate data from hospital EHR",
        change_reason: "CRC eSource Reconciliation Accept & Commit",
      },
      {
        sigToken: "sig-esource-001",
        changeReason: "CRC eSource Reconciliation Accept & Commit",
      }
    );
  });

  it("opens reject modal and enforces justification input before submitting rejection", async () => {
    const cand = getMockCandidates()[0];
    vi.mocked(interopService.rejectEhrCandidate).mockResolvedValue({
      success: true,
      candidate: {
        ...cand,
        status: "REJECTED",
        resolution_action: "REJECT",
      } as any,
      message: "Rejected candidate",
    });

    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    const rejectButtons = wrapper.findAll(".btn-reject");
    await rejectButtons[0].trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Reject EHR Candidate Data");

    const submitBtn = wrapper.find("#btn-confirm-reject");
    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(true);

    const textarea = wrapper.find("#reject-reason");
    await textarea.setValue("Lab sample taken out of protocol visit window");
    await wrapper.vm.$nextTick();
    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(true);

    await wrapper.find("#reject-sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.vm.$nextTick();

    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(false);
    await submitBtn.trigger("click");
    await flushPromises();

    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: "/api/v1/interop/esource/reconcile/reject",
        semanticAction: "interop.esource.reject",
      })
    );
    expect(interopService.rejectEhrCandidate).toHaveBeenCalledWith(
      {
        candidate_id: "cand-1",
        rejection_reason: "Lab sample taken out of protocol visit window",
        change_reason: "CRC eSource Reconciliation Reject",
      },
      {
        sigToken: "sig-esource-001",
        changeReason: "CRC eSource Reconciliation Reject",
      }
    );
  });

  it("opens query modal and submits clinical discrepancy query", async () => {
    const cand = getMockCandidates()[0];
    vi.mocked(interopService.raiseEhrClinicalQuery).mockResolvedValue({
      success: true,
      candidate: {
        ...cand,
        status: "QUERIED",
        resolution_action: "QUERY",
      } as any,
      clinical_query_id: "query-999",
      message: "Opened query",
    });

    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    const queryButtons = wrapper.findAll(".btn-query");
    await queryButtons[0].trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Raise GxP Discrepancy Query");

    const textarea = wrapper.find("#query-explanation");
    await textarea.setValue(
      "Investigator review needed for blood pressure discrepancy"
    );
    await wrapper.vm.$nextTick();

    const submitBtn = wrapper.find(".modal-card .btn-primary");
    await submitBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(interopService.raiseEhrClinicalQuery).toHaveBeenCalledWith({
      candidate_id: "cand-1",
      query_explanation:
        "Investigator review needed for blood pressure discrepancy",
      discrepancy_type: "VALUE_MISMATCH",
      change_reason: "CRC Raised eSource Discrepancy Query",
    });
  });

  it("opens provenance modal and displays raw FHIR JSON payload and CDASH rule", async () => {
    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    const provButtons = wrapper.findAll(".btn-link-provenance");
    expect(provButtons.length).toBeGreaterThan(0);

    await provButtons[0].trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("FHIR Resource Provenance & Audit Trail");
    expect(wrapper.text()).toContain("LOINC 8480-6 -> VS.SYSBP");
    expect(wrapper.text()).toContain("obs-sysbp-1");
  });

  it("selects high confidence items and triggers batchAcceptEhrCandidates", async () => {
    const mockCandidates = getMockCandidates();
    vi.mocked(interopService.batchAcceptEhrCandidates).mockResolvedValue({
      success: true,
      action_count: 3,
      candidates: mockCandidates.map((c) => ({
        ...c,
        status: "ACCEPTED",
        resolution_action: "ACCEPT_COMMIT",
      })) as any,
      message: "Batch accepted candidates",
    });

    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    const highConfBtn = wrapper.find(".btn-select-high-confidence");
    expect(highConfBtn.exists()).toBe(true);

    await highConfBtn.trigger("click");
    await wrapper.vm.$nextTick();

    const batchBar = wrapper.find(".floating-batch-bar");
    expect(batchBar.exists()).toBe(true);
    expect(batchBar.text()).toContain("3 item(s) selected");

    const batchAcceptBtn = wrapper.find(".btn-batch-accept");
    await batchAcceptBtn.trigger("click");
    await wrapper.vm.$nextTick();

    await wrapper.find("#batch-accept-sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.vm.$nextTick();
    await wrapper.find("#btn-confirm-batch-accept").trigger("click");
    await flushPromises();

    const expectedBatchId = await computeBatchBindingId([
      "cand-1",
      "cand-2",
      "cand-3",
    ]);
    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: "/api/v1/interop/esource/reconcile/batch-accept",
        semanticAction: "interop.esource.batch_accept",
        batchId: expectedBatchId,
      })
    );
    expect(interopService.batchAcceptEhrCandidates).toHaveBeenCalledWith(
      {
        candidate_ids: ["cand-1", "cand-2", "cand-3"],
        commit_reason: "CRC batch accepted high-confidence candidate data",
        change_reason: "CRC eSource Batch Reconciliation Accept & Commit",
      },
      {
        sigToken: "sig-esource-001",
        changeReason: "CRC eSource Batch Reconciliation Accept & Commit",
      }
    );
  });

  it("opens batch reject modal and calls batchRejectEhrCandidates with mandatory rationale", async () => {
    const mockCandidates = getMockCandidates();
    vi.mocked(interopService.batchRejectEhrCandidates).mockResolvedValue({
      success: true,
      action_count: 2,
      candidates: mockCandidates.slice(0, 2).map((c) => ({
        ...c,
        status: "REJECTED",
        resolution_action: "REJECT",
      })) as any,
      message: "Batch rejected candidates",
    });

    const wrapper = mountDrawer();

    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 50));

    const checkboxes = wrapper.findAll(".candidate-checkbox");
    expect(checkboxes.length).toBeGreaterThanOrEqual(2);

    await checkboxes[0].setValue(true);
    await checkboxes[1].setValue(true);
    await wrapper.vm.$nextTick();

    const batchBar = wrapper.find(".floating-batch-bar");
    expect(batchBar.text()).toContain("2 item(s) selected");

    const batchRejectBtn = wrapper.find(".btn-batch-reject");
    await batchRejectBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Batch Reject EHR Candidate Data");

    const textarea = wrapper.find("#batch-reject-reason");
    await textarea.setValue("Out of visit window observations");
    await wrapper.find("#batch-reject-sig-password").setValue("Password123!"); // pragma: allowlist secret
    await wrapper.vm.$nextTick();

    const submitBtn = wrapper.find(".btn-submit-batch-reject");
    await submitBtn.trigger("click");
    await flushPromises();

    const expectedBatchId = await computeBatchBindingId(["cand-1", "cand-2"]);
    expect(soaClient.verifySignature).toHaveBeenCalledWith(
      expect.objectContaining({
        action: "/api/v1/interop/esource/reconcile/batch-reject",
        semanticAction: "interop.esource.batch_reject",
        batchId: expectedBatchId,
      })
    );
    expect(interopService.batchRejectEhrCandidates).toHaveBeenCalledWith(
      {
        candidate_ids: ["cand-1", "cand-2"],
        rejection_reason: "Out of visit window observations",
        change_reason: "CRC eSource Batch Reconciliation Reject",
      },
      {
        sigToken: "sig-esource-001",
        changeReason: "CRC eSource Batch Reconciliation Reject",
      }
    );
  });
});
