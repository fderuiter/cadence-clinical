import {
  computed,
  getCurrentInstance,
  onBeforeUnmount,
  onMounted,
  ref,
} from "vue";

export const WORKSPACE_BREAKPOINTS = Object.freeze({
  MOBILE_SHEET: 480,
  TABLET: 768,
  DESKTOP_WIDE: 1280,
});

/**
 * Maps a numeric viewport width in CSS pixels to one of the three clinical workspace tiers.
 *
 * @param {number} width
 * @returns {"desktop" | "tablet" | "mobile"}
 */
export function resolveWorkspaceTierFromWidth(width) {
  if (!Number.isFinite(width)) return "desktop";
  if (width >= WORKSPACE_BREAKPOINTS.DESKTOP_WIDE) return "desktop";
  if (width >= WORKSPACE_BREAKPOINTS.TABLET) return "tablet";
  return "mobile";
}

/**
 * Reactive viewport tier detector for three-tier clinical workspaces:
 * - "desktop" (>= 1280px): Docked inline navigation & inspector panes.
 * - "tablet" (768px–1279px): 60px icon rail + slide-over drawers keeping >= 480px workspace.
 * - "mobile" (< 768px, including 320px–480px phones): <= 90dvh bottom-sheet drawers.
 *
 * Defaults to `defaultTier` ("desktop") in SSR and jsdom environments where `window.matchMedia`
 * is not implemented, while reacting live to `matchMedia` and `resize` events in browsers.
 *
 * @param {{ defaultTier?: "desktop" | "tablet" | "mobile" }} [options]
 */
export function useWorkspaceTier(options = {}) {
  const defaultTier = options.defaultTier || "desktop";

  const evaluateTier = () => {
    if (typeof window === "undefined") {
      return defaultTier;
    }
    if (
      typeof window.__CADENCE_FORCE_WORKSPACE_TIER__ === "string" &&
      ["desktop", "tablet", "mobile"].includes(
        window.__CADENCE_FORCE_WORKSPACE_TIER__
      )
    ) {
      return window.__CADENCE_FORCE_WORKSPACE_TIER__;
    }
    if (typeof window.matchMedia !== "function") {
      return defaultTier;
    }
    const minDesktop = Boolean(
      window.matchMedia(`(min-width: ${WORKSPACE_BREAKPOINTS.DESKTOP_WIDE}px)`)
        ?.matches
    );
    const maxSubDesktop = Boolean(
      window.matchMedia(
        `(max-width: ${WORKSPACE_BREAKPOINTS.DESKTOP_WIDE - 1}px)`
      )?.matches
    );
    const minTablet = Boolean(
      window.matchMedia(`(min-width: ${WORKSPACE_BREAKPOINTS.TABLET}px)`)
        ?.matches
    );
    const maxMobile = Boolean(
      window.matchMedia(`(max-width: ${WORKSPACE_BREAKPOINTS.TABLET - 1}px)`)
        ?.matches
    );
    if (!minDesktop && !maxSubDesktop && !minTablet && !maxMobile) {
      return defaultTier;
    }
    if (minDesktop) {
      return "desktop";
    }
    if (maxMobile && !minTablet) {
      return "mobile";
    }
    if (minTablet || maxSubDesktop) {
      return "tablet";
    }
    return "mobile";
  };

  const tier = ref(evaluateTier());
  const isDesktop = computed(() => tier.value === "desktop");
  const isTablet = computed(() => tier.value === "tablet");
  const isMobile = computed(() => tier.value === "mobile");

  const syncTier = () => {
    tier.value = evaluateTier();
  };

  let cleanupFns = [];

  const bindListeners = () => {
    if (
      typeof window === "undefined" ||
      typeof window.matchMedia !== "function"
    ) {
      return;
    }
    syncTier();
    const desktopMql = window.matchMedia(
      `(min-width: ${WORKSPACE_BREAKPOINTS.DESKTOP_WIDE}px)`
    );
    const tabletMql = window.matchMedia(
      `(min-width: ${WORKSPACE_BREAKPOINTS.TABLET}px)`
    );

    const attach = (mql) => {
      if (!mql) return;
      if (typeof mql.addEventListener === "function") {
        mql.addEventListener("change", syncTier);
        cleanupFns.push(() => mql.removeEventListener("change", syncTier));
      } else if (typeof mql.addListener === "function") {
        mql.addListener(syncTier);
        cleanupFns.push(() => mql.removeListener(syncTier));
      }
    };

    attach(desktopMql);
    attach(tabletMql);
    window.addEventListener("resize", syncTier, { passive: true });
    cleanupFns.push(() => window.removeEventListener("resize", syncTier));
  };

  const unbindListeners = () => {
    for (const fn of cleanupFns) {
      fn();
    }
    cleanupFns = [];
  };

  if (getCurrentInstance()) {
    onMounted(bindListeners);
    onBeforeUnmount(unbindListeners);
  }

  return {
    tier,
    isDesktop,
    isTablet,
    isMobile,
    syncTier,
  };
}
