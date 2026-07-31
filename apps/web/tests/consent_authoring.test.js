import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ConsentAuthoringView from "../src/views/ConsentAuthoringView.vue";
import { useAuthStore } from "../src/stores/auth";
import { econsentService } from "../src/api/econsent";
import { router } from "../src/router";

// Mock econsent API client
vi.mock("../src/api/econsent", () => ({
  econsentService: {
    listTemplates: vi.fn(() => Promise.resolve([])),
    createTemplate: vi.fn(() =>
      Promise.resolve({ template_id: "tpl-new", version_index: 1 })
    ),
    updateTemplate: vi.fn(() =>
      Promise.resolve({ template_id: "tpl-new", version_index: 2 })
    ),
    composeTemplate: vi.fn(() => Promise.resolve({ clauses: [] })),
    publishTemplate: vi.fn(() =>
      Promise.resolve({ template_id: "tpl-1", is_published: true })
    ),
  },
}));

describe("ConsentAuthoringView.vue Component and RBAC Router Tests", () => {
  let pinia;
  let authStore;

  beforeEach(async () => {
    vi.clearAllMocks();
    pinia = createPinia();
    setActivePinia(pinia);
    authStore = useAuthStore();
    // Always reset router to initial route before each test
    await router.push("/login");
  });

  describe("RBAC Routing Permissions", () => {
    it("redirects unauthenticated users to /login", async () => {
      authStore.isAuthenticated = false;
      await router.push("/econsent-authoring");
      expect(router.currentRoute.value.path).toBe("/login");
    });

    it("allows authorized role to enter econsent authoring", async () => {
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Sponsor Designer"]; // Normalized to sponsor_designer

      await router.push("/econsent-authoring");
      expect(router.currentRoute.value.path).toBe("/econsent-authoring");
    });

    it("denies access to non-authorized roles and redirects to /forbidden", async () => {
      authStore.isAuthenticated = true;
      authStore.isDemoMode = true;
      authStore.rawRoles = ["Site Investigator"]; // No design/admin role

      await router.push("/econsent-authoring");
      expect(router.currentRoute.value.path).toBe("/forbidden");
    });
  });

  describe("Inline Gating & Defense-in-depth UI checks", () => {
    it("shows inline access-denied card for users with insufficient roles", () => {
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Site Investigator"]; // insufficient

      const wrapper = mount(ConsentAuthoringView);
      expect(wrapper.find("#access-denied-card").exists()).toBe(true);
      expect(wrapper.text()).toContain(
        "21 CFR Part 11 Role Gating - Access Denied"
      );
    });

    it("renders authoring editor layout when role authorization checks pass", async () => {
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Sponsor Designer"];

      const wrapper = mount(ConsentAuthoringView);
      expect(wrapper.find("#access-denied-card").exists()).toBe(false);
      expect(wrapper.text()).toContain("Consent Templates");
      expect(wrapper.find("#btn-create-template").exists()).toBe(true);
    });
  });

  describe("Template Ordering & Editor Operations", () => {
    beforeEach(() => {
      authStore.isAuthenticated = true;
      authStore.rawRoles = ["Sponsor Designer"];
    });

    it("supports adding and reordering consent clauses using move-up and move-down controls", async () => {
      const wrapper = mount(ConsentAuthoringView);

      // Open new template compose editor
      await wrapper.find("#btn-create-template").trigger("click");
      expect(wrapper.find("#template-editor-pane").exists()).toBe(true);

      // Add clause rows
      const addClauseBtn = wrapper.find(".btn-add-clause");
      await addClauseBtn.trigger("click");
      await addClauseBtn.trigger("click"); // Now has 3 clause rows (defaults to 1 + 2 = 3)

      const inputs = wrapper.findAll(".clause-id-input");
      expect(inputs).toHaveLength(3);

      await inputs[0].setValue("clause-risk");
      await inputs[1].setValue("clause-benefit");
      await inputs[2].setValue("clause-privacy");

      // Verify move-down is enabled for row 0
      const moveDownBtns = wrapper.findAll(".btn-move-down");
      const moveUpBtns = wrapper.findAll(".btn-move-up");

      expect(moveUpBtns[0].attributes("disabled")).toBeDefined(); // First row cannot move up
      expect(moveDownBtns[2].attributes("disabled")).toBeDefined(); // Last row cannot move down

      // Swap index 0 ("clause-risk") with index 1 ("clause-benefit")
      await moveDownBtns[0].trigger("click");

      const reorderedInputs = wrapper.findAll(".clause-id-input");
      expect(reorderedInputs[0].element.value).toBe("clause-benefit");
      expect(reorderedInputs[1].element.value).toBe("clause-risk");
    });

    it("renders publish-validation failures as accessible status live banners", async () => {
      const wrapper = mount(ConsentAuthoringView);
      await wrapper.find("#btn-create-template").trigger("click");

      // Set valid metadata but try to save with missing fields
      const studyInput = wrapper.find("#input-study-id");
      await studyInput.setValue(""); // triggers study_id required check

      await wrapper.find(".btn-save").trigger("click");

      const errBanner = wrapper.find("#editor-validation-error");
      expect(errBanner.exists()).toBe(true);
      expect(errBanner.attributes("role")).toBe("status");
      expect(errBanner.attributes("aria-live")).toBe("polite");
      expect(errBanner.text()).toContain("Study ID is strictly required");
    });

    it("verifies the staged reason modal capture flow", async () => {
      const wrapper = mount(ConsentAuthoringView);
      await wrapper.find("#btn-create-template").trigger("click");

      await wrapper.find("#input-study-id").setValue("study-abc");
      await wrapper.find("#input-template-name").setValue("Form-1");
      await wrapper.find("#input-protocol-version").setValue("v1.0");

      await wrapper.find(".btn-save").trigger("click");

      // Check reason modal is displayed
      const reasonModal = wrapper.find("#reason-modal");
      expect(reasonModal.exists()).toBe(true);
      expect(reasonModal.attributes("role")).toBe("dialog");
      expect(reasonModal.attributes("aria-modal")).toBe("true");

      // Select standard reason
      const select = reasonModal.find("#change-reason-select");
      await select.setValue("Language Correction");

      // Confirm signature save
      await reasonModal.find("#btn-save-change").trigger("click");

      expect(econsentService.createTemplate).toHaveBeenCalledWith(
        expect.objectContaining({
          study_id: "study-abc",
          template_name: "Form-1",
          protocol_version: "v1.0",
        }),
        expect.objectContaining({ changeReason: "Language Correction" })
      );
    });
  });
});
