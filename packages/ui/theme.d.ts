export type ThemePreference = "light" | "dark" | "auto";
export type ResolvedTheme = "light" | "dark";

export interface ThemeControllerOptions {
  storage?: Storage | null;
  storageKey?: string;
  doc?: Document | null;
  win?: Window | null;
}

export interface AppliedThemeState {
  preference: ThemePreference;
  resolvedTheme: ResolvedTheme;
}

export interface ThemeSyncState extends AppliedThemeState {
  systemPrefersDark: boolean;
}

export const THEME_STORAGE_KEY: "vitepress-theme-appearance";
export const THEME_MODES: readonly ["light", "dark", "auto"];
export const PREFERS_DARK_MEDIA_QUERY: "(prefers-color-scheme: dark)";
export const THEME_CHANGE_EVENT: "cadence:theme-change";

export function normalizeThemePreference(rawValue: unknown): ThemePreference;
export function readStoredThemePreference(
  options?: ThemeControllerOptions
): ThemePreference;
export function writeStoredThemePreference(
  preference: unknown,
  options?: ThemeControllerOptions
): ThemePreference;
export function detectSystemDarkPreference(win?: Window | null): boolean;
export function resolveEffectiveTheme(
  preference: unknown,
  systemPrefersDark?: boolean
): ResolvedTheme;
export function cycleThemePreference(
  currentPreference: unknown
): ThemePreference;
export function applyThemeToDocument(
  preference: unknown,
  options?: {
    doc?: Document | null;
    win?: Window | null;
    systemPrefersDark?: boolean;
    emit?: boolean;
  }
): AppliedThemeState;
export function bootstrapTheme(
  options?: ThemeControllerOptions
): AppliedThemeState;
export function subscribeThemeSync(
  onUpdate?: (state: ThemeSyncState) => void,
  options?: ThemeControllerOptions
): () => void;
