<script setup>
import { computed, onBeforeUnmount, onMounted, ref, unref, watch } from "vue";
import { useTimingPreferences } from "../../composables/useTimingPreferences.js";

const props = defineProps({
  controller: {
    type: Object,
    default: null,
  },
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
  latestToastId: {
    type: String,
    default: undefined,
  },
  manageTimers: {
    type: Boolean,
    default: true,
  },
  variantStyle: {
    type: String,
    default: "clinical",
  },
});

const emit = defineEmits(["dismiss", "action", "pause", "resume"]);

const timingPreferences = useTimingPreferences();
const hostRef = ref(null);
const timerState = new Map();
const hoveredIds = ref(new Set());
const focusedIds = ref(new Set());
const explicitlyPausedIds = ref(new Set());

const effectiveManageTimers = computed(() =>
  props.controller ? false : Boolean(props.manageTimers)
);

const sourceToasts = computed(() => {
  if (props.controller) {
    const unwrapped = unref(props.controller.toasts);
    return Array.isArray(unwrapped) ? unwrapped : [];
  }
  return Array.isArray(props.toasts) ? props.toasts : [];
});

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
  sourceToasts.value.map((toast) => {
    const tone = normalizeTone(toast);
    const hasActionTarget = Boolean(
      toast?.action &&
      toast.action.label &&
      (toast.action.to || typeof toast.action.onClick === "function")
    );
    return {
      raw: toast,
      id: toast.id,
      tone,
      isAlert: tone === "error",
      title: typeof toast.title === "string" ? toast.title.trim() : "",
      message: typeof toast.message === "string" ? toast.message : "",
      action: hasActionTarget ? toast.action : null,
      timeoutMs: resolveTimeoutMs(toast, tone),
      isExplicitlyPaused: Boolean(
        toast.isExplicitlyPaused || explicitlyPausedIds.value.has(toast.id)
      ),
    };
  })
);

const assertiveToasts = computed(() =>
  normalizedToasts.value.filter((item) => item.isAlert)
);

const politeToasts = computed(() =>
  normalizedToasts.value.filter((item) => !item.isAlert)
);

const latestItemId = computed(() =>
  normalizedToasts.value.length > 0
    ? normalizedToasts.value[normalizedToasts.value.length - 1].id
    : null
);

function isInternallyPaused(id) {
  return (
    hoveredIds.value.has(id) ||
    focusedIds.value.has(id) ||
    explicitlyPausedIds.value.has(id)
  );
}

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
  if (!effectiveManageTimers.value) return;
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
  if (isInternallyPaused(id)) {
    return;
  }
  emit("resume", id);
  if (
    !effectiveManageTimers.value ||
    timingPreferences.isAutoDismissDisabled.value
  ) {
    return;
  }
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
  if (!effectiveManageTimers.value) return;
  if (
    !item ||
    item.id === undefined ||
    item.timeoutMs <= 0 ||
    timingPreferences.isAutoDismissDisabled.value
  ) {
    clearInternalTimer(item?.id);
    return;
  }
  if (timerState.has(item.id)) return;
  const scaledDuration = timingPreferences.resolveDurationMs(item.timeoutMs);
  if (scaledDuration <= 0) {
    clearInternalTimer(item.id);
    return;
  }
  const isPausedNow = isInternallyPaused(item.id);
  const entry = {
    baseTimeoutMs: item.timeoutMs,
    remainingMs: scaledDuration,
    startedAt: Date.now(),
    paused: isPausedNow,
    timerId: null,
  };
  if (!isPausedNow) {
    entry.timerId = setTimeout(() => {
      timerState.delete(item.id);
      emit("dismiss", item.id);
    }, scaledDuration);
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
        explicitlyPausedIds.value.delete(existingId);
      }
    }
    for (const item of items) {
      startInternalTimerIfNeeded(item);
    }
  },
  { immediate: true }
);

watch(
  () => timingPreferences.timingMode.value,
  () => {
    if (!effectiveManageTimers.value) return;
    for (const item of normalizedToasts.value) {
      clearInternalTimer(item.id);
      startInternalTimerIfNeeded(item);
    }
  }
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

onMounted(() => {
  const hostEl = hostRef.value;
  if (!hostEl) return;
  hostEl.addEventListener("mouseenter", handleHostMouseEnter, true);
  hostEl.addEventListener("mouseleave", handleHostMouseLeave, true);
  hostEl.addEventListener("focusin", handleHostFocusIn);
  hostEl.addEventListener("focusout", handleHostFocusOut);
});

onBeforeUnmount(() => {
  const hostEl = hostRef.value;
  if (hostEl) {
    hostEl.removeEventListener("mouseenter", handleHostMouseEnter, true);
    hostEl.removeEventListener("mouseleave", handleHostMouseLeave, true);
    hostEl.removeEventListener("focusin", handleHostFocusIn);
    hostEl.removeEventListener("focusout", handleHostFocusOut);
  }
  for (const id of Array.from(timerState.keys())) {
    clearInternalTimer(id);
  }
});

function onMouseEnter(id) {
  hoveredIds.value.add(id);
  props.controller?.setHovered?.(id, true);
  pauseToastTimer(id);
}

function onMouseLeave(id) {
  hoveredIds.value.delete(id);
  props.controller?.setHovered?.(id, false);
  resumeToastTimer(id);
}

function onFocusIn(id) {
  focusedIds.value.add(id);
  props.controller?.setFocused?.(id, true);
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
  props.controller?.setFocused?.(id, false);
  resumeToastTimer(id);
}

function handleTogglePause(toast) {
  const id = toast.id;
  if (props.controller && typeof props.controller.togglePause === "function") {
    const nowPaused = props.controller.togglePause(id);
    emit(nowPaused ? "pause" : "resume", id);
    return;
  }
  const nextSet = new Set(explicitlyPausedIds.value);
  if (nextSet.has(id)) {
    nextSet.delete(id);
    explicitlyPausedIds.value = nextSet;
    resumeToastTimer(id);
  } else {
    nextSet.add(id);
    explicitlyPausedIds.value = nextSet;
    pauseToastTimer(id);
  }
}

function handleActionClick(item) {
  if (typeof item.action?.onClick === "function") {
    item.action.onClick(item.raw);
  }
  emit("action", item.raw);
}

function handleDismissClick(id) {
  clearInternalTimer(id);
  hoveredIds.value.delete(id);
  focusedIds.value.delete(id);
  explicitlyPausedIds.value.delete(id);
  props.controller?.dismiss?.(id);
  emit("dismiss", id);
}

function resolveCardId(toastId) {
  if (!props.latestToastId) return undefined;
  return toastId === latestItemId.value ? props.latestToastId : undefined;
}
</script>

<template>
  <div
    ref="hostRef"
    class="toast-host clinical-toast-host"
    :class="{ 'toast-host-portal': variantStyle === 'portal' }"
    :data-portal-toast-host="variantStyle === 'portal' ? 'true' : undefined"
    data-focus-trap-exempt="true"
    role="region"
    :aria-label="ariaLabel"
  >
    <div
      :id="
        latestToastId && normalizedToasts.length === 0
          ? latestToastId
          : undefined
      "
      class="clinical-toast-region clinical-toast-region-assertive"
      role="alert"
      aria-live="assertive"
      aria-atomic="false"
      data-testid="toast-region-assertive"
    >
      <div
        v-for="toast in assertiveToasts"
        :id="resolveCardId(toast.id)"
        :key="toast.id"
        :data-toast-id="String(toast.id)"
        class="toast"
        :class="`toast-${toast.tone}`"
        role="alert"
        aria-live="assertive"
      >
        <div class="toast-body">
          <strong v-if="toast.title" class="toast-title">{{
            toast.title
          }}</strong>
          <span v-if="toast.message" class="toast-message">{{
            toast.message
          }}</span>
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
          ×
        </button>
      </div>
    </div>

    <div
      class="clinical-toast-region clinical-toast-region-polite"
      role="status"
      aria-live="polite"
      aria-atomic="false"
      data-testid="toast-region-polite"
    >
      <div
        v-for="toast in politeToasts"
        :id="resolveCardId(toast.id)"
        :key="toast.id"
        :data-toast-id="String(toast.id)"
        class="toast"
        :class="[
          `toast-${toast.tone}`,
          { 'toast-paused': toast.isExplicitlyPaused },
        ]"
        role="status"
        aria-live="polite"
      >
        <div class="toast-body">
          <strong v-if="toast.title" class="toast-title">{{
            toast.title
          }}</strong>
          <span v-if="toast.message" class="toast-message">{{
            toast.message
          }}</span>
          <button
            v-if="toast.action"
            type="button"
            class="toast-action"
            @click="handleActionClick(toast)"
          >
            {{ toast.action.label }}
          </button>
        </div>
        <div class="toast-controls">
          <button
            v-if="
              toast.timeoutMs > 0 &&
              !timingPreferences.isAutoDismissDisabled.value
            "
            type="button"
            class="toast-pause toast-pause-btn"
            :aria-pressed="toast.isExplicitlyPaused ? 'true' : 'false'"
            @click="handleTogglePause(toast)"
          >
            {{ toast.isExplicitlyPaused ? "Resume" : "Pause"
            }}<span class="sr-only"> notification timer</span>
          </button>
          <button
            type="button"
            class="toast-dismiss"
            :aria-label="dismissLabel"
            @click="handleDismissClick(toast.id)"
          >
            ×
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.toast-host {
  --toast-control-min: var(--semantic-control-target-min, 24px);
  position: fixed;
  right: var(--spacing-md);
  bottom: var(--spacing-md);
  z-index: calc(9999 + 51);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  max-width: min(420px, calc(100vw - 32px));
  pointer-events: none;
}

.toast-host-portal {
  --toast-control-min: var(--touch-target-min, 48px);
}

.clinical-toast-region {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.clinical-toast-region:empty {
  display: none;
}

.toast {
  display: flex;
  align-items: flex-start;
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

.toast-controls {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-2xs);
  flex-shrink: 0;
}

.toast-action {
  display: inline-flex;
  align-items: center;
  align-self: flex-start;
  min-height: var(--toast-control-min);
  min-width: var(--toast-control-min);
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

.toast-pause {
  position: relative;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--toast-control-min);
  min-width: var(--toast-control-min);
  padding: 2px var(--spacing-xs);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-xs);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: var(--font-size-xs);
  font-weight: 600;
  cursor: pointer;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.toast-pause:hover {
  background: var(--semantic-color-surface-header);
}

.toast-dismiss {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: var(--toast-control-min);
  min-height: var(--toast-control-min);
  border: none;
  background: transparent;
  color: var(--semantic-color-text);
  font-size: 1.1rem;
  cursor: pointer;
  line-height: 1;
  padding: var(--spacing-2xs) 6px;
  border-radius: var(--radius-xs);
}

.toast-dismiss:hover {
  background: var(--semantic-color-surface-header);
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
