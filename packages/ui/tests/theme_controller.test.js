// @vitest-environment jsdom
import axe from "axe-core";
import { describe, it, expect, beforeEach, vi } from "vitest";
import { createApp, effectScope, h, nextTick } from "vue";
import {
  ClinicalThemeToggle,
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
  useTheme,
  writeStoredThemePreference,
} from "../index.js";
import * as subpathTheme from "../src/utils/themeController.js";
import { buildThemeBootstrapScript } from "../../../scripts/vite-hoist-csp.mjs";

function createMockMediaQuery(initialMatches = false) {
  let matches = initialMatches;
  const listeners = new Set();
  return {
    get matches() {
      return matches;
    },
    media: PREFERS_DARK_MEDIA_QUERY,
    addEventListener(type, fn) {
      if (type === "change") listeners.add(fn);
    },
    removeEventListener(type, fn) {
      if (type === "change") listeners.delete(fn);
    },
    setMatches(next) {
      matches = Boolean(next);
      for (const fn of [...listeners]) {
        fn({ matches, media: PREFERS_DARK_MEDIA_QUERY });
      }
    },
    get listenerCount() {
      return listeners.size;
    },
  };
}

describe("Shared Theme Controller & ClinicalThemeToggle (ADR-2690)", () => {
  let mediaQueryMock;

  beforeEach(() => {
    window.localStorage.clear();
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.classList.remove("dark");
    document.documentElement.style.colorScheme = "";
    mediaQueryMock = createMockMediaQuery(false);
    Object.defineProperty(window, "matchMedia", {
      configurable: true,
      writable: true,
      value: vi.fn((query) =>
        query === PREFERS_DARK_MEDIA_QUERY
          ? mediaQueryMock
          : createMockMediaQuery(false)
      ),
    });
  });

  it("normalizes VitePress and Cadence theme values and resolves effective light/dark themes", () => {
    expect(THEME_STORAGE_KEY).toBe("vitepress-theme-appearance");
    expect(THEME_MODES).toEqual(["light", "dark", "auto"]);
    expect(subpathTheme.THEME_STORAGE_KEY).toBe(THEME_STORAGE_KEY);

    expect(normalizeThemePreference("light")).toBe("light");
    expect(normalizeThemePreference("dark")).toBe("dark");
    expect(normalizeThemePreference("auto")).toBe("auto");
    expect(normalizeThemePreference("system")).toBe("auto");
    expect(normalizeThemePreference("true")).toBe("dark");
    expect(normalizeThemePreference("false")).toBe("light");
    expect(normalizeThemePreference(null)).toBe("auto");
    expect(normalizeThemePreference("unexpected")).toBe("auto");

    expect(resolveEffectiveTheme("light", true)).toBe("light");
    expect(resolveEffectiveTheme("dark", false)).toBe("dark");
    expect(resolveEffectiveTheme("auto", false)).toBe("light");
    expect(resolveEffectiveTheme("auto", true)).toBe("dark");

    expect(cycleThemePreference("auto")).toBe("light");
    expect(cycleThemePreference("light")).toBe("dark");
    expect(cycleThemePreference("dark")).toBe("auto");
  });

  it("reads/writes storage safely when localStorage throws and applies data-theme, .dark, and color-scheme", () => {
    const throwingStorage = {
      getItem() {
        throw new DOMException("SecurityError");
      },
      setItem() {
        throw new DOMException("QuotaExceededError");
      },
    };

    expect(readStoredThemePreference({ storage: throwingStorage })).toBe(
      "auto"
    );
    expect(
      writeStoredThemePreference("dark", { storage: throwingStorage })
    ).toBe("dark");

    mediaQueryMock.setMatches(true);
    expect(detectSystemDarkPreference(window)).toBe(true);

    const autoDarkState = applyThemeToDocument("auto", {
      doc: document,
      win: window,
    });
    expect(autoDarkState).toEqual({
      preference: "auto",
      resolvedTheme: "dark",
    });
    expect(document.documentElement.getAttribute("data-theme")).toBe("auto");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");

    const explicitLightState = applyThemeToDocument("light", {
      doc: document,
      win: window,
    });
    expect(explicitLightState.resolvedTheme).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);
    expect(document.documentElement.style.colorScheme).toBe("light");
  });

  it("synchronizes bootstrapTheme, cross-tab storage events, and live prefers-color-scheme changes via useTheme and subscribeThemeSync", async () => {
    window.localStorage.setItem(THEME_STORAGE_KEY, "dark");
    const bootState = bootstrapTheme({ doc: document, win: window });
    expect(bootState.preference).toBe("dark");
    expect(bootState.resolvedTheme).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);

    const scope = effectScope();
    let api;
    scope.run(() => {
      api = useTheme();
    });

    expect(api.preference.value).toBe("dark");
    expect(api.isDark.value).toBe(true);

    // Switch to auto while OS is light
    api.setTheme("auto");
    await nextTick();
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("auto");
    expect(api.resolvedTheme.value).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("auto");
    expect(document.documentElement.classList.contains("dark")).toBe(false);

    // Live OS prefers-color-scheme transition updates auto mode immediately
    mediaQueryMock.setMatches(true);
    await nextTick();
    expect(api.systemPrefersDark.value).toBe(true);
    expect(api.resolvedTheme.value).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");

    // Cross-tab storage event updates preference and DOM attributes
    window.localStorage.setItem(THEME_STORAGE_KEY, "light");
    window.dispatchEvent(
      new StorageEvent("storage", {
        key: THEME_STORAGE_KEY,
        newValue: "light",
      })
    );
    await nextTick();
    expect(api.preference.value).toBe("light");
    expect(api.resolvedTheme.value).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");
    expect(document.documentElement.classList.contains("dark")).toBe(false);

    // Custom same-tab event also synchronizes standalone subscribeThemeSync listeners
    const observed = [];
    const unsubscribe = subscribeThemeSync((state) => {
      observed.push(state);
    });
    window.localStorage.setItem(THEME_STORAGE_KEY, "dark");
    window.dispatchEvent(
      new CustomEvent(THEME_CHANGE_EVENT, {
        detail: {
          preference: "dark",
          resolvedTheme: "dark",
          systemPrefersDark: true,
        },
      })
    );
    expect(observed.at(-1)?.preference).toBe("dark");

    unsubscribe();
    scope.stop();
    expect(mediaQueryMock.listenerCount).toBe(0);
  });

  it("compiles a self-contained pre-paint IIFE script via buildThemeBootstrapScript without ES module syntax", () => {
    const iifeSource = buildThemeBootstrapScript();
    expect(iifeSource).not.toMatch(/\bexport\s+/);
    expect(iifeSource).not.toMatch(/\bimport\s+/);
    expect(iifeSource).toContain("bootstrapTheme()");

    window.localStorage.setItem(THEME_STORAGE_KEY, "dark");
    document.documentElement.removeAttribute("data-theme");
    document.documentElement.classList.remove("dark");

    const runner = new Function(iifeSource);
    runner();

    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.style.colorScheme).toBe("dark");
  });

  it("renders accessible zero-emoji ClinicalThemeToggle in cycle and segmented variants and passes axe checks", async () => {
    function mountComponent(Component, props = {}) {
      const container = document.createElement("div");
      document.body.appendChild(container);
      const app = createApp({ render: () => h(Component, props) });
      app.mount(container);
      return {
        container,
        unmount() {
          app.unmount();
          container.remove();
        },
      };
    }

    const cycleMounted = mountComponent(ClinicalThemeToggle, {
      id: "test-cycle-theme",
      variant: "cycle",
      density: "compact",
      showLabel: false,
    });

    const btn = cycleMounted.container.querySelector("#test-cycle-theme");
    expect(btn.getAttribute("aria-label")).toContain("Theme: Auto");
    // Zero Unicode emojis in markup
    expect(cycleMounted.container.innerHTML).not.toMatch(
      /[\u{1F300}-\u{1FAFF}\u2600-\u27BF]/u
    );
    expect(
      cycleMounted.container.querySelectorAll("svg[aria-hidden='true']").length
    ).toBe(1);
    const cycleAxe = await axe.run(cycleMounted.container, {
      rules: { "color-contrast": { enabled: false } },
    });
    expect(cycleAxe.violations).toEqual([]);

    // Cycle: auto -> light -> dark -> auto
    btn.click();
    await nextTick();
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("light");
    expect(document.documentElement.getAttribute("data-theme")).toBe("light");

    btn.click();
    await nextTick();
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("dark");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);

    btn.click();
    await nextTick();
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("auto");
    cycleMounted.unmount();

    const segmentedMounted = mountComponent(ClinicalThemeToggle, {
      id: "test-segmented-theme",
      variant: "segmented",
      density: "touch",
    });

    const segments = Array.from(
      segmentedMounted.container.querySelectorAll(
        ".clinical-theme-toggle-segment"
      )
    );
    expect(segments).toHaveLength(3);
    expect(segments.map((r) => r.textContent.trim())).toEqual([
      "Light",
      "Dark",
      "Auto",
    ]);
    const segmentedAxe = await axe.run(segmentedMounted.container, {
      rules: { "color-contrast": { enabled: false } },
    });
    expect(segmentedAxe.violations).toEqual([]);

    segments[1].click();
    await nextTick();
    expect(segments[1].getAttribute("aria-pressed")).toBe("true");
    expect(document.documentElement.getAttribute("data-theme")).toBe("dark");

    segments[2].click();
    await nextTick();
    expect(window.localStorage.getItem(THEME_STORAGE_KEY)).toBe("auto");
    expect(segments[2].getAttribute("aria-pressed")).toBe("true");

    segmentedMounted.unmount();
  });
});
