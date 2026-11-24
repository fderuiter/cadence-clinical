import { getCurrentScope, onScopeDispose, ref, unref } from "vue";

const INTERACTIVE_CHILD_SELECTOR =
  'button, a[href], input, select, textarea, [role="button"], [role="checkbox"], [role="link"], [role="menuitem"], [role="option"], [role="radio"], [role="switch"], [role="tab"]';

function resolveNumericOption(optionValue, fallback) {
  const raw =
    typeof optionValue === "function" ? optionValue() : unref(optionValue);
  return typeof raw === "number" && Number.isFinite(raw) ? raw : fallback;
}

function resolveModeOption(optionValue, fallback = "point") {
  const raw =
    typeof optionValue === "function" ? optionValue() : unref(optionValue);
  return typeof raw === "string" && raw.length > 0 ? raw : fallback;
}

/**
 * Shared accessible 2D spatial viewport composable (WCAG 2.2 SC 2.1.1 G202 & SC 2.5.7 G219).
 * Centralizes clamped reticle coordinates, arrow-key reticle and pan stepping,
 * single-pointer two-click endpoint placement, pointer coordinate normalization,
 * and throttled polite live-region announcements.
 */
export function useAccessibleViewport(options = {}) {
  const getMinX = () => resolveNumericOption(options.minX, 0);
  const getMaxX = () => resolveNumericOption(options.maxX, 100);
  const getMinY = () => resolveNumericOption(options.minY, 0);
  const getMaxY = () => resolveNumericOption(options.maxY, 100);

  const defaultX =
    typeof options.initialX === "number" && Number.isFinite(options.initialX)
      ? options.initialX
      : (getMinX() + getMaxX()) / 2;
  const defaultY =
    typeof options.initialY === "number" && Number.isFinite(options.initialY)
      ? options.initialY
      : (getMinY() + getMaxY()) / 2;

  const clampPoint = (rawX, rawY) => {
    const minX = getMinX();
    const maxX = getMaxX();
    const minY = getMinY();
    const maxY = getMaxY();
    const safeX =
      typeof rawX === "number" && Number.isFinite(rawX)
        ? rawX
        : (minX + maxX) / 2;
    const safeY =
      typeof rawY === "number" && Number.isFinite(rawY)
        ? rawY
        : (minY + maxY) / 2;
    return {
      x: Math.min(maxX, Math.max(minX, safeX)),
      y: Math.min(maxY, Math.max(minY, safeY)),
    };
  };

  const initialClamped = clampPoint(defaultX, defaultY);
  const reticleX = ref(initialClamped.x);
  const reticleY = ref(initialClamped.y);
  const isReticleActive = ref(Boolean(options.initialActive));
  const pendingAnchor = ref(null);

  const initialPanX = resolveNumericOption(options.initialPanX, 0);
  const initialPanY = resolveNumericOption(options.initialPanY, 0);
  const panOffset =
    options.panOffset &&
    typeof options.panOffset === "object" &&
    "value" in options.panOffset
      ? options.panOffset
      : ref({ x: initialPanX, y: initialPanY });

  const liveAnnouncement = ref("");
  const throttleMs = resolveNumericOption(options.throttleMs, 200);
  let lastAnnouncedAt = 0;
  let pendingTimer = null;
  let queuedMessage = null;

  const clearAnnouncementTimer = () => {
    if (pendingTimer !== null) {
      clearTimeout(pendingTimer);
      pendingTimer = null;
    }
  };

  const announce = (message, { immediate = false } = {}) => {
    const text = String(message ?? "").trim();
    if (!text) return;

    if (immediate || throttleMs <= 0) {
      clearAnnouncementTimer();
      queuedMessage = null;
      lastAnnouncedAt = Date.now();
      liveAnnouncement.value = text;
      return;
    }

    const now = Date.now();
    const elapsed = now - lastAnnouncedAt;
    if (elapsed >= throttleMs && pendingTimer === null) {
      lastAnnouncedAt = now;
      liveAnnouncement.value = text;
      return;
    }

    queuedMessage = text;
    if (pendingTimer === null) {
      const wait = Math.max(1, throttleMs - Math.max(0, elapsed));
      pendingTimer = setTimeout(() => {
        pendingTimer = null;
        if (queuedMessage !== null) {
          lastAnnouncedAt = Date.now();
          liveAnnouncement.value = queuedMessage;
          queuedMessage = null;
        }
      }, wait);
    }
  };

  const flushAnnouncement = () => {
    if (pendingTimer !== null) {
      clearAnnouncementTimer();
    }
    if (queuedMessage !== null) {
      lastAnnouncedAt = Date.now();
      liveAnnouncement.value = queuedMessage;
      queuedMessage = null;
    }
  };

  if (getCurrentScope()) {
    onScopeDispose(() => {
      clearAnnouncementTimer();
      queuedMessage = null;
    });
  }

  const formatPointLabel = (pt) => {
    if (typeof options.formatCoordinates === "function") {
      return options.formatCoordinates(pt);
    }
    const unit = options.unit ?? "%";
    return `${pt.x.toFixed(1)}${unit}, ${pt.y.toFixed(1)}${unit}`;
  };

  const setReticle = (x, y, { announceChange = false } = {}) => {
    const clamped = clampPoint(x, y);
    reticleX.value = clamped.x;
    reticleY.value = clamped.y;
    if (typeof options.onReticleMove === "function") {
      options.onReticleMove({ x: clamped.x, y: clamped.y });
    }
    if (announceChange) {
      announce(`Reticle at ${formatPointLabel(clamped)}`);
    }
    return clamped;
  };

  const stepReticle = (dx, dy, { announceChange = true } = {}) => {
    isReticleActive.value = true;
    return setReticle(reticleX.value + dx, reticleY.value + dy, {
      announceChange,
    });
  };

  const clientToViewport = (event, elementOverride = null) => {
    const el =
      elementOverride ||
      event?.currentTarget ||
      unref(options.viewportRef) ||
      event?.target ||
      null;

    let clientX = event?.clientX;
    let clientY = event?.clientY;
    if (event?.touches && event.touches.length > 0) {
      clientX = event.touches[0].clientX;
      clientY = event.touches[0].clientY;
    } else if (event?.changedTouches && event.changedTouches.length > 0) {
      clientX = event.changedTouches[0].clientX;
      clientY = event.changedTouches[0].clientY;
    }

    if (
      typeof clientX !== "number" ||
      !Number.isFinite(clientX) ||
      typeof clientY !== "number" ||
      !Number.isFinite(clientY)
    ) {
      const fallback = clampPoint(reticleX.value, reticleY.value);
      return { ...fallback, validPointer: false };
    }

    const rect =
      el && typeof el.getBoundingClientRect === "function"
        ? el.getBoundingClientRect()
        : { left: 0, top: 0, width: 0, height: 0 };

    const minX = getMinX();
    const maxX = getMaxX();
    const minY = getMinY();
    const maxY = getMaxY();
    const spanX = maxX - minX;
    const spanY = maxY - minY;

    const fallbackWidth = resolveNumericOption(
      options.fallbackWidth,
      el?.clientWidth || el?.offsetWidth || (spanX > 0 ? spanX : 100)
    );
    const fallbackHeight = resolveNumericOption(
      options.fallbackHeight,
      el?.clientHeight || el?.offsetHeight || (spanY > 0 ? spanY : 100)
    );

    const effectiveWidth = rect.width > 0 ? rect.width : fallbackWidth;
    const effectiveHeight = rect.height > 0 ? rect.height : fallbackHeight;

    const rawX =
      effectiveWidth > 0
        ? minX + ((clientX - rect.left) / effectiveWidth) * spanX
        : (minX + maxX) / 2;
    const rawY =
      effectiveHeight > 0
        ? minY + ((clientY - rect.top) / effectiveHeight) * spanY
        : (minY + maxY) / 2;

    const clamped = clampPoint(rawX, rawY);
    return { ...clamped, validPointer: true };
  };

  const panBy = (dx, dy, { announceChange = true } = {}) => {
    const safeDx = typeof dx === "number" && Number.isFinite(dx) ? dx : 0;
    const safeDy = typeof dy === "number" && Number.isFinite(dy) ? dy : 0;
    const next = {
      x: (panOffset.value?.x ?? 0) + safeDx,
      y: (panOffset.value?.y ?? 0) + safeDy,
    };
    panOffset.value = next;
    if (typeof options.onPan === "function") {
      options.onPan(next, { dx: safeDx, dy: safeDy });
    }
    if (announceChange) {
      announce(
        `Viewport panned to X ${Math.round(next.x)}, Y ${Math.round(next.y)}`
      );
    }
    return next;
  };

  const setPan = (x, y) => {
    const safeX = typeof x === "number" && Number.isFinite(x) ? x : 0;
    const safeY = typeof y === "number" && Number.isFinite(y) ? y : 0;
    panOffset.value = { x: safeX, y: safeY };
    return panOffset.value;
  };

  const resetPan = ({ announceChange = false } = {}) => {
    panOffset.value = { x: initialPanX, y: initialPanY };
    if (announceChange) {
      announce("Viewport pan reset", { immediate: true });
    }
    return panOffset.value;
  };

  const cancelPlacement = ({ announceChange = true } = {}) => {
    if (!pendingAnchor.value) return false;
    pendingAnchor.value = null;
    if (typeof options.onCancelPlacement === "function") {
      options.onCancelPlacement();
    }
    if (announceChange) {
      announce("Placement cancelled", { immediate: true });
    }
    return true;
  };

  const commitPointInternal = (rawPoint, source = "pointer") => {
    const point = clampPoint(rawPoint?.x, rawPoint?.y);
    setReticle(point.x, point.y, { announceChange: false });

    const mode = resolveModeOption(options.mode, "point");
    if (mode !== "segment") {
      if (typeof options.onConfirmPoint === "function") {
        options.onConfirmPoint({ x: point.x, y: point.y, source });
      }
      announce(`Selected ${formatPointLabel(point)}`, { immediate: true });
      return { completed: true, mode: "point", point };
    }

    if (!pendingAnchor.value) {
      pendingAnchor.value = { x: point.x, y: point.y };
      if (typeof options.onAnchorSet === "function") {
        options.onAnchorSet({ x: point.x, y: point.y, source });
      }
      announce(
        `Start point anchored at ${formatPointLabel(point)}. Select second endpoint to complete.`,
        { immediate: true }
      );
      return {
        completed: false,
        anchored: true,
        mode: "segment",
        anchor: { ...pendingAnchor.value },
      };
    }

    const start = { ...pendingAnchor.value };
    const end = { x: point.x, y: point.y };
    const dx = end.x - start.x;
    const dy = end.y - start.y;
    const distance = Math.sqrt(dx * dx + dy * dy);
    const minDistance = resolveNumericOption(options.minDistance, 0);

    if (distance <= minDistance) {
      announce(
        `Second point must be farther from start point (${formatPointLabel(start)}).`,
        { immediate: true }
      );
      return {
        completed: false,
        anchored: true,
        reason: "min-distance",
        distance,
        anchor: start,
      };
    }

    pendingAnchor.value = null;
    const payload = { start, end, dx, dy, distance, source };
    if (typeof options.onCompleteSegment === "function") {
      options.onCompleteSegment(payload);
    }
    announce(
      `Completed measurement from ${formatPointLabel(start)} to ${formatPointLabel(end)}`,
      { immediate: true }
    );
    return { completed: true, mode: "segment", ...payload };
  };

  const placePoint = (rawPoint, source = "pointer") => {
    return commitPointInternal(rawPoint, source);
  };

  const confirmReticlePoint = () => {
    isReticleActive.value = true;
    return commitPointInternal(
      { x: reticleX.value, y: reticleY.value },
      "keyboard"
    );
  };

  const isFromNestedControl = (event) => {
    const target = event?.target;
    const current = event?.currentTarget;
    if (!target || !current || target === current) return false;
    if (typeof target.closest === "function") {
      const match = target.closest(INTERACTIVE_CHILD_SELECTOR);
      return Boolean(match && match !== current);
    }
    return false;
  };

  const resolveReticleDelta = (event) => {
    const normalStep = resolveNumericOption(options.step, 5);
    const coarseStep = resolveNumericOption(options.shiftStep, normalStep * 3);
    const fineStep = resolveNumericOption(
      options.fineStep,
      Math.max(0.5, normalStep / 5)
    );
    const delta = event.shiftKey
      ? coarseStep
      : event.altKey
        ? fineStep
        : normalStep;

    if (event.key === "ArrowLeft") return { dx: -delta, dy: 0 };
    if (event.key === "ArrowRight") return { dx: delta, dy: 0 };
    if (event.key === "ArrowUp") return { dx: 0, dy: -delta };
    if (event.key === "ArrowDown") return { dx: 0, dy: delta };
    return null;
  };

  const handleReticleKeydown = (event) => {
    if (!event || isFromNestedControl(event)) return false;

    const move = resolveReticleDelta(event);
    if (move) {
      event.preventDefault();
      stepReticle(move.dx, move.dy, { announceChange: true });
      return true;
    }

    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      confirmReticlePoint();
      return true;
    }

    if (event.key === "Escape" && pendingAnchor.value) {
      event.preventDefault();
      event.stopPropagation();
      cancelPlacement({ announceChange: true });
      return true;
    }

    return false;
  };

  const handlePanKeydown = (event) => {
    if (!event || isFromNestedControl(event)) return false;

    const normalPan = resolveNumericOption(options.panStep, 24);
    const coarsePan = resolveNumericOption(
      options.shiftPanStep,
      normalPan * 2.5
    );
    const step = event.shiftKey ? coarsePan : normalPan;

    if (event.key === "ArrowLeft") {
      event.preventDefault();
      panBy(-step, 0, { announceChange: true });
      return true;
    }
    if (event.key === "ArrowRight") {
      event.preventDefault();
      panBy(step, 0, { announceChange: true });
      return true;
    }
    if (event.key === "ArrowUp") {
      event.preventDefault();
      panBy(0, -step, { announceChange: true });
      return true;
    }
    if (event.key === "ArrowDown") {
      event.preventDefault();
      panBy(0, step, { announceChange: true });
      return true;
    }
    return false;
  };

  return {
    reticleX,
    reticleY,
    isReticleActive,
    pendingAnchor,
    panOffset,
    liveAnnouncement,
    clampPoint,
    setReticle,
    stepReticle,
    clientToViewport,
    panBy,
    setPan,
    resetPan,
    placePoint,
    confirmReticlePoint,
    cancelPlacement,
    handleReticleKeydown,
    handlePanKeydown,
    announce,
    flushAnnouncement,
  };
}
