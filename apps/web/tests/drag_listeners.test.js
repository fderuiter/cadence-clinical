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

    // Verify no window movement or release listeners exist when page loads
    const getActiveWindowListeners = (types) =>
      addedListeners.filter(
        (l) =>
          types.includes(l.type) &&
          !removedListeners.some(
            (r) => r.type === l.type && r.handler === l.handler
          )
      );

    expect(
      getActiveWindowListeners([
        "mousemove",
        "mouseup",
        "touchmove",
        "touchend",
      ])
    ).toHaveLength(0);

    // 3. Trigger mousedown to start dragging
    const mousedownEvent = new MouseEvent("mousedown", {
      button: 0,
      clientX: 10,
      clientY: 10,
    });
    mermaidWrapper.dispatchEvent(mousedownEvent);

    // Verify global window-level listeners are successfully attached
    const activeListenersAfterDown = getActiveWindowListeners([
      "mousemove",
      "mouseup",
      "touchmove",
      "touchend",
    ]);
    expect(activeListenersAfterDown).toHaveLength(4);
    expect(activeListenersAfterDown.map((l) => l.type)).toContain("mousemove");
    expect(activeListenersAfterDown.map((l) => l.type)).toContain("mouseup");
    expect(activeListenersAfterDown.map((l) => l.type)).toContain("touchmove");
    expect(activeListenersAfterDown.map((l) => l.type)).toContain("touchend");

    // 4. Trigger mouseup on window to release dragging
    const mouseupEvent = new MouseEvent("mouseup");
    window.dispatchEvent(mouseupEvent);

    // Verify all global window-level listeners are completely unregistered immediately
    const activeListenersAfterUp = getActiveWindowListeners([
      "mousemove",
      "mouseup",
      "touchmove",
      "touchend",
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
      getActiveWindowListeners([
        "mousemove",
        "mouseup",
        "touchmove",
        "touchend",
      ])
    ).toHaveLength(0);

    // Trigger touchstart to start touch dragging
    const touchstartEvent = new TouchEvent("touchstart", {
      touches: [{ clientX: 10, clientY: 10 }],
    });
    mermaidWrapper.dispatchEvent(touchstartEvent);

    // Verify global window-level listeners are successfully attached
    expect(
      getActiveWindowListeners([
        "mousemove",
        "mouseup",
        "touchmove",
        "touchend",
      ])
    ).toHaveLength(4);

    // Trigger touchend on window to release dragging
    const touchendEvent = new TouchEvent("touchend");
    window.dispatchEvent(touchendEvent);

    // Verify all global window-level listeners are completely unregistered immediately
    expect(
      getActiveWindowListeners([
        "mousemove",
        "mouseup",
        "touchmove",
        "touchend",
      ])
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
    const mousedownEvent = new MouseEvent("mousedown", {
      button: 0,
      clientX: 10,
      clientY: 10,
    });
    mermaidWrapper.dispatchEvent(mousedownEvent);

    const getActiveWindowListeners = (types) =>
      addedListeners.filter(
        (l) =>
          types.includes(l.type) &&
          !removedListeners.some(
            (r) => r.type === l.type && r.handler === l.handler
          )
      );

    expect(
      getActiveWindowListeners([
        "mousemove",
        "mouseup",
        "touchmove",
        "touchend",
      ])
    ).toHaveLength(4);

    // Unmount component
    wrapper.unmount();

    // Verify all global window-level listeners are completely cleaned up
    expect(
      getActiveWindowListeners([
        "mousemove",
        "mouseup",
        "touchmove",
        "touchend",
      ])
    ).toHaveLength(0);

    // Cleanup DOM
    const parentContainer = container.parentNode?.parentNode;
    if (parentContainer && parentContainer.parentNode) {
      parentContainer.parentNode.removeChild(parentContainer);
    }
  });
});
