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
});
