<template>
  <div
    v-if="onboardingStore.isActive && !onboardingStore.disabled"
    class="onboarding-tour-container"
  >
    <!--Translucent backdrop only on boundary modal steps to highlight onboarding introduction/completion without blocking interaction during edits -->
    <div
      v-if="isModalStep"
      class="onboarding-backdrop"
      role="presentation"
      @click="onboardingStore.dismissTour()"
    />

    <!-- Arrow pointing at anchor (hidden when unanchored or detached) -->
    <div
      v-if="showArrow"
      :style="arrowStyle"
      class="popover-arrow"
      :class="{ 'popover-arrow--bottom': arrowPlacement === 'top' }"
      aria-hidden="true"
    />

    <!-- The Popover Box -->
    <div
      ref="popoverRef"
      :style="popoverStyle"
      class="onboarding-popover-card"
      role="dialog"
      :aria-modal="isModalStep ? 'true' : undefined"
      aria-labelledby="onboarding-tour-title"
      tabindex="-1"
    >
      <!-- Popover Header -->
      <div class="onboarding-popover-header">
        <h4 id="onboarding-tour-title" class="onboarding-popover-title">
          {{ stepTitle }}
        </h4>
        <span class="onboarding-step-counter">
          Step {{ currentStep }} of {{ totalSteps }}
        </span>
      </div>

      <!-- Popover Body Content -->
      <div class="onboarding-popover-body">
        <div v-if="dynamicPrompt" class="onboarding-dynamic-prompt">
          {{ dynamicPrompt }}
        </div>
        <p>{{ stepDescription }}</p>
      </div>

      <!-- Popover Action Buttons -->
      <div class="onboarding-popover-actions">
        <!-- Left buttons -->
        <div class="onboarding-popover-left-actions">
          <button
            v-if="currentStep === 1"
            type="button"
            class="btn btn-secondary btn-tour-dismiss"
            @click="onboardingStore.dismissTour()"
          >
            Dismiss
          </button>
          <button
            v-else
            type="button"
            class="btn btn-secondary btn-tour-back"
            @click="handlePrev"
          >
            Back
          </button>
        </div>

        <!-- Right/Middle buttons -->
        <div class="onboarding-popover-right-actions">
          <button
            type="button"
            class="btn btn-tour-disable"
            @click="onboardingStore.disableTour()"
          >
            Don't show again
          </button>

          <button
            v-if="currentStep < totalSteps"
            type="button"
            class="btn btn-primary btn-tour-next"
            @click="handleNext"
          >
            Next
          </button>
          <button
            v-else
            type="button"
            class="btn btn-success btn-tour-complete"
            @click="handleComplete"
          >
            Complete
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, nextTick, ref, onMounted, onUnmounted, watch } from "vue";
import { useRouter, useRoute } from "vue-router";
import {
  handoffFocus,
  useEscapeClose,
  useFocusTrap,
  useInlineFocusHandoff,
} from "ui";
import { getActivePinia } from "pinia";
import {
  buildScopedStudyLocation,
  resolveActiveWorkspaceScope,
} from "../canvas/palette";
import {
  isTourRouteAuthorized,
  useOnboardingStore,
} from "../stores/onboarding";
import { PERSONA_PRESETS, useAuthStore } from "../stores/auth";
import { isScreenAvailable } from "../router/availability";
import { hasRequiredRole } from "../router";

const NON_SHELL_TOUR_ROUTES = new Set([
  "landing",
  "login",
  "forbidden",
  "unavailable",
]);

const STRIPPED_TOUR_QUERY_KEYS = new Set(
  "stage,section,site,participant,node,form,item,observation,proposal,ask,status,q,spec,visit,studyId,study,siteId,subjectId,subject,enrollmentId,visitId".split(
    ","
  )
);

const VIEWPORT_MARGIN_PX = 12;
const ANCHOR_GAP_PX = 12;
const DEFAULT_CARD_WIDTH_PX = 320;
const DEFAULT_CARD_HEIGHT_PX = 220;
const PASSIVE_LISTENER_OPTIONS = { passive: true };
const SCROLL_CAPTURE_OPTIONS = { capture: true, passive: true };

const props = defineProps({
  activeTab: {
    type: String,
    required: false,
    default: "",
  },
});

const emit = defineEmits(["update:activeTab"]);

const onboardingStore = useOnboardingStore();
const authStore = useAuthStore();
let router;
let route;

try {
  router = useRouter();
  route = useRoute();
} catch {
  // Gracefully fallback if router is not provided in isolated unit test environments
}

function readTourWorkspaceScope() {
  const canvasState = getActivePinia()?.state.value?.studyCanvas ?? null;
  return resolveActiveWorkspaceScope(route ?? undefined, canvasState);
}

const currentStep = computed(() => onboardingStore.currentStep);
const currentStepConfig = computed(
  () => onboardingStore.currentStepConfig || {}
);
const totalSteps = computed(() => onboardingStore.totalSteps || 1);

const effectiveRoles = computed(() => {
  const roles = authStore.normalizedRoles || [];
  const key =
    authStore.currentPersona || onboardingStore.currentPersona || "super_admin";
  const preset = PERSONA_PRESETS.find((p) => p.key === key);
  if (preset && key !== "super_admin" && roles.includes("sponsor_admin")) {
    return preset.requires;
  }
  return authStore.rawRoles?.length
    ? roles
    : preset?.requires || ["sponsor_admin"];
});

function canNavigateToTourRoute(routeName) {
  if (!routeName || NON_SHELL_TOUR_ROUTES.has(routeName)) {
    return false;
  }
  if (!isScreenAvailable(routeName)) {
    return false;
  }
  if (!isTourRouteAuthorized(routeName, effectiveRoles.value)) {
    return false;
  }
  if (!router) {
    return false;
  }
  if (typeof router.hasRoute === "function" && !router.hasRoute(routeName)) {
    return false;
  }
  if (typeof router.resolve === "function") {
    const resolved = router.resolve({ name: routeName });
    if (
      resolved?.meta?.requiresAuth === false ||
      resolved?.meta?.shell === false ||
      resolved?.meta?.layout === "standalone"
    ) {
      return false;
    }
    const matchedRoles = (resolved?.matched || []).flatMap(
      (record) => record.meta?.requiresRole || []
    );
    const requiredRoles =
      matchedRoles.length > 0
        ? matchedRoles
        : resolved?.meta?.requiresRole || [];
    if (
      requiredRoles.length > 0 &&
      !hasRequiredRole(effectiveRoles.value, requiredRoles)
    ) {
      return false;
    }
  }
  return true;
}

const stepTitle = computed(() => currentStepConfig.value?.title || "");
const stepDescription = computed(
  () => currentStepConfig.value?.description || ""
);
const dynamicPrompt = computed(() => currentStepConfig.value?.prompt || "");
const stepTargetDescription = computed(
  () => currentStepConfig.value?.title || `Step ${currentStep.value}`
);

const hasTargetElement = ref(false);
const isArrowDetached = ref(false);
const arrowPlacement = ref("bottom");
const arrowStyle = ref({});
const popoverRef = ref(null);

const isTourOpen = computed(() =>
  Boolean(onboardingStore.isActive && !onboardingStore.disabled)
);
const isBoundaryStep = computed(
  () => currentStep.value === 1 || currentStep.value === totalSteps.value
);
const isModalStep = computed(
  () =>
    isTourOpen.value &&
    isBoundaryStep.value &&
    (!currentStepConfig.value?.targetSelector || !hasTargetElement.value)
);

const showArrow = computed(() =>
  Boolean(
    hasTargetElement.value &&
    currentStepConfig.value?.targetSelector &&
    !isArrowDetached.value
  )
);

const tourHandoff = useInlineFocusHandoff({
  closeFallback: [
    ".btn-start-tour",
    "#btn-onboarding-checklist-trigger",
    "#btn-header-actions-menu",
  ],
});
let launchedFromChecklistDrawer = false;
let pendingAnchoredFocus = false;

useFocusTrap(popoverRef, {
  isOpen: isModalStep,
  restoreFocus: false,
  onEscape: () => onboardingStore.dismissTour(),
});

useEscapeClose(() => onboardingStore.dismissTour(), {
  isOpen: () => isTourOpen.value && !isModalStep.value,
});

watch(
  isTourOpen,
  (open, prevOpen) => {
    if (open && !prevOpen) {
      const activeEl =
        typeof document !== "undefined" ? document.activeElement : null;
      launchedFromChecklistDrawer = Boolean(
        onboardingStore.isDrawerOpen ||
        (activeEl &&
          activeEl !== document.body &&
          (activeEl.id === "btn-drawer-start-tour" ||
            activeEl.id === "btn-onboarding-checklist-trigger" ||
            activeEl.closest?.(
              ".checklist-drawer, #onboarding-checklist-drawer"
            )))
      );
      tourHandoff.rememberTrigger(
        launchedFromChecklistDrawer
          ? "#btn-onboarding-checklist-trigger"
          : activeEl && activeEl !== document.body
            ? activeEl
            : null
      );
      pendingAnchoredFocus = Boolean(currentStepConfig.value?.targetSelector);
    } else if (!open && prevOpen) {
      pendingAnchoredFocus = false;
      const fallbackOrder = launchedFromChecklistDrawer
        ? [
            "#btn-onboarding-checklist-trigger",
            ".btn-start-tour",
            "#btn-header-actions-menu",
          ]
        : [
            ".btn-start-tour",
            "#btn-onboarding-checklist-trigger",
            "#btn-header-actions-menu",
          ];
      launchedFromChecklistDrawer = false;
      void tourHandoff.restoreTrigger(fallbackOrder);
    }
  },
  { immediate: true, flush: "sync" }
);

function buildCenteredPopoverStyle(cssZoomScale = 1) {
  const style = {
    position: "fixed",
    top: "50%",
    left: "50%",
    transform: "translate(-50%, -50%)",
    zIndex: 1000,
  };
  if (cssZoomScale > 1 && typeof window !== "undefined") {
    const vv = window.visualViewport;
    const rawVpW = vv?.width || window.innerWidth || 1024;
    const rawVpH = vv?.height || window.innerHeight || 768;
    const scaledVpW = rawVpW / cssZoomScale;
    const scaledVpH = rawVpH / cssZoomScale;
    style.maxWidth = `${Math.max(0, scaledVpW - VIEWPORT_MARGIN_PX * 2)}px`;
    style.maxHeight = `${Math.max(
      0,
      Math.min(scaledVpH * 0.9, scaledVpH - VIEWPORT_MARGIN_PX * 2)
    )}px`;
  }
  return style;
}

const popoverStyle = ref(buildCenteredPopoverStyle());

let currentRafId = null;
let isDisposed = false;
const pendingTimers = new Set();

function getEffectiveCssZoom() {
  if (typeof document === "undefined" || typeof window === "undefined") {
    return 1;
  }
  const rootEl = document.documentElement;
  if (
    rootEl &&
    typeof rootEl.currentCSSZoom === "number" &&
    rootEl.currentCSSZoom > 0
  ) {
    return rootEl.currentCSSZoom;
  }
  if (typeof window.getComputedStyle === "function" && rootEl) {
    const rawZoom = parseFloat(window.getComputedStyle(rootEl).zoom || "1");
    if (Number.isFinite(rawZoom) && rawZoom > 0) {
      return rawZoom;
    }
  }
  return 1;
}

function isElementVisible(el) {
  if (!el || typeof el.getBoundingClientRect !== "function") {
    return false;
  }
  let cur = el;
  while (cur && cur.nodeType === 1) {
    if (cur.hasAttribute?.("hidden")) {
      return false;
    }
    if (
      typeof window !== "undefined" &&
      typeof window.getComputedStyle === "function"
    ) {
      const style = window.getComputedStyle(cur);
      if (style.display === "none" || style.visibility === "hidden") {
        return false;
      }
    }
    cur = cur.parentElement;
  }
  const rect = el.getBoundingClientRect();
  return rect.width > 0 && rect.height > 0;
}

function findFirstVisibleTarget(selectorList) {
  if (!selectorList || typeof document === "undefined") {
    return null;
  }
  const candidates = String(selectorList)
    .split(",")
    .map((part) => part.trim())
    .filter(Boolean);

  for (const candidate of candidates) {
    let nodes;
    try {
      nodes = document.querySelectorAll(candidate);
    } catch {
      continue;
    }
    for (const el of nodes) {
      if (isElementVisible(el)) {
        return el;
      }
    }
  }
  return null;
}

function cancelPendingRaf() {
  if (currentRafId !== null) {
    if (typeof cancelAnimationFrame === "function") {
      cancelAnimationFrame(currentRafId);
    }
    currentRafId = null;
  }
}

function schedulePositionUpdate(delayMs) {
  if (isDisposed) return;
  const timerId = setTimeout(() => {
    pendingTimers.delete(timerId);
    updatePosition();
  }, delayMs);
  pendingTimers.add(timerId);
}

async function syncStepRoute() {
  if (!onboardingStore.isActive || onboardingStore.disabled) return;
  if (onboardingStore.currentStep > totalSteps.value) {
    onboardingStore.currentStep = totalSteps.value;
    onboardingStore.persist();
  }
  const config = currentStepConfig.value;
  if (!config) return;

  if (router && route) {
    const targetRouteName = config.routeName;
    if (targetRouteName) {
      if (!canNavigateToTourRoute(targetRouteName)) {
        return;
      }
      if (route.name !== targetRouteName) {
        const scope = readTourWorkspaceScope();
        if (targetRouteName === "study") {
          const baseLocation = buildScopedStudyLocation(
            scope.studyCode,
            {},
            {
              studyCode: scope.studyCode,
              siteId: scope.siteId,
              explicitSiteId: scope.explicitSiteId,
              participantRef: scope.participantRef,
              subjectCode: scope.subjectCode,
              enrollmentId: scope.enrollmentId,
              recordParticipantParam: scope.recordParticipantParam,
              inspectorNodeParam: scope.inspectorNodeParam,
            }
          );
          const {
            stage: _stage,
            section: _section,
            ...cleanBaseQuery
          } = baseLocation.query || {};
          const nextQuery = config.query
            ? { ...cleanBaseQuery, ...config.query }
            : cleanBaseQuery;
          await router.push(
            scope.studyCode
              ? { ...baseLocation, query: nextQuery }
              : { name: "study", query: nextQuery }
          );
          return;
        }

        const standaloneQuery = {};
        for (const [key, value] of Object.entries(route.query || {})) {
          if (!STRIPPED_TOUR_QUERY_KEYS.has(key) && value !== undefined) {
            standaloneQuery[key] = value;
          }
        }
        if (scope.studyCode) {
          standaloneQuery.studyId = scope.studyCode;
        }
        if (scope.siteId) {
          standaloneQuery.siteId = scope.siteId;
        }
        if (scope.subjectCode && !scope.subjectCode.includes(":")) {
          standaloneQuery.subjectId = scope.subjectCode;
        }
        if (config.query) {
          Object.assign(standaloneQuery, config.query);
        }
        await router.push({ name: targetRouteName, query: standaloneQuery });
        return;
      }
    }

    if (config.query) {
      const queryRouteName = targetRouteName || route.name;
      if (!canNavigateToTourRoute(queryRouteName)) {
        return;
      }
      let needsUpdate = false;
      const newQuery = { ...route.query };
      for (const [k, v] of Object.entries(config.query)) {
        if (route.query[k] !== v) {
          newQuery[k] = v;
          needsUpdate = true;
        }
      }
      if (needsUpdate) {
        await router.push({
          name: queryRouteName,
          ...(route.params && Object.keys(route.params).length > 0
            ? { params: { ...route.params } }
            : {}),
          query: newQuery,
        });
      }
    }
  }
}

const updatePosition = () => {
  if (
    isDisposed ||
    typeof document === "undefined" ||
    !onboardingStore.isActive ||
    onboardingStore.disabled
  ) {
    return;
  }

  cancelPendingRaf();

  const config = currentStepConfig.value;
  const selector = config?.targetSelector;
  const cssZoomScale = getEffectiveCssZoom();

  if (!selector) {
    hasTargetElement.value = false;
    isArrowDetached.value = true;
    popoverStyle.value = buildCenteredPopoverStyle(cssZoomScale);
    return;
  }

  const MAX_RETRIES = 5;
  let attempts = 0;

  const checkAndPosition = () => {
    if (isDisposed || typeof document === "undefined") {
      currentRafId = null;
      return;
    }

    const zoomScale = getEffectiveCssZoom();
    const el = findFirstVisibleTarget(selector);
    if (el) {
      let rect = el.getBoundingClientRect();
      if (
        rect.width === 0 &&
        rect.height === 0 &&
        typeof el.closest === "function" &&
        el.closest("#header-secondary-menu")
      ) {
        const menuBtn = document.getElementById("btn-header-actions-menu");
        if (menuBtn) {
          rect = menuBtn.getBoundingClientRect();
        }
      }
      if (rect.width > 0 && rect.height > 0) {
        const vv = typeof window !== "undefined" ? window.visualViewport : null;
        const rawVpWidth =
          vv?.width ||
          (typeof window !== "undefined" ? window.innerWidth : 1024) ||
          1024;
        const rawVpHeight =
          vv?.height ||
          (typeof window !== "undefined" ? window.innerHeight : 768) ||
          768;
        const vpWidth = zoomScale > 1 ? rawVpWidth / zoomScale : rawVpWidth;
        const vpHeight = zoomScale > 1 ? rawVpHeight / zoomScale : rawVpHeight;
        const vpOffsetLeft =
          (vv?.offsetLeft || 0) / (zoomScale > 1 ? zoomScale : 1);
        const vpOffsetTop =
          (vv?.offsetTop || 0) / (zoomScale > 1 ? zoomScale : 1);

        const maxAllowedWidth = Math.max(0, vpWidth - VIEWPORT_MARGIN_PX * 2);
        const maxAllowedHeight = Math.max(
          0,
          Math.min(vpHeight * 0.9, vpHeight - VIEWPORT_MARGIN_PX * 2)
        );

        const measuredRect = popoverRef.value?.getBoundingClientRect?.();
        const rawCardWidth =
          (measuredRect?.width
            ? measuredRect.width / (zoomScale > 1 ? zoomScale : 1)
            : 0) ||
          popoverRef.value?.offsetWidth ||
          Math.min(
            DEFAULT_CARD_WIDTH_PX,
            maxAllowedWidth || DEFAULT_CARD_WIDTH_PX
          );
        const rawCardHeight =
          (measuredRect?.height
            ? measuredRect.height / (zoomScale > 1 ? zoomScale : 1)
            : 0) ||
          popoverRef.value?.offsetHeight ||
          Math.min(
            DEFAULT_CARD_HEIGHT_PX,
            maxAllowedHeight || DEFAULT_CARD_HEIGHT_PX
          );

        const cardWidth = Math.min(
          rawCardWidth,
          maxAllowedWidth || rawCardWidth
        );
        const cardHeight = Math.min(
          rawCardHeight,
          maxAllowedHeight || rawCardHeight
        );

        const anchorTop = zoomScale > 1 ? rect.top / zoomScale : rect.top;
        const anchorBottom =
          zoomScale > 1 ? rect.bottom / zoomScale : rect.bottom;
        const anchorLeft = zoomScale > 1 ? rect.left / zoomScale : rect.left;
        const anchorRight = zoomScale > 1 ? rect.right / zoomScale : rect.right;
        const anchorWidth = anchorRight - anchorLeft;
        const anchorCenterX = anchorLeft + anchorWidth / 2;

        const halfWidth = cardWidth / 2;
        const minCenterX = vpOffsetLeft + VIEWPORT_MARGIN_PX + halfWidth;
        const maxCenterX =
          vpOffsetLeft + vpWidth - VIEWPORT_MARGIN_PX - halfWidth;
        const clampedCenterX =
          minCenterX > maxCenterX
            ? vpOffsetLeft + vpWidth / 2
            : Math.min(maxCenterX, Math.max(minCenterX, anchorCenterX));

        const minTop = vpOffsetTop + VIEWPORT_MARGIN_PX;
        const maxTop = Math.max(
          minTop,
          vpOffsetTop + vpHeight - VIEWPORT_MARGIN_PX - cardHeight
        );

        const belowTop = anchorBottom + ANCHOR_GAP_PX;
        const aboveTop = anchorTop - ANCHOR_GAP_PX - cardHeight;
        const spaceBelow =
          vpOffsetTop + vpHeight - VIEWPORT_MARGIN_PX - anchorBottom;
        const spaceAbove = anchorTop - minTop;
        const overflowsBottom =
          belowTop + cardHeight > vpOffsetTop + vpHeight - VIEWPORT_MARGIN_PX;

        let placement = "bottom";
        let unclampedTop = belowTop;
        if (
          overflowsBottom &&
          anchorTop > minTop &&
          (aboveTop >= minTop || spaceAbove > spaceBelow)
        ) {
          placement = "top";
          unclampedTop = aboveTop;
        }

        const clampedTop = Math.min(maxTop, Math.max(minTop, unclampedTop));
        const cardLeft = clampedCenterX - halfWidth;
        const cardRight = clampedCenterX + halfWidth;

        const anchorInViewport =
          anchorBottom > vpOffsetTop &&
          anchorTop < vpOffsetTop + vpHeight &&
          anchorRight > vpOffsetLeft &&
          anchorLeft < vpOffsetLeft + vpWidth;
        const verticallyDetached = Math.abs(clampedTop - unclampedTop) > 4;
        const horizontallyDetached =
          cardRight < anchorLeft + 12 || cardLeft > anchorRight - 12;

        isArrowDetached.value =
          !anchorInViewport || verticallyDetached || horizontallyDetached;
        arrowPlacement.value = placement;

        if (!isArrowDetached.value) {
          const clampedArrowX = Math.min(
            Math.max(cardLeft + 18, anchorCenterX),
            Math.max(cardLeft + 18, cardRight - 18)
          );
          arrowStyle.value = {
            position: "fixed",
            top:
              placement === "top"
                ? `${clampedTop + cardHeight}px`
                : `${clampedTop - 10}px`,
            left: `${clampedArrowX}px`,
            transform: "translateX(-50%)",
            zIndex: 1001,
          };
        }

        const nextStyle = {
          position: "fixed",
          top: `${clampedTop}px`,
          left: `${clampedCenterX}px`,
          transform: "translateX(-50%)",
          zIndex: 1000,
        };
        if (zoomScale > 1) {
          nextStyle.maxWidth = `${maxAllowedWidth}px`;
          nextStyle.maxHeight = `${maxAllowedHeight}px`;
        }
        popoverStyle.value = nextStyle;
        const wasTargetElement = hasTargetElement.value;
        hasTargetElement.value = true;
        currentRafId = null;
        if ((!wasTargetElement || pendingAnchoredFocus) && !isModalStep.value) {
          pendingAnchoredFocus = false;
          void handoffFocus(popoverRef, {
            scrollIfOffscreen: false,
            includeDefaultFallbacks: false,
          });
        }
        return;
      }
    }

    if (attempts < MAX_RETRIES && typeof requestAnimationFrame === "function") {
      attempts++;
      currentRafId = requestAnimationFrame(checkAndPosition);
    } else {
      hasTargetElement.value = false;
      isArrowDetached.value = true;
      popoverStyle.value = buildCenteredPopoverStyle(zoomScale);
      currentRafId = null;
      if (pendingAnchoredFocus && !isModalStep.value) {
        pendingAnchoredFocus = false;
        void handoffFocus(popoverRef, {
          scrollIfOffscreen: false,
          includeDefaultFallbacks: false,
        });
      }
    }
  };

  checkAndPosition();
};

const handleScroll = (event) => {
  const target = event?.target;
  if (
    target &&
    typeof Node !== "undefined" &&
    target instanceof Node &&
    popoverRef.value &&
    typeof popoverRef.value.contains === "function" &&
    popoverRef.value.contains(target)
  ) {
    return;
  }
  updatePosition();
};

const handleNext = async () => {
  const currentDesc = stepTargetDescription.value;
  onboardingStore.nextStep(currentDesc);
  await syncStepRoute();
  updatePosition();
  schedulePositionUpdate(50);
  schedulePositionUpdate(150);
};

const handlePrev = async () => {
  onboardingStore.prevStep();
  await syncStepRoute();
  updatePosition();
  schedulePositionUpdate(50);
  schedulePositionUpdate(150);
};

const handleComplete = () => {
  onboardingStore.nextStep(stepTargetDescription.value);
  onboardingStore.disableTour();
};

watch(
  currentStep,
  (step, prevStep) => {
    if (step !== prevStep && isTourOpen.value) {
      pendingAnchoredFocus = Boolean(currentStepConfig.value?.targetSelector);
    }
  },
  { flush: "sync" }
);

watch(currentStep, async () => {
  if (popoverRef.value) {
    popoverRef.value.scrollTop = 0;
  }
  const config = currentStepConfig.value;
  if (config?.query?.tab) {
    emit("update:activeTab", config.query.tab);
  }
  await syncStepRoute();
  updatePosition();
  schedulePositionUpdate(50);
});

watch(
  () => route?.fullPath,
  () => {
    updatePosition();
    schedulePositionUpdate(50);
  }
);

watch(
  () => props.activeTab,
  (newTab) => {
    if (newTab && onboardingStore.isActive && !onboardingStore.disabled) {
      const steps = onboardingStore.personaSteps;
      const matchingStep = steps.find((s) => s.query?.tab === newTab);
      if (matchingStep && currentStep.value !== matchingStep.step) {
        onboardingStore.currentStep = matchingStep.step;
      }
    }
    updatePosition();
  }
);

watch(
  () => onboardingStore.isActive,
  async (active) => {
    if (active) {
      await syncStepRoute();
      updatePosition();
      await nextTick();
      updatePosition();
    }
  }
);

watch(
  () => [authStore.currentPersona, (authStore.normalizedRoles || []).join(",")],
  async ([newPersona], [oldPersona] = []) => {
    if (newPersona && (oldPersona !== undefined || !onboardingStore.isActive)) {
      onboardingStore.loadPersonaState(newPersona);
      await syncStepRoute();
      updatePosition();
    }
  },
  { immediate: true }
);

onMounted(async () => {
  isDisposed = false;
  window.addEventListener("resize", updatePosition, PASSIVE_LISTENER_OPTIONS);
  window.addEventListener("scroll", handleScroll, SCROLL_CAPTURE_OPTIONS);
  window.visualViewport?.addEventListener(
    "resize",
    updatePosition,
    PASSIVE_LISTENER_OPTIONS
  );
  window.visualViewport?.addEventListener(
    "scroll",
    handleScroll,
    PASSIVE_LISTENER_OPTIONS
  );
  await syncStepRoute();
  updatePosition();
  schedulePositionUpdate(100);
  if (
    onboardingStore.isActive &&
    !onboardingStore.disabled &&
    onboardingStore.events.length === 0
  ) {
    onboardingStore.addEvent(
      "tour_started",
      "Onboarding Guided Tour Started",
      "Automated tour init on mount"
    );
  }
});

onUnmounted(() => {
  isDisposed = true;
  if (typeof window !== "undefined") {
    window.removeEventListener(
      "resize",
      updatePosition,
      PASSIVE_LISTENER_OPTIONS
    );
    window.removeEventListener("scroll", handleScroll, SCROLL_CAPTURE_OPTIONS);
    window.visualViewport?.removeEventListener(
      "resize",
      updatePosition,
      PASSIVE_LISTENER_OPTIONS
    );
    window.visualViewport?.removeEventListener(
      "scroll",
      handleScroll,
      PASSIVE_LISTENER_OPTIONS
    );
  }
  for (const timerId of pendingTimers) {
    clearTimeout(timerId);
  }
  pendingTimers.clear();
  cancelPendingRaf();
});
</script>

<style scoped>
.onboarding-tour-container {
  position: relative;
  z-index: 1000;
}

.onboarding-backdrop {
  position: fixed;
  inset: 0;
  background-color: var(--semantic-color-backdrop);
  z-index: 998;
}

.onboarding-popover-card {
  box-sizing: border-box;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 2px solid var(--semantic-color-interactive-primary);
  border-radius: 12px;
  box-shadow: var(--shadow-xl);
  padding-top: 20px;
  padding-right: calc(20px + env(safe-area-inset-right, 0px));
  padding-bottom: calc(20px + env(safe-area-inset-bottom, 0px));
  padding-left: calc(20px + env(safe-area-inset-left, 0px));
  width: min(320px, calc(100vw - 24px));
  max-width: calc(100vw - 24px);
  max-height: min(90dvh, calc(100vh - 24px));
  overflow-y: auto;
  overscroll-behavior: contain;
  font-family: var(--semantic-font-family);
}

.popover-arrow {
  width: 0;
  height: 0;
  pointer-events: none;
  border-left: 10px solid transparent;
  border-right: 10px solid transparent;
  border-bottom: 10px solid var(--semantic-color-interactive-primary);
}

.popover-arrow--bottom {
  border-bottom: none;
  border-top: 10px solid var(--semantic-color-interactive-primary);
}

.onboarding-popover-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 8px;
  margin-bottom: 12px;
}

.onboarding-popover-title {
  font-weight: 700;
  color: var(--semantic-color-interactive-primary);
  margin: 0;
  font-size: var(--font-size-md);
}

.onboarding-step-counter {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
  font-variant-numeric: tabular-nums;
}

.onboarding-popover-body {
  font-size: var(--font-size-sm);
  line-height: 1.4;
  color: var(--semantic-color-text-body);
  margin-bottom: 16px;
}

.onboarding-dynamic-prompt {
  background-color: var(--semantic-color-success-bg-soft);
  border-left: 3px solid var(--semantic-color-success);
  color: var(--semantic-color-text-body);
  padding: 8px 10px;
  margin-bottom: 10px;
  border-radius: 0 4px 4px 0;
  font-style: italic;
  font-weight: 500;
}

.onboarding-popover-actions {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.onboarding-popover-left-actions {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
}

.onboarding-popover-right-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  align-items: center;
}

.btn-tour-dismiss,
.btn-tour-back,
.btn-tour-disable,
.btn-tour-next,
.btn-tour-complete {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
  min-height: 44px;
  min-width: 44px;
  font-size: var(--font-size-2xs);
  line-height: 1.2;
}

.btn-tour-dismiss,
.btn-tour-back {
  padding: 10px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  cursor: pointer;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.btn-tour-disable {
  padding: 10px 8px;
  color: var(--semantic-color-text-secondary);
  background: none;
  border: none;
  cursor: pointer;
  text-decoration: underline;
}

.btn-tour-next,
.btn-tour-complete {
  padding: 10px 14px;
  color: var(--semantic-color-surface);
  border: none;
  border-radius: 6px;
  cursor: pointer;
  font-weight: 600;
}

.btn-tour-next {
  background-color: var(--semantic-color-interactive-primary);
}

.btn-tour-complete {
  background-color: var(--semantic-color-success);
}
</style>
