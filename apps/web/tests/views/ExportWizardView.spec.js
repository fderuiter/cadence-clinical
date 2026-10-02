import { describe, it, expect, vi, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ExportWizardView from "@/views/ExportWizardView.vue";

describe("ExportWizardView component", () => {
  beforeEach(() => {
    setActivePinia(createPinia());
    vi.stubEnv("VITE_DEMO_MODE", "true");
  });

  it("renders export wizard and fast export buttons", () => {
    const wrapper = mount(ExportWizardView);
    expect(wrapper.text()).toContain("1-Click Fast Regulatory Compilation");
    expect(wrapper.find(".btn-fast-export-odm").exists()).toBe(true);
    expect(wrapper.find(".btn-fast-export-csv").exists()).toBe(true);
  });

  it("executes fast export with stage progress and cancels with AbortSignal", async () => {
    const wrapper = mount(ExportWizardView);
    const odmBtn = wrapper.find(".btn-fast-export-odm");

    const exportPromise = odmBtn.trigger("click");
    await wrapper.vm.$nextTick();

    // Fast export task should now be processing
    expect(wrapper.find(".fast-export-progress-box").exists()).toBe(true);
    expect(wrapper.find(".btn-cancel-fast-export").exists()).toBe(true);

    // Trigger cancel
    await wrapper.find(".btn-cancel-fast-export").trigger("click");
    await exportPromise;
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".fast-export-progress-box").exists()).toBe(false);
  });

  it("executes pipeline export and handles cancel operation in Step 5", async () => {
    const wrapper = mount(ExportWizardView);

    // Go to step 5
    wrapper.vm.currentStep = 5;
    await wrapper.vm.$nextTick();

    const startBtn = wrapper.find(".execution-idle button");
    expect(startBtn.exists()).toBe(true);

    startBtn.trigger("click");
    await wrapper.vm.$nextTick();

    // Execution progress should be active
    expect(wrapper.find(".execution-progress").exists()).toBe(true);
    expect(wrapper.find(".btn-cancel-pipeline-export").exists()).toBe(true);

    // Cancel pipeline export
    await wrapper.find(".btn-cancel-pipeline-export").trigger("click");
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".execution-cancelled").exists()).toBe(true);
    expect(wrapper.text()).toContain("Export Operation Cancelled");
  });
});
