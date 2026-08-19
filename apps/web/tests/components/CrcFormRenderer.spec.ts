/**
 * Unit & Component tests for CrcFormRenderer.vue
 *
 * Requirements Traceability: PRD-SYS-001, PRD-SUB-007, PRD-EDC-005 | GxP 21 CFR Part 11 Regulated
 */

import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import CrcFormRenderer from "../../src/components/persona/CrcFormRenderer.vue";
import { useClinicalStore } from "../../src/stores/clinical";
import { apiClient } from "../../src/api/apiClient";

describe("CrcFormRenderer.vue", () => {
  let pinia: any;
  let clinicalStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    clinicalStore = useClinicalStore();

    vi.spyOn(apiClient, "get").mockResolvedValue([]);
    vi.spyOn(apiClient, "post").mockResolvedValue({});
    vi.spyOn(apiClient, "put").mockResolvedValue({});
    vi.spyOn(apiClient, "delete").mockResolvedValue({});

    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  function createWrapper(customProps = {}) {
    return mount(CrcFormRenderer, {
      props: {
        store: clinicalStore,
        selectedSubjectId: "SUBJ-101",
        selectedVisitId: "Screening",
        isReconsentGated: false,
        showEconsentModal: false,
        showPaperIcfModal: false,
        reconsentSubmitting: false,
        econsentSignerName: "",
        paperIcfDate: "",
        paperIcfNote: "",
        lookupStatuses: {},
        getValidationError: () => null,
        isCraUser: false,
        isAuthorizedForBulkSdv: false,
        sdvStates: {},
        getSdvKey: (id: string) => `sdv_${id}`,
        selectedBatchFields: [],
        ...customProps,
      },
      global: {
        plugins: [pinia],
        stubs: {
          ClinicalFormField: {
            template: '<div class="clinical-form-field-stub" :data-field-id="field.id">Field: {{ field.label }}</div>',
            props: ["field", "modelValue", "query", "error", "lookupStatus", "canManageQueries", "queryLabel"],
          },
        },
      },
    });
  }

  it("renders dynamic forms derived from the subject's active protocol schema version", async () => {
    /**
     * @req: PRD-SYS-001
     * Verify that subject with protocol v2.0.0 projects amended lab and temperature fields,
     * whereas protocol v1.0.0 only renders baseline fields.
     */
    // 1. Mount with SUBJ-101 (active_protocol_version: "2.0.0")
    const wrapperV2 = createWrapper({ selectedSubjectId: "SUBJ-101" });
    expect(wrapperV2.text()).toContain("Protocol Schema v2.0.0");
    
    // v2.0.0 includes amended fields (vs_temp, lb_wbc, lb_gluc, lb_alt)
    const stubsV2 = wrapperV2.findAll(".clinical-form-field-stub");
    const fieldIdsV2 = stubsV2.map((s) => s.attributes("data-field-id"));
    expect(fieldIdsV2).toContain("vs_temp");
    expect(fieldIdsV2).toContain("lb_wbc");
    expect(fieldIdsV2).toContain("lb_gluc");
    expect(fieldIdsV2).toContain("lb_alt");

    // 2. Mount with SUBJ-102 (active_protocol_version: "1.0.0")
    const wrapperV1 = createWrapper({ selectedSubjectId: "SUBJ-102" });
    expect(wrapperV1.text()).toContain("Protocol Schema v1.0.0");
    const stubsV1 = wrapperV1.findAll(".clinical-form-field-stub");
    const fieldIdsV1 = stubsV1.map((s) => s.attributes("data-field-id"));
    expect(fieldIdsV1).not.toContain("lb_wbc");
    expect(fieldIdsV1).not.toContain("vs_temp");
  });

  it("opens subject enrollment modal and assigns subject ID, site ID, consent date, and arm", async () => {
    /**
     * @req: PRD-SYS-001, PRD-SUB-007
     * Verify subject enrollment flow assigns identifier, site ID, consent date, and arm.
     */
    const wrapper = createWrapper();
    expect(wrapper.find("#enroll-subject-modal").exists()).toBe(false);

    // Click 'Enroll New Subject' button
    await wrapper.find("#btn-enroll-subject").trigger("click");
    expect(wrapper.find("#enroll-subject-modal").exists()).toBe(true);

    // Populate enrollment fields
    await wrapper.find("#enroll-subject-id").setValue("SUBJ-101-099");
    await wrapper.find("#enroll-site-id").setValue("SITE-102");
    await wrapper.find("#enroll-consent-date").setValue("2026-08-19");
    await wrapper.find("#enroll-arm-id").setValue("ARM-A");
    await wrapper.find("#enroll-change-reason").setValue("Test enrollment for cohort");

    // Submit enrollment
    await wrapper.find("#btn-confirm-enroll").trigger("click");
    await wrapper.vm.$nextTick();
    await new Promise((resolve) => setTimeout(resolve, 10));
    await wrapper.vm.$nextTick();

    // Verify modal is closed and event emitted
    expect(wrapper.find("#enroll-subject-modal").exists()).toBe(false);
    expect(wrapper.emitted("enroll-subject")).toBeTruthy();
    expect(wrapper.emitted("update:selectedSubjectId")?.[0][0]).toBe("SUBJ-101-099");

    // Verify subject is in the store
    const enrolledSub = clinicalStore.subjects.find((s: any) => s.id === "SUBJ-101-099");
    expect(enrolledSub).toBeDefined();
    expect(enrolledSub?.siteId).toBe("SITE-102");
    expect(enrolledSub?.armId).toBe("ARM-A");
    expect(enrolledSub?.status).toBe("ENROLLED");
  });

  it("evaluates live edit checks in real-time displaying Warning and Discrepancy badges", async () => {
    /**
     * @req: PRD-SYS-001, PRD-EDC-005
     * Verify real-time evaluation of edit check rules:
     * - Systolic BP > 180 mmHg triggers Warning badge
     * - Diastolic BP >= Systolic BP triggers Discrepancy badge
     */
    const wrapper = createWrapper({ selectedSubjectId: "SUBJ-101" });

    // 1. Set Systolic BP > 180 mmHg (Hypertensive alert)
    clinicalStore.formValues.vssbp = "195";
    clinicalStore.formValues.vsdpb = "85";
    clinicalStore.formValues.pulse = "75";
    await wrapper.vm.$nextTick();

    // Summary bar and Warning badge should appear
    expect(wrapper.find("#edit-checks-summary-bar").exists()).toBe(true);
    expect(wrapper.find(".badge-warning").exists()).toBe(true);
    expect(wrapper.text()).toContain("exceeds 180 mmHg threshold");

    // 2. Set Diastolic BP >= Systolic BP (Discrepancy)
    clinicalStore.formValues.vssbp = "120";
    clinicalStore.formValues.vsdpb = "135";
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".badge-discrepancy").exists()).toBe(true);
    expect(wrapper.text()).toContain("Diastolic BP (135 mmHg) cannot equal or exceed Systolic BP (120 mmHg)");

    // 3. Click 'Raise Query' button from edit check banner
    const raiseBtn = wrapper.find(".btn-raise-query");
    expect(raiseBtn.exists()).toBe(true);
    await raiseBtn.trigger("click");
    expect(wrapper.emitted("create-query")).toBeTruthy();
    expect(wrapper.emitted("create-query")?.[0][0]).toBe("vsdpb");
  });

  it("triggers explicit Re-Consent Gate blocking modal when subject has pending re-consent", async () => {
    /**
     * @req: PRD-SYS-001, PRD-SUB-007
     * Verify that attempting to submit or interact while isReconsentGated triggers blocking modal.
     */
    const wrapper = createWrapper({
      selectedSubjectId: "SUBJ-102",
      isReconsentGated: true,
    });

    // Banner is rendered
    expect(wrapper.find("#reconsent-gating-banner").exists()).toBe(true);

    // Re-consent gate blocking modal is not open initially
    expect(wrapper.find("#reconsent-gate-modal").exists()).toBe(false);

    // Attempting to click submit button triggers blocking modal
    await wrapper.find("#btn-submit-ecrf").trigger("click");
    expect(wrapper.find("#reconsent-gate-modal").exists()).toBe(true);
    expect(wrapper.find("#reconsent-gate-modal").text()).toContain("Re-Consent Gate Active");
    expect(wrapper.find("#reconsent-gate-modal").text()).toContain("SUBJ-102");
  });
});
