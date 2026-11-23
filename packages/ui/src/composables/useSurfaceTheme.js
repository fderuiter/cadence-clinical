// Cross-surface light/dark theme persistence shared by VitePress docs, apps/web (/demo/), and apps/subject-portal (/subject-portal/).
export const THEME_APPEARANCE_KEY = "vitepress-theme-appearance";
export const CADENCE_THEME_KEY = "cadence-theme";
export const SURFACE_THEME_EVENT = "cadence:surface-theme-change";

function normalizeThemeValue(raw) {
  const value = String(raw || "")
    .trim()
    .toLowerCase();
  if (value === "dark" || value === "force-dark") return "dark";
  if (value === "light") return "light";
  if (value === "auto") return "auto";
  return null;
}

export function resolveSurfaceTheme(optionsOrDefault = "light") {
  const isOptions = optionsOrDefault && typeof optionsOrDefault === "object";
  const fallback = isOptions
    ? optionsOrDefault.fallback || optionsOrDefault.defaultTheme || "light"
    : optionsOrDefault || "light";
  const storage = isOptions
    ? optionsOrDefault.storage
    : typeof window !== "undefined"
      ? window.localStorage
      : null;

  if (!storage) return fallback;
  try {
    const vpValue = normalizeThemeValue(storage.getItem(THEME_APPEARANCE_KEY));
    if (vpValue) return vpValue;
    const cadenceValue = normalizeThemeValue(
      storage.getItem(CADENCE_THEME_KEY)
    );
    if (cadenceValue) return cadenceValue;
  } catch {
    // Ignore storage access errors in restricted sandboxes
  }
  return fallback;
}

export function isEffectiveDarkTheme(
  themeMode = "light",
  win = typeof window !== "undefined" ? window : null
) {
  if (themeMode === "dark") return true;
  if (themeMode === "auto" && win) {
    return Boolean(win.matchMedia?.("(prefers-color-scheme: dark)")?.matches);
  }
  return false;
}

export function applySurfaceTheme(preference = "light", optionsOrRoot = {}) {
  const isElement =
    optionsOrRoot &&
    typeof optionsOrRoot === "object" &&
    typeof optionsOrRoot.setAttribute === "function";
  const options = isElement ? { rootEl: optionsOrRoot } : optionsOrRoot || {};
  const doc =
    options.doc || (typeof document !== "undefined" ? document : null);
  const win = options.win || (typeof window !== "undefined" ? window : null);
  const root = options.rootEl || doc?.documentElement || null;

  const normalized = normalizeThemeValue(preference) || "light";
  const isDark = isEffectiveDarkTheme(normalized, win);
  if (root) {
    if (options.density) {
      root.setAttribute("data-density", options.density);
    }
    root.setAttribute(
      "data-theme",
      isDark ? "dark" : normalized === "auto" ? "auto" : "light"
    );
    root.classList?.toggle("dark", isDark);
  }
  return { preference: normalized, isDark };
}

export function setSurfaceTheme(preference = "light", options = {}) {
  const normalized = normalizeThemeValue(preference) || "light";
  const storage =
    options.storage ||
    (typeof window !== "undefined" ? window.localStorage : null);
  if (storage) {
    try {
      storage.setItem(THEME_APPEARANCE_KEY, normalized);
      storage.setItem(CADENCE_THEME_KEY, normalized);
    } catch {
      // Ignore storage quota / privacy errors
    }
  }
  const result = applySurfaceTheme(normalized, options);
  const win = options.win || (typeof window !== "undefined" ? window : null);
  if (
    win &&
    typeof win.dispatchEvent === "function" &&
    typeof CustomEvent === "function"
  ) {
    try {
      win.dispatchEvent(
        new CustomEvent(SURFACE_THEME_EVENT, { detail: result })
      );
    } catch {
      // Ignore synthetic event errors in minimal DOM stubs
    }
  }
  return result;
}

export function toggleSurfaceTheme(options = {}) {
  const doc =
    options.doc || (typeof document !== "undefined" ? document : null);
  const root = doc?.documentElement || null;
  const domDark = root
    ? Boolean(
        root.classList?.contains?.("dark") ||
        root.getAttribute?.("data-theme") === "dark"
      )
    : false;
  const currentlyDark = domDark || resolveSurfaceTheme(options) === "dark";
  return setSurfaceTheme(currentlyDark ? "light" : "dark", options);
}

export function initSurfaceThemeSync({
  density = null,
  defaultTheme = "light",
  observeHtmlDarkClass = false,
  doc = typeof document !== "undefined" ? document : null,
  win = typeof window !== "undefined" ? window : null,
  storage = typeof window !== "undefined" ? window.localStorage : null,
} = {}) {
  if (!win || !doc) {
    return () => {};
  }

  const root = doc.documentElement;
  if (density) {
    root.setAttribute("data-density", density);
  }

  let hasExplicitStored;
  try {
    hasExplicitStored = Boolean(
      normalizeThemeValue(storage?.getItem(THEME_APPEARANCE_KEY)) ||
      normalizeThemeValue(storage?.getItem(CADENCE_THEME_KEY))
    );
  } catch {
    hasExplicitStored = false;
  }

  const initialTheme = resolveSurfaceTheme({ storage, fallback: defaultTheme });
  if (
    observeHtmlDarkClass &&
    !hasExplicitStored &&
    root.classList?.contains("dark")
  ) {
    setSurfaceTheme("dark", { storage, doc, win, density });
  } else {
    applySurfaceTheme(initialTheme, { doc, win, density });
  }

  const handleStorage = (event) => {
    if (
      !event ||
      event.key === null ||
      event.key === THEME_APPEARANCE_KEY ||
      event.key === CADENCE_THEME_KEY
    ) {
      const next = resolveSurfaceTheme({ storage, fallback: defaultTheme });
      applySurfaceTheme(next, { doc, win, density });
    }
  };
  win.addEventListener?.("storage", handleStorage);

  let mediaQuery = null;
  const handleMediaChange = () => {
    if (resolveSurfaceTheme({ storage, fallback: defaultTheme }) === "auto") {
      applySurfaceTheme("auto", { doc, win, density });
    }
  };
  if (typeof win.matchMedia === "function") {
    mediaQuery = win.matchMedia("(prefers-color-scheme: dark)");
    if (mediaQuery?.addEventListener) {
      mediaQuery.addEventListener("change", handleMediaChange);
    }
  }

  let observer = null;
  let syncingClass = false;
  if (observeHtmlDarkClass && typeof MutationObserver !== "undefined") {
    observer = new MutationObserver(() => {
      if (syncingClass) return;
      syncingClass = true;
      try {
        const isDark = root.classList.contains("dark");
        const nextMode = isDark ? "dark" : "light";
        root.setAttribute("data-theme", nextMode);
        try {
          storage?.setItem(THEME_APPEARANCE_KEY, nextMode);
          storage?.setItem(CADENCE_THEME_KEY, nextMode);
        } catch {
          // Ignore storage errors
        }
      } finally {
        syncingClass = false;
      }
    });
    observer.observe(root, {
      attributes: true,
      attributeFilter: ["class"],
    });
  }

  return () => {
    win.removeEventListener?.("storage", handleStorage);
    if (mediaQuery?.removeEventListener) {
      mediaQuery.removeEventListener("change", handleMediaChange);
    }
    if (observer) {
      observer.disconnect();
    }
  };
}
