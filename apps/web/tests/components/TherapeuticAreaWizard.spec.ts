import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import TherapeuticAreaWizard from "@/components/clinical/TherapeuticAreaWizard.vue";
import { useOnboardingStore } from "@/stores/onboarding";

describe("TherapeuticAreaWizard.vue - Interactive Therapeutic Area Wizard Specification", () => {
  let pinia: any;
  let onboardingStore: any;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    onboardingStore = useOnboardingStore();
    window.localStorage.clear();
  });

  afterEach(() => {
    vi.restoreAllMocks();
  });

  it("renders 5 therapeutic area starter templates", () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    const taCards = wrapper.findAll(".ta-card");
    expect(taCards.length).toBe(5);

    const text = wrapper.text();
    expect(text).toContain("Oncology / Solid Tumors");
    expect(text).toContain("Cardiology / Heart Failure");
    expect(text).toContain("Neurology / Alzheimer's & MS");
    expect(text).toContain("Immunology / Rheumatoid Arthritis");
    expect(text).toContain("Vaccines / Infectious Disease");
  });

  it("selects a template and updates active configuration", async () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    const cards = wrapper.findAll(".ta-card");
    // Click second template (Cardiology)
    await cards[1].trigger("click");

    expect(wrapper.vm.selectedTemplateId).toBe("cardiology");
    expect(wrapper.vm.activeConfig.protocol_id).toBe("CDNC-2026-CARD");
    expect(wrapper.text()).toContain("Heart Failure");
  });

  it("modifies arm names or sample sizes and updates preview state immediately", async () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    // Modify sample size and arm name
    wrapper.vm.activeConfig.arms[0].target_sample_size = 250;
    wrapper.vm.activeConfig.arms[0].name = "Custom Kinase Inhibitor Arm";
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.totalSampleSize).toBe(
      250 + wrapper.vm.activeConfig.arms[1].target_sample_size
    );
    expect(wrapper.text()).toContain(
      `Target Participants:${250 + wrapper.vm.activeConfig.arms[1].target_sample_size} subjects`
    );
    const inputEl = wrapper.find(".input-arm-name").element as HTMLInputElement;
    expect(inputEl.value).toBe("Custom Kinase Inhibitor Arm");
  });

  it("adds and removes study arms interactively", async () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    const initialArmCount = wrapper.vm.activeConfig.arms.length;
    wrapper.vm.addArm();
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.activeConfig.arms.length).toBe(initialArmCount + 1);

    wrapper.vm.removeArm(wrapper.vm.activeConfig.arms.length - 1);
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.activeConfig.arms.length).toBe(initialArmCount);
  });

  it("modifies visit schedule target days and window tolerances", async () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    wrapper.vm.configTab = "visits";
    await wrapper.vm.$nextTick();

    const visit = wrapper.vm.activeConfig.visits[0];
    visit.target_day = -14;
    visit.window_lower_days = 5;
    await wrapper.vm.$nextTick();

    expect(wrapper.vm.activeConfig.visits[0].target_day).toBe(-14);
    expect(wrapper.vm.activeConfig.visits[0].window_lower_days).toBe(5);
  });

  it("emits synthesize event and marks onboarding task_protocol completed", async () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    const synthBtn = wrapper.find(".btn-synthesize-protocol");
    expect(synthBtn.exists()).toBe(true);

    await synthBtn.trigger("click");

    const emitted = wrapper.emitted("synthesize");
    expect(emitted).toBeTruthy();
    expect(emitted![0][0]).toHaveProperty("protocol_id");
    expect(emitted![0][0]).toHaveProperty("arms");
    expect(emitted![0][0]).toHaveProperty("epochs");
    expect(emitted![0][0]).toHaveProperty("visits");
    expect(emitted![0][0]).toHaveProperty("activities");

    // Verify task_protocol in onboarding store is completed
    const task = onboardingStore.tasks.find(
      (t: any) => t.id === "task_protocol"
    );
    expect(task.completed).toBe(true);
  });

  it("persists guided setup state in localStorage", async () => {
    const wrapper = mount(TherapeuticAreaWizard, {
      global: {
        plugins: [pinia],
      },
    });

    wrapper.vm.activeConfig.protocol_id = "CUSTOM-PERSISTED-001";
    await wrapper.vm.$nextTick();

    const savedRaw = window.localStorage.getItem("cadence_ta_wizard_state");
    expect(savedRaw).not.toBeNull();
    const parsed = JSON.parse(savedRaw!);
    expect(parsed.config.protocol_id).toBe("CUSTOM-PERSISTED-001");
  });
});
