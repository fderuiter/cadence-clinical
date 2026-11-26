export const DEFAULT_VIEWPORT_MARGIN = 12;

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
