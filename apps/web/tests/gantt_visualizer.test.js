import { describe, it, expect, beforeEach, vi } from "vitest";
import { createPinia, setActivePinia } from "pinia";
import { useClinicalStore } from "../src/stores/clinical";
import { useAuthStore } from "../src/stores/auth.js";
import { mount } from "@vue/test-utils";
import ClinicalGanttVisualizer from "../src/components/clinical/ClinicalGanttVisualizer.vue";

// Mock apiClient
vi.mock("../src/api/apiClient.js", () => {
  return {
    apiClient: {
      get: vi.fn(),
      post: vi.fn(),
      put: vi.fn(),
      delete: vi.fn(),
    },
  };
});

beforeEach(() => {
  const pinia = createPinia();
  setActivePinia(pinia);
  const authStore = useAuthStore();
  authStore.accessToken = "mock-keycloak-jwt-token";
  authStore.isAuthenticated = true;
  authStore.isDemoMode = false;
  if (typeof window !== "undefined" && window.localStorage) {
    window.localStorage.clear();
  }
  vi.resetAllMocks();
});

describe("Interactive Arm-Aware Gantt Pathway Visualizer Unit Tests", () => {
  it("renders parallel lanes for defined study arms", () => {
    const store = useClinicalStore();
    // Pre-populate store
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [
        { arm_id: "ARM-1", arm_name: "High Dose Active" },
        { arm_id: "ARM-2", arm_name: "Low Dose Active" },
        { arm_id: "ARM-3", arm_name: "Placebo Control" },
      ],
      epochs: [
        { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
        {
          epoch_id: "EP-TRT-1",
          epoch_name: "Treatment Phase 1",
          sequence: 2,
          arm_id: "ARM-1",
        },
        {
          epoch_id: "EP-TRT-2",
          epoch_name: "Treatment Phase 2",
          sequence: 2,
          arm_id: "ARM-2",
        },
      ],
      encounters: [
        {
          encounter_id: "V-SCR",
          encounter_name: "Screening Visit",
          epoch_id: "EP-SCR",
          sequence: 1,
        },
      ],
    };

    const wrapper = mount(ClinicalGanttVisualizer);

    // Check that there are lanes rendered for each arm
    expect(wrapper.text()).toContain("High Dose Active");
    expect(wrapper.text()).toContain("Low Dose Active");
    expect(wrapper.text()).toContain("Placebo Control");

    // Verify vertical delineator phase text exists
    expect(wrapper.html()).toContain("Phase 1");
    expect(wrapper.html()).toContain("Phase 2");
  });

  it("delineates epoch phase transitions using vertical boundaries", () => {
    const store = useClinicalStore();
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [{ arm_id: "ARM-1", arm_name: "Active Arm" }],
      epochs: [
        { epoch_id: "EP-1", epoch_name: "Screening", sequence: 1 },
        {
          epoch_id: "EP-2",
          epoch_name: "Treatment",
          sequence: 2,
          arm_id: "ARM-1",
        },
        {
          epoch_id: "EP-3",
          epoch_name: "Follow-up",
          sequence: 3,
          arm_id: "ARM-1",
        },
      ],
      encounters: [
        {
          encounter_id: "E1",
          encounter_name: "V1",
          epoch_id: "EP-1",
          sequence: 1,
        },
      ],
    };

    const wrapper = mount(ClinicalGanttVisualizer);

    // There should be vertical boundaries (dashed boundary lines) corresponding to sequence changes
    // With 3 epoch sequences (1, 2, 3), there should be 2 transition lines
    const lines = wrapper.findAll("line");
    // Some lines are lane pathways, some are connector dashed lines, some are vertical boundaries.
    // Let's filter lines with stroke-dasharray="4,4"
    const boundaryLines = lines.filter(
      (line) => line.attributes("stroke-dasharray") === "4,4"
    );
    expect(boundaryLines.length).toBeGreaterThanOrEqual(2);
  });

  it("renders visual curved crossover vectors linking crossover paths between different lanes", () => {
    const store = useClinicalStore();
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [
        { arm_id: "ARM-A", arm_name: "Arm A" },
        { arm_id: "ARM-B", arm_name: "Arm B" },
      ],
      epochs: [
        { epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 },
        {
          epoch_id: "EP-TRT-A",
          epoch_name: "Treatment Phase A",
          sequence: 2,
          arm_id: "ARM-A",
        },
        {
          epoch_id: "EP-TRT-B",
          epoch_name: "Treatment Phase B",
          sequence: 2,
          arm_id: "ARM-B",
        },
      ],
      encounters: [
        {
          encounter_id: "V-TRT-A2",
          encounter_name: "Week 4",
          epoch_id: "EP-TRT-A",
          sequence: 3,
        },
        {
          encounter_id: "V-TRT-B1",
          encounter_name: "Week 2",
          epoch_id: "EP-TRT-B",
          sequence: 4,
        },
      ],
      crossovers: [
        {
          from_visit_id: "V-TRT-A2",
          to_visit_id: "V-TRT-B1",
          label: "Crossover Path",
        },
      ],
    };

    const wrapper = mount(ClinicalGanttVisualizer);

    const paths = wrapper.findAll("path");
    // Find crossover vector line path (has stroke-dasharray="5,3")
    const crossoverPath = paths.find(
      (path) => path.attributes("stroke-dasharray") === "5,3"
    );
    expect(crossoverPath).toBeDefined();
    expect(wrapper.text()).toContain("Crossover Path");
  });

  it("launches the edit popover modal when a visit node is clicked", async () => {
    const store = useClinicalStore();
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [{ arm_id: "ARM-1", arm_name: "Active Arm" }],
      epochs: [{ epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 }],
      encounters: [
        {
          encounter_id: "V-SCR",
          encounter_name: "Screening Visit",
          epoch_id: "EP-SCR",
          sequence: 1,
        },
      ],
    };

    const wrapper = mount(ClinicalGanttVisualizer);

    // Modal is initially closed
    expect(wrapper.find(".gantt-modal-backdrop").exists()).toBe(false);

    // Find and click the circle visit node
    const circle = wrapper.find("circle");
    expect(circle.exists()).toBe(true);
    await circle.trigger("click");

    // Modal should now be open
    expect(wrapper.find(".gantt-modal-backdrop").exists()).toBe(true);
    expect(wrapper.text()).toContain("Edit Encounter Timing Property");
    expect(wrapper.text()).toContain("Screening Visit");
  });

  it("validates against negative timing values and min > max offset rules in popover inputs", async () => {
    const store = useClinicalStore();
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [{ arm_id: "ARM-1", arm_name: "Active" }],
      epochs: [{ epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 }],
      encounters: [
        {
          encounter_id: "V-SCR",
          encounter_name: "Visit 1",
          epoch_id: "EP-SCR",
          sequence: 1,
          planned_day: 5,
          min_offset: 1,
          max_offset: 3,
        },
      ],
    };

    const wrapper = mount(ClinicalGanttVisualizer);
    await wrapper.find("circle").trigger("click");

    // Get inputs
    const inputs = wrapper.findAll("input");
    const targetDayInput = inputs[0];
    const minOffsetInput = inputs[1];
    const maxOffsetInput = inputs[2];

    // 1. Negative target day
    await targetDayInput.setValue(-5);
    expect(wrapper.text()).toContain("Target Day cannot be negative.");
    expect(
      wrapper.find("button.btn-primary").attributes("disabled")
    ).toBeDefined();

    // Reset to valid
    await targetDayInput.setValue(5);
    expect(wrapper.text()).not.toContain("Target Day cannot be negative.");

    // 2. Negative min offset
    await minOffsetInput.setValue(-1);
    expect(wrapper.text()).toContain(
      "Minimum timing offset cannot be negative."
    );

    // Reset to valid
    await minOffsetInput.setValue(1);

    // 3. Min offset > Max offset rule
    await minOffsetInput.setValue(5);
    await maxOffsetInput.setValue(2);
    expect(wrapper.text()).toContain(
      "Minimum offset cannot exceed maximum offset window constraint."
    );
    expect(
      wrapper.find("button.btn-primary").attributes("disabled")
    ).toBeDefined();
  });

  it("strictly prevents saving timing properties unless a trimmed non-empty compliance justification is provided", async () => {
    const store = useClinicalStore();
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [{ arm_id: "ARM-1", arm_name: "Active" }],
      epochs: [{ epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 }],
      encounters: [
        {
          encounter_id: "V-SCR",
          encounter_name: "Visit 1",
          epoch_id: "EP-SCR",
          sequence: 1,
          planned_day: 5,
        },
      ],
    };

    const wrapper = mount(ClinicalGanttVisualizer);
    await wrapper.find("circle").trigger("click");

    const inputs = wrapper.findAll("input");
    const targetDayInput = inputs[0];
    const justificationTextarea = wrapper.find("textarea");

    // Modify a timing property
    await targetDayInput.setValue(10);

    // No justification yet -> save button must be disabled
    expect(
      wrapper.find("button.btn-primary").attributes("disabled")
    ).toBeDefined();

    // Provide empty/whitespace justification
    await justificationTextarea.setValue("   ");
    expect(
      wrapper.find("button.btn-primary").attributes("disabled")
    ).toBeDefined();

    // Provide a valid justification
    await justificationTextarea.setValue(
      "Required due to regulatory protocol amendment."
    );
    expect(
      wrapper.find("button.btn-primary").attributes("disabled")
    ).toBeUndefined();
  });

  it("updates Pinia store immediately upon timing parameter modification & triggers reactive updates", async () => {
    const store = useClinicalStore();
    store.currentUsdm = {
      studyId: "TEST-001",
      arms: [{ arm_id: "ARM-1", arm_name: "Active" }],
      epochs: [{ epoch_id: "EP-SCR", epoch_name: "Screening", sequence: 1 }],
      encounters: [
        {
          encounter_id: "V-SCR",
          encounter_name: "Visit 1",
          epoch_id: "EP-SCR",
          sequence: 1,
          planned_day: 5,
        },
      ],
    };

    // Spy on pushSoAMutation store method
    const mutationSpy = vi
      .spyOn(store, "pushSoAMutation")
      .mockResolvedValue(true);

    const wrapper = mount(ClinicalGanttVisualizer);
    await wrapper.find("circle").trigger("click");

    // Modify timing
    await wrapper.findAll("input")[0].setValue(12); // target day
    await wrapper.findAll("input")[1].setValue(2); // min offset
    await wrapper.findAll("input")[2].setValue(4); // max offset
    await wrapper.find("textarea").setValue("Scientific timing justification.");

    // Save
    await wrapper.find("button.btn-primary").trigger("click");

    // Check store call
    expect(mutationSpy).toHaveBeenCalledWith(
      "visits",
      "V-SCR",
      expect.objectContaining({
        planned_day: 12,
        min_offset: 2,
        max_offset: 4,
      }),
      "Scientific timing justification."
    );
  });
});
