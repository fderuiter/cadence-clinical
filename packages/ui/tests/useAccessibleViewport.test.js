// @vitest-environment jsdom
import { describe, it, expect, vi } from "vitest";
import { createApp, h, nextTick } from "vue";
import {
  MediaPlayer,
  ViewportPanPad,
  useAccessibleViewport,
} from "../index.js";
import { toBeAccessible } from "../accessibility-matcher.js";

expect.extend({ toBeAccessible });

function mountComponent(Component, props = {}) {
  const host = document.createElement("div");
  document.body.appendChild(host);
  const emitted = {};
  const listeners = {};
  for (const [k, v] of Object.entries(props)) {
    if (k.startsWith("on") && typeof v === "function") {
      listeners[k] = v;
    }
  }
  const app = createApp({
    render() {
      return h(Component, {
        ...props,
        onPan: (payload) => {
          emitted.pan = emitted.pan || [];
          emitted.pan.push(payload);
          props.onPan?.(payload);
        },
        onReset: () => {
          emitted.reset = emitted.reset || [];
          emitted.reset.push([]);
          props.onReset?.();
        },
      });
    },
  });
  app.mount(host);
  return {
    host,
    emitted,
    unmount() {
      app.unmount();
      host.remove();
    },
  };
}

describe("useAccessibleViewport, ViewportPanPad & MediaPlayer Spatial Accessibility", () => {
  it("steps reticle with arrow keys, clamps [0, 100], and confirms single-point placement without NaN", () => {
    const confirmed = [];
    const vp = useAccessibleViewport({
      minX: 0,
      maxX: 100,
      minY: 0,
      maxY: 100,
      step: 10,
      shiftStep: 25,
      fineStep: 2,
      throttleMs: 0,
      onConfirmPoint: (pt) => confirmed.push(pt),
    });

    expect(vp.reticleX.value).toBe(50);
    expect(vp.reticleY.value).toBe(50);

    const preventDefault = vi.fn();
    vp.handleReticleKeydown({ key: "ArrowRight", preventDefault });
    vp.handleReticleKeydown({ key: "ArrowUp", shiftKey: true, preventDefault });
    vp.handleReticleKeydown({ key: "ArrowLeft", altKey: true, preventDefault });

    expect(vp.reticleX.value).toBe(58);
    expect(vp.reticleY.value).toBe(25);
    expect(preventDefault).toHaveBeenCalledTimes(3);

    // Clamp beyond bounds and against non-finite inputs
    vp.stepReticle(500, -500);
    expect(vp.reticleX.value).toBe(100);
    expect(vp.reticleY.value).toBe(0);

    const fallback = vp.clientToViewport({});
    expect(fallback.validPointer).toBe(false);
    expect(Number.isNaN(fallback.x)).toBe(false);
    expect(Number.isNaN(fallback.y)).toBe(false);

    vp.handleReticleKeydown({ key: "Enter", preventDefault });
    expect(confirmed).toHaveLength(1);
    expect(confirmed[0]).toEqual({ x: 100, y: 0, source: "keyboard" });
  });

  it("supports two-click and keyboard segment placement with minDistance and Escape cancellation", () => {
    const segments = [];
    const vp = useAccessibleViewport({
      minX: 0,
      maxX: 380,
      minY: 0,
      maxY: 380,
      mode: "segment",
      minDistance: 4,
      throttleMs: 0,
      unit: "px",
      onCompleteSegment: (seg) => segments.push(seg),
    });

    const first = vp.placePoint({ x: 100, y: 120 });
    expect(first.completed).toBe(false);
    expect(vp.pendingAnchor.value).toEqual({ x: 100, y: 120 });

    // Too close -> retains anchor
    const tooClose = vp.placePoint({ x: 101, y: 121 });
    expect(tooClose.completed).toBe(false);
    expect(tooClose.reason).toBe("min-distance");
    expect(vp.pendingAnchor.value).toEqual({ x: 100, y: 120 });

    // Escape cancels pending anchor
    const stopPropagation = vi.fn();
    const preventDefault = vi.fn();
    vp.handleReticleKeydown({ key: "Escape", preventDefault, stopPropagation });
    expect(vp.pendingAnchor.value).toBeNull();

    // Two-step keyboard reticle segment
    vp.setReticle(80, 90);
    vp.confirmReticlePoint();
    expect(vp.pendingAnchor.value).toEqual({ x: 80, y: 90 });

    vp.stepReticle(40, 30);
    vp.confirmReticlePoint();
    expect(vp.pendingAnchor.value).toBeNull();
    expect(segments).toHaveLength(1);
    expect(segments[0].distance).toBe(50);
    expect(segments[0].source).toBe("keyboard");
  });

  it("throttles rapid polite live announcements and flushes trailing state", () => {
    vi.useFakeTimers();
    try {
      const vp = useAccessibleViewport({ throttleMs: 250 });
      vp.announce("Reticle at 55%, 50%");
      expect(vp.liveAnnouncement.value).toBe("Reticle at 55%, 50%");

      vp.announce("Reticle at 60%, 50%");
      vp.announce("Reticle at 65%, 50%");
      // Trailing update held until throttle window elapses
      expect(vp.liveAnnouncement.value).toBe("Reticle at 55%, 50%");

      vi.advanceTimersByTime(260);
      expect(vp.liveAnnouncement.value).toBe("Reticle at 65%, 50%");
    } finally {
      vi.useRealTimers();
    }
  });

  it("renders ViewportPanPad with accessible 4-way buttons and zero axe violations", async () => {
    const mounted = mountComponent(ViewportPanPad, {
      step: 32,
      labelPrefix: "Pan viewport",
      showReset: true,
    });
    try {
      await expect(mounted.host).toBeAccessible();

      mounted.host
        .querySelector('[data-pan-direction="up"]')
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
      mounted.host
        .querySelector('[data-pan-direction="down"]')
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
      mounted.host
        .querySelector('[data-pan-direction="left"]')
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
      mounted.host
        .querySelector('[data-pan-direction="right"]')
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));
      mounted.host
        .querySelector('[data-pan-direction="reset"]')
        .dispatchEvent(new MouseEvent("click", { bubbles: true }));

      expect(mounted.emitted.pan).toEqual([
        { direction: "up", dx: 0, dy: -32 },
        { direction: "down", dx: 0, dy: 32 },
        { direction: "left", dx: -32, dy: 0 },
        { direction: "right", dx: 32, dy: 0 },
      ]);
      expect(mounted.emitted.reset).toHaveLength(1);
    } finally {
      mounted.unmount();
    }
  });

  it("pans zoomed images in MediaPlayer via 4-way buttons and arrow keys while keeping mouse drag", async () => {
    const mounted = mountComponent(MediaPlayer, {
      src: "https://example.com/lesion-scan.png",
      mimeType: "image/png",
      title: "Baseline Lesion Radiograph",
      initialZoom: 2,
    });

    try {
      await nextTick();
      await expect(mounted.host).toBeAccessible();

      const img = mounted.host.querySelector('[data-testid="pannable-image"]');
      expect(img.style.transform).toMatch(
        /translate\(0px(?:,\s*0px)?\)\s*scale\(2\)/
      );

      // Single-pointer 4-way directional buttons (no mousedown)
      mounted.host
        .querySelector('button[aria-label="Pan image right"]')
        .click();
      mounted.host.querySelector('button[aria-label="Pan image down"]').click();
      await nextTick();
      expect(img.style.transform).toContain("translate(24px, 24px)");

      mounted.host.querySelector('button[aria-label="Pan image left"]').click();
      mounted.host.querySelector('button[aria-label="Pan image up"]').click();
      await nextTick();
      expect(img.style.transform).toMatch(/translate\(0px(?:,\s*0px)?\)/);

      // Keyboard arrow-key panning on the viewport
      const viewport = mounted.host.querySelector(
        '[data-testid="image-viewer"]'
      );
      viewport.dispatchEvent(
        new KeyboardEvent("keydown", { key: "ArrowRight", bubbles: true })
      );
      viewport.dispatchEvent(
        new KeyboardEvent("keydown", { key: "ArrowUp", bubbles: true })
      );
      await nextTick();
      expect(img.style.transform).toContain("translate(24px, -24px)");

      // Reset View restores transform
      mounted.host.querySelector('button[aria-label="Reset View"]').click();
      await nextTick();
      expect(img.style.transform).toMatch(
        /translate\(0px(?:,\s*0px)?\)\s*scale\(1\)/
      );

      // Existing mouse drag gesture still works
      viewport.dispatchEvent(
        new MouseEvent("mousedown", {
          clientX: 100,
          clientY: 100,
          bubbles: true,
        })
      );
      viewport.dispatchEvent(
        new MouseEvent("mousemove", {
          clientX: 135,
          clientY: 115,
          bubbles: true,
        })
      );
      window.dispatchEvent(new MouseEvent("mouseup"));
      await nextTick();
      expect(img.style.transform).toContain("translate(35px, 15px)");
    } finally {
      mounted.unmount();
    }
  });
});
