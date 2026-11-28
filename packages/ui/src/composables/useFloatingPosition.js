import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  unref,
  watch,
} from "vue";
import {
  DEFAULT_VIEWPORT_MARGIN,
  computeFloatingPosition,
  getVisualViewportBounds,
  observeVisualViewport,
} from "../utils/floatingGeometry.js";

export {
  DEFAULT_VIEWPORT_MARGIN,
  computeFloatingPosition,
  getVisualViewportBounds,
  observeVisualViewport,
};

function resolveElement(target) {
  const raw = typeof target === "function" ? target() : unref(target);
  if (!raw) return null;
  if (typeof Element !== "undefined" && raw instanceof Element) {
    return raw;
  }
  if (raw.$el && typeof Element !== "undefined" && raw.$el instanceof Element) {
    return raw.$el;
  }
  if (typeof raw.getBoundingClientRect === "function") {
    return raw;
  }
  return null;
}

export function isTargetWithinElements(target, elements = []) {
  if (!target || !Array.isArray(elements)) return false;
  for (const entry of elements) {
    const el = resolveElement(entry);
    if (!el) continue;
    if (el === target) return true;
    if (typeof el.contains === "function" && el.contains(target)) {
      return true;
    }
  }
  return false;
}

export function didFocusExitElements(event, elements = []) {
  if (!event || !Array.isArray(elements) || elements.length === 0) {
    return false;
  }
  if (event.type === "focusin") {
    const nextTarget = event.target;
    if (!nextTarget) return false;
    if (
      typeof document !== "undefined" &&
      (nextTarget === document ||
        nextTarget === document.body ||
        nextTarget === document.documentElement)
    ) {
      return false;
    }
    return !isTargetWithinElements(nextTarget, elements);
  }
  if (!isTargetWithinElements(event.target, elements)) {
    return false;
  }
  const nextTarget = event.relatedTarget;
  if (!nextTarget) {
    return false;
  }
  return !isTargetWithinElements(nextTarget, elements);
}

export function useViewportMaxHeight(options = {}) {
  const {
    shortViewportThreshold = 520,
    shortTopOffset = DEFAULT_VIEWPORT_MARGIN,
    maxTopOffset = 80,
    topOffsetRatio = 0.12,
    bottomMargin = DEFAULT_VIEWPORT_MARGIN,
    minHeight = 120,
  } = options;

  const maxHeightPx = ref(null);
  let stopObserving = null;

  function syncViewportMaxHeight() {
    if (typeof window === "undefined") return;
    const vp = getVisualViewportBounds();
    const topOffset =
      vp.height <= shortViewportThreshold
        ? shortTopOffset
        : Math.max(
            shortTopOffset,
            Math.min(maxTopOffset, Math.round(vp.height * topOffsetRatio))
          );
    maxHeightPx.value = Math.max(
      minHeight,
      Math.floor(vp.height - topOffset - bottomMargin)
    );
  }

  onMounted(() => {
    syncViewportMaxHeight();
    stopObserving = observeVisualViewport(syncViewportMaxHeight);
  });

  onBeforeUnmount(() => {
    if (stopObserving) {
      stopObserving();
      stopObserving = null;
    }
  });

  const maxHeightStyle = computed(() =>
    maxHeightPx.value !== null
      ? { maxHeight: `${maxHeightPx.value}px` }
      : undefined
  );

  return {
    maxHeightPx,
    maxHeightStyle,
    syncViewportMaxHeight,
  };
}

export function useFloatingPosition(anchorRef, floatingRef, options = {}) {
  const coordinates = ref({
    left: DEFAULT_VIEWPORT_MARGIN,
    top: DEFAULT_VIEWPORT_MARGIN,
    maxWidth: 296,
    maxHeight: 232,
    placement: "bottom",
  });

  let resizeObserver = null;
  let stopViewportObserver = null;

  const isOpen = computed(() => {
    if (options.isOpen === undefined) return true;
    return Boolean(
      typeof options.isOpen === "function"
        ? options.isOpen()
        : unref(options.isOpen)
    );
  });

  function readOption(value, fallback) {
    const resolved = typeof value === "function" ? value() : unref(value);
    return resolved === undefined ? fallback : resolved;
  }

  function measureFloatingSize(floatingEl) {
    if (!floatingEl) return { width: 0, height: 0 };
    const rect =
      typeof floatingEl.getBoundingClientRect === "function"
        ? floatingEl.getBoundingClientRect()
        : { width: 0, height: 0 };
    const width = Math.max(rect.width || 0, floatingEl.offsetWidth || 0);
    const scrollChild =
      typeof floatingEl.querySelector === "function"
        ? floatingEl.querySelector(".clinical-popover-scroll")
        : null;
    const childNaturalHeight = scrollChild
      ? (scrollChild.scrollHeight || 0) +
        Math.max(
          0,
          (floatingEl.offsetHeight || 0) - (scrollChild.offsetHeight || 0)
        )
      : 0;
    const naturalHeight = Math.max(
      childNaturalHeight,
      floatingEl.scrollHeight || 0,
      rect.height || 0,
      floatingEl.offsetHeight || 0
    );
    return { width, height: naturalHeight };
  }

  function updatePosition() {
    if (!isOpen.value) return coordinates.value;
    const anchorEl = resolveElement(anchorRef);
    const floatingEl = resolveElement(floatingRef);
    const anchorRect =
      anchorEl && typeof anchorEl.getBoundingClientRect === "function"
        ? anchorEl.getBoundingClientRect()
        : readOption(options.anchorRect, null);
    const floatingSize = measureFloatingSize(floatingEl);

    const next = computeFloatingPosition({
      anchorRect,
      floatingSize,
      placement: readOption(options.placement, "bottom"),
      align: readOption(options.align, "start"),
      offset: readOption(options.offset, 6),
      margin: readOption(options.margin, DEFAULT_VIEWPORT_MARGIN),
      flip: readOption(options.flip, true),
      preferredWidth: readOption(options.preferredWidth, null),
      minHeight: readOption(options.minHeight, 96),
    });

    coordinates.value = next;
    return next;
  }

  function detachListeners() {
    if (resizeObserver) {
      resizeObserver.disconnect();
      resizeObserver = null;
    }
    if (stopViewportObserver) {
      stopViewportObserver();
      stopViewportObserver = null;
    }
  }

  function attachListeners() {
    if (typeof window === "undefined") return;
    if (!stopViewportObserver) {
      stopViewportObserver = observeVisualViewport(updatePosition, {
        trackScroll: true,
      });
    }
    if (typeof ResizeObserver !== "undefined") {
      if (resizeObserver) {
        resizeObserver.disconnect();
      }
      resizeObserver = new ResizeObserver(() => {
        updatePosition();
      });
      const anchorEl = resolveElement(anchorRef);
      const floatingEl = resolveElement(floatingRef);
      if (
        anchorEl &&
        typeof Element !== "undefined" &&
        anchorEl instanceof Element
      ) {
        resizeObserver.observe(anchorEl);
      }
      if (
        floatingEl &&
        typeof Element !== "undefined" &&
        floatingEl instanceof Element
      ) {
        resizeObserver.observe(floatingEl);
      }
    }
  }

  watch(
    [
      isOpen,
      () => resolveElement(anchorRef),
      () => resolveElement(floatingRef),
    ],
    ([open]) => {
      if (!open) {
        detachListeners();
        return;
      }
      updatePosition();
      nextTick(() => {
        if (!isOpen.value) return;
        attachListeners();
        updatePosition();
      });
    },
    { immediate: true }
  );

  onBeforeUnmount(() => {
    detachListeners();
  });

  const floatingStyle = computed(() => {
    const pos = coordinates.value;
    const prefWidth = readOption(options.preferredWidth, null);
    const style = {
      position: "fixed",
      left: `${pos.left}px`,
      top: `${pos.top}px`,
      maxWidth: `${pos.maxWidth}px`,
      maxHeight: `${pos.maxHeight}px`,
    };
    if (Number.isFinite(prefWidth) && prefWidth > 0) {
      style.width = `${Math.min(prefWidth, pos.maxWidth)}px`;
    }
    return style;
  });

  const placement = computed(() => coordinates.value.placement);

  return {
    coordinates,
    floatingStyle,
    placement,
    updatePosition,
  };
}
