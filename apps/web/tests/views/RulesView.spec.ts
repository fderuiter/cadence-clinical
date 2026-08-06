import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount, flushPromises } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import RulesView from "@/views/RulesView.vue";
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

// Setup mock router
const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: "/rules", component: RulesView }],
});

describe("RulesView.vue - Clinical Rules Designer Workspace Specification", () => {
  let pinia: any;
  let authStore: any;
  let clinicalStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);

    authStore = useAuthStore();
    clinicalStore = useClinicalStore();

    // Default to authorized data_manager role
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Data Manager"];

    // Setup mock implementations for apiClient
    vi.mocked(apiClient.get).mockImplementation((url) => {
      if (url.includes("/rules")) {
        return Promise.resolve([
          {
            id: "rule_1",
            type: "skip_logic",
            target_field: "pulse_details",
            target_form: "form_vs",
            action: "show",
            condition: {
              type: "comparison",
              operator: ">",
              operands: [
                {
                  type: "field_ref",
                  field_ref: { field_id: "pulse", form_id: "form_vs" },
                },
                { type: "constant", value: 100 },
              ],
            },
            compiled_xpath: "/clinical_data/form_vs/pulse > 100",
          },
        ]);
      }
      return Promise.resolve([]);
    });

    vi.mocked(apiClient.post).mockImplementation((url, payload) => {
      if (url.includes("/rules/preview") || url.includes("/rules/validate")) {
        return Promise.resolve({
          xpath: "/clinical_data/form_vs/pulse > 100",
          failures: [],
          circular_cycles: [],
        });
      }
      if (url.includes("/rules")) {
        return Promise.resolve({
          id: "rule_mock_saved",
          ...payload,
          compiled_xpath: "/clinical_data/form_vs/pulse > 100",
        });
      }
      return Promise.resolve({});
    });

    vi.mocked(apiClient.put).mockResolvedValue({});
    vi.mocked(apiClient.delete).mockResolvedValue({});

    vi.clearAllMocks();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("gates workspace access to STUDY_DESIGNER or DATA_MANAGER roles and hides the designer if unauthorized", async () => {
    // Set role to site coordinator (not authorized for rule authoring)
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Site Investigator"];

    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await flushPromises();

    // Check that rules gating banner is displayed
    const gatingBanner = wrapper.find(".rules-gating-banner");
    expect(gatingBanner.exists()).toBe(true);
    expect(gatingBanner.text()).toContain("Access Denied");
    expect(gatingBanner.text()).toContain("STUDY_DESIGNER");

    // Workspace should not be visible
    expect(wrapper.find(".tab-btn-rules").exists()).toBe(false);
  });

  it("allows STUDY_DESIGNER (Sponsor Designer) to access the workspace", async () => {
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.rawRoles = ["Sponsor Designer"];

    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await flushPromises();

    // Should NOT see the gating banner
    expect(wrapper.find(".rules-gating-banner").exists()).toBe(false);

    // Header and tab options should render
    expect(wrapper.text()).toContain("Interactive Rules Designer");
    expect(wrapper.find(".tab-btn-rules").exists()).toBe(true);
  });

  it("renders ruleset list and opens visual editor workspace for authorized roles", async () => {
    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await flushPromises();

    // Wait for the mock API call to have occurred (handles async crypto signature delay under CPU load)
    let retries = 20;
    while (vi.mocked(apiClient.get).mock.calls.length === 0 && retries > 0) {
      await flushPromises();
      await new Promise((resolve) => setTimeout(resolve, 10));
      retries--;
    }
    await flushPromises();

    // Authorized role should NOT see the gating banner
    expect(wrapper.find(".rules-gating-banner").exists()).toBe(false);

    // Header and tab options should render
    expect(wrapper.text()).toContain("Interactive Rules Designer");
    expect(wrapper.find(".tab-btn-rules").exists()).toBe(true);

    // List of active rules should be fetched and rendered
    expect(apiClient.get).toHaveBeenCalledWith(
      "/api/v1/studies/study_1/rules",
      expect.any(Object)
    );

    expect(wrapper.text()).toContain("rule_1");
    expect(wrapper.text()).toContain("pulse_details");
  });

  it("composes, compiles, and links custom rules to active visual fields", async () => {
    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await flushPromises();

    // Click 'Create New Rule' button
    const createBtn = wrapper.find("button[class*='btn-primary']");
    expect(createBtn.text()).toContain("Create New Rule");
    await createBtn.trigger("click");

    await flushPromises();

    // The editor should be shown
    expect(wrapper.vm.showEditor).toBe(true);

    // Verifying standard active visual fields are present in target selections
    const targetSelect = wrapper.find("#target-field-select");
    expect(targetSelect.exists()).toBe(true);

    // It should include our active visual fields such as pulse, vssbp, etc.
    expect(targetSelect.html()).toContain("vssbp");
    expect(targetSelect.html()).toContain("pulse");

    // Verify XPath compilation output exists
    expect(wrapper.text()).toContain("Compiled XPath Expression");
  });

  it("gates saving rules with mandatory 21 CFR Part 11 / EU Annex 11 reason audit checks", async () => {
    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia, router],
      },
    });

    await flushPromises();

    // Click 'Create New Rule' button to open the editor properly
    const createBtn = wrapper.find("button[class*='btn-primary']");
    await createBtn.trigger("click");

    await flushPromises();

    // Fill parameters
    wrapper.vm.targetField = "pulse";

    // Find the Save Signed Rule button
    const saveBtn = wrapper
      .findAll("button")
      .find((b) => b.text().includes("Save Signed Rule"));
    expect(saveBtn).toBeDefined();
    await saveBtn!.trigger("click");

    await flushPromises();

    // ReasonModal should be prompted
    expect(wrapper.vm.showReasonModal).toBe(true);

    // Confirming transition with a valid reason registers a block in the compliance ledger
    const initialLedgerSize = clinicalStore.ledgerBlocks.length;

    await wrapper.vm.confirmChangeReason(
      "Added logical safety check for pulse metrics"
    );

    // Ledger blocks should have a new RULE_SAVE audit block
    expect(clinicalStore.ledgerBlocks.length).toBe(initialLedgerSize + 1);
    expect(
      clinicalStore.ledgerBlocks[clinicalStore.ledgerBlocks.length - 1].action
    ).toBe("RULE_SAVE");
    expect(
      clinicalStore.ledgerBlocks[clinicalStore.ledgerBlocks.length - 1].reason
    ).toBe("Added logical safety check for pulse metrics");
  });
});
