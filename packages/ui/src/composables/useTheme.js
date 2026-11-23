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

export function normalizeThemePreference(raw) {
  if (typeof raw !== "string") return "auto";
  const normalized = raw.trim().toLowerCase();
  return normalized === "light" || normalized === "dark" ? normalized : "auto";
}

function getStorage(customStorage) {
  if (customStorage !== undefined) return customStorage;
  try {
    return typeof window !== "undefined" ? window.localStorage : null;
  } catch {
    return null;
  }
}

function getDoc(customDocument) {
  if (customDocument !== undefined) return customDocument;
  return typeof document !== "undefined" ? document : null;
}

function safeGet(store, key) {
  try {
    return typeof store?.getItem === "function" ? store.getItem(key) : null;
  } catch {
    return null;
  }
}

function safeSet(store, key, value) {
  try {
    if (typeof store?.setItem === "function") store.setItem(key, value);
  } catch {
    // Ignore storage quota or sandbox restrictions.
  }
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

export function readStoredThemePreference(storage) {
  const store = getStorage(storage);
  const raw =
    safeGet(store, VITEPRESS_THEME_STORAGE_KEY) ||
    safeGet(store, CADENCE_THEME_STORAGE_KEY);
  return raw ? normalizeThemePreference(raw) : "auto";
}

function persistThemePreference(preference, storage) {
  const store = getStorage(storage);
  safeSet(store, VITEPRESS_THEME_STORAGE_KEY, preference);
  safeSet(store, CADENCE_THEME_STORAGE_KEY, preference);
}

export function resolveEffectiveTheme(preference, systemPrefersDark = false) {
  const normalized = normalizeThemePreference(preference);
  if (normalized === "dark" || normalized === "light") return normalized;
  return systemPrefersDark ? "dark" : "light";
}

export function applyThemeToDocument(preference, options = {}) {
  const normalized = normalizeThemePreference(preference);
  const systemDark =
    typeof options.systemPrefersDark === "boolean"
      ? options.systemPrefersDark
      : detectSystemPrefersDark(options.matchMedia);
  const effective = resolveEffectiveTheme(normalized, systemDark);
  const root =
    options.rootElement ?? getDoc(options.document)?.documentElement ?? null;

  if (root) {
    root.setAttribute("data-theme", normalized);
    root.setAttribute("data-resolved-theme", effective);
    root.classList?.toggle("dark", effective === "dark");
    if (root.style) root.style.colorScheme = effective;
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

  const storage = getStorage(options.storage);
  const doc = getDoc(options.document);
  const mm =
    options.matchMedia ??
    (typeof win.matchMedia === "function" ? win.matchMedia.bind(win) : null);

  const onStorage = (event) => {
    if (
      !event ||
      (event.key !== null &&
        event.key !== VITEPRESS_THEME_STORAGE_KEY &&
        event.key !== CADENCE_THEME_STORAGE_KEY)
    ) {
      return;
    }
    const nextPref =
      event.key === null
        ? "auto"
        : event.newValue != null
          ? normalizeThemePreference(event.newValue)
          : readStoredThemePreference(storage);

    if (event.key === VITEPRESS_THEME_STORAGE_KEY) {
      safeSet(storage, CADENCE_THEME_STORAGE_KEY, nextPref);
    } else if (event.key === CADENCE_THEME_STORAGE_KEY) {
      safeSet(storage, VITEPRESS_THEME_STORAGE_KEY, nextPref);
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

  win.addEventListener?.("storage", onStorage);
  if (typeof mm === "function") {
    try {
      mediaQueryList = mm(DARK_MEDIA_QUERY);
      if (mediaQueryList?.addEventListener) {
        mediaQueryList.addEventListener("change", onMediaChange);
      } else {
        mediaQueryList?.addListener?.(onMediaChange);
      }
    } catch {
      mediaQueryList = null;
    }
  }

  listenersAttached = true;
  detachListeners = () => {
    win.removeEventListener?.("storage", onStorage);
    if (mediaQueryList?.removeEventListener) {
      mediaQueryList.removeEventListener("change", onMediaChange);
    } else {
      mediaQueryList?.removeListener?.(onMediaChange);
    }
    listenersAttached = false;
    detachListeners = null;
  };
}

export function initTheme(options = {}) {
  const storage = getStorage(options.storage);
  const stored = readStoredThemePreference(storage);
  const systemDark = detectSystemPrefersDark(options.matchMedia);

  preferenceState.value = stored;
  systemPrefersDarkState.value = systemDark;

  const applied = applyThemeToDocument(stored, {
    ...options,
    systemPrefersDark: systemDark,
  });

  const vp = safeGet(storage, VITEPRESS_THEME_STORAGE_KEY);
  const cd = safeGet(storage, CADENCE_THEME_STORAGE_KEY);
  if ((vp !== null || cd !== null) && vp !== cd) {
    persistThemePreference(stored, storage);
  }

  if (options.listen !== false) attachGlobalListeners(options);
  return applied;
}

export function getThemePreference() {
  return preferenceState.value;
}

export function getResolvedTheme() {
  return resolvedThemeState.value;
}

export function setThemePreference(nextPreference, options = {}) {
  const normalized = normalizeThemePreference(nextPreference);
  const systemDark =
    typeof options.systemPrefersDark === "boolean"
      ? options.systemPrefersDark
      : detectSystemPrefersDark(options.matchMedia);

  preferenceState.value = normalized;
  systemPrefersDarkState.value = systemDark;

  if (options.persist !== false) {
    persistThemePreference(normalized, getStorage(options.storage));
  }

  return applyThemeToDocument(normalized, {
    ...options,
    systemPrefersDark: systemDark,
  });
}

export function cycleThemePreference(options = {}) {
  const idx = THEME_MODES.indexOf(preferenceState.value);
  return setThemePreference(
    THEME_MODES[(idx + 1) % THEME_MODES.length],
    options
  );
}

export function useTheme(options = {}) {
  if (!listenersAttached && typeof window !== "undefined") {
    initTheme(options);
  }
  return {
    modes: THEME_MODES,
    preference: readonly(preferenceState),
    resolvedTheme: resolvedThemeState,
    isDark: isDarkState,
    setTheme: (mode) => setThemePreference(mode, options),
    cycleTheme: () => cycleThemePreference(options),
    initTheme: () => initTheme(options),
  };
}

export function resetThemeControllerForTests() {
  detachListeners?.();
  preferenceState.value = "auto";
  systemPrefersDarkState.value = false;
}
