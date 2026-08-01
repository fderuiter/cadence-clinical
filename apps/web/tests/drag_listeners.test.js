import { describe, it, expect, vi, beforeEach, afterEach } from "vitest";
import { mount } from "@vue/test-utils";
import { defineComponent } from "vue";
import theme from "../../../docs/.vitepress/theme/index.js";

vi.mock("vitepress/theme", () => ({
  default: {},
}));

const mockRoute = { path: "/test-path" };
vi.mock("vitepress", () => ({
  useRoute: () => mockRoute,
}));

describe("Transient Drag-Scoped Window Listeners", () => {
  let originalAddEventListener;
  let originalRemoveEventListener;
  let addedListeners = [];
  let removedListeners = [];

  beforeEach(() => {
    addedListeners = [];
    removedListeners = [];

    originalAddEventListener = window.addEventListener;
    originalRemoveEventListener = window.removeEventListener;

    window.addEventListener = vi.fn((type, handler, options) => {
      addedListeners.push({ type, handler, options });
      originalAddEventListener(type, handler, options);
    });

    window.removeEventListener = vi.fn((type, handler, options) => {
      removedListeners.push({ type, handler, options });
      originalRemoveEventListener(type, handler, options);
    });
  });

  afterEach(() => {
    window.addEventListener = originalAddEventListener;
    window.removeEventListener = originalRemoveEventListener;
  });

  it("registers window listeners only during active dragging and removes them on release", async () => {
    // 1. Setup a container with class 'mermaid' and an SVG inside it
    const container = document.createElement("div");
    container.className = "mermaid";
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    container.appendChild(svg);
    document.body.appendChild(container);

    // 2. Mount a component that calls setup() of the theme
    const TestComponent = defineComponent({
      setup() {
        theme.setup();
        return {};
      },
      template: "<div></div>",
    });

    vi.useFakeTimers();
    mount(TestComponent);

    // Wait for setTimeout in initPanZoom
    vi.advanceTimersByTime(300);
    vi.useRealTimers();

    // Verify SVG is enhanced and mermaid-wrapper is created
    const mermaidWrapper = document.querySelector(".mermaid-wrapper");
    expect(mermaidWrapper).not.toBeNull();

    const getActiveWindowListeners = (types) =>
      addedListeners.filter(
        (l) =>
          types.includes(l.type) &&
          !removedListeners.some(
            (r) => r.type === l.type && r.handler === l.handler
          )
      );

    expect(
      getActiveWindowListeners(["pointermove", "pointerup", "pointercancel"])
    ).toHaveLength(0);

    // 3. Trigger pointerdown to start dragging
    const pointerdownEvent = new PointerEvent("pointerdown", {
      button: 0,
      clientX: 10,
      clientY: 10,
      pointerId: 1,
      isPrimary: true,
    });
    mermaidWrapper.dispatchEvent(pointerdownEvent);

    // Verify global window-level listeners are successfully attached
    const activeListenersAfterDown = getActiveWindowListeners([
      "pointermove",
      "pointerup",
      "pointercancel",
    ]);
    expect(activeListenersAfterDown).toHaveLength(3);
    expect(activeListenersAfterDown.map((l) => l.type)).toContain(
      "pointermove"
    );
    expect(activeListenersAfterDown.map((l) => l.type)).toContain("pointerup");
    expect(activeListenersAfterDown.map((l) => l.type)).toContain(
      "pointercancel"
    );

    // 4. Trigger pointerup on window to release dragging
    const pointerupEvent = new PointerEvent("pointerup", { pointerId: 1 });
    window.dispatchEvent(pointerupEvent);

    // Verify all global window-level listeners are completely unregistered immediately
    const activeListenersAfterUp = getActiveWindowListeners([
      "pointermove",
      "pointerup",
      "pointercancel",
    ]);
    expect(activeListenersAfterUp).toHaveLength(0);

    // Cleanup DOM
    const parentContainer = container.parentNode?.parentNode;
    if (parentContainer && parentContainer.parentNode) {
      parentContainer.parentNode.removeChild(parentContainer);
    }
  });

  it("registers window listeners during touch dragging and removes them on release", async () => {
    const container = document.createElement("div");
    container.className = "mermaid";
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    container.appendChild(svg);
    document.body.appendChild(container);

    const TestComponent = defineComponent({
      setup() {
        theme.setup();
        return {};
      },
      template: "<div></div>",
    });

    vi.useFakeTimers();
    mount(TestComponent);

    vi.advanceTimersByTime(300);
    vi.useRealTimers();

    const mermaidWrapper = document.querySelector(".mermaid-wrapper");

    const getActiveWindowListeners = (types) =>
      addedListeners.filter(
        (l) =>
          types.includes(l.type) &&
          !removedListeners.some(
            (r) => r.type === l.type && r.handler === l.handler
          )
      );

    expect(
      getActiveWindowListeners(["pointermove", "pointerup", "pointercancel"])
    ).toHaveLength(0);

    // Trigger pointerdown to start touch dragging
    const pointerdownEvent = new PointerEvent("pointerdown", {
      clientX: 10,
      clientY: 10,
      pointerId: 1,
      pointerType: "touch",
    });
    mermaidWrapper.dispatchEvent(pointerdownEvent);

    // Verify global window-level listeners are successfully attached
    expect(
      getActiveWindowListeners(["pointermove", "pointerup", "pointercancel"])
    ).toHaveLength(3);

    // Trigger pointerup on window to release dragging
    const pointerupEvent = new PointerEvent("pointerup", { pointerId: 1 });
    window.dispatchEvent(pointerupEvent);

    // Verify all global window-level listeners are completely unregistered immediately
    expect(
      getActiveWindowListeners(["pointermove", "pointerup", "pointercancel"])
    ).toHaveLength(0);

    // Cleanup DOM
    const parentContainer = container.parentNode?.parentNode;
    if (parentContainer && parentContainer.parentNode) {
      parentContainer.parentNode.removeChild(parentContainer);
    }
  });

  it("removes all active listeners when unmounted mid-drag", async () => {
    const container = document.createElement("div");
    container.className = "mermaid";
    const svg = document.createElementNS("http://www.w3.org/2000/svg", "svg");
    container.appendChild(svg);
    document.body.appendChild(container);

    const TestComponent = defineComponent({
      setup() {
        theme.setup();
        return {};
      },
      template: "<div></div>",
    });

    vi.useFakeTimers();
    const wrapper = mount(TestComponent);

    vi.advanceTimersByTime(300);
    vi.useRealTimers();

    const mermaidWrapper = document.querySelector(".mermaid-wrapper");
    const pointerdownEvent = new PointerEvent("pointerdown", {
      button: 0,
      clientX: 10,
      clientY: 10,
      pointerId: 1,
    });
    mermaidWrapper.dispatchEvent(pointerdownEvent);

    const getActiveWindowListeners = (types) =>
      addedListeners.filter(
        (l) =>
          types.includes(l.type) &&
          !removedListeners.some(
            (r) => r.type === l.type && r.handler === l.handler
          )
      );

    expect(
      getActiveWindowListeners(["pointermove", "pointerup", "pointercancel"])
    ).toHaveLength(3);

    // Unmount component
    wrapper.unmount();

    // Verify all global window-level listeners are completely cleaned up
    expect(
      getActiveWindowListeners(["pointermove", "pointerup", "pointercancel"])
    ).toHaveLength(0);

    // Cleanup DOM
    const parentContainer = container.parentNode?.parentNode;
    if (parentContainer && parentContainer.parentNode) {
      parentContainer.parentNode.removeChild(parentContainer);
    }
  });
});
