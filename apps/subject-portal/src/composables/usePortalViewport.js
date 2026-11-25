import { computed, onBeforeUnmount, onMounted, ref } from "vue";

export const PORTAL_BREAKPOINTS = Object.freeze({
  narrowBreadcrumb: 640,
  tablet: 768,
  compactBanner: 1024,
  wideDesktop: 1280,
  shortLandscapeHeight: 480,
});

function readWindowWidth() {
  if (
    typeof window !== "undefined" &&
    typeof window.innerWidth === "number" &&
    window.innerWidth > 0
  ) {
    return window.innerWidth;
  }
  return PORTAL_BREAKPOINTS.compactBanner;
}

function readWindowHeight() {
  if (
    typeof window !== "undefined" &&
    typeof window.innerHeight === "number" &&
    window.innerHeight > 0
  ) {
    return window.innerHeight;
  }
  return PORTAL_BREAKPOINTS.tablet;
}

const viewportWidth = ref(readWindowWidth());
const viewportHeight = ref(readWindowHeight());
let activeSubscribers = 0;

export function syncPortalViewportWidth() {
  viewportWidth.value = readWindowWidth();
  viewportHeight.value = readWindowHeight();
  return viewportWidth.value;
}

export function usePortalViewport() {
  syncPortalViewportWidth();

  onMounted(() => {
    syncPortalViewportWidth();
    if (typeof window !== "undefined") {
      if (activeSubscribers === 0) {
        window.addEventListener("resize", syncPortalViewportWidth, {
          passive: true,
        });
      }
      activeSubscribers += 1;
    }
  });

  onBeforeUnmount(() => {
    if (typeof window !== "undefined" && activeSubscribers > 0) {
      activeSubscribers -= 1;
      if (activeSubscribers === 0) {
        window.removeEventListener("resize", syncPortalViewportWidth);
      }
    }
  });

  const isNarrowBreadcrumbViewport = computed(
    () => viewportWidth.value < PORTAL_BREAKPOINTS.narrowBreadcrumb
  );
  const isMobileViewport = computed(
    () => viewportWidth.value < PORTAL_BREAKPOINTS.tablet
  );
  const isCompactBannerViewport = computed(
    () => viewportWidth.value < PORTAL_BREAKPOINTS.compactBanner
  );
  const isTabletRailViewport = computed(
    () =>
      viewportWidth.value >= PORTAL_BREAKPOINTS.tablet &&
      viewportWidth.value < PORTAL_BREAKPOINTS.wideDesktop
  );
  const isWideDesktopViewport = computed(
    () => viewportWidth.value >= PORTAL_BREAKPOINTS.wideDesktop
  );
  const isShortLandscapeViewport = computed(
    () => viewportHeight.value <= PORTAL_BREAKPOINTS.shortLandscapeHeight
  );

  return {
    viewportWidth,
    viewportHeight,
    isNarrowBreadcrumbViewport,
    isMobileViewport,
    isCompactBannerViewport,
    isTabletRailViewport,
    isWideDesktopViewport,
    isShortLandscapeViewport,
    syncPortalViewportWidth,
  };
}
