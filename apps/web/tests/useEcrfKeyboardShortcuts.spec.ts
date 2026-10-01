import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount, enableAutoUnmount } from "@vue/test-utils";
import { defineComponent, ref } from "vue";
import { useEcrfKeyboardShortcuts } from "../src/composables/useEcrfKeyboardShortcuts";

enableAutoUnmount(afterEach);

describe("useEcrfKeyboardShortcuts Composable", () => {
  let container: HTMLDivElement;

  beforeEach(() => {
    container = document.createElement("div");
    container.id = "app-test-container";
    document.body.appendChild(container);
  });

  afterEach(() => {
    (document.activeElement as HTMLElement)?.blur();
    document.body.innerHTML = "";
    vi.restoreAllMocks();
  });

  it("attaches keydown listener on mount and detaches on unmount", () => {
    const addSpy = vi.spyOn(window, "addEventListener");
    const removeSpy = vi.spyOn(window, "removeEventListener");

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts();
        return {};
      },
      template: "<div>Test</div>",
    });

    const wrapper = mount(TestComponent, { attachTo: container });
    expect(addSpy).toHaveBeenCalledWith("keydown", expect.any(Function));

    wrapper.unmount();
    expect(removeSpy).toHaveBeenCalledWith("keydown", expect.any(Function));
  });

  it("triggers eCRF submit on Ctrl+S, Cmd+S, and Alt+S", () => {
    const onSubmit = vi.fn();

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts({ onSubmit });
        return {};
      },
      template: "<div>Form Component</div>",
    });

    mount(TestComponent, { attachTo: container });

    // 1. Ctrl+S
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "s", ctrlKey: true, bubbles: true })
    );
    expect(onSubmit).toHaveBeenCalledTimes(1);

    // 2. Cmd+S (Meta+S)
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "s", metaKey: true, bubbles: true })
    );
    expect(onSubmit).toHaveBeenCalledTimes(2);

    // 3. Alt+S
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "s", altKey: true, bubbles: true })
    );
    expect(onSubmit).toHaveBeenCalledTimes(3);
  });

  it("advances focus to the next visible field on Enter key", async () => {
    container.innerHTML = `
      <form id="test-form">
        <input id="field1" type="text" />
        <input id="field2" type="number" />
        <textarea id="field3"></textarea>
        <input id="field4" type="text" />
      </form>
    `;

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts();
        return {};
      },
      template: "<div>Form Test</div>",
    });

    mount(TestComponent, { attachTo: container });

    const input1 = document.getElementById("field1") as HTMLInputElement;
    const input2 = document.getElementById("field2") as HTMLInputElement;
    const textarea = document.getElementById("field3") as HTMLTextAreaElement;

    input1.focus();
    expect(document.activeElement).toBe(input1);

    // Press Enter in input1 -> Should advance to input2
    const enterEvent = new KeyboardEvent("keydown", { key: "Enter", bubbles: true });
    window.dispatchEvent(enterEvent);
    expect(document.activeElement).toBe(input2);

    // Press Enter in textarea -> Should NOT advance (preserve text multiline behavior)
    textarea.focus();
    expect(document.activeElement).toBe(textarea);
    window.dispatchEvent(new KeyboardEvent("keydown", { key: "Enter", bubbles: true }));
    expect(document.activeElement).toBe(textarea);
  });

  it("toggles query drawer for focused field on Alt+Q", () => {
    container.innerHTML = `
      <div id="field-container-vssbp" class="clinical-form-field-wrapper">
        <input id="vssbp" type="text" />
        <button id="query-flag-vssbp" class="query-flag" type="button">💬</button>
      </div>
    `;

    const queryFlag = document.getElementById("query-flag-vssbp") as HTMLButtonElement;
    const clickSpy = vi.spyOn(queryFlag, "click");

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts();
        return {};
      },
      template: "<div>Form Test</div>",
    });

    mount(TestComponent, { attachTo: container });

    const input = document.getElementById("vssbp") as HTMLInputElement;
    input.focus();

    // Press Alt+Q
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "q", altKey: true, bubbles: true })
    );

    expect(clickSpy).toHaveBeenCalled();
  });

  it("navigates active query fields on Alt+N and Alt+P", () => {
    container.innerHTML = `
      <div>
        <input id="f1" type="text" />
        <button id="query-flag-f1" class="query-flag query-status-open" type="button">⚠️</button>
      </div>
      <div>
        <input id="f2" type="text" />
        <button id="query-flag-f2" class="query-flag query-status-none" type="button">💬</button>
      </div>
      <div>
        <input id="f3" type="text" />
        <button id="query-flag-f3" class="query-flag query-status-answered" type="button">⚠️</button>
      </div>
    `;

    const input1 = document.getElementById("f1") as HTMLInputElement;
    const input3 = document.getElementById("f3") as HTMLInputElement;

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts();
        return {};
      },
      template: "<div>Form Test</div>",
    });

    mount(TestComponent, { attachTo: container });

    input1.focus();

    // Alt+N should jump to next field with active query (f3)
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "n", altKey: true, bubbles: true })
    );
    expect(document.activeElement).toBe(input3);

    // Alt+P should jump back to previous field with active query (f1)
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "p", altKey: true, bubbles: true })
    );
    expect(document.activeElement).toBe(input1);
  });

  it("toggles SDV status on Alt+V for CRA Monitor users and ignores for Site Coordinators", () => {
    container.innerHTML = `
      <div id="field-container-vssbp">
        <input id="vssbp" type="text" />
        <div class="sdv-box">
          <input id="sdv-vssbp" type="checkbox" />
        </div>
      </div>
    `;

    const sdvCheckbox = document.getElementById("sdv-vssbp") as HTMLInputElement;
    const clickSpy = vi.spyOn(sdvCheckbox, "click");

    const isCra = ref(false);

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts({
          isCraUser: () => isCra.value,
        });
        return {};
      },
      template: "<div>Form Test</div>",
    });

    mount(TestComponent, { attachTo: container });

    const input = document.getElementById("vssbp") as HTMLInputElement;
    input.focus();

    // 1. As CRC / Site Coordinator (isCra = false) -> Alt+V should NOT trigger SDV checkbox
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "v", altKey: true, bubbles: true })
    );
    expect(clickSpy).not.toHaveBeenCalled();

    // 2. Switch to CRA Monitor role (isCra = true) -> Alt+V triggers SDV checkbox
    isCra.value = true;
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "v", altKey: true, bubbles: true })
    );
    expect(clickSpy).toHaveBeenCalledTimes(1);
  });

  it("opens hotkey overlay on ? or Ctrl+/ and closes on Esc", () => {
    let composableInstance: any;

    const TestComponent = defineComponent({
      setup() {
        composableInstance = useEcrfKeyboardShortcuts();
        return { showOverlay: composableInstance.showHotkeyOverlay };
      },
      template: "<div>Form Test</div>",
    });

    mount(TestComponent, { attachTo: container });

    expect(composableInstance.showHotkeyOverlay.value).toBe(false);

    // Press ? -> Opens overlay
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "?", bubbles: true })
    );
    expect(composableInstance.showHotkeyOverlay.value).toBe(true);

    // Press Escape -> Closes overlay
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
    );
    expect(composableInstance.showHotkeyOverlay.value).toBe(false);

    // Press Ctrl+/ -> Opens overlay
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "/", ctrlKey: true, bubbles: true })
    );
    expect(composableInstance.showHotkeyOverlay.value).toBe(true);
  });

  it("pauses form shortcuts when a modal dialog is active", () => {
    const onSubmit = vi.fn();
    const isModalActive = ref(true);

    const TestComponent = defineComponent({
      setup() {
        useEcrfKeyboardShortcuts({
          onSubmit,
          isModalActive: () => isModalActive.value,
        });
        return {};
      },
      template: "<div>Form Test</div>",
    });

    mount(TestComponent, { attachTo: container });

    // Press Ctrl+S while modal is active -> Should NOT submit
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "s", ctrlKey: true, bubbles: true })
    );
    expect(onSubmit).not.toHaveBeenCalled();

    // Close modal -> Shortcuts active again
    isModalActive.value = false;
    window.dispatchEvent(
      new KeyboardEvent("keydown", { key: "s", ctrlKey: true, bubbles: true })
    );
    expect(onSubmit).toHaveBeenCalledTimes(1);
  });
});
