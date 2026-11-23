<template>
  <ClinicalModal
    :id="idPrefix ? idPrefix + 'reason-modal' : 'reason-modal'"
    :is-open="show"
    :teleport="false"
    :show-close="false"
    size="sm"
    dialog-class="modal reason-modal-dialog"
    :title="title"
    :aria-labelledby="idPrefix + 'modal-title'"
    @close="onCancel"
  >
    <p v-if="description">
      {{ description }}
    </p>

    <form :id="formId" novalidate @submit.prevent="onConfirm">
      <div class="form-group" style="margin-bottom: 12px">
        <label :for="idPrefix + 'change-reason-select'"
          >Select Standard Reason</label
        >
        <select
          :id="idPrefix + 'change-reason-select'"
          v-model="selectedOption"
          class="reason-control"
          @change="handleSelectChange"
          @keydown.enter.prevent="onConfirm"
        >
          <option v-for="opt in options" :key="opt.value" :value="opt.value">
            {{ opt.text }}
          </option>
        </select>
      </div>

      <div class="form-group">
        <label :for="idPrefix + 'change-reason-text'">{{
          isOtherSelected
            ? "Custom Explanation (Required)"
            : "Custom Explanation (Optional)"
        }}</label>
        <textarea
          :id="idPrefix + 'change-reason-text'"
          ref="customTextRef"
          v-model="customText"
          class="reason-control"
          :placeholder="placeholder"
          :required="isOtherSelected"
          :aria-required="isOtherSelected ? 'true' : undefined"
          :aria-invalid="validationError ? 'true' : undefined"
          :aria-describedby="
            validationError ? idPrefix + 'reason-error' : undefined
          "
          @input="clearError"
          @keydown="handleReasonTextKeydown"
        />
      </div>

      <!-- Accessible Validation Feedback -->
      <div
        v-if="validationError"
        :id="idPrefix + 'reason-error'"
        class="validation-error-msg"
        style="
          color: var(--semantic-color-error);
          margin-top: 8px;
          font-weight: 600;
        "
        role="status"
        aria-live="polite"
      >
        ⚠️ {{ validationError }}
      </div>
    </form>

    <template #footer>
      <button
        :id="idPrefix + 'btn-cancel-change'"
        class="btn btn-secondary"
        type="button"
        @click="onCancel"
      >
        {{ cancelText }}
      </button>
      <button
        :id="idPrefix + 'btn-save-change'"
        class="btn btn-primary"
        type="submit"
        :form="formId"
        @click="onSubmitClick"
      >
        {{ confirmText }}
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { computed, nextTick, ref, watch } from "vue";
import { ClinicalModal } from "ui";

const props = defineProps({
  show: {
    type: Boolean,
    required: true,
  },
  title: {
    type: String,
    default: "Reason for Change Required",
  },
  description: {
    type: String,
    default:
      "To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for this modification.",
  },
  options: {
    type: Array,
    default: () => [
      { value: "Initial Entry", text: "Initial Data Entry" },
      {
        value: "Typographical Error",
        text: "Correction of typographical error",
      },
      { value: "Other", text: "Other (specify below)" },
    ],
  },
  defaultOption: {
    type: String,
    default: "",
  },
  idPrefix: {
    type: String,
    default: "",
  },
  cancelText: {
    type: String,
    default: "Cancel Change",
  },
  confirmText: {
    type: String,
    default: "Sign & Save",
  },
  placeholder: {
    type: String,
    default: "Explain the clinical reason for this modification...",
  },
});

const emit = defineEmits(["confirm", "cancel"]);

const selectedOption = ref("");
const customText = ref("");
const validationError = ref("");
const customTextRef = ref(null);

const formId = computed(() => `${props.idPrefix}reason-modal-form`);
const isOtherSelected = computed(() => selectedOption.value === "Other");

// Watch show to reset state on open
watch(
  () => props.show,
  (newVal) => {
    if (newVal) {
      selectedOption.value =
        props.defaultOption || props.options[0]?.value || "";
      customText.value = "";
      validationError.value = "";
    }
  },
  { immediate: true }
);

function handleSelectChange() {
  validationError.value = "";
}

function clearError() {
  validationError.value = "";
}

function onCancel() {
  emit("cancel");
}

function focusReasonText() {
  customTextRef.value?.focus?.();
  void nextTick(() => {
    customTextRef.value?.focus?.();
  });
}

function handleReasonTextKeydown(event) {
  if (event.key === "Enter" && (event.ctrlKey || event.metaKey)) {
    event.preventDefault();
    onConfirm();
  }
}

function onSubmitClick(event) {
  event?.preventDefault?.();
  onConfirm();
}

function onConfirm() {
  const sel = selectedOption.value;
  const cust = customText.value.trim();

  // Strict non-empty and non-whitespace validation for 'Other'
  if (sel === "Other") {
    if (!cust) {
      validationError.value =
        "Custom explanation is required when selecting 'Other'.";
      focusReasonText();
      return;
    }
  }

  // General check that we actually have a non-empty resolved reason
  const finalReason =
    sel === "Other" && cust ? cust : `${sel}${cust ? ": " + cust : ""}`;
  if (!finalReason.trim()) {
    validationError.value =
      "A non-empty reason for change is strictly required.";
    focusReasonText();
    return;
  }

  emit("confirm", finalReason);
}
</script>

<style scoped>
:deep(.reason-modal-dialog) {
  max-width: 450px;
}

p {
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 12px 0;
}

.form-group {
  min-width: 0;
}

.reason-control {
  width: 100%;
  min-width: 0;
  box-sizing: border-box;
  padding: 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-family: var(--font);
  font-size: 0.9rem;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

textarea.reason-control {
  height: 80px;
  resize: vertical;
}

.btn {
  max-width: 100%;
  box-sizing: border-box;
}
</style>
