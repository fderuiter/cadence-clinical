import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { mount } from "@vue/test-utils";
import IcfSectionEditor from "../../src/components/econsent/IcfSectionEditor.vue";

describe("IcfSectionEditor.vue glossary capability", () => {
  let originalMatchMedia;

  beforeEach(() => {
    originalMatchMedia = window.matchMedia;
    // Mock window.alert to prevent alert from executing in tests
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

    // Click should alert on desktop
    termSpan.element.dispatchEvent(new MouseEvent("click", { bubbles: true }));
    await wrapper.vm.$nextTick();
    expect(window.alert).toHaveBeenCalledWith(
      "Glossary Definition:\n\nplacebo: Sample explanation of placebo"
    );
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
});
