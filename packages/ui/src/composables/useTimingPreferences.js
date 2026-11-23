import { computed, ref } from "vue";

export const TIMING_PREFERENCE_STORAGE_KEY = "cadence_ui_timing_mode";

export const TIMING_PREFERENCE_MODES = Object.freeze({
  STANDARD: "standard",
  EXTENDED: "extended",
  OFF: "off",
});

const VALID_MODES = new Set([
  TIMING_PREFERENCE_MODES.STANDARD,
  TIMING_PREFERENCE_MODES.EXTENDED,
  TIMING_PREFERENCE_MODES.OFF,
]);

function readStoredTimingMode() {
  try {
    if (
      typeof globalThis !== "undefined" &&
      globalThis.localStorage &&
      typeof globalThis.localStorage.getItem === "function"
    ) {
      const raw = globalThis.localStorage.getItem(
        TIMING_PREFERENCE_STORAGE_KEY
      );
      if (raw && VALID_MODES.has(raw)) {
        return raw;
      }
    }
  } catch {
    // Ignore storage access errors in restricted or private contexts.
  }
  return TIMING_PREFERENCE_MODES.STANDARD;
}

function writeStoredTimingMode(mode) {
  try {
    if (
      typeof globalThis !== "undefined" &&
      globalThis.localStorage &&
      typeof globalThis.localStorage.setItem === "function"
    ) {
      globalThis.localStorage.setItem(TIMING_PREFERENCE_STORAGE_KEY, mode);
    }
  } catch {
    // Ignore storage quota or security errors.
  }
}

const timingModeRef = ref(readStoredTimingMode());
let storageListenerAttached = false;

function ensureStorageSync() {
  if (
    storageListenerAttached ||
    typeof window === "undefined" ||
    typeof window.addEventListener !== "function"
  ) {
    return;
  }
  storageListenerAttached = true;
  window.addEventListener("storage", (event) => {
    if (
      event &&
      event.key === TIMING_PREFERENCE_STORAGE_KEY &&
      VALID_MODES.has(event.newValue)
    ) {
      timingModeRef.value = event.newValue;
    }
  });
}

/**
 * Shared WCAG 2.2 SC 2.2.1 timing adjustability preference composable.
 * Supports:
 * - "standard": 1x base timeout
 * - "extended": 10x base timeout (WCAG Technique G180)
 * - "off": disable auto-dismiss / manual close only (WCAG Technique G198)
 *
 * Persists only the non-sensitive preference mode string ("standard" | "extended" | "off").
 */
export function useTimingPreferences() {
  ensureStorageSync();

  const multiplier = computed(() => {
    if (timingModeRef.value === TIMING_PREFERENCE_MODES.OFF) return 0;
    if (timingModeRef.value === TIMING_PREFERENCE_MODES.EXTENDED) return 10;
    return 1;
  });

  const isAutoDismissDisabled = computed(
    () => timingModeRef.value === TIMING_PREFERENCE_MODES.OFF
  );

  function setTimingMode(nextMode) {
    const normalized = VALID_MODES.has(nextMode)
      ? nextMode
      : TIMING_PREFERENCE_MODES.STANDARD;
    timingModeRef.value = normalized;
    writeStoredTimingMode(normalized);
    return normalized;
  }

  function resolveDurationMs(baseMs) {
    const numeric = Number(baseMs);
    if (!Number.isFinite(numeric) || numeric <= 0) {
      return 0;
    }
    if (timingModeRef.value === TIMING_PREFERENCE_MODES.OFF) {
      return 0;
    }
    if (timingModeRef.value === TIMING_PREFERENCE_MODES.EXTENDED) {
      return Math.round(numeric * 10);
    }
    return Math.round(numeric);
  }

  return {
    timingMode: timingModeRef,
    multiplier,
    isAutoDismissDisabled,
    setTimingMode,
    resolveDurationMs,
  };
}

export function resetTimingPreferencesForTests() {
  timingModeRef.value = TIMING_PREFERENCE_MODES.STANDARD;
  try {
    if (
      typeof globalThis !== "undefined" &&
      globalThis.localStorage &&
      typeof globalThis.localStorage.removeItem === "function"
    ) {
      globalThis.localStorage.removeItem(TIMING_PREFERENCE_STORAGE_KEY);
    }
  } catch {
    // Ignore cleanup errors.
  }
}
