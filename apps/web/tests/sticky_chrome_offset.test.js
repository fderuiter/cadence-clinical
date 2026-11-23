import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  FOCUS_RING_CLEARANCE_PX,
  clearCanvasStickyTopbarHeight,
  clearShellStickyHeaderHeight,
  ensureFocusedElementClearsStickyChrome,
  getActiveStickyChromeHeightPx,
  isStickyChromeReleased,
  recordStickyChromePointerDown,
  syncRootStickyChromeOffset,
  updateCanvasStickyTopbarHeight,
  updateShellStickyHeaderHeight,
} from "../src/utils/stickyChromeOffset";

describe("stickyChromeOffset synchronization (ADR-2658)", () => {
  const originalInnerHeight = window.innerHeight;
  const originalInnerWidth = window.innerWidth;

  beforeEach(() => {
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: 800,
    });
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: 1280,
    });
    Object.defineProperty(window, "scrollY", {
      configurable: true,
      writable: true,
      value: 0,
    });
    clearCanvasStickyTopbarHeight();
    clearShellStickyHeaderHeight();
  });

  afterEach(() => {
    clearCanvasStickyTopbarHeight();
    clearShellStickyHeaderHeight();
    Object.defineProperty(window, "innerHeight", {
      configurable: true,
      writable: true,
      value: originalInnerHeight,
    });
    Object.defineProperty(window, "innerWidth", {
      configurable: true,
      writable: true,
      value: originalInnerWidth,
    });
  });

  it("publishes live --shell-header-height, --canvas-topbar-height, and --sticky-chrome-offset on :root and cleans up on unmount", () => {
    const shellOnly = updateShellStickyHeaderHeight(48);
    expect(shellOnly).toEqual({
      released: false,
      shellHeight: 48,
      topbarHeight: 0,
      stickyChromeOffset: 48 + FOCUS_RING_CLEARANCE_PX,
    });
    expect(
      document.documentElement.style.getPropertyValue("--shell-header-height")
    ).toBe("48px");
    expect(
      document.documentElement.style.getPropertyValue("--sticky-chrome-offset")
    ).toBe(`${48 + FOCUS_RING_CLEARANCE_PX}px`);

    const stacked = updateCanvasStickyTopbarHeight(46, { inAppShell: true });
    expect(stacked).toEqual({
      released: false,
      shellHeight: 48,
      topbarHeight: 46,
      stickyChromeOffset: 48 + 46 + FOCUS_RING_CLEARANCE_PX,
    });
    expect(getActiveStickyChromeHeightPx()).toBe(94);
    expect(
      document.documentElement.style.getPropertyValue("--canvas-topbar-height")
    ).toBe("46px");
    expect(
      document.documentElement.style.getPropertyValue("--sticky-chrome-offset")
    ).toBe(`${94 + FOCUS_RING_CLEARANCE_PX}px`);

    const afterCanvasUnmount = clearCanvasStickyTopbarHeight();
    expect(afterCanvasUnmount.stickyChromeOffset).toBe(
      48 + FOCUS_RING_CLEARANCE_PX
    );
    expect(
      document.documentElement.style.getPropertyValue("--canvas-topbar-height")
    ).toBe("0px");
  });

  it("releases sticky chrome offsets below 500px viewport height (320x256 and 640x400)", () => {
    updateShellStickyHeaderHeight(48);
    updateCanvasStickyTopbarHeight(45, { inAppShell: true });
    expect(isStickyChromeReleased()).toBe(false);

    window.innerHeight = 400;
    const short400 = syncRootStickyChromeOffset();
    expect(short400).toEqual({
      released: true,
      shellHeight: 0,
      topbarHeight: 0,
      stickyChromeOffset: FOCUS_RING_CLEARANCE_PX,
    });
    expect(getActiveStickyChromeHeightPx()).toBe(0);
    expect(
      document.documentElement.style.getPropertyValue("--sticky-chrome-offset")
    ).toBe(`${FOCUS_RING_CLEARANCE_PX}px`);

    window.innerHeight = 256;
    expect(syncRootStickyChromeOffset().released).toBe(true);

    window.innerHeight = 768;
    const restored = syncRootStickyChromeOffset();
    expect(restored.released).toBe(false);
    expect(restored.stickyChromeOffset).toBe(48 + 45 + FOCUS_RING_CLEARANCE_PX);

    window.innerWidth = 320;
    expect(syncRootStickyChromeOffset().released).toBe(true);
    window.innerWidth = 1280;
    expect(syncRootStickyChromeOffset().released).toBe(false);
  });

  it("scrolls focused main controls out from behind sticky chrome while ignoring pointer interactions and sticky header buttons", () => {
    updateShellStickyHeaderHeight(48);
    updateCanvasStickyTopbarHeight(44, { inAppShell: true });
    window.scrollY = 220;
    const scrollBySpy = vi
      .spyOn(window, "scrollBy")
      .mockImplementation(() => {});

    const btn = document.createElement("button");
    btn.getBoundingClientRect = () => ({
      x: 20,
      y: 30,
      top: 30,
      bottom: 66,
      left: 20,
      right: 140,
      width: 120,
      height: 36,
      toJSON: () => ({}),
    });
    document.body.appendChild(btn);

    ensureFocusedElementClearsStickyChrome(btn);
    expect(scrollBySpy).toHaveBeenCalledWith(
      0,
      30 - (92 + FOCUS_RING_CLEARANCE_PX)
    );

    scrollBySpy.mockClear();
    const header = document.createElement("header");
    header.className = "app-header";
    const headerBtn = document.createElement("button");
    headerBtn.getBoundingClientRect = btn.getBoundingClientRect;
    header.appendChild(headerBtn);
    document.body.appendChild(header);

    ensureFocusedElementClearsStickyChrome(headerBtn);
    expect(scrollBySpy).not.toHaveBeenCalled();

    recordStickyChromePointerDown();
    ensureFocusedElementClearsStickyChrome(btn);
    expect(scrollBySpy).not.toHaveBeenCalled();

    btn.remove();
    header.remove();
    scrollBySpy.mockRestore();
  });
});
