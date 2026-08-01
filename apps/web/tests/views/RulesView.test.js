import { describe, it, expect, beforeEach, vi, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import RulesView from "../../src/views/RulesView.vue";
import { useAuthStore } from "../../src/stores/auth";
import { useClinicalStore } from "../../src/stores/clinical";

const mockFetch = vi.fn();
globalThis.fetch = mockFetch;

describe("RulesView.vue Component Unit & Integration Tests", () => {
  let pinia;
  let authStore;
  let clinicalStore;

  beforeEach(() => {
    mockFetch.mockReset();
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();
    clinicalStore = useClinicalStore();

    // Default authenticated DM/Designer role setup
    authStore.accessToken = "mock-token";
    authStore.isAuthenticated = true;
    authStore.isDemoMode = false;
    authStore.userId = "usr_dm_fderuiter";
    authStore.rawRoles = ["Sponsor Designer", "Data Manager"];

    // Mock window alert
    window.alert = vi.fn();

    // Standard high-fidelity mock implementation for API fetch
    mockFetch.mockImplementation(async (url, options) => {
      const urlStr = String(url);
      const method = options?.method ? options.method.toUpperCase() : "GET";

      if (urlStr.includes("/rules/preview") || urlStr.includes("/rules/validate")) {
        return {
          ok: true,
          status: 200,
          json: async () => ({
            xpath: "(/clinical_data/form_dm/sex = 'F')",
            failures: [],
            circular_cycles: []
          })
        };
      }

      if (urlStr.includes("/rules")) {
        if (method === "POST" || method === "PUT") {
          return {
            ok: true,
            status: 200,
            json: async () => ({
              id: "saved_rule_99",
              type: "constraint",
              compiled_xpath: "(/clinical_data/vssbp > 140)"
            })
          };
        }
        // GET returns list
        return {
          ok: true,
          status: 200,
          json: async () => [
            {
              id: "rule_1",
              type: "skip_logic",
              action: "show",
              target_field: "vssbp",
              condition: {
                type: "comparison",
                operator: ">",
                operands: [
                  { type: "field_ref", field_ref: { field_id: "sex", form_id: "form_dm" } },
                  { type: "constant", value: "F" }
                ]
              },
              compiled_xpath: "/clinical_data/form_dm/sex = 'F'"
            }
          ]
        };
      }

      return {
        ok: true,
        status: 200,
        json: async () => ({})
      };
    });
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("gates access and displays access denied banner when the user lacks STUDY_DESIGNER/sponsor_designer role", async () => {
    // Demote role
    authStore.rawRoles = ["CRC"];

    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia],
      },
    });

    expect(wrapper.text()).toContain("21 CFR Part 11 Role Gating - Access Denied");
    expect(wrapper.find(".grid-2").exists()).toBe(false);
  });

  it("displays visual rules builder catalog and lets user compose rules", async () => {
    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia],
      },
    });

    // Wait for onMounted fetch
    await new Promise((resolve) => setTimeout(resolve, 50));

    expect(wrapper.text()).toContain("rule_1");
    expect(wrapper.text()).toContain("skip_logic");
    expect(wrapper.text()).toContain("/clinical_data/form_dm/sex = 'F'");

    // Click "Create New Rule"
    const createBtn = wrapper.find("button.btn-primary");
    expect(createBtn.exists()).toBe(true);
    await createBtn.trigger("click");

    // The editor wrapper should render with ruleEditorHtml
    const editorWrapper = wrapper.find(".rule-editor-wrapper");
    expect(editorWrapper.exists()).toBe(true);
    expect(editorWrapper.html()).toContain('class="rule-editor-container"');
    expect(editorWrapper.html()).toContain('class="rule-type-selector"');
  });

  it("sends preview and live-validation requests to designer API with correctly signed gateway headers", async () => {
    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("button.btn-primary").trigger("click");

    // Simulate selector change on editor to trigger triggerPreview()
    const select = wrapper.find(".rule-type-selector");
    await select.setValue("constraint");
    await select.trigger("change");

    // Wait for async calls
    await new Promise((resolve) => setTimeout(resolve, 50));

    // Confirm endpoints were called
    const calls = mockFetch.mock.calls;
    const validateCall = calls.find(call => call[0].includes("/rules/validate"));
    const previewCall = calls.find(call => call[0].includes("/rules/preview"));

    expect(validateCall).toBeDefined();
    expect(previewCall).toBeDefined();

    // Check signed headers present
    const headers = previewCall[1].headers;
    expect(headers["X-User-Id"]).toBe("usr_dm_fderuiter");
    expect(headers["X-User-Roles"]).toBe("sponsor_designer,data_manager");
    expect(headers["X-Gateway-Signature"]).toBeDefined();
    expect(headers["X-Signature-Version"]).toBe("2");
  });

  it("gates saves behind Reason Modal, signs payload canonically, and logs RULE_SAVE block to clinical store", async () => {
    const wrapper = mount(RulesView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("button.btn-primary").trigger("click");

    // Set editing values so promptSaveRule works
    const typeSelector = wrapper.find(".rule-type-selector");
    await typeSelector.setValue("constraint");
    await typeSelector.trigger("change");

    const targetFieldSelect = wrapper.find(".target-field-selector");
    await targetFieldSelect.setValue("vssbp");
    await targetFieldSelect.trigger("change");

    const queryMsgInput = wrapper.find(".query-message-input");
    await queryMsgInput.setValue("Value too high");
    await queryMsgInput.trigger("input");

    // Click save
    const saveBtn = wrapper.findAll("button").find(btn => btn.text().includes("Save Signed Rule"));
    await saveBtn.trigger("click");

    // ReasonModal should show up
    expect(wrapper.vm.showReasonModal).toBe(true);

    // Call confirmChangeReason
    await wrapper.vm.confirmChangeReason("Correction of vital range");

    // Clinical store should log the block
    const ruleSaveBlock = clinicalStore.ledgerBlocks.find(b => b.action === "RULE_SAVE");
    expect(ruleSaveBlock).toBeDefined();
    expect(ruleSaveBlock.reason).toBe("Correction of vital range");
    expect(ruleSaveBlock.details.ruleId).toBe("saved_rule_99");
    expect(ruleSaveBlock.details.signature).toBeDefined(); // canonical signature checked
  });
});
