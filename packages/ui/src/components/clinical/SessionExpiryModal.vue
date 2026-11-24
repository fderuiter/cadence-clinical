<script setup>
import { computed, useId } from "vue";
import ClinicalModal from "./ClinicalModal.vue";

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false,
  },
  open: {
    type: Boolean,
    default: false,
  },
  remainingSeconds: {
    type: Number,
    default: 60,
  },
  warningLeadSeconds: {
    type: Number,
    default: 60,
  },
  isExtending: {
    type: Boolean,
    default: false,
  },
  extending: {
    type: Boolean,
    default: false,
  },
  extensionCount: {
    type: Number,
    default: 0,
  },
  canExtend: {
    type: Boolean,
    default: true,
  },
  title: {
    type: String,
    default: "Session Expiring Soon",
  },
  description: {
    type: String,
    default:
      "Your authenticated session is about to expire. Select Extend Session to stay signed in and continue your work without interruption.",
  },
  draftsNotice: {
    type: String,
    default:
      "Local unsaved drafts in this browser remain preserved on this device if your session expires.",
  },
  extendLabel: {
    type: String,
    default: "Extend Session",
  },
  signOutLabel: {
    type: String,
    default: "Sign Out",
  },
  id: {
    type: String,
    default: "session-expiry-modal",
  },
  teleport: {
    type: [Boolean, String],
    default: false,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:open",
  "extend",
  "sign-out",
  "logout",
  "close",
]);

const instanceId = useId();
const descId = computed(() => `${props.id}-desc-${instanceId}`);
const extendBtnId = computed(() => `${props.id}-extend-btn`);
const signOutBtnId = computed(() => `${props.id}-signout-btn`);

const isModalOpen = computed(() => Boolean(props.modelValue || props.open));
const extendingActive = computed(() =>
  Boolean(props.isExtending || props.extending)
);

const clampedSeconds = computed(() =>
  Math.max(0, Math.ceil(Number(props.remainingSeconds) || 0))
);

const formattedCountdown = computed(() => {
  const total = clampedSeconds.value;
  const mins = Math.floor(total / 60);
  const secs = total % 60;
  return `${mins}:${String(secs).padStart(2, "0")}`;
});

const staticLeadSummary = computed(() => {
  const lead = Math.max(20, Math.ceil(Number(props.warningLeadSeconds) || 60));
  return `Approximately ${lead} seconds or less remaining before automatic sign-out.`;
});

function handleExtend() {
  if (extendingActive.value || !props.canExtend) return;
  emit("extend");
}

function handleSignOut() {
  emit("update:modelValue", false);
  emit("update:open", false);
  emit("sign-out");
  emit("logout");
}

function handleClose() {
  if (props.canExtend) {
    handleExtend();
    return;
  }
  emit("close");
}
</script>

<template>
  <ClinicalModal
    :id="id"
    :model-value="isModalOpen"
    :title="title"
    role="alertdialog"
    size="sm"
    :teleport="teleport"
    :initial-focus="`#${extendBtnId}`"
    :aria-describedby="descId"
    close-label="Extend session and close warning"
    data-testid="session-expiry-modal"
    @close="handleClose"
  >
    <div class="session-expiry-body">
      <div
        class="session-expiry-countdown-pill session-expiry-countdown-value"
        aria-hidden="true"
      >
        <span class="countdown-label">Time remaining</span>
        <strong
          class="countdown-digits tabular-nums"
          data-testid="session-expiry-countdown"
          >{{ formattedCountdown }}</strong
        >
      </div>

      <div :id="descId" class="session-expiry-copy">
        <p class="session-expiry-desc">
          {{ description }}
          <span class="sr-only">{{ staticLeadSummary }}</span>
        </p>
        <p v-if="draftsNotice" class="session-expiry-drafts">
          {{ draftsNotice }}
        </p>
      </div>
    </div>

    <template #footer>
      <button
        :id="signOutBtnId"
        type="button"
        class="btn-session-signout"
        data-testid="session-signout-btn"
        :disabled="extendingActive"
        @click="handleSignOut"
      >
        {{ signOutLabel }}
      </button>
      <button
        v-if="canExtend"
        :id="extendBtnId"
        type="button"
        class="btn-session-extend"
        data-testid="session-extend-btn"
        :disabled="extendingActive"
        @click="handleExtend"
      >
        {{ extendingActive ? "Extending..." : extendLabel }}
      </button>
    </template>
  </ClinicalModal>
</template>

<style scoped>
.session-expiry-body {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.session-expiry-countdown-pill {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: var(--spacing-sm);
  padding: var(--spacing-sm) var(--spacing-md);
  border-radius: var(--radius-sm);
  border: 1px solid var(--semantic-color-warning-border-strong);
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-text);
}

.countdown-label {
  font-size: var(--font-size-xs);
  font-weight: 600;
  color: var(--semantic-color-text);
}

.countdown-digits {
  font-family: var(--font-mono);
  font-size: var(--font-size-lg);
  font-weight: 700;
  font-variant-numeric: var(--font-variant-numeric-tabular);
  color: var(--semantic-color-text);
}

.session-expiry-copy {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.session-expiry-desc {
  position: relative;
  margin: 0;
  color: var(--semantic-color-text);
  font-size: var(--font-size-sm);
  line-height: 1.55;
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.session-expiry-drafts {
  margin: 0;
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: var(--radius-xs);
  border: 1px solid var(--semantic-color-info-border);
  background: var(--semantic-color-info-bg);
  color: var(--semantic-color-text);
  font-size: var(--font-size-xs);
  line-height: 1.45;
}

.btn-session-signout,
.btn-session-extend {
  min-height: var(--touch-target-min);
  padding: var(--spacing-xs) var(--spacing-md);
  border-radius: var(--radius-sm);
  font-size: var(--font-size-sm);
  font-weight: 600;
  cursor: pointer;
}

.btn-session-signout {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-session-signout:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn-session-extend {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border: 1px solid var(--semantic-color-primary-dark);
}

.btn-session-extend:hover:not(:disabled) {
  background: var(--semantic-color-primary-dark);
}

.btn-session-signout:disabled,
.btn-session-extend:disabled {
  opacity: 0.65;
  cursor: not-allowed;
}
</style>
