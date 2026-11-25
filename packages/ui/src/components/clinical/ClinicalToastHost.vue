<script setup>
import {
  computed,
  getCurrentInstance,
  nextTick,
  onBeforeUnmount,
  onMounted,
  ref,
  watch,
} from "vue";
import { getVisualViewportBounds } from "../../composables/useFloatingPosition.js";
import ClinicalIcon from "./ClinicalIcon.vue";

const BATCH_CLEAR_THRESHOLD = 4;

const props = defineProps({
  toasts: {
    type: Array,
    default: () => [],
  },
  ariaLabel: {
    type: String,
    default: "Notifications",
  },
  dismissLabel: {
    type: String,
    default: "Dismiss alert",
  },
  clearAllLabel: {
    type: String,
    default: "Clear all",
  },
  position: {
    type: String,
    default: "bottom-right",
  },
  latestToastId: {
    type: String,
    default: undefined,
  },
  manageTimers: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["dismiss", "action", "pause", "resume", "clear"]);

const hostRef = ref(null);
const timerState = new Map();
const hoveredIds = ref(new Set());
const focusedIds = ref(new Set());
const dynamicTopPx = ref(null);
const dynamicMaxHeightPx = ref(null);
let headerResizeObserver = null;

const isTopRight = computed(() => props.position === "top-right");

function normalizeTone(toast) {
  const raw = String(toast?.tone || toast?.variant || "info").toLowerCase();
  if (raw === "error" || raw === "danger") return "error";
  if (raw === "warning") return "warning";
  if (raw === "success") return "success";
  return "info";
}

function resolveTimeoutMs(toast, tone) {
  if (tone === "error") return 0;
  if (typeof toast?.timeoutMs === "number") {
    return toast.timeoutMs > 0 ? toast.timeoutMs : 0;
  }
  if (typeof toast?.duration === "number") {
    return toast.duration > 0 ? toast.duration : 0;
  }
  return 0;
}

const normalizedToasts = computed(() =>
  (Array.isArray(props.toasts) ? props.toasts : []).map((toast) => {
    const tone = normalizeTone(toast);
    return {
      raw: toast,
      id: toast.id,
      tone,
      isAlert: tone === "error",
      title: typeof toast.title === "string" ? toast.title.trim() : "",
      message: typeof toast.message === "string" ? toast.message : "",
      action:
        toast.action && toast.action.label && toast.action.to
          ? toast.action
          : null,
      timeoutMs: resolveTimeoutMs(toast, tone),
    };
  })
);

const showBatchClear = computed(
  () => normalizedToasts.value.length >= BATCH_CLEAR_THRESHOLD
);

function measureHeaderClearancePx(vp) {
  if (typeof document === "undefined") return 56;
  const headerEl = document.querySelector(".app-header");
  if (headerEl && typeof headerEl.getBoundingClientRect === "function") {
    const rect = headerEl.getBoundingClientRect();
    if (rect.height > 0) {
      const visibleBottom = Math.max(0, Math.min(vp.bottom, rect.bottom));
      if (visibleBottom <= 0) {
        return 12;
      }
      return Math.max(12, Math.round(visibleBottom) + 8);
    }
  }
  const rootStyles =
    typeof window !== "undefined" &&
    typeof window.getComputedStyle === "function"
      ? window.getComputedStyle(document.documentElement)
      : null;
  const cssHeaderVar = rootStyles
    ? Number.parseFloat(rootStyles.getPropertyValue("--shell-header-height"))
    : Number.NaN;
  if (Number.isFinite(cssHeaderVar) && cssHeaderVar > 0) {
    return Math.round(cssHeaderVar) + 8;
  }
  return 56;
}

function syncViewportBounds() {
  if (typeof window === "undefined") return;
  const vp = getVisualViewportBounds();
  if (isTopRight.value) {
    const rawTop = measureHeaderClearancePx(vp);
    const maxAllowedTop = Math.max(12, Math.round(vp.height * 0.25));
    const clampedTop = Math.max(
      Math.round(vp.top) + 12,
      Math.min(Math.round(vp.top) + maxAllowedTop, rawTop)
    );
    const maxStackByRatio = Math.floor(vp.height * 0.42);
    const maxStackByClearance = Math.floor(vp.bottom - clampedTop - 88);
    const computedMaxHeight = Math.max(
      84,
      Math.min(
        Math.max(84, vp.height - 24),
        Math.min(maxStackByRatio, maxStackByClearance)
      )
    );
    dynamicTopPx.value = clampedTop;
    dynamicMaxHeightPx.value = computedMaxHeight;
  } else {
    dynamicTopPx.value = null;
    const isPortalHost = Boolean(
      hostRef.value?.getAttribute?.("data-portal-toast-host") === "true"
    );
    const bottomReserve = isPortalHost && vp.width <= 767 ? 96 : 32;
    dynamicMaxHeightPx.value = Math.max(
      96,
      Math.floor(vp.height - bottomReserve)
    );
  }
}

const hostInlineStyle = computed(() => {
  const style = {};
  if (isTopRight.value && dynamicTopPx.value !== null) {
    style.top = `${dynamicTopPx.value}px`;
  }
  if (dynamicMaxHeightPx.value !== null) {
    style.maxHeight = `${dynamicMaxHeightPx.value}px`;
  }
  return style;
});

function clearInternalTimer(id) {
  const entry = timerState.get(id);
  if (!entry) return;
  if (entry.timerId !== null) {
    clearTimeout(entry.timerId);
  }
  timerState.delete(id);
}

function pauseToastTimer(id) {
  emit("pause", id);
  if (!props.manageTimers) return;
  const entry = timerState.get(id);
  if (!entry || entry.paused) return;
  if (entry.timerId !== null) {
    clearTimeout(entry.timerId);
    entry.timerId = null;
  }
  const elapsed = Date.now() - entry.startedAt;
  entry.remainingMs = Math.max(1, entry.remainingMs - elapsed);
  entry.paused = true;
}

function resumeToastTimer(id) {
  if (hoveredIds.value.has(id) || focusedIds.value.has(id)) {
    return;
  }
  emit("resume", id);
  if (!props.manageTimers) return;
  const entry = timerState.get(id);
  if (!entry || !entry.paused) return;
  entry.paused = false;
  entry.startedAt = Date.now();
  entry.timerId = setTimeout(() => {
    timerState.delete(id);
    emit("dismiss", id);
  }, entry.remainingMs);
}

function startInternalTimerIfNeeded(item) {
  if (!props.manageTimers) return;
  if (!item || item.id === undefined || item.timeoutMs <= 0) {
    clearInternalTimer(item?.id);
    return;
  }
  if (timerState.has(item.id)) return;
  const isPausedNow =
    hoveredIds.value.has(item.id) || focusedIds.value.has(item.id);
  const entry = {
    remainingMs: item.timeoutMs,
    startedAt: Date.now(),
    paused: isPausedNow,
    timerId: null,
  };
  if (!isPausedNow) {
    entry.timerId = setTimeout(() => {
      timerState.delete(item.id);
      emit("dismiss", item.id);
    }, item.timeoutMs);
  }
  timerState.set(item.id, entry);
}

watch(
  normalizedToasts,
  (items) => {
    const activeIds = new Set(items.map((item) => item.id));
    for (const existingId of Array.from(timerState.keys())) {
      if (!activeIds.has(existingId)) {
        clearInternalTimer(existingId);
        hoveredIds.value.delete(existingId);
        focusedIds.value.delete(existingId);
      }
    }
    for (const item of items) {
      startInternalTimerIfNeeded(item);
    }
  },
  { immediate: true }
);

function resolveToastIdFromElement(el) {
  if (!el || typeof el.closest !== "function") return null;
  const card = el.closest("[data-toast-id]");
  if (!card) return null;
  const attr = card.getAttribute("data-toast-id");
  const matched = normalizedToasts.value.find(
    (item) => String(item.id) === attr
  );
  return matched ? { id: matched.id, card } : null;
}

function handleHostMouseEnter(event) {
  const resolved = resolveToastIdFromElement(event?.target);
  if (resolved) {
    onMouseEnter(resolved.id);
  }
}

function handleHostMouseLeave(event) {
  const target = event?.target;
  if (
    !target ||
    typeof target.getAttribute !== "function" ||
    !target.hasAttribute("data-toast-id")
  ) {
    return;
  }
  const related = event?.relatedTarget;
  if (
    related &&
    typeof target.contains === "function" &&
    target.contains(related)
  ) {
    return;
  }
  const resolved = resolveToastIdFromElement(target);
  if (resolved) {
    onMouseLeave(resolved.id);
  }
}

function handleHostFocusIn(event) {
  const resolved = resolveToastIdFromElement(event?.target);
  if (resolved) {
    onFocusIn(resolved.id);
  }
}

function handleHostFocusOut(event) {
  const resolved = resolveToastIdFromElement(event?.target);
  if (resolved) {
    onFocusOut(
      { currentTarget: resolved.card, relatedTarget: event?.relatedTarget },
      resolved.id
    );
  }
}

const currentInstance = getCurrentInstance();

function handleViewportOrScroll() {
  syncViewportBounds();
}

onMounted(() => {
  const hostEl = hostRef.value;
  if (hostEl) {
    hostEl.addEventListener("mouseenter", handleHostMouseEnter, true);
    hostEl.addEventListener("mouseleave", handleHostMouseLeave, true);
    hostEl.addEventListener("focusin", handleHostFocusIn);
    hostEl.addEventListener("focusout", handleHostFocusOut);
  }
  syncViewportBounds();
  if (typeof window !== "undefined") {
    window.addEventListener("resize", handleViewportOrScroll, {
      passive: true,
    });
    window.addEventListener("scroll", handleViewportOrScroll, {
      capture: true,
      passive: true,
    });
    if (window.visualViewport) {
      window.visualViewport.addEventListener("resize", handleViewportOrScroll, {
        passive: true,
      });
      window.visualViewport.addEventListener("scroll", handleViewportOrScroll, {
        passive: true,
      });
    }
  }
  if (
    typeof ResizeObserver !== "undefined" &&
    typeof document !== "undefined"
  ) {
    const headerEl = document.querySelector(".app-header");
    if (headerEl) {
      headerResizeObserver = new ResizeObserver(() => syncViewportBounds());
      headerResizeObserver.observe(headerEl);
    }
  }
});

onBeforeUnmount(() => {
  const hostEl = hostRef.value;
  if (hostEl) {
    hostEl.removeEventListener("mouseenter", handleHostMouseEnter, true);
    hostEl.removeEventListener("mouseleave", handleHostMouseLeave, true);
    hostEl.removeEventListener("focusin", handleHostFocusIn);
    hostEl.removeEventListener("focusout", handleHostFocusOut);
  }
  if (typeof window !== "undefined") {
    window.removeEventListener("resize", handleViewportOrScroll);
    window.removeEventListener("scroll", handleViewportOrScroll, true);
    if (window.visualViewport) {
      window.visualViewport.removeEventListener(
        "resize",
        handleViewportOrScroll
      );
      window.visualViewport.removeEventListener(
        "scroll",
        handleViewportOrScroll
      );
    }
  }
  if (headerResizeObserver) {
    headerResizeObserver.disconnect();
    headerResizeObserver = null;
  }
  for (const id of Array.from(timerState.keys())) {
    clearInternalTimer(id);
  }
});

watch(
  () => [props.position, normalizedToasts.value.length],
  () => {
    nextTick(syncViewportBounds);
  }
);

function onMouseEnter(id) {
  hoveredIds.value.add(id);
  pauseToastTimer(id);
}

function onMouseLeave(id) {
  hoveredIds.value.delete(id);
  resumeToastTimer(id);
}

function onFocusIn(id) {
  focusedIds.value.add(id);
  pauseToastTimer(id);
}

function onFocusOut(event, id) {
  const currentTarget = event?.currentTarget;
  const nextFocused = event?.relatedTarget;
  if (
    currentTarget &&
    nextFocused &&
    typeof currentTarget.contains === "function" &&
    currentTarget.contains(nextFocused)
  ) {
    return;
  }
  focusedIds.value.delete(id);
  resumeToastTimer(id);
}

function handleActionClick(item) {
  emit("action", item.raw);
}

function handleDismissClick(id) {
  clearInternalTimer(id);
  hoveredIds.value.delete(id);
  focusedIds.value.delete(id);
  emit("dismiss", id);
}

function handleClearAllClick() {
  const ids = normalizedToasts.value.map((item) => item.id);
  for (const id of Array.from(timerState.keys())) {
    clearInternalTimer(id);
  }
  hoveredIds.value.clear();
  focusedIds.value.clear();
  const rawProps = currentInstance?.vnode?.props;
  const hasClearHandler = Boolean(rawProps?.onClear || rawProps?.onClearOnce);
  emit("clear");
  if (!hasClearHandler) {
    for (const id of ids) {
      emit("dismiss", id);
    }
  }
}

function resolveItemId(index) {
  if (!props.latestToastId) return undefined;
  return index === normalizedToasts.value.length - 1
    ? props.latestToastId
    : undefined;
}
</script>

<template>
  <div
    ref="hostRef"
    class="toast-host"
    :class="[
      isTopRight ? 'toast-host--top-right' : 'toast-host--bottom-right',
      { 'toast-host--has-items': normalizedToasts.length > 0 },
    ]"
    :data-position="isTopRight ? 'top-right' : 'bottom-right'"
    :style="hostInlineStyle"
    data-focus-trap-exempt="true"
    role="region"
    :aria-label="ariaLabel"
  >
    <div v-if="showBatchClear" class="toast-stack-toolbar">
      <button
        type="button"
        class="toast-clear-all"
        @click="handleClearAllClick"
      >
        <ClinicalIcon name="x" :size="14" />
        <span>{{ clearAllLabel }}</span>
        <span class="toast-clear-count tabular-nums"
          >({{ normalizedToasts.length }})</span
        >
      </button>
    </div>
    <div
      v-for="(toast, index) in normalizedToasts"
      :id="resolveItemId(index)"
      :key="toast.id"
      :data-toast-id="String(toast.id)"
      class="toast"
      :class="`toast-${toast.tone}`"
      :role="toast.isAlert ? 'alert' : 'status'"
      :aria-live="toast.isAlert ? 'assertive' : 'polite'"
    >
      <div class="toast-body">
        <strong v-if="toast.title" class="toast-title">{{
          toast.title
        }}</strong>
        <span v-if="toast.message" class="toast-message">{{
          toast.message
        }}</span>
        <slot name="extra" :toast="toast.raw" />
        <button
          v-if="toast.action"
          type="button"
          class="toast-action"
          @click="handleActionClick(toast)"
        >
          {{ toast.action.label }}
        </button>
      </div>
      <button
        type="button"
        class="toast-dismiss"
        :aria-label="dismissLabel"
        @click="handleDismissClick(toast.id)"
      >
        <ClinicalIcon name="x" :size="16" />
      </button>
    </div>
  </div>
</template>

<style scoped>
.toast-host {
  position: fixed;
  right: var(--spacing-md);
  z-index: calc(9999 + 51);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  max-width: min(420px, calc(100vw - 24px));
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
  box-sizing: border-box;
  pointer-events: none;
}

.toast-host--bottom-right {
  bottom: var(--spacing-md);
  max-height: calc(100dvh - 32px);
}

.toast-host--top-right {
  top: calc(48px + var(--spacing-xs));
  bottom: auto;
  max-height: min(42dvh, calc(100dvh - 104px));
}

.toast-host--has-items {
  pointer-events: auto;
}

.toast-stack-toolbar {
  display: flex;
  justify-content: flex-end;
  flex-shrink: 0;
  position: sticky;
  top: 0;
  z-index: 2;
  padding-bottom: 2px;
  pointer-events: auto;
}

.toast-clear-all {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 4px;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 4px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-xs);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: var(--semantic-font-family);
  font-size: var(--font-size-xs);
  font-weight: 600;
  box-shadow: var(--shadow-sm);
  cursor: pointer;
}

.toast-clear-all:hover {
  background: var(--semantic-color-surface-header);
}

.toast-clear-count {
  font-variant-numeric: var(--font-variant-numeric-tabular, tabular-nums);
}

.toast {
  display: flex;
  align-items: flex-start;
  flex-shrink: 0;
  gap: var(--spacing-sm);
  padding: var(--spacing-sm) 14px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-sm);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: var(--semantic-font-family);
  box-shadow: var(--shadow-md);
  pointer-events: auto;
  transition:
    opacity var(--transition-fast),
    transform var(--transition-fast);
}

.toast-info {
  border-color: var(--semantic-color-info-border);
  background: var(--semantic-color-info-bg);
}

.toast-error {
  border-color: var(--semantic-color-error);
  background: var(--semantic-color-error-bg);
}

.toast-warning {
  border-color: var(--semantic-color-warning);
  background: var(--semantic-color-warning-bg);
}

.toast-success {
  border-color: var(--semantic-color-success);
  background: var(--semantic-color-success-bg);
}

.toast-body {
  display: flex;
  flex-direction: column;
  gap: 2px;
  flex: 1;
  min-width: 0;
}

.toast-title {
  font-size: var(--font-size-sm);
  font-weight: 600;
  color: var(--semantic-color-text);
  word-break: break-word;
}

.toast-message {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text);
  word-break: break-word;
}

.toast-action {
  display: inline-flex;
  align-items: center;
  align-self: flex-start;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  margin-top: var(--spacing-2xs);
  padding: 4px var(--spacing-xs);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-xs);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-interactive-primary);
  font-size: var(--font-size-xs);
  font-weight: 600;
  cursor: pointer;
}

.toast-action:hover {
  background: var(--semantic-color-surface-header);
}

.toast-dismiss {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  min-width: var(--semantic-control-target-min, 24px);
  min-height: var(--semantic-control-target-min, 24px);
  border: none;
  background: transparent;
  color: var(--semantic-color-text);
  cursor: pointer;
  line-height: 1;
  padding: var(--spacing-2xs) 6px;
  border-radius: var(--radius-xs);
}

.toast-dismiss:hover {
  background: var(--semantic-color-surface-header);
}

@media (pointer: coarse) {
  .toast-dismiss,
  .toast-action,
  .toast-clear-all {
    min-width: 44px;
    min-height: 44px;
  }
}

@media (max-width: 420px) {
  .toast-host--top-right {
    right: 12px;
    max-width: calc(100vw - 24px);
  }
}

@media (max-height: 360px) {
  .toast-stack-toolbar {
    position: static;
  }
}

@media (max-width: 767px) {
  .toast-host[data-portal-toast-host="true"] {
    bottom: calc(
      var(--touch-target-min, 48px) + var(--spacing-xl, 24px) +
        env(safe-area-inset-bottom, 0px)
    );
    left: var(--spacing-md);
    right: var(--spacing-md);
    max-width: none;
  }
}

@media (prefers-reduced-motion: reduce) {
  .toast {
    transition: none;
  }
}
</style>
