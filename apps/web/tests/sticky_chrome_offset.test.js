import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  FOCUS_RING_CLEARANCE_PX,
  clearCanvasStickyTopbarHeight,
  clearShellStickyContextBarHeight,
  clearShellStickyHeaderHeight,
  ensureFocusedElementClearsStickyChrome,
  getActiveStickyChromeHeightPx,
  isStickyChromeReleased,
  recordStickyChromePointerDown,
  syncRootStickyChromeOffset,
  updateCanvasStickyTopbarHeight,
  updateShellStickyContextBarHeight,
  updateShellStickyHeaderHeight,
} from "../src/utils/stickyChromeOffset";

describe("stickyChromeOffset synchronization (ADR-2663)", () => {
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
    clearShellStickyContextBarHeight();
    clearShellStickyHeaderHeight();
  });

  afterEach(() => {
    clearCanvasStickyTopbarHeight();
    clearShellStickyContextBarHeight();
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

  it("includes sticky #shell-context-bar height across desktop, collapsed-header tablet, and narrow mobile while releasing below 500px height", () => {
    updateShellStickyHeaderHeight(48);
    const withContextBar = updateShellStickyContextBarHeight(44);
    expect(withContextBar).toEqual({
      released: false,
      shellHeight: 48,
      topbarHeight: 0,
      stickyChromeOffset: 48 + 44 + FOCUS_RING_CLEARANCE_PX,
    });
    expect(getActiveStickyChromeHeightPx()).toBe(92);

    // Tablet (< 1280px) downward scroll collapses .app-header (0px) while #shell-context-bar (44px) remains sticky
    window.innerWidth = 768;
    const tabletCollapsed = updateShellStickyHeaderHeight(0);
    expect(tabletCollapsed).toEqual({
      released: false,
      shellHeight: 0,
      topbarHeight: 0,
      stickyChromeOffset: 44 + FOCUS_RING_CLEARANCE_PX,
    });
    expect(getActiveStickyChromeHeightPx()).toBe(44);

    // Narrow mobile portrait (320x667) releases .app-header while keeping #shell-context-bar sticky until < 500px height
    window.innerWidth = 320;
    window.innerHeight = 667;
    const mobilePortrait = syncRootStickyChromeOffset();
    expect(mobilePortrait).toEqual({
      released: true,
      shellHeight: 0,
      topbarHeight: 0,
      stickyChromeOffset: 44 + FOCUS_RING_CLEARANCE_PX,
    });

    // Short viewport height (<= 499px) releases #shell-context-bar as well
    window.innerHeight = 400;
    const shortViewport = syncRootStickyChromeOffset();
    expect(shortViewport).toEqual({
      released: true,
      shellHeight: 0,
      topbarHeight: 0,
      stickyChromeOffset: FOCUS_RING_CLEARANCE_PX,
    });
    expect(getActiveStickyChromeHeightPx()).toBe(0);

    // Controls inside #shell-context-bar are treated as sticky chrome and never trigger scrollBy
    window.innerWidth = 1280;
    window.innerHeight = 800;
    window.scrollY = 200;
    updateShellStickyHeaderHeight(48);
    const scrollBySpy = vi
      .spyOn(window, "scrollBy")
      .mockImplementation(() => {});
    const contextNav = document.createElement("nav");
    contextNav.id = "shell-context-bar";
    const crumbLink = document.createElement("a");
    crumbLink.href = "#/studies";
    crumbLink.getBoundingClientRect = () => ({
      x: 16,
      y: 52,
      top: 52,
      bottom: 96,
      left: 16,
      right: 96,
      width: 80,
      height: 44,
      toJSON: () => ({}),
    });
    contextNav.appendChild(crumbLink);
    document.body.appendChild(contextNav);

    ensureFocusedElementClearsStickyChrome(crumbLink);
    expect(scrollBySpy).not.toHaveBeenCalled();

    clearShellStickyContextBarHeight();
    expect(getActiveStickyChromeHeightPx()).toBe(48);

    contextNav.remove();
    scrollBySpy.mockRestore();
  });
});
