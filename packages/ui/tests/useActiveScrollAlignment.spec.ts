// @vitest-environment jsdom
import { describe, it, expect, vi, afterEach } from "vitest";
import { createApp, defineComponent, h, nextTick, ref } from "vue";
import {
  alignActiveScrollItem,
  computeScrollOverflowState,
  prefersReducedMotion,
  useActiveScrollAlignment,
} from "../index.js";
import {
  alignActiveScrollItem as srcAlignActiveScrollItem,
  computeScrollOverflowState as srcComputeScrollOverflowState,
  prefersReducedMotion as srcPrefersReducedMotion,
  useActiveScrollAlignment as srcUseActiveScrollAlignment,
} from "../src/index.js";

function stubMatchMedia(reducedMotion: boolean) {
  Object.defineProperty(window, "matchMedia", {
    configurable: true,
    writable: true,
    value: vi.fn((query: string) => ({
      matches: reducedMotion && query === "(prefers-reduced-motion: reduce)",
      media: query,
      onchange: null,
      addListener: vi.fn(),
      removeListener: vi.fn(),
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
      dispatchEvent: vi.fn(),
    })),
  });
}

function mockHorizontalGeometry(
  container: HTMLElement,
  config: {
    clientWidth: number;
    scrollWidth: number;
    initialScrollLeft?: number;
    items: Array<{ el: HTMLElement; left: number; width: number }>;
  }
) {
  let scrollLeftValue = config.initialScrollLeft ?? 0;

  Object.defineProperty(container, "clientWidth", {
    configurable: true,
    get: () => config.clientWidth,
  });
  Object.defineProperty(container, "scrollWidth", {
    configurable: true,
    get: () => config.scrollWidth,
  });
  Object.defineProperty(container, "scrollLeft", {
    configurable: true,
    get: () => scrollLeftValue,
    set: (val: number) => {
      scrollLeftValue = val;
    },
  });

  const scrollToSpy = vi.fn(
    (arg: ScrollToOptions | number, maybeTop?: number) => {
      if (typeof arg === "number") {
        scrollLeftValue = arg;
      } else if (arg && typeof arg.left === "number") {
        scrollLeftValue = arg.left;
      }
      void maybeTop;
    }
  );
  container.scrollTo = scrollToSpy as unknown as typeof container.scrollTo;

  container.getBoundingClientRect = () =>
    ({
      left: 0,
      top: 0,
      right: config.clientWidth,
      bottom: 44,
      width: config.clientWidth,
      height: 44,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    }) as DOMRect;

  for (const item of config.items) {
    item.el.getBoundingClientRect = () => {
      const visibleLeft = item.left - scrollLeftValue;
      return {
        left: visibleLeft,
        top: 0,
        right: visibleLeft + item.width,
        bottom: 44,
        width: item.width,
        height: 44,
        x: visibleLeft,
        y: 0,
        toJSON: () => ({}),
      } as DOMRect;
    };
  }

  return { scrollToSpy };
}

describe("useActiveScrollAlignment", () => {
  afterEach(() => {
    vi.restoreAllMocks();
    vi.unstubAllGlobals();
  });

  it("re-exports scroll alignment helpers from both src/index.js and root index.js", () => {
    expect(useActiveScrollAlignment).toBe(srcUseActiveScrollAlignment);
    expect(alignActiveScrollItem).toBe(srcAlignActiveScrollItem);
    expect(computeScrollOverflowState).toBe(srcComputeScrollOverflowState);
    expect(prefersReducedMotion).toBe(srcPrefersReducedMotion);
  });

  it("computes start and end overflow states and clears them at scroll boundaries", () => {
    expect(computeScrollOverflowState(null)).toEqual({
      canScrollStart: false,
      canScrollEnd: false,
      scrollLeft: 0,
      maxScrollLeft: 0,
    });

    const container = document.createElement("div");
    mockHorizontalGeometry(container, {
      clientWidth: 320,
      scrollWidth: 720,
      initialScrollLeft: 0,
      items: [],
    });

    expect(computeScrollOverflowState(container)).toEqual({
      canScrollStart: false,
      canScrollEnd: true,
      scrollLeft: 0,
      maxScrollLeft: 400,
    });

    container.scrollLeft = 180;
    expect(computeScrollOverflowState(container)).toEqual({
      canScrollStart: true,
      canScrollEnd: true,
      scrollLeft: 180,
      maxScrollLeft: 400,
    });

    container.scrollLeft = 400;
    expect(computeScrollOverflowState(container)).toEqual({
      canScrollStart: true,
      canScrollEnd: false,
      scrollLeft: 400,
      maxScrollLeft: 400,
    });
  });

  it("centers off-screen active items and honors prefers-reduced-motion: reduce", () => {
    const container = document.createElement("div");
    const first = document.createElement("button");
    const active = document.createElement("button");
    const trailing = document.createElement("button");
    active.setAttribute("aria-selected", "true");
    container.append(first, active, trailing);

    const { scrollToSpy } = mockHorizontalGeometry(container, {
      clientWidth: 320,
      scrollWidth: 760,
      initialScrollLeft: 0,
      items: [
        { el: first, left: 8, width: 120 },
        { el: active, left: 420, width: 140 },
        { el: trailing, left: 600, width: 140 },
      ],
    });

    stubMatchMedia(true);
    expect(prefersReducedMotion()).toBe(true);

    const result = alignActiveScrollItem(container);
    expect(result.aligned).toBe(true);
    expect(result.behavior).toBe("auto");
    expect(scrollToSpy).toHaveBeenCalledWith({
      left: 330,
      behavior: "auto",
    });
    expect(container.scrollLeft).toBe(330);
    expect(result.canScrollStart).toBe(true);
    expect(result.canScrollEnd).toBe(true);
  });

  it("uses smooth behavior on interactive updates when reduced motion is not requested", () => {
    const container = document.createElement("div");
    const first = document.createElement("a");
    const active = document.createElement("a");
    active.setAttribute("aria-current", "page");
    container.append(first, active);

    const { scrollToSpy } = mockHorizontalGeometry(container, {
      clientWidth: 320,
      scrollWidth: 680,
      initialScrollLeft: 0,
      items: [
        { el: first, left: 0, width: 100 },
        { el: active, left: 480, width: 120 },
      ],
    });

    stubMatchMedia(false);
    expect(prefersReducedMotion()).toBe(false);

    const result = alignActiveScrollItem(container, { immediate: false });
    expect(result.aligned).toBe(true);
    expect(result.behavior).toBe("smooth");
    expect(scrollToSpy).toHaveBeenCalledWith({
      left: 360,
      behavior: "smooth",
    });
  });

  it("aligns on mount, activeKey updates, scroll events, and ResizeObserver callbacks", async () => {
    let resizeCallback: (() => void) | null = null;
    const disconnectSpy = vi.fn();
    class ResizeObserverMock {
      constructor(cb: () => void) {
        resizeCallback = cb;
      }
      observe() {}
      disconnect() {
        disconnectSpy();
      }
    }
    vi.stubGlobal("ResizeObserver", ResizeObserverMock);
    stubMatchMedia(false);

    const activeIndex = ref(2);
    let exposedState: ReturnType<typeof useActiveScrollAlignment> | null = null;

    const Harness = defineComponent({
      setup() {
        const listRef = ref<HTMLElement | null>(null);
        exposedState = useActiveScrollAlignment(listRef, {
          activeKey: () => activeIndex.value,
        });
        return () =>
          h(
            "div",
            { ref: listRef, class: "scroll-strip" },
            [0, 1, 2].map((idx) =>
              h(
                "button",
                {
                  key: idx,
                  "aria-selected": activeIndex.value === idx ? "true" : "false",
                },
                `Tab ${idx}`
              )
            )
          );
      },
    });

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(Harness);
    app.mount(host);
    await nextTick();

    const strip = host.querySelector(".scroll-strip") as HTMLElement;
    const buttons = Array.from(strip.querySelectorAll("button"));
    let dynamicClientWidth = 300;
    const { scrollToSpy } = mockHorizontalGeometry(strip, {
      clientWidth: dynamicClientWidth,
      scrollWidth: 750,
      initialScrollLeft: 0,
      items: [
        { el: buttons[0], left: 0, width: 120 },
        { el: buttons[1], left: 260, width: 120 },
        { el: buttons[2], left: 560, width: 140 },
      ],
    });
    Object.defineProperty(strip, "clientWidth", {
      configurable: true,
      get: () => dynamicClientWidth,
    });

    // Trigger resize observer after geometry mock is wired
    resizeCallback?.();
    expect(strip.scrollLeft).toBe(450);
    expect(exposedState?.canScrollStart.value).toBe(true);
    expect(exposedState?.canScrollEnd.value).toBe(false);

    // Switch active item back to first button
    activeIndex.value = 0;
    await nextTick();
    await nextTick();
    expect(strip.scrollLeft).toBe(0);
    expect(exposedState?.canScrollStart.value).toBe(false);
    expect(exposedState?.canScrollEnd.value).toBe(true);

    // Simulate manual scroll to end boundary
    strip.scrollLeft = 450;
    strip.dispatchEvent(new Event("scroll"));
    expect(exposedState?.canScrollStart.value).toBe(true);
    expect(exposedState?.canScrollEnd.value).toBe(false);

    // Simulate container widening so content no longer overflows
    dynamicClientWidth = 800;
    strip.scrollLeft = 0;
    resizeCallback?.();
    expect(exposedState?.canScrollStart.value).toBe(false);
    expect(exposedState?.canScrollEnd.value).toBe(false);
    expect(scrollToSpy).toHaveBeenCalled();

    app.unmount();
    expect(disconnectSpy).toHaveBeenCalled();
    host.remove();
  });
});
