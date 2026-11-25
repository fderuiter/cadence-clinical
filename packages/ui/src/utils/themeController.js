/**
 * Framework-free DOM, storage, and media-query theme controller shared across
 * apps/web, apps/subject-portal, and docs/.vitepress (vitepress-theme-appearance).
 */

export const THEME_STORAGE_KEY = "vitepress-theme-appearance";
export const THEME_MODES = Object.freeze(["light", "dark", "auto"]);
export const PREFERS_DARK_MEDIA_QUERY = "(prefers-color-scheme: dark)";
export const THEME_CHANGE_EVENT = "cadence:theme-change";

export function normalizeThemePreference(rawValue) {
  if (typeof rawValue === "boolean") {
    return rawValue ? "dark" : "light";
  }
  if (typeof rawValue !== "string") {
    return "auto";
  }
  const v = rawValue.trim().toLowerCase();
  if (v === "light" || v === "dark") return v;
  if (v === "true") return "dark";
  if (v === "false") return "light";
  return "auto";
}

function getWin(win) {
  return win !== undefined
    ? win
    : typeof window !== "undefined"
      ? window
      : null;
}

function getDoc(doc, win) {
  if (doc !== undefined) return doc;
  if (win && win.document) return win.document;
  return typeof document !== "undefined" ? document : null;
}

function getStore(storage, win) {
  if (storage !== undefined) return storage;
  const w = getWin(win);
  try {
    return w ? w.localStorage || null : null;
  } catch {
    return null;
  }
}

export function readStoredThemePreference(options = {}) {
  const s = getStore(options.storage, options.win);
  if (!s || typeof s.getItem !== "function") return "auto";
  try {
    return normalizeThemePreference(
      s.getItem(options.storageKey || THEME_STORAGE_KEY)
    );
  } catch {
    return "auto";
  }
}

export function detectSystemDarkPreference(win) {
  const w = getWin(win);
  try {
    return Boolean(w?.matchMedia?.(PREFERS_DARK_MEDIA_QUERY)?.matches);
  } catch {
    return false;
  }
}

export function resolveEffectiveTheme(preference, systemPrefersDark = false) {
  const p = normalizeThemePreference(preference);
  return p === "dark" || (p === "auto" && systemPrefersDark) ? "dark" : "light";
}

export function applyThemeToDocument(preference, options = {}) {
  const w = getWin(options.win);
  const d = getDoc(options.doc, w);
  const normalized = normalizeThemePreference(preference);
  const sysDark =
    typeof options.systemPrefersDark === "boolean"
      ? options.systemPrefersDark
      : detectSystemDarkPreference(w);
  const resolvedTheme = resolveEffectiveTheme(normalized, sysDark);
  const root = d?.documentElement;
  if (root) {
    if (root.getAttribute("data-theme") !== normalized) {
      root.setAttribute("data-theme", normalized);
    }
    root.classList.toggle("dark", resolvedTheme === "dark");
    if (root.style) root.style.colorScheme = resolvedTheme;
  }
  if (
    options.emit !== false &&
    typeof w?.dispatchEvent === "function" &&
    typeof CustomEvent === "function"
  ) {
    w.dispatchEvent(
      new CustomEvent(THEME_CHANGE_EVENT, {
        detail: { preference: normalized, resolvedTheme },
      })
    );
  }
  return { preference: normalized, resolvedTheme };
}

export function bootstrapTheme(options = {}) {
  return applyThemeToDocument(readStoredThemePreference(options), {
    ...options,
    emit: false,
  });
}

// --- END_PREPAINT_BOOTSTRAP ---

export function writeStoredThemePreference(preference, options = {}) {
  const normalized = normalizeThemePreference(preference);
  const s = getStore(options.storage, options.win);
  if (s && typeof s.setItem === "function") {
    try {
      s.setItem(options.storageKey || THEME_STORAGE_KEY, normalized);
    } catch {
      // Ignore storage quota/security errors.
    }
  }
  return normalized;
}

export function cycleThemePreference(currentPreference) {
  const idx = THEME_MODES.indexOf(normalizeThemePreference(currentPreference));
  return THEME_MODES[(idx + 1) % THEME_MODES.length];
}

export function subscribeThemeSync(onUpdate, options = {}) {
  const w = getWin(options.win);
  const d = getDoc(options.doc, w);
  const storageKey = options.storageKey || THEME_STORAGE_KEY;
  if (!w) return () => {};

  const notify = (prefOverride, applyDom = true) => {
    const preference =
      prefOverride !== undefined
        ? normalizeThemePreference(prefOverride)
        : readStoredThemePreference({
            storage: options.storage,
            storageKey,
            win: w,
          });
    const systemPrefersDark = detectSystemDarkPreference(w);
    const resolvedTheme = applyDom
      ? applyThemeToDocument(preference, {
          doc: d,
          win: w,
          systemPrefersDark,
          emit: false,
        }).resolvedTheme
      : resolveEffectiveTheme(preference, systemPrefersDark);
    if (typeof onUpdate === "function") {
      onUpdate({ preference, resolvedTheme, systemPrefersDark });
    }
  };

  const onStorage = (e) => {
    if (!e || (e.key !== null && e.key !== storageKey)) return;
    notify(e.key === storageKey ? e.newValue : undefined, true);
  };
  const onMedia = () => notify(undefined, true);
  const onCustom = (e) => notify(e?.detail?.preference, false);

  w.addEventListener("storage", onStorage);
  w.addEventListener(THEME_CHANGE_EVENT, onCustom);

  let mql = null;
  try {
    mql = w.matchMedia?.(PREFERS_DARK_MEDIA_QUERY) || null;
    if (mql?.addEventListener) mql.addEventListener("change", onMedia);
    else if (mql?.addListener) mql.addListener(onMedia);
  } catch {
    mql = null;
  }

  return () => {
    w.removeEventListener("storage", onStorage);
    w.removeEventListener(THEME_CHANGE_EVENT, onCustom);
    if (mql?.removeEventListener) mql.removeEventListener("change", onMedia);
    else if (mql?.removeListener) mql.removeListener(onMedia);
  };
}
