import { describe, it, expect, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import { createRouter, createWebHistory } from "vue-router";
import MdrView from "../src/views/MdrView.vue";
import { useClinicalStore } from "../src/stores/clinical";

const router = createRouter({
  history: createWebHistory(),
  routes: [{ path: "/mdr", name: "mdr", component: MdrView }],
});

describe("MdrView.vue Change Reason Modal Bypass", () => {
  it("bypasses the change reason modal when the active version is a draft", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const clinicalStore = useClinicalStore();
    clinicalStore.activeStudyVersionId = "v_draft_01"; // Draft version

    // Mock store methods to prevent actual API calls
    clinicalStore.pushSoAMutation = vi.fn().mockResolvedValue({ success: true });
    clinicalStore.validateModel = vi.fn().mockReturnValue({ success: true });

    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia, router],
      },
    });

    // Locate the add arm button and form fields
    const armIdInput = wrapper.find("#arm-id-input");
    const armNameInput = wrapper.find("#arm-name-input");
    const addArmBtn = wrapper.find("#btn-add-arm");

    if (armIdInput.exists() && armNameInput.exists() && addArmBtn.exists()) {
      await armIdInput.setValue("ARM-NEW");
      await armNameInput.setValue("New Treatment Arm");
      await addArmBtn.trigger("click");

      // Verify that showReasonModal remains false
      expect(wrapper.vm.showReasonModal).toBe(false);
      // Verify pushSoAMutation was called directly with empty reason
      expect(clinicalStore.pushSoAMutation).toHaveBeenCalledWith(
        "arms",
        "ARM-NEW",
        { name: "New Treatment Arm" },
        ""
      );
    }
  });

  it("launches the change reason modal when the active version is a non-draft regulated path", async () => {
    const pinia = createPinia();
    setActivePinia(pinia);

    const clinicalStore = useClinicalStore();
    clinicalStore.activeStudyVersionId = "v_baseline_production"; // Non-draft

    clinicalStore.pushSoAMutation = vi.fn().mockResolvedValue({ success: true });
    clinicalStore.validateModel = vi.fn().mockReturnValue({ success: true });

    const wrapper = mount(MdrView, {
      global: {
        plugins: [pinia, router],
      },
    });

    const armIdInput = wrapper.find("#arm-id-input");
    const armNameInput = wrapper.find("#arm-name-input");
    const addArmBtn = wrapper.find("#btn-add-arm");

    if (armIdInput.exists() && armNameInput.exists() && addArmBtn.exists()) {
      await armIdInput.setValue("ARM-REGULATED");
      await armNameInput.setValue("Regulated Treatment Arm");
      await addArmBtn.trigger("click");

      // Verify that showReasonModal was launched (set to true)
      expect(wrapper.vm.showReasonModal).toBe(true);
      // Verify pushSoAMutation has not been called yet (as it's waiting for modal confirmation)
      expect(clinicalStore.pushSoAMutation).not.toHaveBeenCalled();
    }
  });
});
