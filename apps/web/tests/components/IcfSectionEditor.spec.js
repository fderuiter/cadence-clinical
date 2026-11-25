import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import { createPinia, setActivePinia } from "pinia";
import IcfSectionEditor from "../../src/components/econsent/IcfSectionEditor.vue";
import { useToastStore } from "../../src/stores/toasts.js";

describe("IcfSectionEditor.vue glossary capability", () => {
  let originalMatchMedia;
  let pinia;

  beforeEach(() => {
    pinia = createPinia();
    setActivePinia(pinia);
    originalMatchMedia = window.matchMedia;
    vi.spyOn(window, "alert").mockImplementation(() => {});
  });

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
    vi.restoreAllMocks();
  });

  it("desktop behavior with hover support", async () => {
    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: true,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));

    const section = {
      id: "sec_1",
      title: "Introduction",
      html: 'This is a <span class="glossary-term" data-definition="Sample explanation of placebo">placebo</span> used in trials.',
    };

    const wrapper = mount(IcfSectionEditor, {
      props: { section },
    });

    // Check editor loaded the html
    const canvas = wrapper.find(".editor-canvas");
    expect(canvas.exists()).toBe(true);

    const termSpan = wrapper.find(".glossary-term");
    expect(termSpan.exists()).toBe(true);

    // Mouse over the glossary term to trigger popover
    termSpan.element.dispatchEvent(
      new MouseEvent("mouseover", { bubbles: true })
    );
    await wrapper.vm.$nextTick();

    expect(wrapper.find(".glossary-popover").exists()).toBe(true);
    expect(wrapper.find(".popover-body").text()).toContain(
      "Sample explanation of placebo"
    );

    // Mouse out should hide it
    termSpan.element.dispatchEvent(
      new MouseEvent("mouseout", { bubbles: true })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.find(".glossary-popover").exists()).toBe(false);

    // Click should push an accessible info toast on desktop without window.alert
    termSpan.element.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    await wrapper.vm.$nextTick();
    expect(window.alert).not.toHaveBeenCalled();
    expect(useToastStore().toasts).toEqual([
      expect.objectContaining({
        tone: "info",
        title: "Glossary Definition: placebo",
        message: "placebo: Sample explanation of placebo",
      }),
    ]);
  });

  it("touch/mobile behavior without hover support (tap to toggle)", async () => {
    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));

    const section = {
      id: "sec_2",
      title: "Protocol Methods",
      html: 'This is a <span class="glossary-term" data-definition="An active control therapy">comparator</span> used in trials.',
    };

    const wrapper = mount(IcfSectionEditor, {
      props: { section },
    });

    const glossarySpan = wrapper.find(".glossary-term");
    expect(glossarySpan.exists()).toBe(true);

    // Mouse over should not trigger popover
    glossarySpan.element.dispatchEvent(
      new MouseEvent("mouseover", { bubbles: true })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.find(".glossary-popover").exists()).toBe(false);

    // Click/tap should show popover on first tap
    glossarySpan.element.dispatchEvent(
      new MouseEvent("click", { bubbles: true })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.find(".glossary-popover").exists()).toBe(true);
    expect(wrapper.find(".popover-body").text()).toContain(
      "comparator: An active control therapy"
    );

    // Clicking again should hide popover
    glossarySpan.element.dispatchEvent(
      new MouseEvent("click", { bubbles: true })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.find(".glossary-popover").exists()).toBe(false);

    // Click again to show
    glossarySpan.element.dispatchEvent(
      new MouseEvent("click", { bubbles: true })
    );
    await wrapper.vm.$nextTick();
    expect(wrapper.find(".glossary-popover").exists()).toBe(true);

    // Clicking outside should hide it. We can simulate document click.
    const outerDiv = document.createElement("div");
    outerDiv.className = "outside-element";
    document.body.appendChild(outerDiv);

    outerDiv.dispatchEvent(new MouseEvent("click", { bubbles: true }));

    await wrapper.vm.$nextTick();
    expect(wrapper.find(".glossary-popover").exists()).toBe(false);

    document.body.removeChild(outerDiv);
  });

  it("clamps .glossary-popover within 12px of narrow viewports, sustains hover onto popover, supports keyboard focus on touch devices, and dismisses on Escape", async () => {
    const originalInnerWidth = window.innerWidth;
    const originalInnerHeight = window.innerHeight;
    const originalVisualViewport = window.visualViewport;

    try {
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        writable: true,
        value: 320,
      });
      Object.defineProperty(window, "innerHeight", {
        configurable: true,
        writable: true,
        value: 640,
      });
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        writable: true,
        value: {
          width: 320,
          height: 640,
          offsetLeft: 0,
          offsetTop: 0,
          addEventListener: vi.fn(),
          removeEventListener: vi.fn(),
        },
      });

      // 1. Hover-capable desktop: moving pointer from term onto popover keeps popover open
      window.matchMedia = vi.fn().mockImplementation((query) => ({
        matches: true,
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }));

      const section = {
        id: "sec_3",
        title: "Randomization",
        html: 'Near edge <span class="glossary-term" data-definition="Allocation by chance across arms">randomization</span> term.',
      };

      const wrapper = mount(IcfSectionEditor, {
        attachTo: document.body,
        props: { section },
        global: { plugins: [pinia] },
      });
      await wrapper.vm.$nextTick();

      const termEl = wrapper.find(".glossary-term").element;
      expect(termEl.getAttribute("tabindex")).toBe("0");
      vi.spyOn(termEl, "getBoundingClientRect").mockReturnValue({
        left: 270,
        top: 120,
        right: 314,
        bottom: 142,
        width: 44,
        height: 22,
        x: 270,
        y: 120,
        toJSON: () => ({}),
      });

      termEl.dispatchEvent(new MouseEvent("mouseover", { bubbles: true }));
      await wrapper.vm.$nextTick();

      const popoverWrapper = wrapper.find(".glossary-popover");
      expect(popoverWrapper.exists()).toBe(true);
      const leftPx = Number.parseFloat(popoverWrapper.element.style.left);
      expect(leftPx).toBeGreaterThanOrEqual(12);
      expect(leftPx + 280).toBeLessThanOrEqual(320 - 12);

      // Pointer moves from term into popover -> stays open (WCAG 2.2 SC 1.4.13 Hoverable)
      popoverWrapper.element.dispatchEvent(new MouseEvent("mouseenter"));
      termEl.dispatchEvent(
        new MouseEvent("mouseout", {
          bubbles: true,
          relatedTarget: popoverWrapper.element,
        })
      );
      await wrapper.vm.$nextTick();
      expect(wrapper.find(".glossary-popover").exists()).toBe(true);

      // Escape dismisses without moving pointer (WCAG 2.2 SC 1.4.13 Dismissible)
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      await wrapper.vm.$nextTick();
      expect(wrapper.find(".glossary-popover").exists()).toBe(false);
      wrapper.unmount();

      // 2. Non-hover / touch device with external keyboard: focusin opens popover
      window.matchMedia = vi.fn().mockImplementation((query) => ({
        matches: false,
        media: query,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      }));

      const touchWrapper = mount(IcfSectionEditor, {
        attachTo: document.body,
        props: { section },
        global: { plugins: [pinia] },
      });
      await touchWrapper.vm.$nextTick();

      const touchTermEl = touchWrapper.find(".glossary-term").element;
      touchTermEl.dispatchEvent(new FocusEvent("focusin", { bubbles: true }));
      await touchWrapper.vm.$nextTick();
      expect(touchWrapper.find(".glossary-popover").exists()).toBe(true);

      touchTermEl.dispatchEvent(
        new FocusEvent("focusout", {
          bubbles: true,
          relatedTarget: document.body,
        })
      );
      await touchWrapper.vm.$nextTick();
      expect(touchWrapper.find(".glossary-popover").exists()).toBe(false);
      touchWrapper.unmount();
    } finally {
      Object.defineProperty(window, "innerWidth", {
        configurable: true,
        writable: true,
        value: originalInnerWidth,
      });
      Object.defineProperty(window, "innerHeight", {
        configurable: true,
        writable: true,
        value: originalInnerHeight,
      });
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        writable: true,
        value: originalVisualViewport,
      });
    }
  });
});
