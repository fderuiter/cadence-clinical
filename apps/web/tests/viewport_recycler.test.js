import { mount } from "@vue/test-utils";
import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import ClinicalFormField from "../src/components/clinical/ClinicalFormField.vue";

describe("Viewport-Driven DOM Recycler Integration Tests", () => {
  let intersectionCallback;
  let resizeCallback;

  beforeEach(() => {
    // Mock IntersectionObserver
    vi.stubGlobal(
      "IntersectionObserver",
      vi.fn().mockImplementation(function (callback) {
        intersectionCallback = callback;
        this.observe = vi.fn();
        this.unobserve = vi.fn();
        this.disconnect = vi.fn();
      })
    );

    // Mock ResizeObserver
    vi.stubGlobal(
      "ResizeObserver",
      vi.fn().mockImplementation(function (callback) {
        resizeCallback = callback;
        this.observe = vi.fn();
        this.unobserve = vi.fn();
        this.disconnect = vi.fn();
      })
    );
  });

  afterEach(() => {
    vi.unstubAllGlobals();
  });

  it("should enforce a minimum height of 44px to satisfy WCAG", () => {
    /**
     * Requirement 3: The layout wrapper must track and enforce a minimum height of 44px
     * to satisfy WCAG touch-target requirements.
     */
    const field = { id: "field-1", label: "Test Input", type: "text" };
    const wrapper = mount(ClinicalFormField, {
      props: {
        field,
        modelValue: "Initial Value",
      },
    });

    const wrapperDiv = wrapper.find(".clinical-form-field-wrapper");
    expect(wrapperDiv.exists()).toBe(true);
    expect(wrapperDiv.element.style.minHeight).toBe("44px");
  });

  it("should mount child components when intersecting", async () => {
    /**
     * Requirement 1: The system must wrap each clinical form field in a layout wrapper
     * that monitors viewport entry and exit using a native browser IntersectionObserver.
     */
    const field = { id: "field-1", label: "Test Input", type: "text" };
    const wrapper = mount(ClinicalFormField, {
      props: {
        field,
        modelValue: "Initial Value",
      },
    });

    // Simulate element entering the viewport
    intersectionCallback([{ isIntersecting: true }]);
    await wrapper.vm.$nextTick();

    // The child input component should be rendered
    const input = wrapper.find("input");
    expect(input.exists()).toBe(true);
    expect(input.element.value).toBe("Initial Value");
  });

  it("should keep child components mounted when offscreen (retaining standard DOM inputs for accessibility)", async () => {
    /**
     * Requirement 1: The system must retain all form field wrappers and their child inputs in the active DOM structure.
     * Requirement 2: The system must use CSS content-visibility: auto on field wrappers.
     */
    const field = { id: "field-1", label: "Test Input", type: "text" };
    const wrapper = mount(ClinicalFormField, {
      props: {
        field,
        modelValue: "Initial Value",
      },
    });

    // Simulate element entering viewport and measuring height
    intersectionCallback([{ isIntersecting: true }]);
    await wrapper.vm.$nextTick();

    // Mock measuring height
    resizeCallback([
      {
        target: wrapper.element,
        borderBoxSize: [{ blockSize: 80 }],
      },
    ]);
    await wrapper.vm.$nextTick();

    // Simulate element exiting viewport (going offscreen)
    intersectionCallback([{ isIntersecting: false }]);
    await wrapper.vm.$nextTick();

    // Child component is NOT unmounted (it remains mounted in active DOM)
    const input = wrapper.find("input");
    expect(input.exists()).toBe(true);

    // Browser-native layout skipping is instructed via content-visibility: auto
    const wrapperDiv = wrapper.find(".clinical-form-field-wrapper");
    expect(wrapperDiv.element.style.contentVisibility).toBe("auto");

    // Dynamic browser-accessible layout size is assigned using contain-intrinsic-size to prevent layout shifts
    expect(wrapperDiv.element.style.containIntrinsicSize).toBe("auto 80px");
  });

  it("retains user input state because child components remain mounted in the DOM when offscreen", async () => {
    /**
     * Requirement 4: The browser must natively handle sequential tab-key focus navigation across all offscreen and onscreen fields.
     */
    const field = { id: "field-1", label: "Test Input", type: "text" };
    const wrapper = mount(ClinicalFormField, {
      props: {
        field,
        modelValue: "Persistent State",
      },
    });

    // 1. Enter viewport
    intersectionCallback([{ isIntersecting: true }]);
    await wrapper.vm.$nextTick();
    expect(wrapper.find("input").element.value).toBe("Persistent State");

    // 2. Scroll out
    intersectionCallback([{ isIntersecting: false }]);
    await wrapper.vm.$nextTick();
    // Input must still exist in the active DOM so that keyboard focus can tab to it
    expect(wrapper.find("input").exists()).toBe(true);
    expect(wrapper.find("input").element.value).toBe("Persistent State");

    // 3. Scroll back in
    intersectionCallback([{ isIntersecting: true }]);
    await wrapper.vm.$nextTick();
    expect(wrapper.find("input").element.value).toBe("Persistent State");
  });

  it("should implement Dynamic contain-intrinsic-size based on cached measurements to prevent layout shifts", async () => {
    /**
     * Requirement 3: The system must assign a browser-accessible layout size using contain-intrinsic-size
     * based on cached measurements.
     */
    const field = { id: "field-1", label: "Test Input", type: "text" };
    const wrapper = mount(ClinicalFormField, {
      props: {
        field,
        modelValue: "Guarded Content",
      },
    });

    // 1. Initial offscreen intersection event (isIntersecting = false)
    intersectionCallback([{ isIntersecting: false }]);
    await wrapper.vm.$nextTick();

    // Child must be mounted
    let input = wrapper.find("input");
    expect(input.exists()).toBe(true);

    // It should have default contain-intrinsic-size before any height is captured
    const wrapperDiv = wrapper.find(".clinical-form-field-wrapper");
    expect(wrapperDiv.element.style.containIntrinsicSize).toBe("auto 44px");

    // 2. While offscreen, ResizeObserver reports its first height measurement
    resizeCallback([
      {
        target: wrapper.element,
        borderBoxSize: [{ blockSize: 120 }],
      },
    ]);
    await wrapper.vm.$nextTick();

    // Child component remains mounted in active DOM
    input = wrapper.find("input");
    expect(input.exists()).toBe(true);

    // Dynamic contain-intrinsic-size updates based on the captured height measurement
    expect(wrapperDiv.element.style.containIntrinsicSize).toBe("auto 120px");
  });
});
