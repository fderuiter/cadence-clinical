import { describe, it, expect, beforeEach } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import DicomMprViewer from "../../src/components/imaging/DicomMprViewer.vue";

describe("DicomMprViewer.vue - Mobile Single-Column Vertical Stack and Canvas Scaling", () => {
  let pinia: ReturnType<typeof createPinia>;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
  });

  it("renders all three viewports (Axial, Sagittal, Coronal) in default mpr-tri-view mode", () => {
    const wrapper = mount(DicomMprViewer, {
      props: {
        seriesUid: "1.2.840.113619.2.55.3.200.1",
      },
    });

    const grid = wrapper.find(".viewports-grid");
    expect(grid.exists()).toBe(true);
    expect(grid.classes()).toContain("mpr-tri-view");

    const cards = wrapper.findAll(".viewport-card");
    expect(cards.length).toBe(3);

    expect(wrapper.find(".axial-card").exists()).toBe(true);
    expect(wrapper.find(".sagittal-card").exists()).toBe(true);
    expect(wrapper.find(".coronal-card").exists()).toBe(true);
  });

  it("displays orientation plane badges with correct plane names and slice indicators", () => {
    const wrapper = mount(DicomMprViewer, {
      props: {
        seriesUid: "1.2.840.113619.2.55.3.200.1",
      },
    });

    const axialBadge = wrapper.find(".axial-badge");
    const sagittalBadge = wrapper.find(".sagittal-badge");
    const coronalBadge = wrapper.find(".coronal-badge");

    expect(axialBadge.exists()).toBe(true);
    expect(axialBadge.text()).toContain("AXIAL");

    expect(sagittalBadge.exists()).toBe(true);
    expect(sagittalBadge.text()).toContain("SAGITTAL");

    expect(coronalBadge.exists()).toBe(true);
    expect(coronalBadge.text()).toContain("CORONAL");
  });

  it("renders 100% fluid slice sliders with proper ARIA accessibility labels", () => {
    const wrapper = mount(DicomMprViewer, {
      props: {
        seriesUid: "1.2.840.113619.2.55.3.200.1",
      },
    });

    const sliders = wrapper.findAll(".slice-slider");
    expect(sliders.length).toBe(3);

    sliders.forEach((slider) => {
      expect(slider.attributes("type")).toBe("range");
      expect(slider.classes()).toContain("slice-slider");
    });

    const axialSlider = wrapper.find(
      'input[aria-label="Axial Slice Navigator"]'
    );
    const sagittalSlider = wrapper.find(
      'input[aria-label="Sagittal Slice Navigator"]'
    );
    const coronalSlider = wrapper.find(
      'input[aria-label="Coronal Slice Navigator"]'
    );

    expect(axialSlider.exists()).toBe(true);
    expect(sagittalSlider.exists()).toBe(true);
    expect(coronalSlider.exists()).toBe(true);
  });

  it("accurately calculates canvas coordinates with scaled bounding rect ratio for mouse and touch interactions", async () => {
    const wrapper = mount(DicomMprViewer, {
      props: {
        seriesUid: "1.2.840.113619.2.55.3.200.1",
        activeTool: "POINTER",
      },
    });

    const axialCanvas = wrapper.find({ ref: "axialCanvasRef" });
    expect(axialCanvas.exists()).toBe(true);

    const canvasEl = axialCanvas.element as HTMLCanvasElement;

    // Mock getBoundingClientRect for scaled mobile canvas (380px internal buffer scaled to 280px in CSS)
    canvasEl.getBoundingClientRect = () => ({
      left: 10,
      top: 10,
      width: 280,
      height: 280,
      right: 290,
      bottom: 290,
      x: 10,
      y: 10,
      toJSON: () => {},
    });

    // Touch event in center of 280px CSS canvas (left=10, touchX=150 => relativeX=140 => center of 280px CSS)
    // Scale factor = 380 / 280 = 1.35714 => mapped X = 140 * 1.35714 = 190 (exact center of 380px buffer)
    const touchStartEvent = new Event("touchstart", {
      bubbles: true,
      cancelable: true,
    });
    Object.defineProperty(touchStartEvent, "touches", {
      value: [{ clientX: 150, clientY: 150 }],
    });

    canvasEl.dispatchEvent(touchStartEvent);
    await wrapper.vm.$nextTick();

    // Center click (mapped X=190, Y=190 out of 380) should update sagittalSlice and coronalSlice to 256 (display 257 / 512)
    const text = wrapper.text();
    expect(text).toContain("SAGITTAL (X: 257 / 512)");
    expect(text).toContain("CORONAL (Y: 257 / 512)");
  });

  it("emits caliperCreated when drawing a caliper on touch drag with CALIPER_1D tool", async () => {
    const wrapper = mount(DicomMprViewer, {
      props: {
        seriesUid: "1.2.840.113619.2.55.3.200.1",
        activeTool: "CALIPER_1D",
      },
    });

    const axialCanvas = wrapper.find({ ref: "axialCanvasRef" });
    const canvasEl = axialCanvas.element as HTMLCanvasElement;

    canvasEl.getBoundingClientRect = () => ({
      left: 0,
      top: 0,
      width: 380,
      height: 380,
      right: 380,
      bottom: 380,
      x: 0,
      y: 0,
      toJSON: () => {},
    });

    // Touch start at (100, 100)
    const touchStartEvent = new Event("touchstart", {
      bubbles: true,
      cancelable: true,
    });
    Object.defineProperty(touchStartEvent, "touches", {
      value: [{ clientX: 100, clientY: 100 }],
    });
    canvasEl.dispatchEvent(touchStartEvent);

    // Touch end at (150, 100) => dx = 50px * 0.75mm = 37.5mm
    const touchEndEvent = new Event("touchend", {
      bubbles: true,
      cancelable: true,
    });
    Object.defineProperty(touchEndEvent, "changedTouches", {
      value: [{ clientX: 150, clientY: 100 }],
    });
    canvasEl.dispatchEvent(touchEndEvent);
    await wrapper.vm.$nextTick();

    const emitted = wrapper.emitted("caliperCreated");
    expect(emitted).toBeTruthy();
    expect(emitted![0][0]).toEqual({
      plane: "AXIAL",
      tool: "CALIPER_1D",
      lengthMm: 37.5,
      points: [
        { x: 100, y: 100 },
        { x: 150, y: 100 },
      ],
      sliceIndex: 24,
    });
  });
});
