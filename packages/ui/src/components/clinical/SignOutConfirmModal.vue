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
  title: {
    type: String,
    default: "Sign Out With Unsaved Drafts?",
  },
  description: {
    type: String,
    default:
      "You have unsaved local form or questionnaire edits in this browser. Pending keystrokes have been saved locally, but signing out now will close your active session and clear account-scoped workspace drafts.",
  },
  stayLabel: {
    type: String,
    default: "Stay & Keep Editing",
  },
  confirmLabel: {
    type: String,
    default: "Sign Out Anyway",
  },
  id: {
    type: String,
    default: "signout-confirm-modal",
  },
  teleport: {
    type: [Boolean, String],
    default: false,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:open",
  "confirm",
  "cancel",
]);

const instanceId = useId();
const descId = computed(() => `${props.id}-desc-${instanceId}`);
const stayBtnId = computed(() => `${props.id}-stay-btn`);
const confirmBtnId = computed(() => `${props.id}-confirm-btn`);

const isModalOpen = computed(() => Boolean(props.modelValue || props.open));

function handleStay() {
  emit("update:modelValue", false);
  emit("update:open", false);
  emit("cancel");
}

function handleConfirm() {
  emit("update:modelValue", false);
  emit("update:open", false);
  emit("confirm");
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
    :initial-focus="`#${stayBtnId}`"
    :aria-describedby="descId"
    close-label="Stay signed in"
    data-testid="signout-confirm-modal"
    @close="handleStay"
  >
    <p :id="descId" class="signout-confirm-desc">
      {{ description }}
    </p>

    <template #footer>
      <button
        :id="stayBtnId"
        type="button"
        class="btn-signout-stay"
        data-testid="signout-stay-btn"
        @click="handleStay"
      >
        {{ stayLabel }}
      </button>
      <button
        :id="confirmBtnId"
        type="button"
        class="btn-signout-confirm"
        data-testid="signout-confirm-btn"
        @click="handleConfirm"
      >
        {{ confirmLabel }}
      </button>
    </template>
  </ClinicalModal>
</template>

<style scoped>
.signout-confirm-desc {
  margin: 0;
  color: var(--semantic-color-text-dark);
  font-size: 0.95rem;
  line-height: 1.55;
}

.btn-signout-stay,
.btn-signout-confirm {
  min-height: 44px;
  padding: 10px 16px;
  border-radius: 6px;
  font-size: 0.9rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-signout-stay {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-signout-stay:hover:not(:disabled) {
  background: var(--semantic-color-surface-subtle);
}

.btn-signout-confirm {
  background: var(--semantic-color-error);
  color: var(--semantic-color-text-on-solid);
  border: 1px solid var(--semantic-color-error-text-dark);
}

.btn-signout-confirm:hover:not(:disabled) {
  background: var(--semantic-color-error-text-dark);
}

.btn-signout-stay:focus-visible,
.btn-signout-confirm:focus-visible {
  outline: 2px solid var(--semantic-color-primary);
  outline-offset: 2px;
}
</style>
