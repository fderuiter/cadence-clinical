<template>
  <div
    v-if="onboardingStore.isActive && !onboardingStore.disabled"
    class="onboarding-tour-container"
    style="position: relative; z-index: 1000"
  >
    <!-- Translucent backdrop only on steps 1 and 5 to highlight onboarding introduction/completion without blocking interaction during edits -->
    <div
      v-if="currentStep === 1 || currentStep === 5"
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
        background-color: white;
        color: var(--primary);
        border: 2px solid var(--accent);
        border-radius: 12px;
        box-shadow:
          0 10px 25px -5px rgba(0, 0, 0, 0.15),
          0 8px 10px -6px rgba(0, 0, 0, 0.15);
        padding: 20px;
        width: 320px;
        font-family: var(--font);
      "
    >
      <!-- Arrow pointing up (only on anchored steps) -->
      <div
        v-if="currentStep >= 2 && currentStep <= 4"
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
          border-bottom: 10px solid var(--accent);
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
            color: var(--accent);
            margin: 0;
            font-size: 1.05rem;
          "
        >
          {{ stepTitle }}
        </h4>
        <span style="font-size: 0.75rem; color: #64748b; font-weight: 600">
          Step {{ currentStep }} of 5
        </span>
      </div>

      <!-- Popover Body Content -->
      <div
        style="
          font-size: 0.85rem;
          line-height: 1.4;
          color: #334155;
          margin-bottom: 16px;
        "
      >
        <div
          style="
            background-color: #f0fdf4;
            border-left: 3px solid #22c55e;
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
              border: 1px solid #cbd5e1;
              border-radius: 6px;
              cursor: pointer;
              background: white;
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
              border: 1px solid #cbd5e1;
              border-radius: 6px;
              cursor: pointer;
              background: white;
            "
            @click="onboardingStore.prevStep()"
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
              color: #64748b;
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
            v-if="currentStep < 5"
            class="btn btn-primary btn-tour-next"
            style="
              padding: 6px 14px;
              font-size: 0.75rem;
              background-color: var(--accent);
              color: white;
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
              background-color: #22c55e;
              color: white;
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
import { useOnboardingStore } from "../stores/onboarding";
import { useAuthStore } from "../stores/auth";

const props = defineProps({
  activeTab: {
    type: String,
    required: true,
  },
});

const emit = defineEmits(["update:activeTab"]);

const onboardingStore = useOnboardingStore();
const authStore = useAuthStore();

const currentStep = computed(() => onboardingStore.currentStep);
const roles = computed(() => authStore.normalizedRoles || []);
const hasRole = (role) => roles.value.includes(role);

const stepTitle = computed(() => {
  switch (currentStep.value) {
    case 1:
      return "Welcome to the Sandbox";
    case 2:
      return "Schedule of Activities";
    case 3:
      return "MDR Concept Browse";
    case 4:
      return "Alignment & Differences";
    case 5:
      return "Tour Complete!";
    default:
      return "";
  }
});

const stepDescription = computed(() => {
  switch (currentStep.value) {
    case 1:
      return "This interactive sandbox guides you through designing clinical trial protocol metadata. Let's explore the workspace together step-by-step!";
    case 2:
      return "This tab shows the Schedule of Activities (SoA). Here you can build trial cohorts, epochs, and define study visits. Try adding an Arm or Epoch to see updates in real-time.";
    case 3:
      return "This tab allows you to browse concepts from standard clinical controlled vocabularies. Align your variables with CDISC definitions directly in the sandbox.";
    case 4:
      return "This tab shows real-time changes compared to standard CDISC templates, allowing you to track protocol deviations and structural changes as you edit.";
    case 5:
      return "Your trial design and onboarding events are safely saved in your browser's persistent storage. Turn on Onboarding Telemetry in the footer to inspect captured user interactions!";
    default:
      return "";
  }
});

const stepTargetDescription = computed(() => {
  switch (currentStep.value) {
    case 1:
      return "Welcome Popover";
    case 2:
      return "Interactive SoA & USDM Tab";
    case 3:
      return "MDR Concept Browse & Edit Tab";
    case 4:
      return "Alignment & Differences Report Tab";
    case 5:
      return "Tour Completion";
    default:
      return "";
  }
});

const popoverStyle = ref({
  position: "fixed",
  top: "50%",
  left: "50%",
  transform: "translate(-50%, -50%)",
  zIndex: 1000,
});

const updatePosition = () => {
  if (!onboardingStore.isActive || onboardingStore.disabled) return;

  const step = currentStep.value;
  let selector = null;
  if (step === 2) selector = ".tab-btn-soa";
  else if (step === 3) selector = ".tab-btn-mdr";
  else if (step === 4) selector = ".tab-btn-diff";

  if (selector) {
    requestAnimationFrame(() => {
      const el = document.querySelector(selector);
      if (el) {
        const rect = el.getBoundingClientRect();
        popoverStyle.value = {
          position: "fixed",
          top: `${rect.bottom + 12}px`,
          left: `${rect.left + rect.width / 2}px`,
          transform: "translateX(-50%)",
          zIndex: 1000,
        };
      } else {
        popoverStyle.value = {
          position: "fixed",
          top: "50%",
          left: "50%",
          transform: "translate(-50%, -50%)",
          zIndex: 1000,
        };
      }
    });
  } else {
    popoverStyle.value = {
      position: "fixed",
      top: "50%",
      left: "50%",
      transform: "translate(-50%, -50%)",
      zIndex: 1000,
    };
  }
};

const handleNext = () => {
  const currentDesc = stepTargetDescription.value;
  onboardingStore.nextStep(currentDesc);
};

const handleComplete = () => {
  onboardingStore.nextStep(stepTargetDescription.value);
  onboardingStore.disableTour();
};

const dynamicPrompt = computed(() => {
  const step = currentStep.value;
  if (hasRole("sponsor_designer") || hasRole("designer")) {
    if (step === 1)
      return "Welcome, Designer! Ready to architect clinical protocols and CDISC USDM data structures with maximum accuracy?";
    if (step === 2)
      return "Design the perfect Schedule of Activities with clinical precision for your study arms and epochs.";
    if (step === 3)
      return "Browse and bind CDISC terminology concepts directly into your study parameters.";
    if (step === 4)
      return "Audit and verify that your clinical data elements are strictly aligned with GxP and regulatory standards.";
    if (step === 5)
      return "All set! Your clinical metadata schema is fully authored, mapped, and persisted.";
  }
  if (hasRole("data_manager")) {
    if (step === 1)
      return "Welcome, Data Manager! You can map concepts, curate eCRF templates, and inspect data collection rules.";
    if (step === 2)
      return "Validate that all scheduled study activities map directly to clean data collection points.";
    if (step === 3)
      return "Enforce standard terminology to prevent data entry inconsistencies during downstream capture.";
    if (step === 4)
      return "Generate alignment metrics and difference reports to ensure complete database lock compliance.";
    if (step === 5)
      return "Fantastic! Your clinical metadata definitions are securely stored and ready for electronic capture mapping.";
  }
  if (hasRole("sponsor_admin")) {
    if (step === 1)
      return "Welcome, Administrator! Oversee the full digital data flow, audit trails, and multi-tenant compliance settings.";
    if (step === 2)
      return "Review and approve the global Schedule of Activities to ensure operational feasibility.";
    if (step === 3)
      return "Browse controlled vocabularies and manage organization-wide metadata catalogs.";
    if (step === 4)
      return "Analyze difference reports to ensure zero schema drift across all study versions.";
    if (step === 5)
      return "Excellent! The study configuration is fully locked, logged, and securely backed up.";
  }

  if (step === 1)
    return "Welcome to the Clinical Sandbox! Let's walk through how to build and persist a custom study design.";
  if (step === 2)
    return "Define study arms, epochs, and design a Schedule of Activities matrix.";
  if (step === 3)
    return "Search and browse controlled clinical vocabularies and CDISC concept codes.";
  if (step === 4)
    return "Check differences and ensure consistency across protocol metadata models.";
  if (step === 5)
    return "Congratulations! You have successfully explored the core features of the clinical sandbox.";
  return "";
});

watch(currentStep, (newStep) => {
  if (newStep === 2) emit("update:activeTab", "soa");
  else if (newStep === 3) emit("update:activeTab", "mdr");
  else if (newStep === 4) emit("update:activeTab", "diff");
  updatePosition();
});

watch(
  () => props.activeTab,
  (newTab) => {
    if (onboardingStore.isActive && !onboardingStore.disabled) {
      if (newTab === "soa" && currentStep.value !== 2) {
        onboardingStore.currentStep = 2;
      } else if (newTab === "mdr" && currentStep.value !== 3) {
        onboardingStore.currentStep = 3;
      } else if (newTab === "diff" && currentStep.value !== 4) {
        onboardingStore.currentStep = 4;
      }
    }
    updatePosition();
  }
);

watch(
  () => onboardingStore.isActive,
  (active) => {
    if (active) updatePosition();
  }
);

onMounted(() => {
  window.addEventListener("resize", updatePosition);
  updatePosition();
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
});
</script>
