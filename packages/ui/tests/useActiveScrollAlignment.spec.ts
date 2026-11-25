// @vitest-environment jsdom
import { describe, expect, it, vi, afterEach } from "vitest";
import { createApp, defineComponent, h, nextTick, ref } from "vue";
import {
  alignActiveScrollItem,
  computeScrollOverflowState,
  prefersReducedMotion,
  useActiveScrollAlignment,
} from "../index.js";

function mockHorizontalLayout(
  container: HTMLElement,
  {
    clientWidth,
    scrollWidth,
    initialScrollLeft = 0,
    itemRects,
  }: {
    clientWidth: number;
    scrollWidth: number;
    initialScrollLeft?: number;
    itemRects: Array<{ el: HTMLElement; left: number; width: number }>;
  }
) {
  let scrollLeftValue = initialScrollLeft;

  Object.defineProperty(container, "clientWidth", {
    configurable: true,
    get: () => clientWidth,
  });
  Object.defineProperty(container, "scrollWidth", {
    configurable: true,
    get: () => scrollWidth,
  });
  Object.defineProperty(container, "scrollLeft", {
    configurable: true,
    get: () => scrollLeftValue,
    set: (val: number) => {
      scrollLeftValue = Math.max(
        0,
        Math.min(val, container.scrollWidth - container.clientWidth)
      );
    },
  });

  container.getBoundingClientRect = () =>
    ({
      left: 0,
      right: clientWidth,
      top: 0,
      bottom: 44,
      width: clientWidth,
      height: 44,
      x: 0,
      y: 0,
      toJSON: () => ({}),
    }) as DOMRect;

  const scrollToSpy = vi.fn(
    (opts?: ScrollToOptions | number, y?: number): void => {
      if (typeof opts === "object" && opts !== null) {
        if (typeof opts.left === "number") {
          container.scrollLeft = opts.left;
        }
      } else if (typeof opts === "number") {
        void y;
        container.scrollLeft = opts;
      }
      container.dispatchEvent(new Event("scroll"));
    }
  );
  container.scrollTo = scrollToSpy as unknown as typeof container.scrollTo;

  for (const item of itemRects) {
    item.el.getBoundingClientRect = () =>
      ({
        left: item.left - scrollLeftValue,
        right: item.left - scrollLeftValue + item.width,
        top: 0,
        bottom: 44,
        width: item.width,
        height: 44,
        x: item.left - scrollLeftValue,
        y: 0,
        toJSON: () => ({}),
      }) as DOMRect;
  }

  return { scrollToSpy };
}

describe("useActiveScrollAlignment", () => {
  const originalMatchMedia = window.matchMedia;
  const originalResizeObserver = globalThis.ResizeObserver;

  afterEach(() => {
    window.matchMedia = originalMatchMedia;
    globalThis.ResizeObserver = originalResizeObserver;
    vi.restoreAllMocks();
  });

  it("computes start/end overflow boundaries accurately and suppresses cues when content fits", () => {
    expect(computeScrollOverflowState(null)).toEqual({
      canScrollStart: false,
      canScrollEnd: false,
      scrollLeft: 0,
      maxScrollLeft: 0,
    });

    const fitEl = document.createElement("div");
    Object.defineProperty(fitEl, "clientWidth", { value: 320 });
    Object.defineProperty(fitEl, "scrollWidth", { value: 320 });
    expect(computeScrollOverflowState(fitEl)).toEqual({
      canScrollStart: false,
      canScrollEnd: false,
      scrollLeft: 0,
      maxScrollLeft: 0,
    });

    const overflowEl = document.createElement("div");
    Object.defineProperty(overflowEl, "clientWidth", { value: 320 });
    Object.defineProperty(overflowEl, "scrollWidth", { value: 620 });
    overflowEl.scrollLeft = 0;
    expect(computeScrollOverflowState(overflowEl)).toEqual({
      canScrollStart: false,
      canScrollEnd: true,
      scrollLeft: 0,
      maxScrollLeft: 300,
    });

    overflowEl.scrollLeft = 140;
    expect(computeScrollOverflowState(overflowEl)).toEqual({
      canScrollStart: true,
      canScrollEnd: true,
      scrollLeft: 140,
      maxScrollLeft: 300,
    });

    overflowEl.scrollLeft = 300;
    expect(computeScrollOverflowState(overflowEl)).toEqual({
      canScrollStart: true,
      canScrollEnd: false,
      scrollLeft: 300,
      maxScrollLeft: 300,
    });
  });

  it("snaps first item to 0, centers intermediate items, snaps last item to maxScrollLeft, and respects prefers-reduced-motion", () => {
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));
    expect(prefersReducedMotion()).toBe(false);

    const list = document.createElement("ul");
    const items = [0, 1, 2, 3, 4].map((idx) => {
      const li = document.createElement("li");
      const btn = document.createElement("button");
      btn.setAttribute("role", "tab");
      btn.setAttribute("aria-selected", idx === 0 ? "true" : "false");
      li.appendChild(btn);
      list.appendChild(li);
      return { li, btn, left: idx * 120, width: 110 };
    });

    const { scrollToSpy } = mockHorizontalLayout(list, {
      clientWidth: 300,
      scrollWidth: 600,
      initialScrollLeft: 180,
      itemRects: items.flatMap((it) => [
        { el: it.li, left: it.left, width: it.width },
        { el: it.btn, left: it.left, width: it.width },
      ]),
    });

    // 1. First item active with immediate: true -> snaps to 0 with behavior: "auto"
    const firstTarget = alignActiveScrollItem(list, { immediate: true });
    expect(firstTarget).toBe(0);
    expect(scrollToSpy).toHaveBeenLastCalledWith({
      left: 0,
      behavior: "auto",
    });

    // 2. Intermediate item (#2 at left=240, width=110 -> center=295 -> 295 - 150 = 145) -> smooth scroll
    items[0].btn.setAttribute("aria-selected", "false");
    items[2].btn.setAttribute("aria-selected", "true");
    const midTarget = alignActiveScrollItem(list, { immediate: false });
    expect(midTarget).toBe(145);
    expect(scrollToSpy).toHaveBeenLastCalledWith({
      left: 145,
      behavior: "smooth",
    });

    // 3. Last item (#4 at left=480, width=110) -> snaps to maxScrollLeft (300)
    items[2].btn.setAttribute("aria-selected", "false");
    items[4].btn.setAttribute("aria-selected", "true");
    const lastTarget = alignActiveScrollItem(list, { immediate: false });
    expect(lastTarget).toBe(300);
    expect(scrollToSpy).toHaveBeenLastCalledWith({
      left: 300,
      behavior: "smooth",
    });

    // 4. prefers-reduced-motion: reduce forces behavior: "auto" even when immediate is false
    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: query.includes("prefers-reduced-motion"),
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));
    expect(prefersReducedMotion()).toBe(true);

    items[4].btn.setAttribute("aria-selected", "false");
    items[1].btn.setAttribute("aria-selected", "true");
    const reducedTarget = alignActiveScrollItem(list, { immediate: false });
    expect(reducedTarget).toBe(25);
    expect(scrollToSpy).toHaveBeenLastCalledWith({
      left: 25,
      behavior: "auto",
    });
  });

  it("reactively aligns active item on mount, activeKey changes, and ResizeObserver child/container updates", async () => {
    let resizeCallback = null as (() => void) | null;
    const observedElements: Element[] = [];

    globalThis.ResizeObserver = class MockResizeObserver {
      constructor(cb: ResizeObserverCallback) {
        resizeCallback = () => cb([], this as unknown as ResizeObserver);
      }
      observe(el: Element) {
        observedElements.push(el);
      }
      unobserve() {}
      disconnect() {
        observedElements.length = 0;
      }
    } as unknown as typeof ResizeObserver;

    window.matchMedia = vi.fn().mockImplementation((query: string) => ({
      matches: false,
      media: query,
      addEventListener: vi.fn(),
      removeEventListener: vi.fn(),
    }));

    const activeId = ref("tab-4");
    const tabIds = ["tab-0", "tab-1", "tab-2", "tab-3", "tab-4"];

    let exposed: ReturnType<typeof useActiveScrollAlignment> | null = null;
    const Harness = defineComponent({
      setup() {
        const listRef = ref<HTMLElement | null>(null);
        const alignment = useActiveScrollAlignment(listRef, {
          activeKey: activeId,
        });
        exposed = alignment;
        return () =>
          h(
            "div",
            {
              "data-overflow-start": alignment.canScrollStart.value
                ? "true"
                : "false",
              "data-overflow-end": alignment.canScrollEnd.value
                ? "true"
                : "false",
            },
            [
              h(
                "ul",
                { ref: listRef, class: "test-scroll-list" },
                tabIds.map((id) =>
                  h("li", { key: id }, [
                    h(
                      "button",
                      {
                        type: "button",
                        role: "tab",
                        "aria-selected":
                          activeId.value === id ? "true" : "false",
                      },
                      id
                    ),
                  ])
                )
              ),
            ]
          );
      },
    });

    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp(Harness);
    app.mount(host);

    const ul = host.querySelector(".test-scroll-list") as HTMLElement;
    let currentScrollWidth = 600;
    const itemEntries = Array.from(ul.querySelectorAll("li")).map(
      (li, idx) => ({
        li,
        btn: li.querySelector("button") as HTMLElement,
        left: idx * 120,
        width: 110,
      })
    );

    const { scrollToSpy } = mockHorizontalLayout(ul, {
      clientWidth: 300,
      scrollWidth: currentScrollWidth,
      initialScrollLeft: 0,
      itemRects: itemEntries.flatMap((it) => [
        Object.defineProperties(
          { el: it.li, left: 0, width: 0 },
          {
            left: { get: () => it.left },
            width: { get: () => it.width },
          }
        ),
        Object.defineProperties(
          { el: it.btn, left: 0, width: 0 },
          {
            left: { get: () => it.left },
            width: { get: () => it.width },
          }
        ),
      ]),
    });

    await nextTick();
    // Initial mount aligns immediately to tab-4 (maxScrollLeft = 300) with behavior: "auto"
    expect(ul.scrollLeft).toBe(300);
    expect(scrollToSpy).toHaveBeenCalledWith({ left: 300, behavior: "auto" });
    expect(exposed!.canScrollStart.value).toBe(true);
    expect(exposed!.canScrollEnd.value).toBe(false);
    // Observes both the scroll container and its child items (for 200% rem font-scaling)
    expect(observedElements.length).toBeGreaterThanOrEqual(6);

    // Switch active tab to tab-2 -> smooth centers at 145 with both cues active
    activeId.value = "tab-2";
    await nextTick();
    await nextTick();
    expect(ul.scrollLeft).toBe(145);
    expect(scrollToSpy).toHaveBeenLastCalledWith({
      left: 145,
      behavior: "smooth",
    });
    expect(exposed!.canScrollStart.value).toBe(true);
    expect(exposed!.canScrollEnd.value).toBe(true);

    // Simulate 200% font-scaling increasing scrollWidth via ResizeObserver without changing outer clientWidth
    currentScrollWidth = 800;
    Object.defineProperty(ul, "scrollWidth", {
      configurable: true,
      get: () => currentScrollWidth,
    });
    itemEntries[2].left = 360;
    itemEntries[2].width = 160;
    resizeCallback?.();
    expect(ul.scrollLeft).toBe(290);

    app.unmount();
    host.remove();
  });
});
