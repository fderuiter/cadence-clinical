import { computed, ref, watch } from "vue";
import { useTimingPreferences } from "./useTimingPreferences.js";

let globalNextToastId = 1;

const POLITE_TONES = new Set(["info", "success", "warning"]);

function normalizeTone(rawTone) {
  const lowered = String(rawTone || "")
    .trim()
    .toLowerCase();
  if (lowered === "error" || lowered === "danger") {
    return "error";
  }
  if (POLITE_TONES.has(lowered)) {
    return lowered;
  }
  return "info";
}

function normalizeAction(action) {
  if (!action || typeof action !== "object") return null;
  const label = typeof action.label === "string" ? action.label.trim() : "";
  if (!label) return null;
  const to = typeof action.to === "string" ? action.to.trim() : "";
  const onClick = typeof action.onClick === "function" ? action.onClick : null;
  if (!to && !onClick) return null;
  return {
    label,
    ...(to ? { to } : {}),
    ...(onClick ? { onClick } : {}),
  };
}

function defaultFormatError(err) {
  if (!err) return "An unexpected error occurred.";
  if (typeof err === "string") return err;
  const detail =
    err?.data?.detail ||
    err?.data?.message ||
    err?.message ||
    "Request failed.";
  if (err?.status) {
    return `${detail} (HTTP ${err.status})`;
  }
  return String(detail);
}

/**
 * Shared reactive toast queue controller implementing WCAG 2.2 SC 2.2.1, SC 2.2.2, and SC 4.1.3:
 * - Classifies `tone === "error"` into `assertiveToasts` (`role="alert"`, `aria-live="assertive"`).
 * - Classifies `"success"`, `"warning"`, and `"info"` into `politeToasts` (`role="status"`, `aria-live="polite"`).
 * - Persists error alerts until dismissed by default (`persistErrors = true`).
 * - Pauses auto-dismiss timers on pointer hover, keyboard focus, or explicit pause toggle (WCAG G4).
 * - Scales or disables auto-dismiss durations via `useTimingPreferences` (`1x`, `10x`, `off`).
 */
export function createToastController(options = {}) {
  const {
    defaultTone = "info",
    defaultTimeoutMs = 5000,
    apiErrorTimeoutMs = 0,
    persistErrors = true,
    formatError = defaultFormatError,
    timingPreferences = useTimingPreferences(),
    now = () => Date.now(),
  } = options;

  const toasts = ref([]);
  const isAllPaused = ref(false);

  /** @type {Map<number, { timerId: ReturnType<typeof setTimeout> | null, startedAt: number, remainingMs: number }>} */
  const timerStateById = new Map();

  const politeToasts = computed(() =>
    toasts.value.filter((item) => item.tone !== "error")
  );
  const assertiveToasts = computed(() =>
    toasts.value.filter((item) => item.tone === "error")
  );

  function isItemPaused(item) {
    if (!item) return false;
    return Boolean(
      item.isHovered ||
      item.isFocused ||
      item.isExplicitlyPaused ||
      isAllPaused.value
    );
  }

  function clearItemTimer(id) {
    const entry = timerStateById.get(id);
    if (entry && entry.timerId !== null) {
      clearTimeout(entry.timerId);
      entry.timerId = null;
    }
  }

  function pauseItemTimer(id) {
    const entry = timerStateById.get(id);
    if (!entry) return;
    if (entry.timerId !== null) {
      clearTimeout(entry.timerId);
      entry.timerId = null;
      const elapsed = Math.max(0, now() - entry.startedAt);
      entry.remainingMs = Math.max(1, entry.remainingMs - elapsed);
    }
  }

  function scheduleItemTimer(item) {
    if (!item) return;
    clearItemTimer(item.id);

    if (
      item.baseTimeoutMs <= 0 ||
      timingPreferences.isAutoDismissDisabled.value ||
      isItemPaused(item)
    ) {
      return;
    }

    let entry = timerStateById.get(item.id);
    if (!entry) {
      entry = {
        timerId: null,
        startedAt: now(),
        remainingMs: timingPreferences.resolveDurationMs(item.baseTimeoutMs),
      };
      timerStateById.set(item.id, entry);
    }

    if (entry.remainingMs <= 0) {
      entry.remainingMs = timingPreferences.resolveDurationMs(
        item.baseTimeoutMs
      );
    }
    if (entry.remainingMs <= 0) return;

    entry.startedAt = now();
    entry.timerId = setTimeout(() => {
      const current = toasts.value.find((t) => t.id === item.id);
      if (!current) {
        timerStateById.delete(item.id);
        return;
      }
      if (
        isItemPaused(current) ||
        timingPreferences.isAutoDismissDisabled.value
      ) {
        entry.timerId = null;
        return;
      }
      dismiss(item.id);
    }, entry.remainingMs);
  }

  const stopWatchTiming = watch(
    () => timingPreferences.timingMode.value,
    () => {
      for (const item of toasts.value) {
        if (item.baseTimeoutMs <= 0) continue;
        clearItemTimer(item.id);
        if (timingPreferences.isAutoDismissDisabled.value) {
          continue;
        }
        const resolved = timingPreferences.resolveDurationMs(
          item.baseTimeoutMs
        );
        timerStateById.set(item.id, {
          timerId: null,
          startedAt: now(),
          remainingMs: resolved,
        });
        if (!isItemPaused(item)) {
          scheduleItemTimer(item);
        }
      }
    }
  );

  function dismiss(id) {
    clearItemTimer(id);
    timerStateById.delete(id);
    toasts.value = toasts.value.filter((item) => item.id !== id);
  }

  function clear() {
    for (const id of timerStateById.keys()) {
      clearItemTimer(id);
    }
    timerStateById.clear();
    toasts.value = [];
  }

  function push(payload = {}) {
    const rawTone = payload.tone ?? payload.variant ?? defaultTone;
    const tone = normalizeTone(rawTone);
    const id = globalNextToastId++;
    const rawTimeout =
      payload.timeoutMs !== undefined
        ? payload.timeoutMs
        : payload.duration !== undefined
          ? payload.duration
          : defaultTimeoutMs;
    const requestedTimeoutMs = Number.isFinite(Number(rawTimeout))
      ? Math.max(0, Number(rawTimeout))
      : defaultTimeoutMs;
    const baseTimeoutMs =
      tone === "error" && persistErrors ? 0 : requestedTimeoutMs;

    const item = {
      id,
      tone,
      variant: tone,
      ariaRole: tone === "error" ? "alert" : "status",
      livePoliteness: tone === "error" ? "assertive" : "polite",
      title: typeof payload.title === "string" ? payload.title.trim() : "",
      message:
        payload.message !== undefined && payload.message !== null
          ? String(payload.message)
          : "",
      action: normalizeAction(payload.action),
      timeoutMs: baseTimeoutMs,
      baseTimeoutMs,
      isHovered: false,
      isFocused: false,
      isExplicitlyPaused: false,
    };

    toasts.value = [...toasts.value, item];

    if (baseTimeoutMs > 0) {
      const initialDuration =
        timingPreferences.resolveDurationMs(baseTimeoutMs);
      timerStateById.set(id, {
        timerId: null,
        startedAt: now(),
        remainingMs: initialDuration,
      });
      const reactiveItem = toasts.value.find((t) => t.id === id);
      scheduleItemTimer(reactiveItem);
    }

    return id;
  }

  function addToast(payload) {
    return push(payload);
  }

  function apiError(title, err) {
    console.error(`${title}:`, err);
    return push({
      tone: "error",
      title,
      message: formatError(err),
      timeoutMs: apiErrorTimeoutMs,
    });
  }

  function setHovered(id, hovered) {
    const item = toasts.value.find((t) => t.id === id);
    if (!item) return;
    const next = Boolean(hovered);
    if (item.isHovered === next) return;
    item.isHovered = next;
    if (next) {
      pauseItemTimer(id);
    } else if (!isItemPaused(item)) {
      scheduleItemTimer(item);
    }
  }

  function setFocused(id, focused) {
    const item = toasts.value.find((t) => t.id === id);
    if (!item) return;
    const next = Boolean(focused);
    if (item.isFocused === next) return;
    item.isFocused = next;
    if (next) {
      pauseItemTimer(id);
    } else if (!isItemPaused(item)) {
      scheduleItemTimer(item);
    }
  }

  function pause(id) {
    const item = toasts.value.find((t) => t.id === id);
    if (!item) return;
    item.isExplicitlyPaused = true;
    pauseItemTimer(id);
  }

  function resume(id) {
    const item = toasts.value.find((t) => t.id === id);
    if (!item) return;
    item.isExplicitlyPaused = false;
    if (!isItemPaused(item)) {
      scheduleItemTimer(item);
    }
  }

  function togglePause(id) {
    const item = toasts.value.find((t) => t.id === id);
    if (!item) return false;
    if (item.isExplicitlyPaused) {
      resume(id);
      return false;
    }
    pause(id);
    return true;
  }

  function pauseAll() {
    isAllPaused.value = true;
    for (const item of toasts.value) {
      pauseItemTimer(item.id);
    }
  }

  function resumeAll() {
    isAllPaused.value = false;
    for (const item of toasts.value) {
      if (!isItemPaused(item)) {
        scheduleItemTimer(item);
      }
    }
  }

  function dispose() {
    stopWatchTiming();
    clear();
  }

  return {
    toasts,
    politeToasts,
    assertiveToasts,
    isAllPaused,
    push,
    addToast,
    apiError,
    dismiss,
    clear,
    setHovered,
    setFocused,
    pause,
    resume,
    togglePause,
    pauseAll,
    resumeAll,
    dispose,
  };
}
