import { nextTick, onBeforeUnmount, onMounted, ref, watch } from "vue";

const DEFAULT_ACTIVE_SELECTOR = '[aria-current="page"], [aria-selected="true"]';
const DEFAULT_OVERFLOW_THRESHOLD = 1;

/**
 * Checks whether the user prefers reduced motion.
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
 * Computes horizontal scroll overflow boundaries for a scrollable container.
 * @param {HTMLElement | null | undefined} container
 * @param {number} [threshold=1]
 * @returns {{ canScrollStart: boolean, canScrollEnd: boolean, scrollLeft: number, maxScrollLeft: number }}
 */
export function computeScrollOverflowState(
  container,
  threshold = DEFAULT_OVERFLOW_THRESHOLD
) {
  if (!container) {
    return {
      canScrollStart: false,
      canScrollEnd: false,
      scrollLeft: 0,
      maxScrollLeft: 0,
    };
  }
  const clientWidth = Number(container.clientWidth) || 0;
  const scrollWidth = Number(container.scrollWidth) || 0;
  const maxScrollLeft = Math.max(0, scrollWidth - clientWidth);
  const rawScrollLeft = Number(container.scrollLeft) || 0;
  const clampedScrollLeft = Math.min(Math.max(0, rawScrollLeft), maxScrollLeft);

  if (maxScrollLeft <= threshold) {
    return {
      canScrollStart: false,
      canScrollEnd: false,
      scrollLeft: clampedScrollLeft,
      maxScrollLeft,
    };
  }

  return {
    canScrollStart: clampedScrollLeft > threshold,
    canScrollEnd: maxScrollLeft - clampedScrollLeft > threshold,
    scrollLeft: clampedScrollLeft,
    maxScrollLeft,
  };
}

function resolveItemHorizontalBounds(container, item, currentScrollLeft) {
  const containerRect =
    typeof container.getBoundingClientRect === "function"
      ? container.getBoundingClientRect()
      : null;
  const itemRect =
    typeof item.getBoundingClientRect === "function"
      ? item.getBoundingClientRect()
      : null;

  if (
    containerRect &&
    itemRect &&
    (containerRect.width > 0 || itemRect.width > 0)
  ) {
    const start = itemRect.left - containerRect.left + currentScrollLeft;
    const width = itemRect.width;
    return { start, end: start + width, width };
  }

  let offset = 0;
  /** @type {HTMLElement | null} */
  let cursor = item;
  while (cursor && cursor !== container) {
    offset += Number(cursor.offsetLeft) || 0;
    cursor = /** @type {HTMLElement | null} */ (cursor.offsetParent);
  }
  const width = Number(item.offsetWidth) || Number(item.clientWidth) || 0;
  return { start: offset, end: offset + width, width };
}

function resolveSelectableItems(container, itemSelector) {
  if (itemSelector) {
    return Array.from(container.querySelectorAll(itemSelector));
  }
  return Array.from(container.children);
}

/** @type {WeakMap<HTMLElement, number>} */
const pendingSmoothTargets = new WeakMap();

/**
 * Scrolls the active item inside a horizontal scroll container into view:
 * - First item snaps to 0 (when it fits) so leading boundary is flush.
 * - Last item snaps to maxScrollLeft (when it fits) so trailing boundary cue clears
 *   and preceding context remains visible.
 * - Intermediate items center within the container, clamped to [0, maxScrollLeft].
 * - Uses instant scrolling ("auto") on initial mount/immediate calls or when
 *   prefers-reduced-motion: reduce is active; uses "smooth" otherwise.
 *
 * @param {HTMLElement | null | undefined} container
 * @param {{
 *   selector?: string,
 *   itemSelector?: string,
 *   immediate?: boolean,
 *   threshold?: number
 * }} [options]
 * @returns {number | null} Target scrollLeft when aligned, or null if no scroll needed/possible.
 */
export function alignActiveScrollItem(container, options = {}) {
  if (!container) return null;

  const threshold = options.threshold ?? DEFAULT_OVERFLOW_THRESHOLD;
  const { maxScrollLeft, scrollLeft: currentScrollLeft } =
    computeScrollOverflowState(container, threshold);

  if (maxScrollLeft <= threshold) {
    pendingSmoothTargets.delete(container);
    return null;
  }

  const selector = options.selector || DEFAULT_ACTIVE_SELECTOR;
  /** @type {HTMLElement | null} */
  const activeEl = container.querySelector(selector);
  if (!activeEl) return null;

  const containerWidth = Number(container.clientWidth) || 0;
  if (containerWidth <= 0) return null;

  const { start, width } = resolveItemHorizontalBounds(
    container,
    activeEl,
    currentScrollLeft
  );

  const items = resolveSelectableItems(container, options.itemSelector);
  const itemHolder =
    items.find(
      (candidate) => candidate === activeEl || candidate.contains(activeEl)
    ) || null;
  const itemIndex = itemHolder ? items.indexOf(itemHolder) : -1;
  const isFirst = itemIndex === 0;
  const isLast = itemIndex > 0 && itemIndex === items.length - 1;

  let targetLeft;
  if (width >= containerWidth) {
    targetLeft = Math.min(Math.max(0, start), maxScrollLeft);
  } else if (isFirst && start + width <= containerWidth) {
    targetLeft = 0;
  } else if (isLast && maxScrollLeft <= start) {
    targetLeft = maxScrollLeft;
  } else {
    const centered = start + width / 2 - containerWidth / 2;
    targetLeft = Math.min(Math.max(0, Math.round(centered)), maxScrollLeft);
  }

  const useAuto = Boolean(options.immediate) || prefersReducedMotion();
  const behavior = useAuto ? "auto" : "smooth";

  if (!useAuto && pendingSmoothTargets.get(container) === targetLeft) {
    return targetLeft;
  }

  if (Math.abs(currentScrollLeft - targetLeft) <= threshold) {
    pendingSmoothTargets.delete(container);
    return targetLeft;
  }

  if (useAuto) {
    pendingSmoothTargets.delete(container);
  } else {
    pendingSmoothTargets.set(container, targetLeft);
  }

  if (typeof container.scrollTo === "function") {
    try {
      container.scrollTo({ left: targetLeft, behavior });
      if (useAuto) {
        container.scrollLeft = targetLeft;
      }
      return targetLeft;
    } catch {
      // Fallback for JSDOM or legacy engines that do not accept ScrollToOptions
    }
  }

  container.scrollLeft = targetLeft;
  return targetLeft;
}

/**
 * Reactive composable that aligns the active horizontal navigation item into view
 * and tracks non-blocking start/end overflow indicator states across mount,
 * active item transitions, user scroll, and container/child ResizeObserver updates.
 *
 * @param {import("vue").Ref<HTMLElement | null | undefined>} containerRef
 * @param {{
 *   activeKey?: import("vue").Ref<unknown> | (() => unknown),
 *   selector?: string,
 *   itemSelector?: string,
 *   threshold?: number
 * }} [options]
 */
export function useActiveScrollAlignment(containerRef, options = {}) {
  const canScrollStart = ref(false);
  const canScrollEnd = ref(false);
  const threshold = options.threshold ?? DEFAULT_OVERFLOW_THRESHOLD;

  /** @type {ResizeObserver | null} */
  let resizeObserver = null;
  /** @type {MutationObserver | null} */
  let mutationObserver = null;
  /** @type {HTMLElement | null} */
  let boundContainer = null;

  function updateOverflow() {
    const el = containerRef.value;
    const state = computeScrollOverflowState(el, threshold);
    if (
      el &&
      Math.abs(state.scrollLeft - (pendingSmoothTargets.get(el) ?? -1)) <=
        threshold
    ) {
      pendingSmoothTargets.delete(el);
    }
    canScrollStart.value = state.canScrollStart;
    canScrollEnd.value = state.canScrollEnd;
  }

  /**
   * @param {{ immediate?: boolean }} [alignOptions]
   */
  function alignActive(alignOptions = {}) {
    const el = containerRef.value;
    if (!el) {
      updateOverflow();
      return null;
    }
    const target = alignActiveScrollItem(el, {
      selector: options.selector,
      itemSelector: options.itemSelector,
      immediate: alignOptions.immediate,
      threshold,
    });
    updateOverflow();
    return target;
  }

  function handleScroll() {
    updateOverflow();
  }

  function observeChildren(el) {
    if (!resizeObserver || !el) return;
    resizeObserver.disconnect();
    resizeObserver.observe(el);
    for (const child of Array.from(el.children)) {
      if (child instanceof HTMLElement) {
        resizeObserver.observe(child);
      }
    }
  }

  function bindContainer(el) {
    if (boundContainer === el) return;
    unbindContainer();
    if (!el) {
      updateOverflow();
      return;
    }
    boundContainer = el;
    el.addEventListener("scroll", handleScroll, { passive: true });

    if (typeof ResizeObserver !== "undefined") {
      resizeObserver = new ResizeObserver(() => {
        alignActive({ immediate: true });
      });
      observeChildren(el);
    }

    if (typeof MutationObserver !== "undefined") {
      mutationObserver = new MutationObserver(() => {
        if (boundContainer) {
          observeChildren(boundContainer);
        }
        updateOverflow();
      });
      mutationObserver.observe(el, { childList: true });
    }
  }

  function unbindContainer() {
    if (boundContainer) {
      boundContainer.removeEventListener("scroll", handleScroll);
      pendingSmoothTargets.delete(boundContainer);
      boundContainer = null;
    }
    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
    if (mutationObserver) {
      mutationObserver.disconnect();
      mutationObserver = null;
    }
  }

  onMounted(() => {
    bindContainer(containerRef.value || null);
    void nextTick(() => {
      bindContainer(containerRef.value || null);
      alignActive({ immediate: true });
    });
  });

  watch(
    () => containerRef.value,
    (nextEl) => {
      bindContainer(nextEl || null);
      void nextTick(() => {
        alignActive({ immediate: true });
      });
    }
  );

  if (options.activeKey) {
    watch(
      typeof options.activeKey === "function"
        ? options.activeKey
        : () => options.activeKey?.value,
      () => {
        void nextTick(() => {
          alignActive({ immediate: false });
        });
      }
    );
  }

  onBeforeUnmount(() => {
    unbindContainer();
  });

  return {
    canScrollStart,
    canScrollEnd,
    updateOverflow,
    alignActive,
  };
}
