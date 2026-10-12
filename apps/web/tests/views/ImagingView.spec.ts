import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import ImagingView from "../../src/views/ImagingView.vue";
import DicomMprViewer from "../../src/components/imaging/DicomMprViewer.vue";
import OncologyCalipers from "../../src/components/imaging/OncologyCalipers.vue";

describe("ImagingView.vue - Dual-viewport longitudinal comparison layout with synchronized crosshairs", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("allows switching between Single Timepoint View and Dual Timepoint Comparison View", async () => {
    const wrapper = mount(ImagingView, {
      global: {
        plugins: [pinia],
      },
    });

    // Default is single mode
    expect(wrapper.findAllComponents(DicomMprViewer).length).toBe(1);

    // Switch to Dual Timepoint Comparison View
    const modeSelect = wrapper.find("#view-mode-select");
    expect(modeSelect.exists()).toBe(true);

    await modeSelect.setValue("dual");

    const viewers = wrapper.findAllComponents(DicomMprViewer);
    expect(viewers.length).toBe(2);

    const viewportA = viewers[0];
    const viewportB = viewers[1];

    expect(viewportA.props("timepoint")).toBe("BASELINE");
    expect(viewportA.props("viewportLabel")).toBe("Viewport A (Baseline)");

    expect(viewportB.props("timepoint")).toBe("CYCLE_2_DAY_1");
    expect(viewportB.props("viewportLabel")).toBe("Viewport B (Follow-up)");
  });

  it("Viewport A and Viewport B independently load DICOM series belonging to distinct timepoints", async () => {
    const wrapper = mount(ImagingView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#view-mode-select").setValue("dual");

    const selectA = wrapper.find("#timepoint-a-select");
    const selectB = wrapper.find("#timepoint-b-select");

    expect(selectA.exists()).toBe(true);
    expect(selectB.exists()).toBe(true);

    // Change Viewport A timepoint to CYCLE_4_DAY_1
    await selectA.setValue("CYCLE_4_DAY_1");

    const viewers = wrapper.findAllComponents(DicomMprViewer);
    expect(viewers[0].props("timepoint")).toBe("CYCLE_4_DAY_1");
    expect(viewers[1].props("timepoint")).toBe("CYCLE_2_DAY_1");
  });

  it("enabling viewport synchronization locks mouse wheel slice navigation and crosshair position across viewports", async () => {
    const wrapper = mount(ImagingView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#view-mode-select").setValue("dual");

    const syncBtn = wrapper.find(".btn-sync-toggle");
    expect(syncBtn.exists()).toBe(true);
    expect(syncBtn.classes()).toContain("synced");

    const viewers = wrapper.findAllComponents(DicomMprViewer);
    expect(viewers[0].props("isSynced")).toBe(true);
    expect(viewers[1].props("isSynced")).toBe(true);

    // Viewport A emits updated syncState
    await viewers[0].vm.$emit("update:syncedState", {
      axialSlice: 15,
      sagittalSlice: 200,
      coronalSlice: 300,
      selectedPreset: "LUNG",
    });

    await wrapper.vm.$nextTick();

    // Viewport B receives updated syncedState
    expect(viewers[1].props("syncedState")).toEqual({
      axialSlice: 15,
      sagittalSlice: 200,
      coronalSlice: 300,
      selectedPreset: "LUNG",
    });
  });

  it("disabling viewport synchronization allows independent scrolling and window level adjustments per viewport", async () => {
    const wrapper = mount(ImagingView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#view-mode-select").setValue("dual");

    const syncBtn = wrapper.find(".btn-sync-toggle");
    await syncBtn.trigger("click"); // Unlink sync

    expect(syncBtn.text()).toContain("Viewports Unlinked");

    const viewers = wrapper.findAllComponents(DicomMprViewer);
    expect(viewers[0].props("isSynced")).toBe(false);
    expect(viewers[1].props("isSynced")).toBe(false);
  });

  it("caliper measurements drawn in either viewport generate distinct, timepoint-stamped annotations in OncologyCalipers.vue", async () => {
    const wrapper = mount(ImagingView, {
      global: {
        plugins: [pinia],
      },
    });

    await wrapper.find("#view-mode-select").setValue("dual");

    const viewers = wrapper.findAllComponents(DicomMprViewer);

    // Simulate drawing a caliper in Viewport A (Baseline)
    await viewers[0].vm.$emit("caliperCreated", {
      plane: "AXIAL",
      tool: "CALIPER_1D",
      lengthMm: 28.4,
      points: [
        { x: 50, y: 50 },
        { x: 90, y: 50 },
      ],
      sliceIndex: 12,
      timepoint: "BASELINE",
      viewportLabel: "Viewport A",
    });

    // Simulate drawing a caliper in Viewport B (Cycle 2 Day 1)
    await viewers[1].vm.$emit("caliperCreated", {
      plane: "AXIAL",
      tool: "CALIPER_1D",
      lengthMm: 14.2,
      points: [
        { x: 100, y: 100 },
        { x: 120, y: 100 },
      ],
      sliceIndex: 12,
      timepoint: "CYCLE_2_DAY_1",
      viewportLabel: "Viewport B",
    });

    await wrapper.vm.$nextTick();

    const calipersComponent = wrapper.findComponent(OncologyCalipers);
    expect(calipersComponent.exists()).toBe(true);

    const annotations = calipersComponent.props("annotations") as any[];
    expect(annotations.length).toBe(4); // 2 initial + 2 new

    const newAnnotA = annotations.find((a) => a.longest_diameter_mm === 28.4);
    const newAnnotB = annotations.find((a) => a.longest_diameter_mm === 14.2);

    expect(newAnnotA).toBeTruthy();
    expect(newAnnotA.timepoint).toBe("BASELINE");
    expect(newAnnotA.viewport_origin).toBe("Viewport A");

    expect(newAnnotB).toBeTruthy();
    expect(newAnnotB.timepoint).toBe("CYCLE_2_DAY_1");
    expect(newAnnotB.viewport_origin).toBe("Viewport B");

    // Check rendered timepoint pills in OncologyCalipers table
    const text = calipersComponent.text();
    expect(text).toContain("Baseline");
    expect(text).toContain("Cycle 2 Day 1");
  });
});
