import {
  computed,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  unref,
  watch,
} from "vue";

export const DEFAULT_VIEWPORT_MARGIN = 12;

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

export function getVisualViewportBounds() {
  if (typeof window === "undefined") {
    return {
      left: 0,
      top: 0,
      width: 1024,
      height: 768,
      right: 1024,
      bottom: 768,
    };
  }

  const docEl =
    typeof document !== "undefined" ? document.documentElement : null;
  const fallbackWidth =
    (docEl && docEl.clientWidth > 0 ? docEl.clientWidth : 0) ||
    window.innerWidth ||
    1024;
  const fallbackHeight =
    (docEl && docEl.clientHeight > 0 ? docEl.clientHeight : 0) ||
    window.innerHeight ||
    768;

  const vv = window.visualViewport;
  if (vv && vv.width > 0 && vv.height > 0) {
    const left = Number.isFinite(vv.offsetLeft) ? vv.offsetLeft : 0;
    const top = Number.isFinite(vv.offsetTop) ? vv.offsetTop : 0;
    const width = vv.width;
    const height = vv.height;
    return {
      left,
      top,
      width,
      height,
      right: left + width,
      bottom: top + height,
    };
  }

  return {
    left: 0,
    top: 0,
    width: fallbackWidth,
    height: fallbackHeight,
    right: fallbackWidth,
    bottom: fallbackHeight,
  };
}

export function observeVisualViewport(onUpdate, options = {}) {
  if (typeof window === "undefined" || typeof onUpdate !== "function") {
    return () => {};
  }
  const { trackScroll = false } = options;
  let active = true;
  const handler = () => {
    if (active) onUpdate();
  };

  window.addEventListener("resize", handler, { passive: true });
  if (trackScroll) {
    window.addEventListener("scroll", handler, {
      capture: true,
      passive: true,
    });
  }
  const vv = window.visualViewport;
  if (vv) {
    vv.addEventListener("resize", handler, { passive: true });
    vv.addEventListener("scroll", handler, { passive: true });
  }

  return () => {
    if (!active) return;
    active = false;
    window.removeEventListener("resize", handler);
    if (trackScroll) {
      window.removeEventListener("scroll", handler, true);
    }
    if (vv) {
      vv.removeEventListener("resize", handler);
      vv.removeEventListener("scroll", handler);
    }
  };
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

export function computeFloatingPosition(options = {}) {
  const {
    anchorRect,
    floatingSize,
    viewport = getVisualViewportBounds(),
    placement = "bottom",
    align = "start",
    offset = 6,
    margin = DEFAULT_VIEWPORT_MARGIN,
    flip = true,
    preferredWidth = null,
    minHeight = 96,
  } = options;

  const safeMargin = Math.max(0, Number.isFinite(margin) ? margin : 12);
  const safeOffset = Number.isFinite(offset) ? offset : 6;
  const vpLeft = Number.isFinite(viewport?.left) ? viewport.left : 0;
  const vpTop = Number.isFinite(viewport?.top) ? viewport.top : 0;
  const vpWidth = Math.max(
    0,
    Number.isFinite(viewport?.width) ? viewport.width : 1024
  );
  const vpHeight = Math.max(
    0,
    Number.isFinite(viewport?.height) ? viewport.height : 768
  );
  const vpRight = vpLeft + vpWidth;
  const vpBottom = vpTop + vpHeight;

  const maxWidth = Math.max(0, vpWidth - safeMargin * 2);
  const availHeight = Math.max(0, vpHeight - safeMargin * 2);

  if (!anchorRect) {
    return {
      left: vpLeft + safeMargin,
      top: vpTop + safeMargin,
      maxWidth,
      maxHeight: availHeight,
      placement: placement === "top" ? "top" : "bottom",
    };
  }

  const anchorLeft = Number.isFinite(anchorRect.left)
    ? anchorRect.left
    : vpLeft + safeMargin;
  const anchorTop = Number.isFinite(anchorRect.top)
    ? anchorRect.top
    : vpTop + safeMargin;
  const anchorWidth = Math.max(
    0,
    Number.isFinite(anchorRect.width)
      ? anchorRect.width
      : (anchorRect.right ?? anchorLeft) - anchorLeft
  );
  const anchorHeight = Math.max(
    0,
    Number.isFinite(anchorRect.height)
      ? anchorRect.height
      : (anchorRect.bottom ?? anchorTop) - anchorTop
  );
  const anchorRight = Number.isFinite(anchorRect.right)
    ? anchorRect.right
    : anchorLeft + anchorWidth;
  const anchorBottom = Number.isFinite(anchorRect.bottom)
    ? anchorRect.bottom
    : anchorTop + anchorHeight;

  const measuredWidth = Math.max(
    0,
    Number.isFinite(floatingSize?.width) ? floatingSize.width : 0
  );
  const measuredHeight = Math.max(
    0,
    Number.isFinite(floatingSize?.height) ? floatingSize.height : 0
  );

  const desiredWidth =
    measuredWidth > 0
      ? measuredWidth
      : Number.isFinite(preferredWidth) && preferredWidth > 0
        ? preferredWidth
        : Math.min(280, maxWidth);
  const panelWidth = Math.min(maxWidth, desiredWidth);

  let rawLeft = anchorLeft;
  if (align === "end") {
    rawLeft = anchorRight - panelWidth;
  } else if (align === "center") {
    rawLeft = anchorLeft + (anchorWidth - panelWidth) / 2;
  }

  const minLeft = vpLeft + safeMargin;
  const maxLeft = Math.max(minLeft, vpRight - safeMargin - panelWidth);
  const left = Math.min(Math.max(rawLeft, minLeft), maxLeft);

  const spaceBelow = vpBottom - safeMargin - (anchorBottom + safeOffset);
  const spaceAbove = anchorTop - safeOffset - (vpTop + safeMargin);
  const effectiveMinHeight = Math.min(
    availHeight,
    Math.max(48, Number.isFinite(minHeight) ? minHeight : 96)
  );
  const neededHeight =
    measuredHeight > 0
      ? Math.min(availHeight, measuredHeight)
      : effectiveMinHeight;

  let resolvedPlacement = placement === "top" ? "top" : "bottom";
  if (flip) {
    if (
      resolvedPlacement === "bottom" &&
      spaceBelow < neededHeight &&
      spaceAbove > spaceBelow
    ) {
      resolvedPlacement = "top";
    } else if (
      resolvedPlacement === "top" &&
      spaceAbove < neededHeight &&
      spaceBelow > spaceAbove
    ) {
      resolvedPlacement = "bottom";
    }
  }

  let top;
  let maxHeight;

  if (resolvedPlacement === "top") {
    if (spaceAbove >= effectiveMinHeight) {
      maxHeight = Math.min(availHeight, Math.max(0, spaceAbove));
      const renderedHeight =
        measuredHeight > 0 ? Math.min(measuredHeight, maxHeight) : maxHeight;
      top = Math.max(
        vpTop + safeMargin,
        anchorTop - safeOffset - renderedHeight
      );
    } else {
      const renderedHeight =
        measuredHeight > 0
          ? Math.min(measuredHeight, availHeight)
          : effectiveMinHeight;
      top = Math.max(
        vpTop + safeMargin,
        Math.min(
          anchorTop - safeOffset - renderedHeight,
          vpBottom - safeMargin - renderedHeight
        )
      );
      maxHeight = Math.max(0, vpBottom - safeMargin - top);
    }
  } else if (spaceBelow >= effectiveMinHeight) {
    top = Math.max(vpTop + safeMargin, anchorBottom + safeOffset);
    maxHeight = Math.max(0, vpBottom - safeMargin - top);
  } else {
    const renderedHeight =
      measuredHeight > 0
        ? Math.min(measuredHeight, availHeight)
        : effectiveMinHeight;
    top = Math.max(
      vpTop + safeMargin,
      Math.min(
        anchorBottom + safeOffset,
        vpBottom - safeMargin - renderedHeight
      )
    );
    maxHeight = Math.max(0, vpBottom - safeMargin - top);
  }

  return {
    left: Math.round(left),
    top: Math.round(top),
    maxWidth: Math.round(maxWidth),
    maxHeight: Math.round(maxHeight),
    placement: resolvedPlacement,
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
