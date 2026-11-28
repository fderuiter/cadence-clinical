// @vitest-environment jsdom
import fs from "node:fs";
import path from "node:path";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, h, nextTick, ref } from "vue";
import {
  ClinicalIcon,
  ClinicalPopover,
  ClinicalToastHost,
  computeFloatingPosition,
  getVisualViewportBounds,
  observeVisualViewport,
  useViewportMaxHeight,
} from "../index.js";

describe("Floating primitives, ClinicalPopover clamping, and ClinicalToastHost", () => {
  let originalInnerWidth;
  let originalInnerHeight;
  let originalVisualViewport;
  const mountedApps = [];

  beforeEach(() => {
    originalInnerWidth = window.innerWidth;
    originalInnerHeight = window.innerHeight;
    originalVisualViewport = window.visualViewport;
  });

  afterEach(() => {
    while (mountedApps.length > 0) {
      const { app, host } = mountedApps.pop();
      app.unmount();
      host.remove();
    }
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
    vi.restoreAllMocks();
  });

  function mountHost(setupFn) {
    const host = document.createElement("div");
    document.body.appendChild(host);
    const app = createApp({
      setup: setupFn,
    });
    app.mount(host);
    mountedApps.push({ app, host });
    return host;
  }

  function mockViewport(width, height, offsetLeft = 0, offsetTop = 0) {
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: width,
    });
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: height,
    });
    Object.defineProperty(document.documentElement, "clientWidth", {
      configurable: true,
      value: width,
    });
    Object.defineProperty(document.documentElement, "clientHeight", {
      configurable: true,
      value: height,
    });
    Object.defineProperty(window, "visualViewport", {
      configurable: true,
      writable: true,
      value: {
        width,
        height,
        offsetLeft,
        offsetTop,
        addEventListener: vi.fn(),
        removeEventListener: vi.fn(),
      },
    });
  }

  it("clamps floating panels within 12px of visualViewport edges (including zoomed/panned offsets) and flips when bottom space is insufficient", () => {
    mockViewport(320, 256);
    const vp = getVisualViewportBounds();
    expect(vp).toEqual({
      left: 0,
      top: 0,
      width: 320,
      height: 256,
      right: 320,
      bottom: 256,
    });

    // Left-aligned trigger with align="end" and 280px menu width must clamp left >= 12px
    const leftEdgePos = computeFloatingPosition({
      anchorRect: {
        left: 0,
        top: 8,
        right: 140,
        bottom: 40,
        width: 140,
        height: 32,
      },
      floatingSize: { width: 280, height: 320 },
      viewport: vp,
      placement: "bottom",
      align: "end",
      offset: 4,
      margin: 12,
    });

    expect(leftEdgePos.left).toBeGreaterThanOrEqual(12);
    expect(
      leftEdgePos.left + Math.min(280, leftEdgePos.maxWidth)
    ).toBeLessThanOrEqual(320 - 12);
    expect(leftEdgePos.top + leftEdgePos.maxHeight).toBeLessThanOrEqual(
      256 - 12
    );

    // Zoomed/panned visualViewport with non-zero offsetLeft/offsetTop clamps inside visible window
    mockViewport(320, 256, 48, 64);
    const zoomedVp = getVisualViewportBounds();
    expect(zoomedVp).toEqual({
      left: 48,
      top: 64,
      width: 320,
      height: 256,
      right: 368,
      bottom: 320,
    });
    const zoomedPos = computeFloatingPosition({
      anchorRect: {
        left: 20,
        top: 40,
        right: 120,
        bottom: 68,
        width: 100,
        height: 28,
      },
      floatingSize: { width: 260, height: 180 },
      viewport: zoomedVp,
      placement: "bottom",
      align: "start",
      offset: 6,
      margin: 12,
    });
    expect(zoomedPos.left).toBeGreaterThanOrEqual(48 + 12);
    expect(zoomedPos.left + 260).toBeLessThanOrEqual(368 - 12);
    expect(zoomedPos.top).toBeGreaterThanOrEqual(64 + 12);
    expect(zoomedPos.top + zoomedPos.maxHeight).toBeLessThanOrEqual(320 - 12);

    // Center alignment and right-edge glossary trigger at x=265 on 320x640 must clamp right <= 308px
    const centerPos = computeFloatingPosition({
      anchorRect: {
        left: 120,
        top: 80,
        right: 200,
        bottom: 112,
        width: 80,
        height: 32,
      },
      floatingSize: { width: 200, height: 100 },
      viewport: {
        left: 0,
        top: 0,
        width: 320,
        height: 640,
        right: 320,
        bottom: 640,
      },
      placement: "bottom",
      align: "center",
      offset: 6,
      margin: 12,
    });
    expect(centerPos.left).toBe(60);

    const rightEdgePos = computeFloatingPosition({
      anchorRect: {
        left: 265,
        top: 120,
        right: 312,
        bottom: 142,
        width: 47,
        height: 22,
      },
      floatingSize: { width: 280, height: 90 },
      viewport: {
        left: 0,
        top: 0,
        width: 320,
        height: 640,
        right: 320,
        bottom: 640,
      },
      placement: "bottom",
      align: "start",
      offset: 6,
      margin: 12,
    });

    expect(rightEdgePos.left).toBe(28);
    expect(rightEdgePos.left + 280).toBe(308);
    expect(rightEdgePos.maxWidth).toBe(296);

    // Trigger near bottom of 320x640 flips to top placement
    const bottomFlipPos = computeFloatingPosition({
      anchorRect: {
        left: 16,
        top: 580,
        right: 180,
        bottom: 616,
        width: 164,
        height: 36,
      },
      floatingSize: { width: 280, height: 220 },
      viewport: {
        left: 0,
        top: 0,
        width: 320,
        height: 640,
        right: 320,
        bottom: 640,
      },
      placement: "bottom",
      align: "end",
      offset: 4,
      margin: 12,
    });

    expect(bottomFlipPos.placement).toBe("top");
    expect(bottomFlipPos.top).toBeGreaterThanOrEqual(12);
    expect(bottomFlipPos.top + 220).toBeLessThanOrEqual(580 - 4);
  });

  it("observes visualViewport resize/scroll events and computes reactive useViewportMaxHeight bounds", async () => {
    mockViewport(320, 256);
    const callback = vi.fn();
    const stop = observeVisualViewport(callback, { trackScroll: true });
    window.dispatchEvent(new Event("resize"));
    window.dispatchEvent(new Event("scroll"));
    expect(callback).toHaveBeenCalledTimes(2);
    stop();
    window.dispatchEvent(new Event("resize"));
    expect(callback).toHaveBeenCalledTimes(2);

    let capturedStyle = null;
    const host = mountHost(() => {
      const { maxHeightStyle } = useViewportMaxHeight({
        shortViewportThreshold: 520,
        shortTopOffset: 12,
        bottomMargin: 12,
        minHeight: 140,
      });
      capturedStyle = maxHeightStyle;
      return () => h("div", { style: maxHeightStyle.value }, "Bounded");
    });

    await nextTick();
    expect(capturedStyle.value).toEqual({ maxHeight: "232px" });
    expect(host.firstElementChild.style.maxHeight).toBe("232px");
  });

  it("clamps ClinicalPopover end-aligned menus within 12px at 320x256 and 320x640, restores focus on Escape, and closes on outside click", async () => {
    for (const [vw, vh] of [
      [320, 256],
      [320, 640],
    ]) {
      mockViewport(vw, vh);
      const isOpen = ref(false);
      const anchorRef = ref(null);

      const host = mountHost(
        () => () =>
          h("div", [
            h(
              "button",
              {
                ref: anchorRef,
                class: "menu-trigger-btn",
                type: "button",
                onClick: () => {
                  isOpen.value = !isOpen.value;
                },
              },
              [h(ClinicalIcon, { name: "chevron-down", size: 14 }), "Actions"]
            ),
            h(
              ClinicalPopover,
              {
                modelValue: isOpen.value,
                "onUpdate:modelValue": (val) => {
                  isOpen.value = val;
                },
                anchor: () => anchorRef.value,
                align: "end",
                offset: 4,
                preferredWidth: 280,
                viewportMargin: 12,
                role: "menu",
                ariaLabel: "Workspace actions",
                popoverClass: "test-menu-popover",
              },
              {
                default: () =>
                  h(
                    "button",
                    {
                      class: "menu-item-btn",
                      type: "button",
                      role: "menuitem",
                    },
                    "Open checklist"
                  ),
              }
            ),
          ])
      );

      const toggleBtn = host.querySelector(".menu-trigger-btn");
      vi.spyOn(toggleBtn, "getBoundingClientRect").mockReturnValue({
        left: 4,
        top: 8,
        right: 156,
        bottom: 42,
        width: 152,
        height: 34,
        x: 4,
        y: 8,
        toJSON: () => ({}),
      });

      toggleBtn.click();
      await nextTick();

      const menu = host.querySelector(".test-menu-popover");
      expect(menu).toBeTruthy();
      expect(menu.getAttribute("role")).toBe("menu");

      const leftPx = Number.parseFloat(menu.style.left);
      const topPx = Number.parseFloat(menu.style.top);
      const maxHeightPx = Number.parseFloat(menu.style.maxHeight);

      expect(leftPx).toBeGreaterThanOrEqual(12);
      expect(topPx).toBeGreaterThanOrEqual(12);
      expect(topPx + maxHeightPx).toBeLessThanOrEqual(vh - 12);

      // Focus inside menu then press Escape -> closes and restores focus to trigger
      const firstItem = menu.querySelector(".menu-item-btn");
      firstItem.focus();
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      await nextTick();
      await nextTick();
      expect(host.querySelector(".test-menu-popover")).toBeNull();
      expect(document.activeElement).toBe(toggleBtn);

      // Re-open and click outside -> closes menu
      toggleBtn.click();
      await nextTick();
      expect(host.querySelector(".test-menu-popover")).toBeTruthy();
      document.body.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      await nextTick();
      expect(host.querySelector(".test-menu-popover")).toBeNull();
    }
  });

  it("supports ClinicalPopover Escape dismissal, hover-bridge wrapper, and teleport='body'", async () => {
    mockViewport(390, 844);
    const anchor = document.createElement("button");
    anchor.getBoundingClientRect = () => ({
      left: 340,
      top: 100,
      right: 386,
      bottom: 124,
      width: 46,
      height: 24,
    });
    document.body.appendChild(anchor);

    const onClose = vi.fn();
    try {
      mountHost(
        () => () =>
          h(
            ClinicalPopover,
            {
              modelValue: true,
              anchor,
              teleport: "body",
              hoverBridge: true,
              preferredWidth: 280,
              viewportMargin: 12,
              onClose,
            },
            {
              default: () => h("div", { class: "pop-inner" }, "Definition"),
            }
          )
      );

      await nextTick();
      const pop = document.body.querySelector(".clinical-popover");
      expect(pop).toBeTruthy();
      expect(pop.parentElement).toBe(document.body);
      expect(pop.classList.contains("clinical-popover--hover-bridge")).toBe(
        true
      );
      expect(
        document.body.querySelector(".clinical-popover-scroll")
      ).toBeTruthy();

      const leftPx = Number.parseFloat(pop.style.left);
      expect(leftPx).toBeGreaterThanOrEqual(12);
      expect(leftPx + 280).toBeLessThanOrEqual(390 - 12);

      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      await nextTick();
      expect(onClose).toHaveBeenCalledWith({ reason: "escape" });
    } finally {
      anchor.remove();
    }
  });

  it("renders ClinicalToastHost top-right and bottom-right placement, vector dismiss icon, batch clear at >= 4 toasts, and 44px coarse-pointer CSS rules", async () => {
    mockViewport(320, 256);
    const sampleToasts = Array.from({ length: 6 }, (_, idx) => ({
      id: `t-${idx + 1}`,
      tone: "error",
      title: `Validation failure ${idx + 1}`,
      message: "Rule signature check failed.",
    }));

    const activeToasts = ref(sampleToasts.slice(0, 3));
    const onClear = vi.fn();
    const onDismiss = vi.fn();

    const host = mountHost(
      () => () =>
        h(ClinicalToastHost, {
          toasts: activeToasts.value,
          position: "top-right",
          onClear,
          onDismiss,
        })
    );

    const toastHostEl = host.querySelector(".toast-host");
    expect(toastHostEl.classList.contains("toast-host--top-right")).toBe(true);
    expect(toastHostEl.getAttribute("data-position")).toBe("top-right");
    // Batch clear hidden when fewer than 4 toasts
    expect(host.querySelector(".toast-clear-all")).toBeNull();

    // Dismiss button uses vector ClinicalIcon instead of legacy multiplication glyph
    const dismissBtn = host.querySelector(".toast-dismiss");
    expect(dismissBtn.querySelector("svg.clinical-icon")).toBeTruthy();
    expect(dismissBtn.textContent.trim()).toBe("");

    // Show batch clear when toasts.length >= 4
    activeToasts.value = sampleToasts;
    await nextTick();

    const clearBtn = host.querySelector(".toast-clear-all");
    expect(clearBtn).toBeTruthy();
    expect(clearBtn.textContent).toContain("Clear all");
    expect(clearBtn.textContent).toContain("(6)");

    const topPx = Number.parseFloat(toastHostEl.style.top);
    const maxHeightPx = Number.parseFloat(toastHostEl.style.maxHeight);
    expect(topPx).toBeGreaterThanOrEqual(12);
    expect(topPx + maxHeightPx).toBeLessThanOrEqual(256 - 76);

    clearBtn.click();
    expect(onClear).toHaveBeenCalledTimes(1);
    expect(onDismiss).not.toHaveBeenCalled();

    // When onClear listener is omitted, batch clear falls back to per-toast onDismiss
    const fallbackDismiss = vi.fn();
    const fallbackHost = mountHost(
      () => () =>
        h(ClinicalToastHost, {
          toasts: sampleToasts.slice(0, 4),
          position: "bottom-right",
          "data-portal-toast-host": "true",
          onDismiss: fallbackDismiss,
        })
    );
    await nextTick();
    fallbackHost.querySelector(".toast-clear-all").click();
    expect(fallbackDismiss).toHaveBeenCalledTimes(4);

    // Verify coarse-pointer 44px rule in ClinicalToastHost.vue source
    const sfcSource = fs.readFileSync(
      path.resolve(
        import.meta.dirname,
        "../src/components/clinical/ClinicalToastHost.vue"
      ),
      "utf8"
    );
    expect(sfcSource).toMatch(
      /@media\s*\(pointer:\s*coarse\)\s*\{[\s\S]*?\.toast-dismiss[\s\S]*?min-width:\s*44px;[\s\S]*?min-height:\s*44px;/
    );
  });

  it("renders ClinicalIcon menu, chevron-down, and chevron-up vector paths", () => {
    for (const iconName of ["menu", "chevron-down", "chevron-up", "x"]) {
      const host = mountHost(() => () => h(ClinicalIcon, { name: iconName }));
      const svg = host.querySelector("svg");
      expect(svg.getAttribute("aria-hidden")).toBe("true");
      expect(svg.children.length).toBeGreaterThan(0);
    }
  });

  it("dismisses ClinicalPopover on outside focusout while preserving focus transitions inside anchor or popover", async () => {
    mockViewport(1024, 768);
    const isOpen = ref(true);
    const closeEvents = [];
    const anchorRef = ref(null);

    const host = mountHost(
      () => () =>
        h("div", [
          h(
            "button",
            { ref: anchorRef, id: "popover-anchor", type: "button" },
            "Anchor"
          ),
          h(
            ClinicalPopover,
            {
              modelValue: isOpen.value,
              "onUpdate:modelValue": (val) => {
                isOpen.value = val;
              },
              onClose: (payload) => closeEvents.push(payload),
              anchor: () => anchorRef.value,
              popoverClass: "focusout-test-popover",
            },
            {
              default: () => [
                h("button", { id: "popover-item-1", type: "button" }, "Item 1"),
                h("button", { id: "popover-item-2", type: "button" }, "Item 2"),
              ],
            }
          ),
          h("button", { id: "outside-main-btn", type: "button" }, "Outside"),
        ])
    );
    await nextTick();

    const anchorBtn = host.querySelector("#popover-anchor");
    const item1 = host.querySelector("#popover-item-1");
    const item2 = host.querySelector("#popover-item-2");
    const outsideBtn = host.querySelector("#outside-main-btn");

    anchorBtn.focus();
    item1.focus();
    await nextTick();
    expect(isOpen.value).toBe(true);

    item2.focus();
    await nextTick();
    expect(isOpen.value).toBe(true);

    outsideBtn.focus();
    await nextTick();
    expect(isOpen.value).toBe(false);
    expect(closeEvents).toContainEqual({ reason: "focusout" });
    expect(document.activeElement).toBe(outsideBtn);
  });
});
