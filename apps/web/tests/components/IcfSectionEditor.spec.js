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

  it("clamps glossary popover within 12px on narrow viewports, persists across hover bridge, supports keyboard focus on coarse viewports, and dismisses on Escape", async () => {
    const originalVisualViewport = window.visualViewport;
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

    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: true,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));

    const section = {
      id: "sec_3",
      title: "Randomization",
      html: 'Right-edge term <span class="glossary-term" data-definition="Assigned by chance">randomization</span>.',
    };

    const wrapper = mount(IcfSectionEditor, {
      attachTo: document.body,
      props: { section },
    });

    try {
      await wrapper.vm.$nextTick();
      const termSpan = wrapper.find(".glossary-term");
      expect(termSpan.attributes("tabindex")).toBe("0");

      vi.spyOn(termSpan.element, "getBoundingClientRect").mockReturnValue({
        top: 120,
        bottom: 142,
        left: 250,
        right: 312,
        width: 62,
        height: 22,
      });

      termSpan.element.dispatchEvent(
        new MouseEvent("mouseover", { bubbles: true })
      );
      await wrapper.vm.$nextTick();
      await wrapper.vm.$nextTick();

      const popover = wrapper.find(".glossary-popover");
      expect(popover.exists()).toBe(true);
      expect(popover.classes()).toContain("clinical-popover--hover-bridge");
      const style = popover.attributes("style");
      expect(style).toContain("position: fixed");
      expect(style).toContain("left: 28px");

      // Moving pointer from term into popover keeps popover open (WCAG 1.4.13)
      termSpan.element.dispatchEvent(
        new MouseEvent("mouseout", {
          bubbles: true,
          relatedTarget: popover.element,
        })
      );
      await wrapper.vm.$nextTick();
      expect(wrapper.find(".glossary-popover").exists()).toBe(true);

      // Escape dismisses open popover
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", cancelable: true })
      );
      await wrapper.vm.$nextTick();
      expect(wrapper.find(".glossary-popover").exists()).toBe(false);
    } finally {
      wrapper.unmount();
    }

    // Coarse/touch viewport still reveals glossary popover on keyboard focusin/focusout
    window.matchMedia = vi.fn().mockImplementation((query) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));

    const touchWrapper = mount(IcfSectionEditor, {
      attachTo: document.body,
      props: { section },
    });

    try {
      await touchWrapper.vm.$nextTick();
      const touchTerm = touchWrapper.find(".glossary-term");
      touchTerm.element.dispatchEvent(
        new FocusEvent("focusin", { bubbles: true })
      );
      await touchWrapper.vm.$nextTick();
      expect(touchWrapper.find(".glossary-popover").exists()).toBe(true);

      touchTerm.element.dispatchEvent(
        new FocusEvent("focusout", { bubbles: true, relatedTarget: null })
      );
      await touchWrapper.vm.$nextTick();
      expect(touchWrapper.find(".glossary-popover").exists()).toBe(false);
    } finally {
      touchWrapper.unmount();
      Object.defineProperty(window, "visualViewport", {
        configurable: true,
        writable: true,
        value: originalVisualViewport,
      });
    }
  });
});
