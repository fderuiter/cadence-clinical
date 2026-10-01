import { describe, it, expect, vi } from "vitest";
import { mount } from "@vue/test-utils";
import EcrfHotkeyOverlay from "../../src/components/EcrfHotkeyOverlay.vue";

describe("EcrfHotkeyOverlay.vue", () => {
  it("renders hotkey cheat sheet modal overlay with keybindings", () => {
    const wrapper = mount(EcrfHotkeyOverlay, {
      props: { show: true },
    });

    expect(wrapper.text()).toContain("eCRF Keyboard Shortcuts & Hotkeys");
    expect(wrapper.text()).toContain("Enter");
    expect(wrapper.text()).toContain("Ctrl+S");
    expect(wrapper.text()).toContain("Alt+Q");
    expect(wrapper.text()).toContain("Alt+N");
    expect(wrapper.text()).toContain("Alt+P");
    expect(wrapper.text()).toContain("Alt+V");
    expect(wrapper.text()).toContain("?");
  });

  it("emits close event when close button is clicked", async () => {
    const wrapper = mount(EcrfHotkeyOverlay, {
      props: { show: true },
    });

    const closeBtn = wrapper.find(".btn-close-hotkeys");
    expect(closeBtn.exists()).toBe(true);

    await closeBtn.trigger("click");
    expect(wrapper.emitted("close")).toBeTruthy();
  });
});
