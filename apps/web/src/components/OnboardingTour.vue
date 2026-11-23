<template>
  <ClinicalModal
    :is-open="isTourOpen"
    :modal="isModalStep"
    :teleport="false"
    :show-header="false"
    :show-footer="false"
    backdrop-class="onboarding-backdrop"
    dialog-class="onboarding-popover-card"
    body-class="onboarding-popover-surface"
    aria-labelledby="onboarding-tour-title"
    :style="popoverStyle"
    @close="onboardingStore.dismissTour()"
  >
    <!-- Arrow pointing up (only on anchored steps with valid target selector) -->
    <div
      v-if="hasTargetElement && currentStepConfig?.targetSelector"
      class="popover-arrow"
    />

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
      <div>
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
  </ClinicalModal>
</template>

<script setup>
import { computed, ref, onMounted, onUnmounted, watch } from "vue";
import { useRouter, useRoute } from "vue-router";
import { ClinicalModal } from "ui";
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

const currentStep = computed(() => onboardingStore.currentStep);
const currentStepConfig = computed(
  () => onboardingStore.currentStepConfig || {}
);
const totalSteps = computed(() => onboardingStore.totalSteps || 1);

const effectiveRoles = computed(() => {
  if (authStore.rawRoles?.length > 0) {
    return authStore.normalizedRoles || [];
  }
  const personaKey =
    authStore.currentPersona || onboardingStore.currentPersona || "super_admin";
  const preset = PERSONA_PRESETS.find((p) => p.key === personaKey);
  return preset ? [...preset.requires] : ["sponsor_admin"];
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

const isTourOpen = computed(() =>
  Boolean(onboardingStore.isActive && !onboardingStore.disabled)
);
const isModalStep = computed(
  () =>
    isTourOpen.value &&
    (currentStep.value === 1 ||
      currentStep.value === totalSteps.value ||
      !hasTargetElement.value)
);

const popoverStyle = ref({
  position: "fixed",
  top: "50%",
  left: "50%",
  transform: "translate(-50%, -50%)",
  zIndex: 1000,
});

let currentRafId = null;
let isDisposed = false;
const pendingTimers = new Set();

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
        const query = config.query
          ? { ...route.query, ...config.query }
          : { ...route.query };
        await router.push({ name: targetRouteName, query });
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

  if (!selector) {
    hasTargetElement.value = false;
    popoverStyle.value = {
      position: "fixed",
      top: "50%",
      left: "50%",
      transform: "translate(-50%, -50%)",
      zIndex: 1000,
    };
    return;
  }

  const MAX_RETRIES = 5;
  let attempts = 0;

  const checkAndPosition = () => {
    if (isDisposed || typeof document === "undefined") {
      currentRafId = null;
      return;
    }
    const el = document.querySelector(selector);
    if (el) {
      const rect = el.getBoundingClientRect();
      if (rect.width > 0 && rect.height > 0) {
        popoverStyle.value = {
          position: "fixed",
          top: `${rect.bottom + 12}px`,
          left: `${rect.left + rect.width / 2}px`,
          transform: "translateX(-50%)",
          zIndex: 1000,
        };
        hasTargetElement.value = true;
        currentRafId = null;
        return;
      }
    }
    if (attempts < MAX_RETRIES && typeof requestAnimationFrame === "function") {
      attempts++;
      currentRafId = requestAnimationFrame(checkAndPosition);
    } else {
      hasTargetElement.value = false;
      popoverStyle.value = {
        position: "fixed",
        top: "50%",
        left: "50%",
        transform: "translate(-50%, -50%)",
        zIndex: 1000,
      };
      currentRafId = null;
    }
  };

  checkAndPosition();
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

watch(currentStep, async () => {
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
  (active) => {
    if (active) {
      syncStepRoute();
      updatePosition();
    }
  }
);

watch(
  () => authStore.currentPersona,
  async (newPersona, oldPersona) => {
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
  window.addEventListener("resize", updatePosition);
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
    window.removeEventListener("resize", updatePosition);
  }
  for (const timerId of pendingTimers) {
    clearTimeout(timerId);
  }
  pendingTimers.clear();
  cancelPendingRaf();
});
</script>

<style scoped>
:deep(.onboarding-backdrop:not(.is-non-modal)) {
  background-color: rgba(0, 0, 0, 0.4);
  z-index: 998;
}

:deep(.onboarding-popover-card) {
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 2px solid var(--semantic-color-interactive-primary);
  border-radius: 12px;
  box-shadow:
    0 10px 25px -5px rgba(0, 0, 0, 0.15),
    0 8px 10px -6px rgba(0, 0, 0, 0.15);
  width: min(320px, calc(100vw - 24px));
  max-width: 320px;
  font-family: var(--semantic-font-family);
  overflow: visible;
}

:deep(.onboarding-popover-surface) {
  position: relative;
  padding: 20px;
  overflow-x: hidden;
  overflow-y: auto;
}

@media (max-width: 480px) {
  :deep(.onboarding-popover-card:not(.is-non-modal)) {
    position: relative !important;
    top: auto !important;
    left: auto !important;
    transform: none !important;
    width: 100%;
    max-width: 100%;
    max-height: 90vh;
    max-height: 90dvh;
    border-bottom-left-radius: 0;
    border-bottom-right-radius: 0;
  }
}

.popover-arrow {
  position: absolute;
  top: -10px;
  left: 50%;
  transform: translateX(-50%);
  width: 0;
  height: 0;
  border-left: 10px solid transparent;
  border-right: 10px solid transparent;
  border-bottom: 10px solid var(--semantic-color-interactive-primary);
}

.onboarding-popover-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 12px;
}

.onboarding-popover-title {
  font-weight: 700;
  color: var(--semantic-color-interactive-primary);
  margin: 0;
  font-size: 1.05rem;
}

.onboarding-step-counter {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.onboarding-popover-body {
  font-size: 0.85rem;
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
  gap: 8px;
}

.onboarding-popover-right-actions {
  display: flex;
  gap: 6px;
  align-items: center;
}

.btn-tour-dismiss,
.btn-tour-back {
  padding: 6px 12px;
  font-size: 0.75rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  cursor: pointer;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.btn-tour-disable {
  padding: 6px 8px;
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
  background: none;
  border: none;
  cursor: pointer;
  text-decoration: underline;
}

.btn-tour-next,
.btn-tour-complete {
  padding: 6px 14px;
  font-size: 0.75rem;
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
