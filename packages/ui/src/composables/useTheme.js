import { computed, getCurrentScope, onScopeDispose, ref } from "vue";
import {
  PREFERS_DARK_MEDIA_QUERY,
  THEME_CHANGE_EVENT,
  THEME_MODES,
  THEME_STORAGE_KEY,
  applyThemeToDocument,
  bootstrapTheme,
  cycleThemePreference,
  detectSystemDarkPreference,
  normalizeThemePreference,
  readStoredThemePreference,
  resolveEffectiveTheme,
  subscribeThemeSync,
  writeStoredThemePreference,
} from "../utils/themeController.js";

export {
  PREFERS_DARK_MEDIA_QUERY,
  THEME_CHANGE_EVENT,
  THEME_MODES,
  THEME_STORAGE_KEY,
  applyThemeToDocument,
  bootstrapTheme,
  cycleThemePreference,
  detectSystemDarkPreference,
  normalizeThemePreference,
  readStoredThemePreference,
  resolveEffectiveTheme,
  subscribeThemeSync,
  writeStoredThemePreference,
};

/**
 * Reactive Vue theme controller synchronized with localStorage ("vitepress-theme-appearance"),
 * cross-tab storage events, and live prefers-color-scheme media query transitions.
 *
 * @param {{
 *   storage?: Storage | null,
 *   storageKey?: string,
 *   doc?: Document | null,
 *   win?: Window | null
 * }} [options]
 */
export function useTheme(options = {}) {
  const initialPreference = readStoredThemePreference(options);
  const initialSystemDark = detectSystemDarkPreference(options.win);
  const preferenceRef = ref(initialPreference);
  const systemPrefersDarkRef = ref(initialSystemDark);

  applyThemeToDocument(initialPreference, {
    doc: options.doc,
    win: options.win,
    systemPrefersDark: initialSystemDark,
    emit: false,
  });

  const resolvedTheme = computed(() =>
    resolveEffectiveTheme(preferenceRef.value, systemPrefersDarkRef.value)
  );
  const isDark = computed(() => resolvedTheme.value === "dark");

  function setTheme(nextPreference) {
    const normalized = writeStoredThemePreference(nextPreference, options);
    const currentSystemDark = detectSystemDarkPreference(options.win);
    preferenceRef.value = normalized;
    systemPrefersDarkRef.value = currentSystemDark;
    applyThemeToDocument(normalized, {
      doc: options.doc,
      win: options.win,
      systemPrefersDark: currentSystemDark,
      emit: true,
    });
    return normalized;
  }

  function cycleTheme() {
    return setTheme(cycleThemePreference(preferenceRef.value));
  }

  function syncTheme() {
    const stored = readStoredThemePreference(options);
    const currentSystemDark = detectSystemDarkPreference(options.win);
    preferenceRef.value = stored;
    systemPrefersDarkRef.value = currentSystemDark;
    return applyThemeToDocument(stored, {
      doc: options.doc,
      win: options.win,
      systemPrefersDark: currentSystemDark,
      emit: false,
    });
  }

  const preference = computed({
    get: () => preferenceRef.value,
    set: (nextValue) => {
      setTheme(nextValue);
    },
  });

  const unsubscribe = subscribeThemeSync((state) => {
    preferenceRef.value = state.preference;
    systemPrefersDarkRef.value = state.systemPrefersDark;
  }, options);

  if (getCurrentScope()) {
    onScopeDispose(() => {
      unsubscribe();
    });
  }

  return {
    preference,
    theme: preference,
    resolvedTheme,
    isDark,
    systemPrefersDark: computed(() => systemPrefersDarkRef.value),
    modes: THEME_MODES,
    setTheme,
    cycleTheme,
    syncTheme,
    stop: unsubscribe,
  };
}
