<script setup>
import { computed, ref, watch } from "vue";
import ClinicalDrawer from "./ClinicalDrawer.vue";
import ConfidenceBadge from "./ConfidenceBadge.vue";

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false,
  },
  isOpen: {
    type: Boolean,
    default: false,
  },
  title: {
    type: String,
    default: "AI Suggestion Review",
  },
  originalValue: {
    type: [String, Object, Array, Number, Boolean],
    default: "",
  },
  suggestedValue: {
    type: [String, Object, Array, Number, Boolean],
    required: true,
  },
  confidenceScore: {
    type: Number,
    default: null,
  },
  modelIdentifier: {
    type: String,
    default: "",
  },
  promptSummary: {
    type: String,
    default: "",
  },
  entityName: {
    type: String,
    default: "",
  },
  fieldLabel: {
    type: String,
    default: "",
  },
  requireReason: {
    type: Boolean,
    default: true,
  },
  isApproving: {
    type: Boolean,
    default: false,
  },
  isRejecting: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:isOpen",
  "close",
  "accept",
  "approve",
  "edit",
  "dismiss",
  "reject",
]);

const drawerOpen = computed({
  get: () => props.modelValue || props.isOpen,
  set: (val) => {
    emit("update:modelValue", val);
    emit("update:isOpen", val);
  },
});

const isEditing = ref(false);
const reasonForChange = ref("");
const validationError = ref("");

const formatDisplayValue = (val) => {
  if (val === null || val === undefined) return "—";
  if (typeof val === "object") {
    try {
      return JSON.stringify(val, null, 2);
    } catch {
      return String(val);
    }
  }
  return String(val);
};

const editedValueText = ref(formatDisplayValue(props.suggestedValue));

watch(
  () => props.suggestedValue,
  (newVal) => {
    editedValueText.value = formatDisplayValue(newVal);
  },
  { deep: true, immediate: true }
);

const closeDrawer = () => {
  drawerOpen.value = false;
  emit("close");
  isEditing.value = false;
  validationError.value = "";
  reasonForChange.value = "";
};

// ClinicalDrawer delegates Escape dismissal via handleKeyDown
const handleKeyDown = (e) => {
  if (!e || e.key === "Escape") {
    closeDrawer();
  }
};

const getParsedFinalValue = () => {
  if (!isEditing.value) {
    return props.suggestedValue;
  }
  const text = editedValueText.value.trim();
  if (
    typeof props.suggestedValue === "object" &&
    props.suggestedValue !== null
  ) {
    try {
      return JSON.parse(text);
    } catch {
      return text;
    }
  }
  return text;
};

const validateReason = () => {
  if (props.requireReason && !reasonForChange.value.trim()) {
    validationError.value =
      "21 CFR Part 11 compliance requires a reason for change before acceptance.";
    return false;
  }
  validationError.value = "";
  return true;
};

const handleAccept = () => {
  if (!validateReason()) return;
  const finalValue = getParsedFinalValue();
  const payload = {
    value: finalValue,
    originalValue: props.originalValue,
    reasonForChange: reasonForChange.value.trim(),
    confidenceScore: props.confidenceScore,
    modelIdentifier: props.modelIdentifier,
    isModified: isEditing.value,
  };
  emit("accept", payload);
  emit("approve", payload);
  closeDrawer();
};

const handleDismiss = () => {
  const payload = {
    reasonForChange: reasonForChange.value.trim() || "Rejected by reviewer",
    originalValue: props.originalValue,
    suggestedValue: props.suggestedValue,
  };
  emit("dismiss", payload);
  emit("reject", payload);
  closeDrawer();
};

const toggleEdit = () => {
  isEditing.value = !isEditing.value;
  if (isEditing.value) {
    emit("edit", { value: editedValueText.value });
  }
};
</script>

<template>
  <ClinicalDrawer
    :model-value="drawerOpen"
    :aria-label="title"
    role="dialog"
    aria-modal="true"
    size="lg"
    overlay-class="ai-drawer-backdrop"
    panel-class="ai-drawer-panel"
    body-class="ai-drawer-body"
    close-label="Close AI suggestion drawer"
    @close="handleKeyDown"
  >
    <template #header>
      <header class="ai-drawer-header">
        <div class="ai-drawer-title-group">
          <div class="ai-header-badge-row">
            <span class="ai-drawer-sparkle" aria-hidden="true">✨</span>
            <h2 class="ai-drawer-title">
              {{ title }}
            </h2>
            <ConfidenceBadge
              v-if="confidenceScore !== null && confidenceScore !== undefined"
              :score="confidenceScore"
              size="sm"
            />
          </div>
          <p v-if="entityName || fieldLabel" class="ai-drawer-subtitle">
            Target:
            <strong
              >{{ entityName ? `${entityName} › ` : ""
              }}{{ fieldLabel || "Clinical Field" }}</strong
            >
          </p>
        </div>
        <button
          type="button"
          class="ai-drawer-close-btn"
          aria-label="Close AI suggestion drawer"
          @click="closeDrawer"
        >
          ✕
        </button>
      </header>
    </template>

    <template #subheader>
      <!-- Provenance Info Banner -->
      <div v-if="modelIdentifier || promptSummary" class="ai-provenance-banner">
        <div v-if="modelIdentifier" class="ai-provenance-item">
          <span class="ai-provenance-label">Model:</span>
          <code class="ai-provenance-val">{{ modelIdentifier }}</code>
        </div>
        <div v-if="promptSummary" class="ai-provenance-item">
          <span class="ai-provenance-label">Rationale:</span>
          <span class="ai-provenance-val">{{ promptSummary }}</span>
        </div>
      </div>
    </template>

    <!-- Diff Content (div inside ClinicalDrawer body, avoiding nested main landmarks) -->
    <div class="ai-diff-container">
      <!-- Original Section -->
      <div class="ai-diff-box ai-diff-original">
        <div class="ai-diff-header">
          <span class="ai-diff-tag">Original Baseline</span>
        </div>
        <div class="ai-diff-content">
          <pre class="ai-diff-pre">{{ formatDisplayValue(originalValue) }}</pre>
        </div>
      </div>

      <!-- AI Suggested Section -->
      <div
        class="ai-diff-box ai-diff-suggested"
        :class="{ 'is-editing': isEditing }"
      >
        <div class="ai-diff-header">
          <span class="ai-diff-tag ai-tag-ai">AI Proposed</span>
          <button type="button" class="ai-edit-toggle-btn" @click="toggleEdit">
            {{ isEditing ? "👁 View Structured" : "✏️ Edit Suggestion" }}
          </button>
        </div>
        <div class="ai-diff-content">
          <textarea
            v-if="isEditing"
            v-model="editedValueText"
            class="ai-edit-textarea"
            aria-label="Edit AI proposed value"
            rows="6"
          ></textarea>
          <pre v-else class="ai-diff-pre ai-pre-highlight">{{
            formatDisplayValue(suggestedValue)
          }}</pre>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Reason for Change Input -->
    <div class="ai-compliance-section">
      <label for="ai-reason-for-change" class="ai-reason-label">
        Reason for Change (21 CFR Part 11 Audit Justification)
        <span v-if="requireReason" class="ai-required-star">*</span>
      </label>
      <input
        id="ai-reason-for-change"
        v-model="reasonForChange"
        type="text"
        class="ai-reason-input"
        :class="{ 'has-error': validationError }"
        placeholder="e.g., Confirmed AI coding suggestion against MedDRA LLT v27.0"
        aria-required="true"
        @input="validationError = ''"
      />
      <p v-if="validationError" class="ai-validation-error" role="alert">
        {{ validationError }}
      </p>
    </div>

    <!-- Footer Actions -->
    <template #footer>
      <div class="ai-drawer-footer-inner">
        <button
          type="button"
          class="btn btn-dismiss"
          :disabled="isApproving || isRejecting"
          @click="handleDismiss"
        >
          Dismiss / Reject
        </button>
        <div class="ai-footer-right">
          <button type="button" class="btn btn-secondary" @click="closeDrawer">
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-accept"
            :disabled="isApproving || isRejecting"
            @click="handleAccept"
          >
            <span v-if="isApproving" class="ai-spinner" aria-hidden="true"
              >⏳</span
            >
            <span v-else>Accept & Apply</span>
          </button>
        </div>
      </div>
    </template>
  </ClinicalDrawer>
</template>

<style scoped>
:deep(.ai-drawer-backdrop) {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: var(--semantic-color-backdrop-soft);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
}

:deep(.ai-drawer-panel) {
  width: var(
    --semantic-drawer-width-lg,
    var(--drawer-width-lg, min(100vw, 640px))
  );
  max-width: 100vw;
  height: 100vh;
  background: var(--color-surface);
  border-left: 1px solid var(--color-border);
  box-shadow: var(--shadow-drawer-left);
  display: flex;
  flex-direction: column;
  animation: ai-slide-in 0.25s cubic-bezier(0.16, 1, 0.3, 1);
}

:deep(.ai-drawer-panel:focus:not(:focus-visible)) {
  outline: none;
}

@keyframes ai-slide-in {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

/* Header */
.ai-drawer-header {
  display: flex;
  align-items: flex-start;
  justify-content: space-between;
  padding: 18px 24px;
  border-bottom: 1px solid var(--color-border);
  background: var(--color-surface-muted);
}

.ai-header-badge-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs, 8px);
  flex-wrap: wrap;
}

.ai-drawer-sparkle {
  font-size: var(--font-size-lg);
}

.ai-drawer-title {
  margin: 0;
  font-size: var(--font-size-md);
  font-weight: 600;
  color: var(--color-text);
}

.ai-drawer-subtitle {
  margin: 4px 0 0 0;
  font-size: var(--font-size-xs);
  color: var(--color-text-muted);
}

.ai-drawer-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background: transparent;
  border: none;
  font-size: var(--font-size-md);
  color: var(--color-text-muted);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
  transition: background 0.15s;
}

.ai-drawer-close-btn:hover:not(:disabled) {
  background: var(--color-border);
  color: var(--color-text);
}

/* Provenance Banner */
.ai-provenance-banner {
  padding: 10px 24px;
  background: var(--color-primary-light);
  border-bottom: 1px solid var(--semantic-color-info-border-accent);
  font-size: var(--font-size-xs);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.ai-provenance-item {
  display: flex;
  align-items: baseline;
  gap: 6px;
}

.ai-provenance-label {
  font-weight: 600;
  color: var(--color-primary-dark);
}

.ai-provenance-val {
  color: var(--color-text);
}

code.ai-provenance-val {
  background: var(--semantic-color-surface);
  padding: 1px 4px;
  border-radius: 3px;
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
}

/* Body */
:deep(.ai-drawer-body) {
  flex: 1;
  padding: 20px 24px;
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: 20px;
}

/* Diff Container */
.ai-diff-container {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.ai-diff-box {
  border: 1px solid var(--color-border);
  border-radius: var(--radius-md, 6px);
  background: var(--color-surface);
  padding: 2px;
}

.ai-diff-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: var(--color-surface-muted);
  border-bottom: 1px solid var(--color-border);
  border-radius: var(--radius-xs, 4px) var(--radius-xs, 4px) 0 0;
}

.ai-diff-tag {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--color-text-muted);
}

.ai-tag-ai {
  color: var(--color-primary);
}

.ai-edit-toggle-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background: transparent;
  border: none;
  font-size: var(--font-size-2xs);
  font-weight: 500;
  color: var(--color-primary);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.ai-edit-toggle-btn:hover:not(:disabled) {
  background: var(--color-primary-light);
}

.ai-diff-content {
  padding: 12px;
}

.ai-diff-pre {
  margin: 0;
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-xs);
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--color-text);
}

.ai-pre-highlight {
  background: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
  padding: 6px;
  border-radius: 4px;
}

.ai-diff-suggested {
  border-color: var(--semantic-color-success-border);
}

.ai-edit-textarea {
  width: 100%;
  padding: 8px;
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-md);
  border: 1px solid var(--color-primary);
  border-radius: 4px;
  box-sizing: border-box;
  resize: vertical;
}

/* Compliance Reason Section */
.ai-compliance-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.ai-reason-label {
  font-size: var(--font-size-xs);
  font-weight: 600;
  color: var(--color-text);
}

.ai-required-star {
  color: var(--color-error);
}

.ai-reason-input {
  width: 100%;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 8px 12px;
  font-size: var(--font-size-md);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: var(--radius-md, 6px);
  box-sizing: border-box;
  transition: border-color 0.15s;
}

.ai-reason-input:focus-visible {
  border-color: var(--semantic-color-accent);
  outline: 3px solid var(--semantic-color-accent);
  outline-offset: 2px;
  box-shadow: 0 0 0 2px var(--semantic-color-text-on-solid);
}

.ai-reason-input.has-error {
  border-color: var(--color-error);
}

.ai-validation-error {
  margin: 0;
  font-size: var(--font-size-2xs);
  color: var(--color-error);
}

/* Footer */
.ai-drawer-footer-inner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
}

.ai-footer-right {
  display: flex;
  gap: 10px;
}

.btn {
  padding: 8px 16px;
  font-size: var(--font-size-xs);
  font-weight: 500;
  border-radius: var(--radius-md, 6px);
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.15s ease;
}

.btn-secondary {
  background: var(--color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--color-text);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn-dismiss {
  background: transparent;
  border-color: var(--color-error);
  color: var(--color-error);
}

.btn-dismiss:hover:not(:disabled) {
  background: var(--color-error-bg);
}

.btn-dismiss:disabled,
.btn-secondary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-accept {
  background: var(--color-primary);
  color: var(--semantic-color-surface);
}

.btn-accept:hover:not(:disabled) {
  background: var(--color-primary-dark);
}

.btn-accept:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.ai-spinner {
  display: inline-block;
  animation: ai-spin 1.2s infinite linear;
}

@keyframes ai-spin {
  0% {
    transform: rotate(0deg);
  }
  100% {
    transform: rotate(360deg);
  }
}

/* Mobile Breakpoint Overrides (max-width: 640px) */
@media (max-width: 640px) {
  .ai-drawer-panel {
    width: 100vw;
    max-width: 100vw;
  }

  .ai-drawer-header,
  .ai-drawer-body,
  .ai-drawer-footer {
    padding-left: 16px;
    padding-right: 16px;
  }

  .ai-drawer-footer {
    flex-direction: column;
    gap: 12px;
  }

  .ai-footer-right {
    width: 100%;
    flex-direction: column;
  }

  .ai-footer-right .btn,
  .btn-dismiss {
    width: 100%;
    min-height: var(--semantic-touch-target-min, 48px);
    display: inline-flex;
    align-items: center;
    justify-content: center;
  }
}
</style>
