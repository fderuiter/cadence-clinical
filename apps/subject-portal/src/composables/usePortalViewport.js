import { computed, onBeforeUnmount, onMounted, ref } from "vue";

function readWindowWidth() {
  if (
    typeof window !== "undefined" &&
    typeof window.innerWidth === "number" &&
    window.innerWidth > 0
  ) {
    return window.innerWidth;
  }
  return 1024;
}

const viewportWidth = ref(readWindowWidth());
let activeSubscribers = 0;

export function syncPortalViewportWidth() {
  viewportWidth.value = readWindowWidth();
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

  const isNarrowBreadcrumbViewport = computed(() => viewportWidth.value < 640);
  const isMobileViewport = computed(() => viewportWidth.value < 768);
  const isCompactBannerViewport = computed(() => viewportWidth.value < 1024);
  const isTabletRailViewport = computed(
    () => viewportWidth.value >= 768 && viewportWidth.value < 1280
  );
  const isWideDesktopViewport = computed(() => viewportWidth.value >= 1280);

  return {
    viewportWidth,
    isNarrowBreadcrumbViewport,
    isMobileViewport,
    isCompactBannerViewport,
    isTabletRailViewport,
    isWideDesktopViewport,
    syncPortalViewportWidth,
  };
}
