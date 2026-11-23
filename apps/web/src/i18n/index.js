import { createI18n } from "vue-i18n";

export const SUPPORTED_LOCALES = ["en", "es", "fr", "de"];
export const DEFAULT_LOCALE = "en";

const LOCALE_LOADERS = {
  en: () => import("../locales/en.json"),
  es: () => import("../locales/es.json"),
  fr: () => import("../locales/fr.json"),
  de: () => import("../locales/de.json"),
};

const loadedLocales = new Set();
const inflightLocaleLoads = new Map();
const installedApps = new WeakSet();

export function getStoredLocale() {
  if (typeof localStorage !== "undefined") {
    const stored =
      localStorage.getItem("user_locale") || localStorage.getItem("locale");
    if (stored && SUPPORTED_LOCALES.includes(stored)) {
      return stored;
    }
  }
  return DEFAULT_LOCALE;
}

export const i18n = createI18n({
  legacy: false,
  globalInjection: true,
  locale: getStoredLocale(),
  fallbackLocale: DEFAULT_LOCALE,
  messages: {},
  missingWarn: process.env.NODE_ENV !== "production",
  fallbackWarn: process.env.NODE_ENV !== "production",
});

export function loadLocaleMessages(locale) {
  const targetLocale = SUPPORTED_LOCALES.includes(locale)
    ? locale
    : DEFAULT_LOCALE;
  if (loadedLocales.has(targetLocale)) {
    return Promise.resolve();
  }
  const existing = inflightLocaleLoads.get(targetLocale);
  if (existing) {
    return existing;
  }
  const loader = LOCALE_LOADERS[targetLocale];
  if (!loader) {
    return Promise.resolve();
  }
  const loadPromise = loader()
    .then((mod) => {
      const messages =
        mod && typeof mod === "object" && "default" in mod ? mod.default : mod;
      i18n.global.setLocaleMessage(targetLocale, messages || {});
      loadedLocales.add(targetLocale);
    })
    .finally(() => {
      inflightLocaleLoads.delete(targetLocale);
    });
  inflightLocaleLoads.set(targetLocale, loadPromise);
  return loadPromise;
}

export function preloadAllLocales() {
  return Promise.all(
    SUPPORTED_LOCALES.map((locale) => loadLocaleMessages(locale))
  );
}

export async function ensureI18nInstalled(app, requestedLocale) {
  const activeLocale =
    requestedLocale && SUPPORTED_LOCALES.includes(requestedLocale)
      ? requestedLocale
      : getStoredLocale();
  const localesToLoad =
    activeLocale === DEFAULT_LOCALE
      ? [DEFAULT_LOCALE]
      : [DEFAULT_LOCALE, activeLocale];
  await Promise.all(localesToLoad.map((loc) => loadLocaleMessages(loc)));
  if (i18n.mode === "legacy") {
    i18n.global.locale = activeLocale;
  } else {
    i18n.global.locale.value = activeLocale;
  }
  if (typeof document !== "undefined") {
    document.documentElement.lang = activeLocale;
  }
  if (app && !installedApps.has(app)) {
    app.use(i18n);
    installedApps.add(app);
  }
}

export function setGlobalLocale(locale) {
  if (!SUPPORTED_LOCALES.includes(locale)) return Promise.resolve();
  const applyLocale = () => {
    if (i18n.mode === "legacy") {
      i18n.global.locale = locale;
    } else {
      i18n.global.locale.value = locale;
    }
    if (typeof localStorage !== "undefined") {
      localStorage.setItem("user_locale", locale);
      localStorage.setItem("locale", locale);
    }
    if (typeof document !== "undefined") {
      document.documentElement.lang = locale;
    }
  };

  if (loadedLocales.has(locale)) {
    applyLocale();
    return Promise.resolve();
  }

  applyLocale();
  return loadLocaleMessages(locale);
}

if (import.meta.env?.MODE === "test") {
  const preloadPromise = preloadAllLocales();
  if (typeof globalThis.beforeAll === "function") {
    globalThis.beforeAll(() => preloadPromise);
  }
}
