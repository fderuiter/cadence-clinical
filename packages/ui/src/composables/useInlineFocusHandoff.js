import {
  getCurrentInstance,
  getCurrentScope,
  nextTick,
  onBeforeUnmount,
  onScopeDispose,
  unref,
  watch,
} from "vue";
import {
  isElementInertOrHidden,
  isJSDOMEnv,
  resolveOption,
} from "./useFocusTrap.js";

const DEFAULT_INLINE_FALLBACKS = ["#canvas-workspace", "#main-content"];

const NATIVE_INTERACTIVE_TAGS = /* @__PURE__ */ new Set([
  "BUTTON",
  "INPUT",
  "SELECT",
  "TEXTAREA",
  "SUMMARY",
]);

export const escapeIdSelector = (rawId) => {
  if (!rawId) return null;
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return `#${CSS.escape(rawId)}`;
  }
  return `#${String(rawId).replace(/([^\w-])/g, "\\$1")}`;
};

export const queryDocumentSelector = (selector, container) => {
  if (!selector || typeof selector !== "string") return null;
  if (container && typeof container.querySelector === "function") {
    const scoped = container.querySelector(selector);
    if (scoped) return scoped;
  }
  if (typeof document === "undefined") return null;
  if (typeof document.querySelector === "function") {
    return document.querySelector(selector);
  }
  if (
    selector.startsWith("#") &&
    typeof document.getElementById === "function"
  ) {
    return document.getElementById(selector.slice(1));
  }
  if (document.body && typeof document.body.querySelector === "function") {
    return document.body.querySelector(selector);
  }
  return null;
};

export const resolveFocusCandidateElement = (candidate, container) => {
  if (!candidate) return null;
  const raw = resolveOption(candidate);
  if (!raw) return null;
  if (typeof raw === "string") {
    return queryDocumentSelector(raw, container);
  }
  const el = raw.$el || raw;
  return el && typeof el.focus === "function" ? el : null;
};

const isInlineTargetFocusable = (el, container) => {
  if (!el || typeof el.focus !== "function") return false;
  if (typeof document !== "undefined") {
    if (el === document.body || el === document.documentElement) {
      return false;
    }
    if (typeof document.contains === "function" && !document.contains(el)) {
      return false;
    }
  }
  if (
    typeof el.getAttribute === "function" &&
    el.getAttribute("aria-disabled") === "true" &&
    NATIVE_INTERACTIVE_TAGS.has(el.tagName)
  ) {
    return false;
  }
  return !isElementInertOrHidden(el, container || null);
};

const ensureProgrammaticTabIndex = (el) => {
  if (!el || typeof el.hasAttribute !== "function") return;
  if (el.hasAttribute("tabindex")) return;
  const tag = String(el.tagName || "").toUpperCase();
  if (NATIVE_INTERACTIVE_TAGS.has(tag)) return;
  if ((tag === "A" || tag === "AREA") && el.hasAttribute("href")) return;
  if (typeof el.setAttribute === "function") {
    el.setAttribute("tabindex", "-1");
  }
};

const PROGRAMMATIC_FOCUS_KEY = Symbol.for("cadence.programmaticFocusState");

export const resolveStickyTopOffsetPx = (options = {}) => {
  if (
    typeof options.stickyOffset === "number" &&
    Number.isFinite(options.stickyOffset)
  ) {
    return Math.max(0, options.stickyOffset);
  }
  if (typeof document === "undefined" || !document.documentElement) {
    return 0;
  }
  let raw = "";
  if (
    document.documentElement.style &&
    typeof document.documentElement.style.getPropertyValue === "function"
  ) {
    raw = document.documentElement.style
      .getPropertyValue("--sticky-chrome-offset")
      .trim();
  }
  if (
    !raw &&
    typeof window !== "undefined" &&
    typeof window.getComputedStyle === "function"
  ) {
    try {
      raw = window
        .getComputedStyle(document.documentElement)
        .getPropertyValue("--sticky-chrome-offset")
        .trim();
    } catch {
      raw = "";
    }
  }
  const parsed = Number.parseFloat(raw);
  return Number.isFinite(parsed) && parsed > 0 ? parsed : 0;
};

const resolveScrollAnchor = (candidate, container) => {
  if (!candidate) return null;
  const raw = resolveOption(candidate);
  if (!raw) return null;
  if (typeof raw === "string") {
    return queryDocumentSelector(raw, container);
  }
  return raw.$el || raw;
};

const getWindowScrollY = () => {
  if (typeof window === "undefined") return 0;
  return (
    window.scrollY ??
    window.pageYOffset ??
    (typeof document !== "undefined"
      ? document.documentElement?.scrollTop
      : 0) ??
    0
  );
};

const hasMeasuredRect = (rect) =>
  Boolean(rect && (rect.width > 0 || rect.height > 0 || !isJSDOMEnv()));

const applyInlineFocus = (el, options = {}, container = null) => {
  ensureProgrammaticTabIndex(el);
  const scrollIfOffscreen = Boolean(options.scrollIfOffscreen);
  const preventScroll =
    options.preventScroll !== undefined ? Boolean(options.preventScroll) : true;

  if (scrollIfOffscreen && typeof window !== "undefined") {
    const minClearTop = resolveStickyTopOffsetPx(options);
    const vh =
      window.innerHeight ||
      (typeof document !== "undefined" &&
        document.documentElement?.clientHeight) ||
      0;
    const vw =
      window.innerWidth ||
      (typeof document !== "undefined" &&
        document.documentElement?.clientWidth) ||
      0;

    const scrollAnchor = resolveScrollAnchor(options.scrollTarget, container);
    if (
      scrollAnchor &&
      typeof scrollAnchor.getBoundingClientRect === "function"
    ) {
      const anchorRect = scrollAnchor.getBoundingClientRect();
      if (hasMeasuredRect(anchorRect) && vh > 0) {
        if (
          (anchorRect.bottom < minClearTop || anchorRect.top > vh) &&
          typeof scrollAnchor.scrollIntoView === "function"
        ) {
          scrollAnchor.scrollIntoView({ block: "start", inline: "nearest" });
        }
        const updatedAnchorRect = scrollAnchor.getBoundingClientRect();
        const scrollY = getWindowScrollY();
        if (typeof window.scrollBy === "function") {
          if (updatedAnchorRect.top < minClearTop && scrollY > 0) {
            window.scrollBy(0, Math.floor(updatedAnchorRect.top - minClearTop));
          } else if (updatedAnchorRect.top > vh - 80) {
            window.scrollBy(0, Math.ceil(updatedAnchorRect.top - minClearTop));
          }
        }
      }
    }

    if (typeof el.getBoundingClientRect === "function") {
      let rect = el.getBoundingClientRect();
      if (hasMeasuredRect(rect) && vh > 0) {
        const isOffscreen =
          rect.bottom < minClearTop ||
          rect.top > vh ||
          (vw > 0 && (rect.right < 0 || rect.left > vw));
        if (isOffscreen && typeof el.scrollIntoView === "function") {
          el.scrollIntoView({ block: "nearest", inline: "nearest" });
          rect = el.getBoundingClientRect();
        }
        const scrollY = getWindowScrollY();
        if (typeof window.scrollBy === "function") {
          if (rect.top < minClearTop && scrollY > 0) {
            window.scrollBy(0, Math.floor(rect.top - minClearTop));
          } else if (
            rect.bottom > vh &&
            rect.height <= Math.max(48, vh - minClearTop)
          ) {
            window.scrollBy(0, Math.ceil(rect.bottom - vh + 12));
          }
        }
      }
    }
  }

  globalThis[PROGRAMMATIC_FOCUS_KEY] = { target: el, at: Date.now() };
  try {
    el.focus({ preventScroll });
  } catch {
    el.focus();
  }
};

export const normalizeCandidateList = (input) => {
  if (input === null || input === undefined) return [];
  return Array.isArray(input) ? input : [input];
};

export const findFocusableCandidate = (candidates, container) => {
  for (const candidate of candidates) {
    if (!candidate) continue;
    const resolvedValue =
      typeof candidate === "function" ? candidate() : unref(candidate);
    for (const item of normalizeCandidateList(resolvedValue)) {
      const el = resolveFocusCandidateElement(item, container);
      if (el && isInlineTargetFocusable(el, container)) {
        return el;
      }
    }
  }
  return null;
};

/**
 * Hands off keyboard focus to the first visible, non-inert candidate target
 * after DOM updates settle, falling back to `#canvas-workspace` or `#main-content`
 * without trapping `Tab` or marking background regions `inert`.
 *
 * @param {HTMLElement | string | import("vue").Ref<HTMLElement | null> | (() => any) | Array<any>} targets
 * @param {Object} [options]
 * @param {HTMLElement | import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | null} [options.container]
 * @param {HTMLElement | string | import("vue").Ref<HTMLElement | null> | (() => any) | Array<any>} [options.fallback]
 * @param {boolean} [options.scrollIfOffscreen=false]
 * @param {boolean} [options.preventScroll=true]
 * @param {boolean} [options.awaitNextTick=true]
 * @param {boolean} [options.includeDefaultFallbacks=true]
 * @returns {Promise<HTMLElement | null>}
 */
export async function handoffFocus(targets, options = {}) {
  if (options.awaitNextTick !== false) {
    await nextTick();
  }
  if (typeof document === "undefined") {
    return null;
  }

  const container = resolveOption(options.container) || null;
  const el = findFocusableCandidate(
    [
      ...normalizeCandidateList(targets),
      ...normalizeCandidateList(options.fallback),
      ...(options.includeDefaultFallbacks === false
        ? []
        : DEFAULT_INLINE_FALLBACKS),
    ],
    container
  );
  if (el) {
    applyInlineFocus(el, options, container);
    return el;
  }

  return null;
}

/**
 * Coordinates trigger memory and inline focus handoff across conditional `v-if`
 * disclosures, filter chips, and workspace zoom transitions without trapping `Tab`.
 *
 * @param {Object} [options]
 * @param {import("vue").Ref<boolean> | (() => boolean)} [options.isOpen]
 * @param {any} [options.openTarget]
 * @param {any} [options.closeFallback]
 * @param {any} [options.fallback]
 * @param {HTMLElement | import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | null} [options.container]
 * @param {boolean} [options.scrollIfOffscreen=false]
 */
export function useInlineFocusHandoff(options = {}) {
  let savedTriggerEl = null;
  let savedTriggerSelector = null;

  const rememberTrigger = (explicitTrigger) => {
    if (typeof explicitTrigger === "string" && explicitTrigger.trim()) {
      savedTriggerSelector = explicitTrigger.trim();
      savedTriggerEl = queryDocumentSelector(
        savedTriggerSelector,
        resolveOption(options.container) || null
      );
      return savedTriggerEl;
    }

    const candidate =
      explicitTrigger !== undefined
        ? resolveFocusCandidateElement(
            explicitTrigger,
            resolveOption(options.container) || null
          )
        : typeof document !== "undefined"
          ? document.activeElement
          : null;

    if (
      !candidate ||
      (typeof document !== "undefined" &&
        (candidate === document.body || candidate === document.documentElement))
    ) {
      return null;
    }

    savedTriggerEl = candidate;
    const rawId =
      (typeof candidate.getAttribute === "function" &&
        candidate.getAttribute("id")) ||
      candidate.id ||
      null;
    savedTriggerSelector = rawId ? escapeIdSelector(rawId) : null;
    return savedTriggerEl;
  };

  const clearTrigger = () => {
    savedTriggerEl = null;
    savedTriggerSelector = null;
  };

  const handoff = (targets, handoffOpts = {}) =>
    handoffFocus(targets, {
      container: options.container,
      fallback:
        handoffOpts.fallback !== undefined
          ? handoffOpts.fallback
          : options.fallback,
      scrollIfOffscreen:
        handoffOpts.scrollIfOffscreen !== undefined
          ? handoffOpts.scrollIfOffscreen
          : options.scrollIfOffscreen,
      ...handoffOpts,
    });

  const restoreTrigger = (fallbackTargets, handoffOpts = {}) => {
    const triggerCandidates = [];
    if (savedTriggerEl) triggerCandidates.push(savedTriggerEl);
    if (savedTriggerSelector) triggerCandidates.push(savedTriggerSelector);
    if (!handoffOpts.keepTrigger) {
      clearTrigger();
    }
    return handoffFocus(
      [...triggerCandidates, ...normalizeCandidateList(fallbackTargets)],
      {
        container: options.container,
        fallback:
          handoffOpts.fallback !== undefined
            ? handoffOpts.fallback
            : (options.closeFallback ?? options.fallback),
        scrollIfOffscreen:
          handoffOpts.scrollIfOffscreen !== undefined
            ? handoffOpts.scrollIfOffscreen
            : false,
        ...handoffOpts,
      }
    );
  };

  let stopOpenWatcher = null;
  if (options.isOpen !== undefined) {
    stopOpenWatcher = watch(
      () => Boolean(resolveOption(options.isOpen)),
      (open, prevOpen) => {
        if (open && !prevOpen) {
          rememberTrigger();
          if (options.openTarget !== undefined) {
            void handoff(options.openTarget, {
              scrollIfOffscreen: options.scrollIfOffscreen ?? true,
            });
          }
        } else if (!open && prevOpen) {
          void restoreTrigger(options.closeFallback);
        }
      },
      { flush: "sync" }
    );

    if (getCurrentInstance()) {
      onBeforeUnmount(() => {
        if (stopOpenWatcher) stopOpenWatcher();
      });
    } else if (getCurrentScope()) {
      onScopeDispose(() => {
        if (stopOpenWatcher) stopOpenWatcher();
      });
    }
  }

  return {
    rememberTrigger,
    restoreTrigger,
    clearTrigger,
    handoff,
  };
}

export {
  handoffRetryFocus,
  useRetryFocusHandoff,
} from "./useRetryFocusHandoff.js";
