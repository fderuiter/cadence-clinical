import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import AmendmentDiffView from "@/views/AmendmentDiffView.vue";
import { useAuthStore } from "@/stores/auth";
import { useClinicalStore } from "@/stores/clinical";
import { apiClient } from "@/api/apiClient";

vi.mock("@/api/apiClient", () => {
  return {
    apiClient: {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    },
  };
});

describe("AmendmentDiffView.vue - Protocol Amendments & Semantic Diff Specification", () => {
  let pinia: any;
  let authStore: any;
  let clinicalStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    authStore = useAuthStore();
    clinicalStore = useClinicalStore();

    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Sponsor Designer", "Medical Monitor"];

    vi.mocked(apiClient.get).mockImplementation((url) => {
      if (url.includes("/amendments") || url.includes("/diff")) {
        return Promise.resolve({
          baseVersion: "1.0.0",
          amendedVersion: "2.0.0",
          requiresReconsent: true,
          structureDiff: {
            addedArms: [{ id: "arm_c", name: "Dose Escalation Cohort C" }],
            modifiedEpochs: [{ id: "epoch_treat", name: "Extended Treatment" }],
          },
          soaDiff: {
            addedVisits: [{ id: "v_week_16", name: "Week 16 Follow-up" }],
            modifiedProcedures: [
              { id: "proc_biomarker", name: "PK Blood Sampling" },
            ],
          },
          eligibilityDiff: {
            addedCriteria: [{ id: "crit_alt_01", text: "ALT/AST <= 2.5x ULN" }],
          },
        });
      }
      return Promise.resolve([]);
    });

    vi.mocked(apiClient.post).mockResolvedValue({ status: "SUCCESS" });
    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders the protocol amendment comparison header and controls", () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    expect(wrapper.text()).toContain(
      "Protocol Amendments & In-Flight Subject Migration"
    );
    expect(wrapper.find("#btn-create-amendment").exists()).toBe(true);
    expect(wrapper.find("#base-version-select").exists()).toBe(true);
    expect(wrapper.find("#amended-version-select").exists()).toBe(true);
  });

  it("displays re-consent gating status and subject impact counters", () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    expect(wrapper.text()).toContain(
      "In-Flight Subject Migration & Re-Consent Analyzer"
    );
    expect(wrapper.text()).toContain("Re-Consent Mandated");
    expect(wrapper.text()).toContain("IMMUTABLE BRANCH");
  });

  it("opens create amendment modal when trigger button is clicked", async () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    const createBtn = wrapper.find("#btn-create-amendment");
    await createBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.showCreateModal).toBe(true);
  });

  it("supports switching between diff inspector tabs", async () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    const tabs = wrapper.findAll(".tab-btn");
    if (tabs.length > 1) {
      await tabs[1].trigger("click");
      await wrapper.vm.$nextTick();
      expect(tabs[1].classes()).toContain("active");
    }
  });

  it("guides study manager through the 4-step upversioning wizard and publishes via API", async () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    // Launch wizard
    const createBtn = wrapper.find("#btn-create-amendment");
    await createBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.wizardStep).toBe(1);
    expect(wrapper.text()).toContain(
      "Step 1: Amendment Classification & Scope"
    );

    // Advance to Step 2
    wrapper.vm.goToWizardStep(2);
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.wizardStep).toBe(2);
    expect(wrapper.text()).toContain(
      "Step 2: Target Version & Study Scope Selection"
    );

    // Advance to Step 3 (Predictive impact analysis)
    wrapper.vm.goToWizardStep(3);
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.wizardStep).toBe(3);
    expect(wrapper.text()).toContain(
      "Step 3: Predictive Site & Subject Impact Analysis"
    );

    // Advance to Step 4 & publish
    wrapper.vm.goToWizardStep(4);
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.wizardStep).toBe(4);
    expect(wrapper.find("#btn-publish-amendment").exists()).toBe(true);

    // Trigger publish
    await wrapper.find("#btn-publish-amendment").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/designer/amendments/branch",
      expect.objectContaining({
        study_id: "CADENCE-101",
        base_version_tag: "1.0.0",
        amendment_type: "major",
        requires_reconsent: true,
      }),
      {}
    );

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/execution/amendments/publish",
      expect.objectContaining({
        study_id: "CADENCE-101",
        version_number: "2.0.0",
      })
    );
  });

  it("switches to site coordinator bulk workspace, filters by site, and executes bulk re-consent", async () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    // Switch to site coordinator mode
    const navBtns = wrapper.findAll(".mode-nav-btn");
    expect(navBtns.length).toBeGreaterThan(1);
    await navBtns[1].trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.activeMode).toBe("coordinator");
    expect(wrapper.text()).toContain(
      "Site Coordinator Bulk Re-Consent Workspace"
    );

    // Test site filter select
    const siteSelect = wrapper.find("#site-filter-select");
    expect(siteSelect.exists()).toBe(true);
    await siteSelect.setValue("SITE-101");
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.siteFilter).toBe("SITE-101");

    // Select gated subjects
    wrapper.vm.selectedSubjectIds = ["SUBJ-102", "SUBJ-103"];
    await wrapper.vm.$nextTick();

    // Verify sticky batch toolbar is displayed
    const batchToolbar = wrapper.find("#sticky-batch-toolbar");
    expect(batchToolbar.exists()).toBe(true);
    expect(wrapper.text()).toContain("2 Subject(s) Selected");

    // Open bulk re-consent modal
    await wrapper.find("#btn-batch-reconsent").trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.showBulkReconsentModal).toBe(true);
    expect(wrapper.find("#bulk-reconsent-modal").exists()).toBe(true);

    // Execute bulk re-consent sign-off
    await wrapper.find("#btn-submit-bulk-signature").trigger("click");
    await flushPromises();

    expect(apiClient.post).toHaveBeenCalledWith(
      "/api/v1/execution/amendments/bulk-reconsent",
      expect.objectContaining({
        subject_ids: ["SUBJ-102", "SUBJ-103"],
        study_id: "CADENCE-101",
        protocol_version: "2.0.0",
        signature_type: "ECONSENT",
      })
    );

    // Verify subjects cleared
    expect(wrapper.vm.selectedSubjectIds).toEqual([]);
    expect(wrapper.vm.showBulkReconsentModal).toBe(false);
  });

  it("renders Amendment Impact Summary and multi-layer diff tabs", async () => {
    const wrapper = mount(AmendmentDiffView, {
      global: { plugins: [pinia] },
    });

    // Switch to graph diff tab
    const tabs = wrapper.findAll(".tab-btn");
    expect(tabs.length).toBeGreaterThan(1);
    await tabs[1].trigger("click");
    await wrapper.vm.$nextTick();

    // Check Impact Summary section
    expect(wrapper.text()).toContain("Protocol Amendment Impact Summary");
    expect(wrapper.text()).toContain("Operational Burden Delta");
    expect(wrapper.text()).toContain("MANDATORY RE-CONSENT GATED");

    // Check layer buttons
    const layerBtns = wrapper.findAll(".layer-tab-btn");
    expect(layerBtns.length).toBe(3);
    expect(layerBtns[0].text()).toContain("USDM Graph & SoA Matrix Diff");
    expect(layerBtns[1].text()).toContain("Eligibility Criteria Diff");
    expect(layerBtns[2].text()).toContain("eCRF Forms & Data Capture Diff");

    // Switch to Eligibility layer
    await layerBtns[1].trigger("click");
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.activeDiffLayer).toBe("eligibility");
    expect(wrapper.text()).toContain("Eligibility Criteria Modifications");

    // Switch to eCRF Forms layer
    await layerBtns[2].trigger("click");
    await wrapper.vm.$nextTick();
    expect(wrapper.vm.activeDiffLayer).toBe("ecrf");
    expect(wrapper.text()).toContain("eCRF Form Definitions");
  });
});
