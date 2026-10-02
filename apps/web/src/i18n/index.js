import { createI18n } from "vue-i18n";
import en from "../locales/en.json";
import es from "../locales/es.json";
import fr from "../locales/fr.json";
import de from "../locales/de.json";

export const SUPPORTED_LOCALES = ["en", "es", "fr", "de"];
export const DEFAULT_LOCALE = "en";

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
  messages: {
    en,
    es,
    fr,
    de,
  },
  missingWarn: process.env.NODE_ENV !== "production",
  fallbackWarn: process.env.NODE_ENV !== "production",
});

export function setGlobalLocale(locale) {
  if (!SUPPORTED_LOCALES.includes(locale)) return;
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
}
