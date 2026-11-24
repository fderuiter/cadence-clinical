import { computed, readonly, ref } from "vue";

export const THEME_MODES = Object.freeze(["light", "dark", "auto"]);
export const VITEPRESS_THEME_STORAGE_KEY = "vitepress-theme-appearance";
export const CADENCE_THEME_STORAGE_KEY = "cadence-theme-preference";

const DARK_MEDIA_QUERY = "(prefers-color-scheme: dark)";

const preferenceState = ref("auto");
const systemDarkState = ref(false);
const resolvedThemeState = computed(() =>
  resolveEffectiveTheme(preferenceState.value, systemDarkState.value)
);
const isDarkState = computed(() => resolvedThemeState.value === "dark");

let detachListeners = null;

export function normalizeThemePreference(raw) {
  const s = typeof raw === "string" ? raw.trim().toLowerCase() : "";
  return s === "light" || s === "dark" ? s : "auto";
}

const getStorage = (s) => {
  if (s !== undefined) return s;
  try {
    return typeof window !== "undefined" ? window.localStorage : null;
  } catch {
    return null;
  }
};

const getDoc = (d) =>
  d !== undefined ? d : typeof document !== "undefined" ? document : null;

const safeGet = (s, k) => {
  try {
    return s?.getItem?.(k) ?? null;
  } catch {
    return null;
  }
};

const safeSet = (s, k, v) => {
  try {
    s?.setItem?.(k, v);
  } catch {
    // Ignore storage quota or sandbox restrictions.
  }
};

const querySystemDark = (mm) => {
  const fn =
    mm ??
    (typeof window !== "undefined" && typeof window.matchMedia === "function"
      ? window.matchMedia.bind(window)
      : null);
  try {
    return Boolean(fn?.(DARK_MEDIA_QUERY)?.matches);
  } catch {
    return false;
  }
};

const resolveSystemDark = (opts) =>
  typeof opts?.systemPrefersDark === "boolean"
    ? opts.systemPrefersDark
    : querySystemDark(opts?.matchMedia);

export function readStoredThemePreference(storage) {
  const s = getStorage(storage);
  const raw =
    safeGet(s, VITEPRESS_THEME_STORAGE_KEY) ||
    safeGet(s, CADENCE_THEME_STORAGE_KEY);
  return raw ? normalizeThemePreference(raw) : "auto";
}

const persistBoth = (pref, s) => {
  safeSet(s, VITEPRESS_THEME_STORAGE_KEY, pref);
  safeSet(s, CADENCE_THEME_STORAGE_KEY, pref);
};

export function resolveEffectiveTheme(preference, systemPrefersDark = false) {
  const n = normalizeThemePreference(preference);
  return n === "auto" ? (systemPrefersDark ? "dark" : "light") : n;
}

export function applyThemeToDocument(preference, options = {}) {
  const normalized = normalizeThemePreference(preference);
  const systemDark = resolveSystemDark(options);
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
  if (detachListeners) return;
  const win =
    options.window ?? (typeof window !== "undefined" ? window : undefined);
  if (!win) return;

  const s = getStorage(options.storage);
  const doc = getDoc(options.document);
  const mm =
    options.matchMedia ??
    (typeof win.matchMedia === "function" ? win.matchMedia.bind(win) : null);

  const onStorage = (e) => {
    if (
      !e ||
      (e.key !== null &&
        e.key !== VITEPRESS_THEME_STORAGE_KEY &&
        e.key !== CADENCE_THEME_STORAGE_KEY)
    ) {
      return;
    }
    const next =
      e.key === null
        ? "auto"
        : e.newValue != null
          ? normalizeThemePreference(e.newValue)
          : readStoredThemePreference(s);

    if (e.key === VITEPRESS_THEME_STORAGE_KEY) {
      safeSet(s, CADENCE_THEME_STORAGE_KEY, next);
    } else if (e.key === CADENCE_THEME_STORAGE_KEY) {
      safeSet(s, VITEPRESS_THEME_STORAGE_KEY, next);
    }

    systemDarkState.value = querySystemDark(mm);
    preferenceState.value = next;
    applyThemeToDocument(next, {
      document: doc,
      matchMedia: mm,
      systemPrefersDark: systemDarkState.value,
    });
  };

  let mql = null;
  const onMedia = (e) => {
    const nextDark =
      typeof e?.matches === "boolean" ? e.matches : querySystemDark(mm);
    systemDarkState.value = nextDark;
    if (preferenceState.value === "auto") {
      applyThemeToDocument("auto", {
        document: doc,
        systemPrefersDark: nextDark,
      });
    }
  };

  win.addEventListener?.("storage", onStorage);
  try {
    mql = mm?.(DARK_MEDIA_QUERY) ?? null;
    (mql?.addEventListener
      ? mql.addEventListener.bind(mql, "change")
      : mql?.addListener?.bind(mql))?.(onMedia);
  } catch {
    mql = null;
  }

  detachListeners = () => {
    win.removeEventListener?.("storage", onStorage);
    (mql?.removeEventListener
      ? mql.removeEventListener.bind(mql, "change")
      : mql?.removeListener?.bind(mql))?.(onMedia);
    detachListeners = null;
  };
}

export function initTheme(options = {}) {
  const s = getStorage(options.storage);
  const stored = readStoredThemePreference(s);
  const systemDark = resolveSystemDark(options);

  preferenceState.value = stored;
  systemDarkState.value = systemDark;

  const applied = applyThemeToDocument(stored, {
    ...options,
    systemPrefersDark: systemDark,
  });

  const vp = safeGet(s, VITEPRESS_THEME_STORAGE_KEY);
  const cd = safeGet(s, CADENCE_THEME_STORAGE_KEY);
  if ((vp !== null || cd !== null) && vp !== cd) {
    persistBoth(stored, s);
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
  const systemDark = resolveSystemDark(options);

  preferenceState.value = normalized;
  systemDarkState.value = systemDark;

  if (options.persist !== false) {
    persistBoth(normalized, getStorage(options.storage));
  }

  return applyThemeToDocument(normalized, {
    ...options,
    systemPrefersDark: systemDark,
  });
}

export function cycleThemePreference(options = {}) {
  return setThemePreference(
    THEME_MODES[
      (THEME_MODES.indexOf(preferenceState.value) + 1) % THEME_MODES.length
    ],
    options
  );
}

export function useTheme(options = {}) {
  if (!detachListeners && typeof window !== "undefined") {
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
  systemDarkState.value = false;
}
