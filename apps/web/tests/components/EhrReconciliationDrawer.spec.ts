import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import EhrReconciliationDrawer from "../../src/components/clinical/EhrReconciliationDrawer.vue";
import { interopService } from "../../src/api/interop";

vi.mock("../../src/api/interop", () => ({
  interopService: {
    listEhrCandidates: vi.fn(),
    acceptEhrCandidate: vi.fn(),
    rejectEhrCandidate: vi.fn(),
    raiseEhrClinicalQuery: vi.fn(),
    getEhrProvenance: vi.fn(),
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

    expect(interopService.acceptEhrCandidate).toHaveBeenCalledWith({
      candidate_id: "cand-1",
      commit_reason: "CRC accepted candidate data from hospital EHR",
      change_reason: "CRC eSource Reconciliation Accept & Commit",
    });
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

    const submitBtn = wrapper.find(".btn-danger");
    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(true);

    const textarea = wrapper.find("#reject-reason");
    await textarea.setValue("Lab sample taken out of protocol visit window");
    await wrapper.vm.$nextTick();

    expect((submitBtn.element as HTMLButtonElement).disabled).toBe(false);
    await submitBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(interopService.rejectEhrCandidate).toHaveBeenCalledWith({
      candidate_id: "cand-1",
      rejection_reason: "Lab sample taken out of protocol visit window",
      change_reason: "CRC eSource Reconciliation Reject",
    });
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
});
