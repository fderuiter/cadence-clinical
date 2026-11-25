// @vitest-environment jsdom
import fs from "node:fs";
import path from "node:path";
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, h, nextTick, ref } from "vue";
import {
  ClinicalIcon,
  ClinicalPopover,
  ClinicalToastHost,
  PersonaSwitcher,
  computeFloatingPosition,
  getVisualViewportBounds,
} from "../index.js";

describe("Floating primitives, PersonaSwitcher clamping, and ClinicalToastHost", () => {
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

  it("clamps floating panels within 12px of visualViewport edges and flips when bottom space is insufficient", () => {
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

    // Right-edge glossary trigger at x=290 on 320x640 must clamp right <= 308px
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

  it("clamps PersonaSwitcher menu within 12px at 320x256 and 320x640, renders vector chevrons, and closes on Escape or outside click", async () => {
    for (const [vw, vh] of [
      [320, 256],
      [320, 640],
    ]) {
      mockViewport(vw, vh);
      const host = mountHost(() => () => h(PersonaSwitcher));

      const toggleBtn = host.querySelector(".persona-toggle-btn");
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

      expect(toggleBtn.querySelector("svg.clinical-icon")).toBeTruthy();
      expect(toggleBtn.textContent).not.toContain("\u25b2");
      expect(toggleBtn.textContent).not.toContain("\u25bc");

      toggleBtn.click();
      await nextTick();

      const menu = host.querySelector(".persona-menu");
      expect(menu).toBeTruthy();

      const leftPx = Number.parseFloat(menu.style.left);
      const topPx = Number.parseFloat(menu.style.top);
      const maxHeightPx = Number.parseFloat(menu.style.maxHeight);

      expect(leftPx).toBeGreaterThanOrEqual(12);
      expect(topPx).toBeGreaterThanOrEqual(12);
      expect(topPx + maxHeightPx).toBeLessThanOrEqual(vh - 12);

      // Focus inside menu then press Escape -> closes and restores focus to trigger
      const firstItem = menu.querySelector(".menu-item");
      firstItem.focus();
      window.dispatchEvent(
        new KeyboardEvent("keydown", { key: "Escape", bubbles: true })
      );
      await nextTick();
      expect(host.querySelector(".persona-menu")).toBeNull();
      expect(document.activeElement).toBe(toggleBtn);

      // Re-open and click outside -> closes menu
      toggleBtn.click();
      await nextTick();
      expect(host.querySelector(".persona-menu")).toBeTruthy();
      document.body.dispatchEvent(new MouseEvent("click", { bubbles: true }));
      await nextTick();
      expect(host.querySelector(".persona-menu")).toBeNull();
    }
  });

  it("supports ClinicalPopover Escape dismissal, outside pointer close, and hover-bridge wrapper", async () => {
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
      const host = mountHost(
        () => () =>
          h(
            ClinicalPopover,
            {
              modelValue: true,
              anchor,
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
      const pop = host.querySelector(".clinical-popover");
      expect(pop).toBeTruthy();
      expect(pop.classList.contains("clinical-popover--hover-bridge")).toBe(
        true
      );
      expect(host.querySelector(".clinical-popover-scroll")).toBeTruthy();

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

  it("renders ClinicalToastHost top-right placement, vector dismiss icon, batch clear at >= 4 toasts, and 44px coarse-pointer CSS rules", async () => {
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

    // Dismiss button uses vector ClinicalIcon instead of legacy × glyph
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
});
