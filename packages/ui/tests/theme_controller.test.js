// @vitest-environment jsdom
import { describe, it, expect, beforeEach, afterEach, vi } from "vitest";
import { createApp, h, nextTick } from "vue";
import {
  ThemeSwitcher,
  THEME_MODES,
  VITEPRESS_THEME_STORAGE_KEY,
  CADENCE_THEME_STORAGE_KEY,
  normalizeThemePreference,
  readStoredThemePreference,
  resolveEffectiveTheme,
  applyThemeToDocument,
  initTheme,
  getThemePreference,
  getResolvedTheme,
  setThemePreference,
  cycleThemePreference,
  useTheme,
  resetThemeControllerForTests,
} from "../index.js";
import { toBeAccessible } from "../accessibility-matcher.js";

expect.extend({ toBeAccessible });

function createMockMatchMedia(initialDark = false) {
  let matches = initialDark;
  const listeners = new Set();
  const mql = {
    get matches() {
      return matches;
    },
    media: "(prefers-color-scheme: dark)",
    addEventListener: vi.fn((type, cb) => {
      if (type === "change") listeners.add(cb);
    }),
    removeEventListener: vi.fn((type, cb) => {
      if (type === "change") listeners.delete(cb);
    }),
    dispatchChange(nextDark) {
      matches = nextDark;
      for (const cb of listeners) {
        cb({ matches: nextDark, media: "(prefers-color-scheme: dark)" });
      }
    },
  };
  const matchMedia = vi.fn(() => mql);
  return { matchMedia, mql };
}

describe("Shared Theme Controller & Lucide ThemeSwitcher", () => {
  beforeEach(() => {
    resetThemeControllerForTests();
    window.localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.removeAttribute("data-resolved-theme");
    document.documentElement.classList.remove("dark");
    document.documentElement.style.colorScheme = "";
  });

  afterEach(() => {
    resetThemeControllerForTests();
  });

  it("normalizes preferences and resolves effective light/dark themes deterministically", () => {
    expect(THEME_MODES).toEqual(["light", "dark", "auto"]);
    expect(normalizeThemePreference("dark")).toBe("dark");
    expect(normalizeThemePreference("  LIGHT ")).toBe("light");
    expect(normalizeThemePreference("auto")).toBe("auto");
    expect(normalizeThemePreference("system")).toBe("auto");
    expect(normalizeThemePreference(null)).toBe("auto");

    expect(resolveEffectiveTheme("light", true)).toBe("light");
    expect(resolveEffectiveTheme("dark", false)).toBe("dark");
    expect(resolveEffectiveTheme("auto", false)).toBe("light");
    expect(resolveEffectiveTheme("auto", true)).toBe("dark");
  });

  it("reads VitePress appearance first, falls back to cadence-theme-preference, and aligns both keys on initTheme", () => {
    const { matchMedia } = createMockMatchMedia(true);

    window.localStorage.setItem(VITEPRESS_THEME_STORAGE_KEY, "dark");
    expect(readStoredThemePreference(window.localStorage)).toBe("dark");

    const applied = initTheme({
      storage: window.localStorage,
      matchMedia,
      document,
    });
    expect(applied).toEqual({
      preference: "dark",
      resolvedTheme: "dark",
      systemPrefersDark: true,
    });
    expect(window.localStorage.getItem(CADENCE_THEME_STORAGE_KEY)).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.getAttribute("data-resolved-theme")).toBe(
      "dark"
    );
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");

    resetThemeControllerForTests();
    window.localStorage.clear();
    window.localStorage.setItem(CADENCE_THEME_STORAGE_KEY, "light");
    expect(readStoredThemePreference(window.localStorage)).toBe("light");

    initTheme({ storage: window.localStorage, matchMedia, document });
    expect(window.localStorage.getItem(VITEPRESS_THEME_STORAGE_KEY)).toBe(
      "light"
    );
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(document.documentElement.style.colorScheme).toBe("light");
  });

  it("reacts live to prefers-color-scheme changes in auto mode while keeping explicit light/dark pinned", () => {
    const { matchMedia, mql } = createMockMatchMedia(false);
    initTheme({ storage: window.localStorage, matchMedia, document });

    expect(getThemePreference()).toBe("auto");
    expect(getResolvedTheme()).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);

    mql.dispatchChange(true);
    expect(getResolvedTheme()).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("auto");
    expect(document.documentElement.getAttribute("data-resolved-theme")).toBe(
      "dark"
    );
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");

    // Pin explicit light mode: OS dark change must not override light mode
    setThemePreference("light", {
      storage: window.localStorage,
      matchMedia,
      document,
    });
    expect(getResolvedTheme()).toBe("light");
    mql.dispatchChange(true);
    expect(getResolvedTheme()).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
  });

  it("synchronizes across co-hosted VitePress and SPA tabs on window storage events and cycles modes", () => {
    const { matchMedia } = createMockMatchMedia(true);
    initTheme({ storage: window.localStorage, matchMedia, document, window });

    window.localStorage.setItem(VITEPRESS_THEME_STORAGE_KEY, "dark");
    window.dispatchEvent(
      new StorageEvent("storage", {
        key: VITEPRESS_THEME_STORAGE_KEY,
        newValue: "dark",
      })
    );
    expect(getThemePreference()).toBe("dark");
    expect(window.localStorage.getItem(CADENCE_THEME_STORAGE_KEY)).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);

    window.localStorage.setItem(CADENCE_THEME_STORAGE_KEY, "light");
    window.dispatchEvent(
      new StorageEvent("storage", {
        key: CADENCE_THEME_STORAGE_KEY,
        newValue: "light",
      })
    );
    expect(getThemePreference()).toBe("light");
    expect(window.localStorage.getItem(VITEPRESS_THEME_STORAGE_KEY)).toBe(
      "light"
    );
    expect(document.documentElement.classList.contains("dark")).toBe(false);

    // Clearing storage falls back to auto (resolved against dark OS media)
    window.localStorage.clear();
    window.dispatchEvent(
      new StorageEvent("storage", {
        key: null,
        newValue: null,
      })
    );
    expect(getThemePreference()).toBe("auto");
    expect(getResolvedTheme()).toBe("dark");

    // Cycle order: auto -> light -> dark -> auto
    expect(cycleThemePreference({ matchMedia }).preference).toBe("light");
    expect(cycleThemePreference({ matchMedia }).preference).toBe("dark");
    expect(cycleThemePreference({ matchMedia }).preference).toBe("auto");
  });

  it("handles applyThemeToDocument and useTheme gracefully under restricted storage", () => {
    const throwingStorage = {
      getItem() {
        throw new Error("Quota/SecurityError");
      },
      setItem() {
        throw new Error("Quota/SecurityError");
      },
    };
    expect(readStoredThemePreference(throwingStorage)).toBe("auto");
    const applied = applyThemeToDocument("dark", { document });
    expect(applied.resolvedTheme).toBe("dark");

    const theme = useTheme({ storage: throwingStorage, listen: false });
    expect(theme.modes).toEqual(["light", "dark", "auto"]);
    theme.setTheme("dark");
    expect(theme.isDark.value).toBe(true);
    theme.cycleTheme();
    expect(theme.preference.value).toBe("auto");
    theme.initTheme();
  });

  it("renders Lucide SVG icons with zero Unicode emojis, accessible ARIA state, and passes axe-core in compact and comfortable variants", async () => {
    function mountDom(Component, props = {}) {
      const container = document.createElement("div");
      document.body.appendChild(container);
      const emitted = [];
      const app = createApp({
        render() {
          return h(Component, {
            ...props,
            onChange: (...args) => emitted.push(args),
          });
        },
      });
      app.mount(container);
      return {
        container,
        element: container.firstElementChild,
        emitted,
        unmount() {
          app.unmount();
          container.remove();
        },
      };
    }

    const { matchMedia } = createMockMatchMedia(false);
    initTheme({ storage: window.localStorage, matchMedia, document });

    const wrapper = mountDom(ThemeSwitcher, {
      id: "test-theme-switcher",
      size: "comfortable",
    });

    expect(wrapper.element.getAttribute("role")).toBe("group");
    expect(wrapper.element.getAttribute("aria-label")).toBe("Appearance theme");
    expect(
      wrapper.element.classList.contains("clinical-theme-switcher--comfortable")
    ).toBe(true);

    const buttons = Array.from(wrapper.element.querySelectorAll("button"));
    expect(buttons).toHaveLength(3);

    // Zero Unicode Pictographic Emojis
    expect(wrapper.element.outerHTML).not.toMatch(/\p{Extended_Pictographic}/u);

    for (const btn of buttons) {
      const svg = btn.querySelector("svg");
      expect(svg).not.toBeNull();
      expect(svg.getAttribute("aria-hidden")).toBe("true");
    }

    expect(buttons[2].getAttribute("aria-pressed")).toBe("true");
    buttons[1].click();
    await nextTick();

    expect(buttons[1].getAttribute("aria-pressed")).toBe("true");
    expect(buttons[2].getAttribute("aria-pressed")).toBe("false");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(wrapper.emitted[0]?.[0]).toEqual({
      preference: "dark",
      resolvedTheme: "dark",
    });

    await expect(wrapper.container).toBeAccessible();
    wrapper.unmount();

    const labeledCompact = mountDom(ThemeSwitcher, {
      size: "compact",
      showLabels: true,
    });
    expect(
      labeledCompact.element.classList.contains(
        "clinical-theme-switcher--compact"
      )
    ).toBe(true);
    expect(labeledCompact.element.textContent).toContain("Light");
    expect(labeledCompact.element.textContent).toContain("Dark");
    expect(labeledCompact.element.textContent).toContain("Auto");
    await expect(labeledCompact.container).toBeAccessible();
    labeledCompact.unmount();
  });
});
