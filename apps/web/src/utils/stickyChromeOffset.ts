export const SHORT_VIEWPORT_MAX_HEIGHT = 499;
export const NARROW_VIEWPORT_MAX_WIDTH = 640;
export const FOCUS_RING_CLEARANCE_PX = 12;

export interface StickyChromeSyncResult {
  released: boolean;
  shellHeight: number;
  topbarHeight: number;
  stickyChromeOffset: number;
}

export interface CanvasTopbarUpdateOptions {
  inAppShell?: boolean;
}

let shellMounted = false;
let shellHeaderHeightPx = 0;
let canvasMounted = false;
let canvasTopbarHeightPx = 0;
let canvasInShell = true;
let lastPointerInteractionAt = 0;

const NATIVE_FOCUSABLE_CONTROLS = new Set([
  "A",
  "BUTTON",
  "INPUT",
  "SELECT",
  "SUMMARY",
  "TEXTAREA",
]);

const INTERACTIVE_ROLES = new Set([
  "button",
  "link",
  "tab",
  "menuitem",
  "checkbox",
  "radio",
  "switch",
  "combobox",
  "gridcell",
  "treeitem",
]);

export function isStickyChromeReleased(): boolean {
  if (typeof window === "undefined") {
    return false;
  }
  if (
    typeof window.innerHeight === "number" &&
    window.innerHeight > 0 &&
    window.innerHeight <= SHORT_VIEWPORT_MAX_HEIGHT
  ) {
    return true;
  }
  if (
    typeof window.innerWidth === "number" &&
    window.innerWidth > 0 &&
    window.innerWidth <= NARROW_VIEWPORT_MAX_WIDTH
  ) {
    return true;
  }
  if (typeof window.matchMedia === "function") {
    try {
      const mql = window.matchMedia(
        `(max-height: ${SHORT_VIEWPORT_MAX_HEIGHT}px), (max-width: ${NARROW_VIEWPORT_MAX_WIDTH}px)`
      );
      if (mql && mql.matches) {
        return true;
      }
    } catch {
      // Ignore matchMedia errors in test stubs
    }
  }
  return false;
}

export function getActiveStickyChromeHeightPx(): number {
  if (isStickyChromeReleased()) {
    return 0;
  }
  const activeShell =
    shellMounted && (!canvasMounted || canvasInShell)
      ? Math.max(0, Math.round(shellHeaderHeightPx))
      : 0;
  const activeTopbar = canvasMounted
    ? Math.max(0, Math.round(canvasTopbarHeightPx))
    : 0;
  return activeShell + activeTopbar;
}

export function syncRootStickyChromeOffset(): StickyChromeSyncResult {
  const released = isStickyChromeReleased();
  const effectiveShell =
    !released && shellMounted
      ? Math.max(0, Math.round(shellHeaderHeightPx))
      : 0;
  const effectiveTopbar =
    !released && canvasMounted
      ? Math.max(0, Math.round(canvasTopbarHeightPx))
      : 0;
  const activeChrome =
    (effectiveShell && (!canvasMounted || canvasInShell) ? effectiveShell : 0) +
    effectiveTopbar;
  const stickyChromeOffset = activeChrome + FOCUS_RING_CLEARANCE_PX;

  if (
    typeof document !== "undefined" &&
    document.documentElement &&
    document.documentElement.style
  ) {
    document.documentElement.style.setProperty(
      "--shell-header-height",
      `${effectiveShell}px`
    );
    document.documentElement.style.setProperty(
      "--canvas-topbar-height",
      `${effectiveTopbar}px`
    );
    document.documentElement.style.setProperty(
      "--sticky-chrome-offset",
      `${stickyChromeOffset}px`
    );
  }

  return {
    released,
    shellHeight: effectiveShell,
    topbarHeight: effectiveTopbar,
    stickyChromeOffset,
  };
}

export function updateShellStickyHeaderHeight(
  heightPx: number
): StickyChromeSyncResult {
  shellMounted = true;
  shellHeaderHeightPx =
    Number.isFinite(heightPx) && heightPx > 0 ? Math.round(heightPx) : 0;
  return syncRootStickyChromeOffset();
}

export function clearShellStickyHeaderHeight(): StickyChromeSyncResult {
  shellMounted = false;
  shellHeaderHeightPx = 0;
  return syncRootStickyChromeOffset();
}

export function updateCanvasStickyTopbarHeight(
  heightPx: number,
  options: CanvasTopbarUpdateOptions = {}
): StickyChromeSyncResult {
  canvasMounted = true;
  if (typeof options.inAppShell === "boolean") {
    canvasInShell = options.inAppShell;
  }
  canvasTopbarHeightPx =
    Number.isFinite(heightPx) && heightPx > 0 ? Math.round(heightPx) : 0;
  return syncRootStickyChromeOffset();
}

export function clearCanvasStickyTopbarHeight(): StickyChromeSyncResult {
  canvasMounted = false;
  canvasTopbarHeightPx = 0;
  canvasInShell = true;
  return syncRootStickyChromeOffset();
}

export function recordStickyChromePointerDown(): void {
  lastPointerInteractionAt = Date.now();
}

function isElementLike(candidate: unknown): candidate is Element {
  return Boolean(
    candidate &&
    typeof candidate === "object" &&
    typeof (candidate as Element).getBoundingClientRect === "function"
  );
}

function isInsideStickyOrModalOverlay(el: Element): boolean {
  if (typeof el.closest !== "function") return true;
  if (
    el.closest(
      '.app-header, .topbar, .skip-link, [role="dialog"], [role="alertdialog"], .navigator, #canvas-inspector'
    )
  ) {
    return true;
  }
  if (
    typeof window !== "undefined" &&
    typeof window.getComputedStyle === "function"
  ) {
    let cur: Element | null = el;
    while (cur && cur !== document.body && cur !== document.documentElement) {
      const pos = window.getComputedStyle(cur).position;
      if (pos === "fixed" || pos === "sticky") {
        return true;
      }
      cur = cur.parentElement;
    }
  }
  return false;
}

export function ensureFocusedElementClearsStickyChrome(
  eventOrTarget: Event | EventTarget | Element | null | undefined
): void {
  if (typeof window === "undefined" || typeof document === "undefined") {
    return;
  }
  const rawTarget =
    eventOrTarget &&
    typeof eventOrTarget === "object" &&
    "target" in eventOrTarget
      ? eventOrTarget.target
      : eventOrTarget;
  if (!isElementLike(rawTarget)) {
    return;
  }
  if (rawTarget === document.body || rawTarget === document.documentElement) {
    return;
  }
  if (Date.now() - lastPointerInteractionAt < 150) {
    return;
  }
  if (isInsideStickyOrModalOverlay(rawTarget)) {
    return;
  }

  const vh = window.innerHeight || document.documentElement?.clientHeight || 0;
  if (vh <= 0) {
    return;
  }

  const rect = rawTarget.getBoundingClientRect();
  if (rect.width <= 0 && rect.height <= 0) {
    return;
  }

  const activeStickyPx = getActiveStickyChromeHeightPx();
  const minClearTop = activeStickyPx + FOCUS_RING_CLEARANCE_PX;
  const availableHeight = Math.max(48, vh - minClearTop);

  const tag = String(rawTarget.tagName || "").toUpperCase();
  const role =
    typeof rawTarget.getAttribute === "function"
      ? rawTarget.getAttribute("role") || ""
      : "";
  const isNativeControl =
    NATIVE_FOCUSABLE_CONTROLS.has(tag) || INTERACTIVE_ROLES.has(role);

  if (!isNativeControl && rect.height > availableHeight * 0.6) {
    return;
  }

  const scrollY =
    window.scrollY ??
    window.pageYOffset ??
    document.documentElement?.scrollTop ??
    0;

  if (rect.top < minClearTop && scrollY > 0) {
    const delta = Math.floor(rect.top - minClearTop);
    if (delta < 0 && typeof window.scrollBy === "function") {
      window.scrollBy(0, delta);
    }
    return;
  }

  if (rect.bottom > vh && rect.height <= availableHeight) {
    const delta = Math.ceil(rect.bottom - vh + FOCUS_RING_CLEARANCE_PX);
    if (delta > 0 && typeof window.scrollBy === "function") {
      window.scrollBy(0, delta);
    }
  }
}
