import { computed, readonly, ref } from "vue";

export const THEME_MODES = Object.freeze(["light", "dark", "auto"]);
export const VITEPRESS_THEME_STORAGE_KEY = "vitepress-theme-appearance";
export const CADENCE_THEME_STORAGE_KEY = "cadence-theme-preference";

const DARK_MEDIA_QUERY = "(prefers-color-scheme: dark)";

const preferenceState = ref("auto");
const systemPrefersDarkState = ref(false);
const resolvedThemeState = computed(() =>
  resolveEffectiveTheme(preferenceState.value, systemPrefersDarkState.value)
);
const isDarkState = computed(() => resolvedThemeState.value === "dark");

let listenersAttached = false;
let detachListeners = null;

/**
 * Normalizes a raw theme string into one of "light", "dark", or "auto".
 * VitePress stores "auto" (or "system" in custom themes) for system-following mode.
 */
export function normalizeThemePreference(raw) {
  if (typeof raw !== "string") return "auto";
  const normalized = raw.trim().toLowerCase();
  if (normalized === "light" || normalized === "dark") {
    return normalized;
  }
  return "auto";
}

function getDefaultStorage(customStorage) {
  if (customStorage !== undefined) return customStorage;
  if (typeof window !== "undefined") {
    try {
      return window.localStorage;
    } catch {
      return null;
    }
  }
  return null;
}

function getDefaultDocument(customDocument) {
  if (customDocument !== undefined) return customDocument;
  if (typeof document !== "undefined") return document;
  return null;
}

function detectSystemPrefersDark(customMatchMedia) {
  const mm =
    customMatchMedia ??
    (typeof window !== "undefined" && typeof window.matchMedia === "function"
      ? window.matchMedia.bind(window)
      : null);
  if (typeof mm !== "function") return false;
  try {
    return Boolean(mm(DARK_MEDIA_QUERY)?.matches);
  } catch {
    return false;
  }
}

/**
 * Reads the stored theme preference from VitePress appearance storage first
 * (so switching appearance in VitePress docs immediately carries into co-hosted
 * demos on the same origin), falling back to cadence-theme-preference, then "auto".
 */
export function readStoredThemePreference(storage) {
  const store = getDefaultStorage(storage);
  if (!store || typeof store.getItem !== "function") {
    return "auto";
  }
  try {
    const vitepressRaw = store.getItem(VITEPRESS_THEME_STORAGE_KEY);
    if (vitepressRaw !== null && vitepressRaw !== "") {
      return normalizeThemePreference(vitepressRaw);
    }
    const cadenceRaw = store.getItem(CADENCE_THEME_STORAGE_KEY);
    if (cadenceRaw !== null && cadenceRaw !== "") {
      return normalizeThemePreference(cadenceRaw);
    }
  } catch {
    return "auto";
  }
  return "auto";
}

function persistThemePreference(preference, storage) {
  const store = getDefaultStorage(storage);
  if (!store || typeof store.setItem !== "function") return;
  try {
    store.setItem(VITEPRESS_THEME_STORAGE_KEY, preference);
    store.setItem(CADENCE_THEME_STORAGE_KEY, preference);
  } catch {
    // Ignore storage quota or sandbox restrictions.
  }
}

/**
 * Resolves the effective rendered color scheme ("light" or "dark") from a preference.
 */
export function resolveEffectiveTheme(preference, systemPrefersDark = false) {
  const normalized = normalizeThemePreference(preference);
  if (normalized === "dark") return "dark";
  if (normalized === "light") return "light";
  return systemPrefersDark ? "dark" : "light";
}

/**
 * Applies data-theme, data-resolved-theme, .dark class, and CSS color-scheme
 * onto the root <html> element.
 */
export function applyThemeToDocument(preference, options = {}) {
  const normalized = normalizeThemePreference(preference);
  const systemDark =
    typeof options.systemPrefersDark === "boolean"
      ? options.systemPrefersDark
      : detectSystemPrefersDark(options.matchMedia);
  const effective = resolveEffectiveTheme(normalized, systemDark);
  const doc = getDefaultDocument(options.document);
  const root = options.rootElement ?? doc?.documentElement ?? null;

  if (root) {
    root.setAttribute("data-theme", normalized);
    root.setAttribute("data-resolved-theme", effective);
    if (root.classList) {
      root.classList.toggle("dark", effective === "dark");
    }
    if (root.style) {
      root.style.colorScheme = effective;
    }
  }

  return {
    preference: normalized,
    resolvedTheme: effective,
    systemPrefersDark: systemDark,
  };
}

function attachGlobalListeners(options = {}) {
  if (listenersAttached) return;
  const win =
    options.window ?? (typeof window !== "undefined" ? window : undefined);
  if (!win) return;

  const storage = getDefaultStorage(options.storage);
  const doc = getDefaultDocument(options.document);
  const mm =
    options.matchMedia ??
    (typeof win.matchMedia === "function" ? win.matchMedia.bind(win) : null);

  const onStorage = (event) => {
    if (!event) return;
    if (
      event.key !== null &&
      event.key !== VITEPRESS_THEME_STORAGE_KEY &&
      event.key !== CADENCE_THEME_STORAGE_KEY
    ) {
      return;
    }
    const nextPref =
      event.key === null
        ? "auto"
        : event.newValue !== null && event.newValue !== undefined
          ? normalizeThemePreference(event.newValue)
          : readStoredThemePreference(storage);

    if (
      event.key === VITEPRESS_THEME_STORAGE_KEY &&
      storage &&
      typeof storage.setItem === "function"
    ) {
      try {
        storage.setItem(CADENCE_THEME_STORAGE_KEY, nextPref);
      } catch {
        // Ignore storage write errors in restricted frames.
      }
    } else if (
      event.key === CADENCE_THEME_STORAGE_KEY &&
      storage &&
      typeof storage.setItem === "function"
    ) {
      try {
        storage.setItem(VITEPRESS_THEME_STORAGE_KEY, nextPref);
      } catch {
        // Ignore storage write errors in restricted frames.
      }
    }

    systemPrefersDarkState.value = detectSystemPrefersDark(mm);
    preferenceState.value = nextPref;
    applyThemeToDocument(nextPref, {
      document: doc,
      matchMedia: mm,
      systemPrefersDark: systemPrefersDarkState.value,
    });
  };

  let mediaQueryList = null;
  const onMediaChange = (event) => {
    const nextDark =
      typeof event?.matches === "boolean"
        ? event.matches
        : detectSystemPrefersDark(mm);
    systemPrefersDarkState.value = nextDark;
    if (preferenceState.value === "auto") {
      applyThemeToDocument("auto", {
        document: doc,
        systemPrefersDark: nextDark,
      });
    }
  };

  if (typeof win.addEventListener === "function") {
    win.addEventListener("storage", onStorage);
  }

  if (typeof mm === "function") {
    try {
      mediaQueryList = mm(DARK_MEDIA_QUERY);
      if (mediaQueryList) {
        if (typeof mediaQueryList.addEventListener === "function") {
          mediaQueryList.addEventListener("change", onMediaChange);
        } else if (typeof mediaQueryList.addListener === "function") {
          mediaQueryList.addListener(onMediaChange);
        }
      }
    } catch {
      mediaQueryList = null;
    }
  }

  listenersAttached = true;
  detachListeners = () => {
    if (typeof win.removeEventListener === "function") {
      win.removeEventListener("storage", onStorage);
    }
    if (mediaQueryList) {
      if (typeof mediaQueryList.removeEventListener === "function") {
        mediaQueryList.removeEventListener("change", onMediaChange);
      } else if (typeof mediaQueryList.removeListener === "function") {
        mediaQueryList.removeListener(onMediaChange);
      }
    }
    listenersAttached = false;
    detachListeners = null;
  };
}

/**
 * Initializes the document theme from shared storage and attaches cross-tab
 * storage and prefers-color-scheme media listeners idempotently.
 */
export function initTheme(options = {}) {
  const storage = getDefaultStorage(options.storage);
  const stored = readStoredThemePreference(storage);
  const systemDark = detectSystemPrefersDark(options.matchMedia);

  preferenceState.value = stored;
  systemPrefersDarkState.value = systemDark;

  const applied = applyThemeToDocument(stored, {
    ...options,
    systemPrefersDark: systemDark,
  });

  // Keep both keys aligned when bootstrapping from VitePress-only or Cadence-only state.
  if (storage && typeof storage.getItem === "function") {
    try {
      const vp = storage.getItem(VITEPRESS_THEME_STORAGE_KEY);
      const cd = storage.getItem(CADENCE_THEME_STORAGE_KEY);
      if ((vp !== null || cd !== null) && vp !== cd) {
        persistThemePreference(stored, storage);
      }
    } catch {
      // Ignore storage access errors.
    }
  }

  if (options.listen !== false) {
    attachGlobalListeners(options);
  }

  return applied;
}

export function getThemePreference() {
  return preferenceState.value;
}

export function getResolvedTheme() {
  return resolvedThemeState.value;
}

/**
 * Updates, persists, and applies the user's theme preference ("light", "dark", or "auto").
 */
export function setThemePreference(nextPreference, options = {}) {
  const normalized = normalizeThemePreference(nextPreference);
  const storage = getDefaultStorage(options.storage);
  const systemDark =
    typeof options.systemPrefersDark === "boolean"
      ? options.systemPrefersDark
      : detectSystemPrefersDark(options.matchMedia);

  preferenceState.value = normalized;
  systemPrefersDarkState.value = systemDark;

  if (options.persist !== false) {
    persistThemePreference(normalized, storage);
  }

  return applyThemeToDocument(normalized, {
    ...options,
    systemPrefersDark: systemDark,
  });
}

/**
 * Cycles through "light" -> "dark" -> "auto" in deterministic order.
 */
export function cycleThemePreference(options = {}) {
  const idx = THEME_MODES.indexOf(preferenceState.value);
  const next = THEME_MODES[(idx + 1) % THEME_MODES.length];
  return setThemePreference(next, options);
}

/**
 * Shared Vue 3 theme controller composable for staff and participant apps.
 */
export function useTheme(options = {}) {
  if (!listenersAttached && typeof window !== "undefined") {
    initTheme(options);
  }

  function setTheme(mode) {
    return setThemePreference(mode, options);
  }

  function cycleTheme() {
    return cycleThemePreference(options);
  }

  return {
    modes: THEME_MODES,
    preference: readonly(preferenceState),
    resolvedTheme: resolvedThemeState,
    isDark: isDarkState,
    setTheme,
    cycleTheme,
    initTheme: () => initTheme(options),
  };
}

export function resetThemeControllerForTests() {
  if (typeof detachListeners === "function") {
    detachListeners();
  }
  preferenceState.value = "auto";
  systemPrefersDarkState.value = false;
}
