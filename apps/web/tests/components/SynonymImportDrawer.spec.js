import { describe, it, expect, beforeEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import SynonymImportDrawer from "../../src/components/SynonymImportDrawer.vue";
import { useCodingStore } from "../../src/stores/coding.js";
import { useAuthStore } from "../../src/stores/auth.js";
import { apiClient } from "../../src/api/apiClient.js";

vi.mock("../../src/api/apiClient.js", () => ({
  apiClient: {
    get: vi.fn().mockResolvedValue([]),
    post: vi.fn().mockResolvedValue({}),
    delete: vi.fn().mockResolvedValue({}),
  },
}));

describe("SynonymImportDrawer.vue Component", () => {
  let codingStore;
  let authStore;

  beforeEach(() => {
    setActivePinia(createPinia());
    codingStore = useCodingStore();
    authStore = useAuthStore();

    // Default authorized user (Data Manager in Demo Mode or with role)
    authStore.isDemoMode = true;
    codingStore.isSynonymDrawerOpen = true;
    vi.clearAllMocks();
  });

  it("renders drawer panel when isSynonymDrawerOpen is true", () => {
    const wrapper = mount(SynonymImportDrawer);

    expect(wrapper.find(".synonym-drawer-panel").exists()).toBe(true);
    expect(wrapper.text()).toContain("Bulk Synonym Dictionary Import");
    expect(wrapper.text()).toContain("Import New Rules");
    expect(wrapper.text()).toContain("Audit History");
    expect(wrapper.text()).toContain("Active Rules");
  });

  it("hides drawer when isSynonymDrawerOpen is false", () => {
    codingStore.isSynonymDrawerOpen = false;
    const wrapper = mount(SynonymImportDrawer);

    expect(wrapper.find(".synonym-drawer-panel").exists()).toBe(false);
  });

  it("displays pre-flight report metrics and error breakdown table when report is present", async () => {
    codingStore.synonymPreflightReport = {
      totalRows: 10,
      validCount: 8,
      duplicateCount: 1,
      invalidCount: 1,
      canImport: true,
      invalidRows: [
        {
          rowNumber: 5,
          verbatimText: "BAD_TERM",
          dictionaryType: "MEDDRA",
          errorReason: "Missing target code",
        },
      ],
    };

    const wrapper = mount(SynonymImportDrawer);

    expect(wrapper.find(".preflight-report-card").exists()).toBe(true);
    expect(wrapper.text()).toContain("Pre-flight Passed");
    expect(wrapper.text()).toContain("10"); // Total rows
    expect(wrapper.text()).toContain("8"); // Valid rows
    expect(wrapper.text()).toContain("1"); // Duplicates
    expect(wrapper.text()).toContain("BAD_TERM");
    expect(wrapper.text()).toContain("Missing target code");
  });

  it("enables Ingest button only when preflight canImport is true AND reasonForChange is filled", async () => {
    codingStore.synonymPreflightReport = {
      totalRows: 5,
      validCount: 5,
      duplicateCount: 0,
      invalidCount: 0,
      canImport: true,
      invalidRows: [],
    };

    const wrapper = mount(SynonymImportDrawer);

    // Mock selected file
    wrapper.vm.selectedFile = new File(["test"], "synonyms.csv", {
      type: "text/csv",
    });
    await wrapper.vm.$nextTick();

    const confirmBtn = wrapper.find(".confirm-import-btn");
    expect(confirmBtn.element.disabled).toBe(true);

    // Fill Part 11 reason for change
    const reasonInput = wrapper.find("#synonym-reason-for-change");
    await reasonInput.setValue("Updated MedDRA 26.0 protocol synonyms");

    expect(confirmBtn.element.disabled).toBe(false);
  });

  it("shows access restriction banner for unauthorized users", () => {
    authStore.isDemoMode = false;
    authStore.roles = ["cra"]; // CRA is not authorized

    const wrapper = mount(SynonymImportDrawer);

    expect(wrapper.find(".access-restricted-banner").exists()).toBe(true);
    expect(wrapper.text()).toContain("Access Restricted");
    expect(wrapper.text()).toContain(
      "DATA_MANAGER or SPONSOR_DESIGNER authorization"
    );
  });

  it("switches tabs between Import, Audit History, and Active Rules", async () => {
    const mockRules = [
      {
        id: "rule-1",
        verbatim_text: "HEADACHE",
        target_code: "10019211",
        target_term: "Headache",
        scope: "STUDY",
      },
    ];
    apiClient.get.mockResolvedValue(mockRules);

    const wrapper = mount(SynonymImportDrawer);

    // Switch to Active Rules tab
    const tabs = wrapper.findAll("button");
    const rulesTabBtn = tabs.find((b) => b.text().includes("Active Rules"));
    await rulesTabBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Active Synonym Rules Store");
    expect(wrapper.text()).toContain("HEADACHE");
    expect(wrapper.text()).toContain("10019211");

    // Switch to Audit History tab
    const historyTabBtn = tabs.find((b) => b.text().includes("Audit History"));
    await historyTabBtn.trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.text()).toContain("Bulk Synonym Import Job Ledger");
  });
});
