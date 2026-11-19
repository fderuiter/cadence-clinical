<template>
  <div
    v-if="onboardingStore.isActive && !onboardingStore.disabled"
    class="onboarding-tour-container"
    style="position: relative; z-index: 1000"
  >
    <!-- Translucent backdrop only on unanchored modal steps to highlight onboarding introduction/completion without blocking interaction during edits -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div
      v-if="
        currentStep === 1 || currentStep === totalSteps || !hasTargetElement
      "
      class="onboarding-backdrop"
      style="
        position: fixed;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        background-color: rgba(0, 0, 0, 0.4);
        z-index: 998;
      "
      @click="onboardingStore.dismissTour()"
    />

    <!-- The Popover Box -->
    <div
      :style="popoverStyle"
      class="onboarding-popover-card"
      style="
        background-color: var(--semantic-color-surface);
        color: var(--semantic-color-text);
        border: 2px solid var(--semantic-color-interactive-primary);
        border-radius: 12px;
        box-shadow:
          0 10px 25px -5px rgba(0, 0, 0, 0.15),
          0 8px 10px -6px rgba(0, 0, 0, 0.15);
        padding: 20px;
        width: 320px;
        font-family: var(--semantic-font-family);
      "
    >
      <!-- Arrow pointing up (only on anchored steps with valid target selector) -->
      <div
        v-if="hasTargetElement && currentStepConfig?.targetSelector"
        class="popover-arrow"
        style="
          position: absolute;
          top: -10px;
          left: 50%;
          transform: translateX(-50%);
          width: 0;
          height: 0;
          border-left: 10px solid transparent;
          border-right: 10px solid transparent;
          border-bottom: 10px solid var(--semantic-color-interactive-primary);
        "
      />

      <!-- Popover Header -->
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: flex-start;
          margin-bottom: 12px;
        "
      >
        <h4
          style="
            font-weight: 700;
            color: var(--semantic-color-interactive-primary);
            margin: 0;
            font-size: 1.05rem;
          "
        >
          {{ stepTitle }}
        </h4>
        <span
          style="
            font-size: 0.75rem;
            color: var(--semantic-color-text-secondary);
            font-weight: 600;
          "
        >
          Step {{ currentStep }} of {{ totalSteps }}
        </span>
      </div>

      <!-- Popover Body Content -->
      <div
        style="
          font-size: 0.85rem;
          line-height: 1.4;
          color: var(--semantic-color-text-body);
          margin-bottom: 16px;
        "
      >
        <div
          v-if="dynamicPrompt"
          style="
            background-color: var(--semantic-color-success-bg-soft);
            border-left: 3px solid var(--semantic-color-success);
            color: var(--semantic-color-text-body);
            padding: 8px 10px;
            margin-bottom: 10px;
            border-radius: 0 4px 4px 0;
            font-style: italic;
            font-weight: 500;
          "
        >
          {{ dynamicPrompt }}
        </div>
        <p>{{ stepDescription }}</p>
      </div>

      <!-- Popover Action Buttons -->
      <div
        style="
          display: flex;
          justify-content: space-between;
          align-items: center;
          gap: 8px;
        "
      >
        <!-- Left buttons -->
        <div>
          <button
            v-if="currentStep === 1"
            class="btn btn-secondary btn-tour-dismiss"
            style="
              padding: 6px 12px;
              font-size: 0.75rem;
              border: 1px solid var(--semantic-color-border-control);
              border-radius: 6px;
              cursor: pointer;
              background: var(--semantic-color-surface);
              color: var(--semantic-color-text);
            "
            @click="onboardingStore.dismissTour()"
          >
            Dismiss
          </button>
          <button
            v-else
            class="btn btn-secondary btn-tour-back"
            style="
              padding: 6px 12px;
              font-size: 0.75rem;
              border: 1px solid var(--semantic-color-border-control);
              border-radius: 6px;
              cursor: pointer;
              background: var(--semantic-color-surface);
              color: var(--semantic-color-text);
            "
            @click="handlePrev"
          >
            Back
          </button>
        </div>

        <!-- Right/Middle buttons -->
        <div style="display: flex; gap: 6px; align-items: center">
          <button
            class="btn btn-tour-disable"
            style="
              padding: 6px 8px;
              font-size: 0.7rem;
              color: var(--semantic-color-text-secondary);
              background: none;
              border: none;
              cursor: pointer;
              text-decoration: underline;
            "
            @click="onboardingStore.disableTour()"
          >
            Don't show again
          </button>

          <button
            v-if="currentStep < totalSteps"
            class="btn btn-primary btn-tour-next"
            style="
              padding: 6px 14px;
              font-size: 0.75rem;
              background-color: var(--semantic-color-interactive-primary);
              color: var(--semantic-color-surface);
              border: none;
              border-radius: 6px;
              cursor: pointer;
              font-weight: 600;
            "
            @click="handleNext"
          >
            Next
          </button>
          <button
            v-else
            class="btn btn-success btn-tour-complete"
            style="
              padding: 6px 14px;
              font-size: 0.75rem;
              background-color: var(--semantic-color-success);
              color: var(--semantic-color-surface);
              border: none;
              border-radius: 6px;
              cursor: pointer;
              font-weight: 600;
            "
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
import { computed, ref, onMounted, onUnmounted, watch } from "vue";
import { useRouter, useRoute } from "vue-router";
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

const popoverStyle = ref({
  position: "fixed",
  top: "50%",
  left: "50%",
  transform: "translate(-50%, -50%)",
  zIndex: 1000,
});

let currentRafId = null;

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
  if (!onboardingStore.isActive || onboardingStore.disabled) return;

  if (currentRafId !== null) {
    cancelAnimationFrame(currentRafId);
    currentRafId = null;
  }

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
    if (attempts < MAX_RETRIES) {
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
  setTimeout(updatePosition, 50);
  setTimeout(updatePosition, 150);
};

const handlePrev = async () => {
  onboardingStore.prevStep();
  await syncStepRoute();
  updatePosition();
  setTimeout(updatePosition, 50);
  setTimeout(updatePosition, 150);
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
  setTimeout(updatePosition, 50);
});

watch(
  () => route?.fullPath,
  () => {
    updatePosition();
    setTimeout(updatePosition, 50);
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
  async (newPersona) => {
    if (newPersona) {
      onboardingStore.loadPersonaState(newPersona);
      await syncStepRoute();
      updatePosition();
    }
  },
  { immediate: true }
);

onMounted(async () => {
  window.addEventListener("resize", updatePosition);
  await syncStepRoute();
  updatePosition();
  setTimeout(updatePosition, 100);
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
  window.removeEventListener("resize", updatePosition);
  if (currentRafId !== null) {
    cancelAnimationFrame(currentRafId);
    currentRafId = null;
  }
});
</script>
