import { nextTick, onBeforeUnmount, onMounted, ref, unref, watch } from "vue";
import { isJSDOMEnv, resolveOption } from "./useFocusTrap.js";

const DEFAULT_ACTIVE_SELECTOR = '[aria-current="page"], [aria-selected="true"]';

function resolveContainerElement(target) {
  const raw =
    typeof target === "function" ? target() : unref(resolveOption(target));
  if (!raw) return null;
  const el = raw.$el || raw;
  return el && typeof el === "object" && "scrollLeft" in el ? el : null;
}

/**
 * Returns true when the user has requested reduced motion via OS/browser settings.
 *
 * @returns {boolean}
 */
export function prefersReducedMotion() {
  if (
    typeof window === "undefined" ||
    typeof window.matchMedia !== "function"
  ) {
    return false;
  }
  try {
    return Boolean(
      window.matchMedia("(prefers-reduced-motion: reduce)")?.matches
    );
  } catch {
    return false;
  }
}

/**
 * Computes horizontal overflow cue visibility for a scrollable container.
 *
 * @param {HTMLElement | import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | null} container
 * @param {number} [threshold=1]
 * @returns {{ canScrollStart: boolean, canScrollEnd: boolean, scrollLeft: number, maxScrollLeft: number }}
 */
export function computeScrollOverflowState(container, threshold = 1) {
  const el = resolveContainerElement(container);
  if (!el) {
    return {
      canScrollStart: false,
      canScrollEnd: false,
      scrollLeft: 0,
      maxScrollLeft: 0,
    };
  }
  const clientWidth = Math.max(0, Number(el.clientWidth) || 0);
  const scrollWidth = Math.max(0, Number(el.scrollWidth) || 0);
  const maxScrollLeft = Math.max(0, scrollWidth - clientWidth);
  const rawScrollLeft = Math.max(0, Number(el.scrollLeft) || 0);
  const scrollLeft = Math.min(rawScrollLeft, maxScrollLeft);
  const tol = Math.max(0.5, Number(threshold) || 1);

  return {
    canScrollStart: maxScrollLeft > tol && scrollLeft > tol,
    canScrollEnd: maxScrollLeft > tol && scrollLeft < maxScrollLeft - tol,
    scrollLeft,
    maxScrollLeft,
  };
}

function measureItemWithinContainer(containerEl, activeEl) {
  const containerRect =
    typeof containerEl.getBoundingClientRect === "function"
      ? containerEl.getBoundingClientRect()
      : null;
  const activeRect =
    typeof activeEl.getBoundingClientRect === "function"
      ? activeEl.getBoundingClientRect()
      : null;

  const currentScrollLeft = Math.max(0, Number(containerEl.scrollLeft) || 0);

  if (
    containerRect &&
    activeRect &&
    (containerRect.width > 0 ||
      activeRect.width > 0 ||
      activeRect.left !== containerRect.left)
  ) {
    const itemStart = activeRect.left - containerRect.left + currentScrollLeft;
    const itemWidth = Math.max(
      0,
      activeRect.width || Number(activeEl.offsetWidth) || 0
    );
    return { itemStart, itemWidth };
  }

  let offsetLeft = 0;
  let cursor = activeEl;
  while (cursor && cursor !== containerEl) {
    offsetLeft += Number(cursor.offsetLeft) || 0;
    const nextParent = cursor.offsetParent;
    if (nextParent && containerEl.contains(nextParent)) {
      cursor = nextParent;
    } else {
      cursor = cursor.parentElement;
    }
    if (cursor === containerEl) break;
  }
  const itemWidth = Math.max(0, Number(activeEl.offsetWidth) || 0);
  return { itemStart: offsetLeft, itemWidth };
}

function resolveTopLevelChild(containerEl, descendantEl) {
  let cursor = descendantEl;
  while (
    cursor &&
    cursor.parentElement &&
    cursor.parentElement !== containerEl
  ) {
    cursor = cursor.parentElement;
  }
  return cursor?.parentElement === containerEl ? cursor : null;
}

/**
 * Aligns the active `[aria-current="page"]` or `[aria-selected="true"]` element
 * inside a horizontal scroll container while honoring `prefers-reduced-motion: reduce`.
 *
 * @param {HTMLElement | import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null) | null} container
 * @param {Object} [options]
 * @param {string} [options.selector='[aria-current="page"], [aria-selected="true"]']
 * @param {boolean} [options.immediate=false]
 * @param {"auto" | "smooth"} [options.behavior]
 * @param {number} [options.threshold=1]
 * @returns {{ aligned: boolean, targetScrollLeft: number, behavior: "auto" | "smooth", canScrollStart: boolean, canScrollEnd: boolean }}
 */
export function alignActiveScrollItem(container, options = {}) {
  const containerEl = resolveContainerElement(container);
  const immediate = Boolean(options.immediate) || prefersReducedMotion();
  const behavior = immediate ? "auto" : options.behavior || "smooth";

  if (!containerEl || typeof containerEl.querySelector !== "function") {
    return {
      aligned: false,
      targetScrollLeft: 0,
      behavior,
      canScrollStart: false,
      canScrollEnd: false,
    };
  }

  const selector = options.selector || DEFAULT_ACTIVE_SELECTOR;
  const matches =
    typeof containerEl.querySelectorAll === "function"
      ? Array.from(containerEl.querySelectorAll(selector))
      : [];
  const activeEl =
    matches.length > 0
      ? matches[matches.length - 1]
      : containerEl.querySelector(selector);

  const clientWidth = Math.max(0, Number(containerEl.clientWidth) || 0);
  const scrollWidth = Math.max(0, Number(containerEl.scrollWidth) || 0);
  const maxScrollLeft = Math.max(0, scrollWidth - clientWidth);

  if (!activeEl || clientWidth <= 0 || maxScrollLeft <= 0) {
    if (maxScrollLeft <= 0 && Number(containerEl.scrollLeft) !== 0) {
      containerEl.scrollLeft = 0;
    }
    delete containerEl.__cadenceScrollTarget;
    const overflow = computeScrollOverflowState(containerEl, options.threshold);
    return {
      aligned: false,
      targetScrollLeft: overflow.scrollLeft,
      behavior,
      canScrollStart: overflow.canScrollStart,
      canScrollEnd: overflow.canScrollEnd,
    };
  }

  const { itemStart, itemWidth } = measureItemWithinContainer(
    containerEl,
    activeEl
  );
  const topChild = resolveTopLevelChild(containerEl, activeEl);
  const isFirstItem = Boolean(
    topChild && topChild === containerEl.firstElementChild
  );
  const isLastItem = Boolean(
    topChild && topChild === containerEl.lastElementChild
  );

  let targetScrollLeft;
  if (itemWidth >= clientWidth) {
    targetScrollLeft = Math.min(
      maxScrollLeft,
      Math.max(0, Math.round(itemStart))
    );
  } else if (isFirstItem && itemStart + itemWidth <= clientWidth) {
    targetScrollLeft = 0;
  } else if (isLastItem && scrollWidth - itemStart <= clientWidth) {
    targetScrollLeft = maxScrollLeft;
  } else {
    const centered = itemStart + itemWidth / 2 - clientWidth / 2;
    targetScrollLeft = Math.min(
      maxScrollLeft,
      Math.max(0, Math.round(centered))
    );
  }

  const currentScrollLeft = Math.max(0, Number(containerEl.scrollLeft) || 0);
  if (Math.abs(currentScrollLeft - targetScrollLeft) > 0.5) {
    if (behavior === "auto") {
      delete containerEl.__cadenceScrollTarget;
      if (typeof containerEl.scrollTo === "function") {
        try {
          containerEl.scrollTo({ left: targetScrollLeft, behavior: "auto" });
        } catch {
          // Fall through to direct scrollLeft assignment
        }
      }
      containerEl.scrollLeft = targetScrollLeft;
    } else if (containerEl.__cadenceScrollTarget !== targetScrollLeft) {
      containerEl.__cadenceScrollTarget = targetScrollLeft;
      if (typeof containerEl.scrollTo === "function") {
        try {
          containerEl.scrollTo({ left: targetScrollLeft, behavior: "smooth" });
        } catch {
          containerEl.scrollLeft = targetScrollLeft;
        }
        if (isJSDOMEnv()) {
          containerEl.scrollLeft = targetScrollLeft;
        }
      } else {
        containerEl.scrollLeft = targetScrollLeft;
      }
    }
  } else {
    delete containerEl.__cadenceScrollTarget;
  }

  const overflow = computeScrollOverflowState(containerEl, options.threshold);
  return {
    aligned: true,
    targetScrollLeft,
    behavior,
    canScrollStart: overflow.canScrollStart,
    canScrollEnd: overflow.canScrollEnd,
  };
}

/**
 * Reactive composable that aligns the active item inside a horizontal scroll
 * container on mount, active-item changes, and container/child resize while
 * tracking directional overflow cues (`canScrollStart`, `canScrollEnd`).
 *
 * @param {import("vue").Ref<HTMLElement | null> | (() => HTMLElement | null)} containerRef
 * @param {Object} [options]
 * @param {import("vue").WatchSource | Array<import("vue").WatchSource>} [options.activeKey]
 * @param {string} [options.selector='[aria-current="page"], [aria-selected="true"]']
 * @param {number} [options.threshold=1]
 */
export function useActiveScrollAlignment(containerRef, options = {}) {
  const canScrollStart = ref(false);
  const canScrollEnd = ref(false);

  let resizeObserver = null;
  let observedContainer = null;

  const updateOverflowState = () => {
    const el = resolveContainerElement(containerRef);
    if (
      el &&
      typeof el.__cadenceScrollTarget === "number" &&
      Math.abs((Number(el.scrollLeft) || 0) - el.__cadenceScrollTarget) <= 1
    ) {
      delete el.__cadenceScrollTarget;
    }
    const state = computeScrollOverflowState(el, options.threshold);
    canScrollStart.value = state.canScrollStart;
    canScrollEnd.value = state.canScrollEnd;
    return state;
  };

  const alignActiveItem = (alignOptions = {}) => {
    const result = alignActiveScrollItem(containerRef, {
      selector: options.selector,
      threshold: options.threshold,
      ...alignOptions,
    });
    canScrollStart.value = result.canScrollStart;
    canScrollEnd.value = result.canScrollEnd;
    return result;
  };

  const observeContainerAndChildren = (el) => {
    if (!resizeObserver || !el) return;
    resizeObserver.disconnect();
    resizeObserver.observe(el);
    if (el.children) {
      for (const child of Array.from(el.children)) {
        resizeObserver.observe(child);
      }
    }
  };

  const teardownListeners = () => {
    if (observedContainer) {
      observedContainer.removeEventListener("scroll", updateOverflowState);
      observedContainer = null;
    }
    if (resizeObserver) {
      resizeObserver.disconnect();
    }
  };

  const bindContainer = (el) => {
    if (observedContainer === el) {
      observeContainerAndChildren(el);
      return;
    }
    teardownListeners();
    if (!el) return;
    observedContainer = el;
    el.addEventListener("scroll", updateOverflowState, { passive: true });
    if (typeof ResizeObserver !== "undefined") {
      if (!resizeObserver) {
        resizeObserver = new ResizeObserver(() => {
          alignActiveItem({ immediate: true });
        });
      }
      observeContainerAndChildren(el);
    }
  };

  onMounted(() => {
    const el = resolveContainerElement(containerRef);
    bindContainer(el);
    alignActiveItem({ immediate: true });
  });

  watch(
    () => resolveContainerElement(containerRef),
    (nextEl) => {
      bindContainer(nextEl);
      if (nextEl) {
        alignActiveItem({ immediate: true });
      } else {
        canScrollStart.value = false;
        canScrollEnd.value = false;
      }
    }
  );

  if (options.activeKey !== undefined) {
    watch(options.activeKey, async () => {
      await nextTick();
      const el = resolveContainerElement(containerRef);
      if (el) {
        observeContainerAndChildren(el);
        alignActiveItem({ immediate: false });
      }
    });
  }

  onBeforeUnmount(() => {
    teardownListeners();
    resizeObserver = null;
  });

  return {
    canScrollStart,
    canScrollEnd,
    alignActiveItem,
    updateOverflowState,
  };
}
