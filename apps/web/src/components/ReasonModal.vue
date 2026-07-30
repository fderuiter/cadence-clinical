<template>
  <div
    v-if="show"
    :id="idPrefix ? idPrefix + 'reason-modal' : 'reason-modal'"
    class="modal-overlay"
    style="display: flex"
    role="dialog"
    aria-modal="true"
    :aria-labelledby="idPrefix + 'modal-title'"
  >
    <div class="modal">
      <div :id="idPrefix + 'modal-title'" class="modal-header">
        {{ title }}
      </div>
      <div class="modal-body">
        <p v-if="description">{{ description }}</p>

        <div class="form-group" style="margin-bottom: 12px">
          <label :for="idPrefix + 'change-reason-select'">Select Standard Reason</label>
          <select
            :id="idPrefix + 'change-reason-select'"
            v-model="selectedOption"
            @change="handleSelectChange"
          >
            <option
              v-for="opt in options"
              :key="opt.value"
              :value="opt.value"
            >
              {{ opt.text }}
            </option>
          </select>
        </div>

        <div class="form-group">
          <label :for="idPrefix + 'change-reason-text'">Custom Explanation (Optional)</label>
          <textarea
            :id="idPrefix + 'change-reason-text'"
            v-model="customText"
            :placeholder="placeholder"
            @input="clearError"
          />
        </div>

        <!-- Accessible Validation Feedback -->
        <div
          v-if="validationError"
          :id="idPrefix + 'reason-error'"
          class="validation-error-msg"
          style="color: #ef4444; margin-top: 8px; font-weight: 600"
          role="status"
          aria-live="polite"
        >
          ⚠️ {{ validationError }}
        </div>
      </div>
      <div class="modal-footer">
        <button
          :id="idPrefix + 'btn-cancel-change'"
          class="btn"
          type="button"
          @click="onCancel"
        >
          {{ cancelText }}
        </button>
        <button
          :id="idPrefix + 'btn-save-change'"
          class="btn btn-primary"
          type="button"
          @click="onConfirm"
        >
          {{ confirmText }}
        </button>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";

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
    default: "To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for this modification.",
  },
  options: {
    type: Array,
    default: () => [
      { value: "Initial Entry", text: "Initial Data Entry" },
      { value: "Typographical Error", text: "Correction of typographical error" },
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
  }
});

const emit = defineEmits(["confirm", "cancel"]);

const selectedOption = ref("");
const customText = ref("");
const validationError = ref("");

// Watch show to reset state on open
watch(
  () => props.show,
  (newVal) => {
    if (newVal) {
      selectedOption.value = props.defaultOption || (props.options[0]?.value || "");
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

function onConfirm() {
  const sel = selectedOption.value;
  const cust = customText.value.trim();

  // Strict non-empty and non-whitespace validation for 'Other'
  if (sel === "Other") {
    if (!cust) {
      validationError.value = "Custom explanation is required when selecting 'Other'.";
      return;
    }
  }

  // General check that we actually have a non-empty resolved reason
  const finalReason = sel === "Other" && cust ? cust : `${sel}${cust ? ": " + cust : ""}`;
  if (!finalReason.trim()) {
    validationError.value = "A non-empty reason for change is strictly required.";
    return;
  }

  emit("confirm", finalReason);
}
</script>
