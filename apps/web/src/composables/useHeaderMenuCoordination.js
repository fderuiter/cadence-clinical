import { nextTick, onMounted, onUnmounted, ref, watch } from "vue";
import {
  computeFloatingPosition,
  getVisualViewportBounds,
  observeVisualViewport,
} from "ui";

const DEFAULT_VIEWPORT_MARGIN = 12;
const SECONDARY_MENU_COLLAPSE_BREAKPOINT = 1280;
const MOBILE_MENU_MIN_HEIGHT = 96;

/**
 * Coordinates AppShell's responsive header menus (#mobile-nav-menu and
 * #header-secondary-menu) with mutual exclusion, visualViewport 12px edge
 * clamping, outside pointer dismissal, and stack-aware single-Escape focus
 * restoration.
 */
export function useHeaderMenuCoordination({
  refs = {},
  hasBlockingModalOverlay = () => false,
  viewportMargin = DEFAULT_VIEWPORT_MARGIN,
} = {}) {
  const isMobileMenuOpen = ref(false);
  const isSecondaryMenuOpen = ref(false);

  const secondaryMenuWrapperRef = refs.secondaryWrapper || ref(null);
  const secondaryMenuToggleRef = refs.secondaryToggle || ref(null);
  const secondaryMenuPanelRef = refs.secondaryPanel || ref(null);
  const mobileMenuToggleRef = refs.mobileToggle || ref(null);
  const mobileMenuPanelRef = refs.mobilePanel || ref(null);

  const secondaryMenuStyle = ref({});
  const mobileMenuStyle = ref({});

  let stopVisualViewport = null;

  function updateSecondaryMenuPosition() {
    if (!isSecondaryMenuOpen.value || typeof window === "undefined") {
      secondaryMenuStyle.value = {};
      return;
    }

    const viewport = getVisualViewportBounds();
    if (
      window.innerWidth > SECONDARY_MENU_COLLAPSE_BREAKPOINT &&
      viewport.width > SECONDARY_MENU_COLLAPSE_BREAKPOINT
    ) {
      secondaryMenuStyle.value = {};
      return;
    }

    const anchorEl =
      secondaryMenuToggleRef.value || secondaryMenuWrapperRef.value;
    const panelEl = secondaryMenuPanelRef.value;
    if (
      !anchorEl ||
      typeof anchorEl.getBoundingClientRect !== "function" ||
      !panelEl ||
      typeof panelEl.getBoundingClientRect !== "function"
    ) {
      secondaryMenuStyle.value = {};
      return;
    }

    const anchorRect = anchorEl.getBoundingClientRect();
    const availableWidth = Math.max(0, viewport.width - viewportMargin * 2);
    const isPhoneSheet = window.innerWidth <= 640 || viewport.width <= 640;
    const measuredRect = panelEl.getBoundingClientRect();
    const targetWidth = isPhoneSheet
      ? availableWidth
      : Math.min(
          Math.max(measuredRect.width || 240, Math.min(210, availableWidth)),
          Math.min(280, availableWidth)
        );
    const naturalHeight = Math.max(
      panelEl.scrollHeight || 0,
      measuredRect.height || 0,
      120
    );

    const pos = computeFloatingPosition({
      anchorRect,
      floatingSize: { width: targetWidth, height: naturalHeight },
      viewport,
      placement: "bottom",
      align: "end",
      offset: 6,
      margin: viewportMargin,
      flip: true,
    });

    secondaryMenuStyle.value = {
      position: "fixed",
      top: `${Math.round(pos.top)}px`,
      left: `${Math.round(pos.left)}px`,
      right: "auto",
      width: isPhoneSheet ? `${Math.round(pos.maxWidth)}px` : undefined,
      maxWidth: isPhoneSheet
        ? `${Math.round(pos.maxWidth)}px`
        : `${Math.round(Math.min(280, pos.maxWidth))}px`,
      maxHeight: `${Math.round(pos.maxHeight)}px`,
    };
  }

  function updateMobileMenuPosition() {
    if (!isMobileMenuOpen.value || typeof window === "undefined") {
      mobileMenuStyle.value = {};
      return;
    }

    const viewport = getVisualViewportBounds();
    if (window.innerWidth > 860 && viewport.width > 860) {
      mobileMenuStyle.value = {};
      return;
    }

    const anchorEl =
      mobileMenuToggleRef.value ||
      (typeof document !== "undefined"
        ? document.querySelector(".app-header")
        : null);
    const panelEl = mobileMenuPanelRef.value;
    if (
      !anchorEl ||
      typeof anchorEl.getBoundingClientRect !== "function" ||
      !panelEl ||
      typeof panelEl.getBoundingClientRect !== "function"
    ) {
      mobileMenuStyle.value = {};
      return;
    }

    const headerEl =
      typeof document !== "undefined"
        ? document.querySelector(".app-header")
        : null;
    const anchorRect =
      headerEl &&
      typeof headerEl.getBoundingClientRect === "function" &&
      headerEl.getBoundingClientRect().height > 0
        ? headerEl.getBoundingClientRect()
        : anchorEl.getBoundingClientRect();
    const availableWidth = Math.max(0, viewport.width - viewportMargin * 2);
    const naturalHeight = Math.max(
      panelEl.scrollHeight || 0,
      panelEl.offsetHeight || 0,
      MOBILE_MENU_MIN_HEIGHT
    );

    const pos = computeFloatingPosition({
      anchorRect,
      floatingSize: { width: availableWidth, height: naturalHeight },
      viewport,
      placement: "bottom",
      align: "start",
      offset: 4,
      margin: viewportMargin,
      flip: true,
      preferredWidth: availableWidth,
      minHeight: MOBILE_MENU_MIN_HEIGHT,
    });

    mobileMenuStyle.value = {
      position: "fixed",
      left: `${Math.round(viewport.left + viewportMargin)}px`,
      right: "auto",
      width: `${Math.round(availableWidth)}px`,
      top: `${Math.round(pos.top)}px`,
      maxWidth: `${Math.round(pos.maxWidth)}px`,
      maxHeight: `${Math.round(pos.maxHeight)}px`,
    };
  }

  function syncMenuPositions() {
    updateSecondaryMenuPosition();
    updateMobileMenuPosition();
  }

  function startViewportTracking() {
    if (stopVisualViewport || typeof window === "undefined") return;
    stopVisualViewport = observeVisualViewport(syncMenuPositions);
    window.addEventListener("scroll", syncMenuPositions, true);
  }

  function stopViewportTracking() {
    if (stopVisualViewport) {
      stopVisualViewport();
      stopVisualViewport = null;
    }
    if (typeof window !== "undefined") {
      window.removeEventListener("scroll", syncMenuPositions, true);
    }
  }

  function closeMobileMenu({ restoreFocus = false } = {}) {
    if (!isMobileMenuOpen.value) return;
    isMobileMenuOpen.value = false;
    mobileMenuStyle.value = {};
    if (restoreFocus) {
      nextTick(() => {
        const trigger =
          mobileMenuToggleRef.value ||
          (typeof document !== "undefined"
            ? document.getElementById("btn-mobile-menu-toggle")
            : null);
        trigger?.focus?.();
      });
    }
  }

  function closeSecondaryMenu({ restoreFocus = false } = {}) {
    if (!isSecondaryMenuOpen.value) return;
    isSecondaryMenuOpen.value = false;
    secondaryMenuStyle.value = {};
    if (restoreFocus) {
      nextTick(() => {
        const trigger =
          secondaryMenuToggleRef.value ||
          (typeof document !== "undefined"
            ? document.getElementById("btn-header-actions-menu")
            : null);
        trigger?.focus?.();
      });
    }
  }

  function closeAllHeaderMenus() {
    isMobileMenuOpen.value = false;
    isSecondaryMenuOpen.value = false;
    secondaryMenuStyle.value = {};
    mobileMenuStyle.value = {};
  }

  function toggleMobileMenu() {
    const nextOpen = !isMobileMenuOpen.value;
    if (nextOpen && isSecondaryMenuOpen.value) {
      isSecondaryMenuOpen.value = false;
      secondaryMenuStyle.value = {};
    }
    isMobileMenuOpen.value = nextOpen;
  }

  function toggleSecondaryMenu() {
    const nextOpen = !isSecondaryMenuOpen.value;
    if (nextOpen && isMobileMenuOpen.value) {
      isMobileMenuOpen.value = false;
      mobileMenuStyle.value = {};
    }
    isSecondaryMenuOpen.value = nextOpen;
  }

  function handleDocumentPointerDown(event) {
    const target = event?.target;
    if (!target) return;

    if (isSecondaryMenuOpen.value) {
      const wrapperEl = secondaryMenuWrapperRef.value;
      const panelEl = secondaryMenuPanelRef.value;
      const insideSecondary =
        (wrapperEl && wrapperEl.contains(target)) ||
        (panelEl && panelEl.contains(target));
      if (!insideSecondary) {
        closeSecondaryMenu();
      }
    }

    if (isMobileMenuOpen.value) {
      const toggleEl =
        mobileMenuToggleRef.value ||
        (typeof document !== "undefined"
          ? document.getElementById("btn-mobile-menu-toggle")
          : null);
      const panelEl =
        mobileMenuPanelRef.value ||
        (typeof document !== "undefined"
          ? document.getElementById("mobile-nav-menu")
          : null);
      const insideMobile =
        (toggleEl && toggleEl.contains(target)) ||
        (panelEl && panelEl.contains(target));
      if (!insideMobile) {
        closeMobileMenu();
      }
    }
  }

  function handleEscapeKey(event) {
    if (!isSecondaryMenuOpen.value && !isMobileMenuOpen.value) {
      return false;
    }
    if (hasBlockingModalOverlay()) {
      return false;
    }
    event?.preventDefault?.();
    if (isSecondaryMenuOpen.value) {
      closeSecondaryMenu({ restoreFocus: true });
      return true;
    }
    if (isMobileMenuOpen.value) {
      closeMobileMenu({ restoreFocus: true });
      return true;
    }
    return false;
  }

  watch(
    [isSecondaryMenuOpen, isMobileMenuOpen],
    ([secondaryOpen, mobileOpen]) => {
      if (secondaryOpen || mobileOpen) {
        startViewportTracking();
        syncMenuPositions();
        nextTick(syncMenuPositions);
      } else {
        secondaryMenuStyle.value = {};
        mobileMenuStyle.value = {};
        stopViewportTracking();
      }
    },
    { flush: "post" }
  );

  onMounted(() => {
    if (typeof document !== "undefined") {
      document.addEventListener("pointerdown", handleDocumentPointerDown);
    }
  });

  onUnmounted(() => {
    stopViewportTracking();
    if (typeof document !== "undefined") {
      document.removeEventListener("pointerdown", handleDocumentPointerDown);
    }
  });

  return {
    isMobileMenuOpen,
    isSecondaryMenuOpen,
    secondaryMenuStyle,
    mobileMenuStyle,
    closeMobileMenu,
    closeSecondaryMenu,
    closeAllHeaderMenus,
    toggleMobileMenu,
    toggleSecondaryMenu,
    handleEscapeKey,
    syncMenuPositions,
  };
}
