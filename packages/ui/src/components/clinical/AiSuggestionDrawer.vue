<script setup>
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from "vue";
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

const drawerRef = ref(null);
const previousActiveElement = ref(null);
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

const handleKeyDown = (e) => {
  if (e.key === "Escape" && drawerOpen.value) {
    closeDrawer();
  }
};

watch(
  () => drawerOpen.value,
  async (open) => {
    if (open) {
      previousActiveElement.value = document.activeElement;
      await nextTick();
      if (drawerRef.value) {
        drawerRef.value.focus();
      }
    } else if (previousActiveElement.value) {
      previousActiveElement.value.focus();
    }
  }
);

onMounted(() => {
  window.addEventListener("keydown", handleKeyDown);
});

onUnmounted(() => {
  window.removeEventListener("keydown", handleKeyDown);
});

const getParsedFinalValue = () => {
  if (!isEditing.value) {
    return props.suggestedValue;
  }
  const text = editedValueText.value.trim();
  if (typeof props.suggestedValue === "object" && props.suggestedValue !== null) {
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
    validationError.value = "21 CFR Part 11 compliance requires a reason for change before acceptance.";
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
  <Teleport to="body">
    <div
      v-if="drawerOpen"
      class="ai-drawer-backdrop"
      role="presentation"
      @click.self="closeDrawer"
    >
      <aside
        ref="drawerRef"
        class="ai-drawer-panel"
        role="dialog"
        aria-modal="true"
        :aria-label="title"
        tabindex="-1"
      >
        <!-- Header -->
        <header class="ai-drawer-header">
          <div class="ai-drawer-title-group">
            <div class="ai-header-badge-row">
              <span class="ai-drawer-sparkle" aria-hidden="true">✨</span>
              <h2 class="ai-drawer-title">{{ title }}</h2>
              <ConfidenceBadge
                v-if="confidenceScore !== null && confidenceScore !== undefined"
                :score="confidenceScore"
                size="sm"
              />
            </div>
            <p v-if="entityName || fieldLabel" class="ai-drawer-subtitle">
              Target: <strong>{{ entityName ? `${entityName} › ` : '' }}{{ fieldLabel || 'Clinical Field' }}</strong>
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

        <!-- Main Diff Content -->
        <main class="ai-drawer-body">
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
            <div class="ai-diff-box ai-diff-suggested" :class="{ 'is-editing': isEditing }">
              <div class="ai-diff-header">
                <span class="ai-diff-tag ai-tag-ai">AI Proposed</span>
                <button
                  type="button"
                  class="ai-edit-toggle-btn"
                  @click="toggleEdit"
                >
                  {{ isEditing ? '👁 View Structured' : '✏️ Edit Suggestion' }}
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
                <pre v-else class="ai-diff-pre ai-pre-highlight">{{ formatDisplayValue(suggestedValue) }}</pre>
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
        </main>

        <!-- Footer Actions -->
        <footer class="ai-drawer-footer">
          <button
            type="button"
            class="btn btn-dismiss"
            :disabled="isApproving || isRejecting"
            @click="handleDismiss"
          >
            Dismiss / Reject
          </button>
          <div class="ai-footer-right">
            <button
              type="button"
              class="btn btn-secondary"
              @click="closeDrawer"
            >
              Cancel
            </button>
            <button
              type="button"
              class="btn btn-accept"
              :disabled="isApproving || isRejecting"
              @click="handleAccept"
            >
              <span v-if="isApproving" class="ai-spinner" aria-hidden="true">⏳</span>
              <span v-else>Accept & Apply</span>
            </button>
          </div>
        </footer>
      </aside>
    </div>
  </Teleport>
</template>

<style scoped>
.ai-drawer-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 9999;
  display: flex;
  justify-content: flex-end;
}

.ai-drawer-panel {
  width: min(640px, 94vw);
  height: 100vh;
  background: var(--color-surface, #ffffff);
  border-left: 1px solid var(--color-border, #e2e8f0);
  box-shadow: -8px 0 24px -4px rgba(0, 0, 0, 0.12);
  display: flex;
  flex-direction: column;
  outline: none;
  animation: ai-slide-in 0.25s cubic-bezier(0.16, 1, 0.3, 1);
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
  border-bottom: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface-muted, #f8fafc);
}

.ai-header-badge-row {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs, 8px);
  flex-wrap: wrap;
}

.ai-drawer-sparkle {
  font-size: 1.25rem;
}

.ai-drawer-title {
  margin: 0;
  font-size: var(--font-size-md, 16px);
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.ai-drawer-subtitle {
  margin: 4px 0 0 0;
  font-size: var(--font-size-xs, 12px);
  color: var(--color-text-muted, #475569);
}

.ai-drawer-close-btn {
  background: transparent;
  border: none;
  font-size: 16px;
  color: var(--color-text-muted, #475569);
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
  transition: background 0.15s;
}

.ai-drawer-close-btn:hover {
  background: #e2e8f0;
  color: var(--color-text, #0f172a);
}

/* Provenance Banner */
.ai-provenance-banner {
  padding: 10px 24px;
  background: var(--color-primary-light, #e0f2fe);
  border-bottom: 1px solid #bae6fd;
  font-size: var(--font-size-xs, 12px);
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
  color: var(--color-primary-dark, #014d76);
}

.ai-provenance-val {
  color: var(--color-text, #0f172a);
}

code.ai-provenance-val {
  background: rgba(255, 255, 255, 0.7);
  padding: 1px 4px;
  border-radius: 3px;
  font-family: var(--font-mono, monospace);
  font-size: 11px;
}

/* Body */
.ai-drawer-body {
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
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: var(--radius-md, 6px);
  overflow: hidden;
  background: var(--color-surface, #ffffff);
}

.ai-diff-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 8px 12px;
  background: var(--color-surface-muted, #f8fafc);
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.ai-diff-tag {
  font-size: var(--font-size-2xs, 11px);
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--color-text-muted, #475569);
}

.ai-tag-ai {
  color: var(--color-primary, #026597);
}

.ai-edit-toggle-btn {
  background: transparent;
  border: none;
  font-size: 11px;
  font-weight: 500;
  color: var(--color-primary, #026597);
  cursor: pointer;
  padding: 2px 6px;
  border-radius: 4px;
}

.ai-edit-toggle-btn:hover {
  background: var(--color-primary-light, #e0f2fe);
}

.ai-diff-content {
  padding: 12px;
}

.ai-diff-pre {
  margin: 0;
  font-family: var(--font-mono, monospace);
  font-size: var(--font-size-xs, 12px);
  white-space: pre-wrap;
  word-break: break-word;
  color: var(--color-text, #0f172a);
}

.ai-pre-highlight {
  background: #f0fdf4;
  color: #166534;
  padding: 6px;
  border-radius: 4px;
}

.ai-diff-suggested {
  border-color: #86efac;
}

.ai-edit-textarea {
  width: 100%;
  padding: 8px;
  font-family: var(--font-mono, monospace);
  font-size: 12px;
  border: 1px solid var(--color-primary, #026597);
  border-radius: 4px;
  box-sizing: border-box;
  resize: vertical;
  outline: none;
}

/* Compliance Reason Section */
.ai-compliance-section {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.ai-reason-label {
  font-size: var(--font-size-xs, 12px);
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.ai-required-star {
  color: var(--color-error, #b91c1c);
}

.ai-reason-input {
  width: 100%;
  padding: 8px 12px;
  font-size: var(--font-size-sm, 13px);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: var(--radius-md, 6px);
  outline: none;
  box-sizing: border-box;
  transition: border-color 0.15s;
}

.ai-reason-input:focus {
  border-color: var(--color-primary, #026597);
  box-shadow: 0 0 0 2px var(--color-primary-light, #e0f2fe);
}

.ai-reason-input.has-error {
  border-color: var(--color-error, #b91c1c);
}

.ai-validation-error {
  margin: 0;
  font-size: 12px;
  color: var(--color-error, #b91c1c);
}

/* Footer */
.ai-drawer-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 24px;
  border-top: 1px solid var(--color-border, #e2e8f0);
  background: var(--color-surface-muted, #f8fafc);
}

.ai-footer-right {
  display: flex;
  gap: 10px;
}

.btn {
  padding: 8px 16px;
  font-size: 13px;
  font-weight: 500;
  border-radius: var(--radius-md, 6px);
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.15s ease;
}

.btn-secondary {
  background: var(--color-surface, #ffffff);
  border-color: var(--color-border, #e2e8f0);
  color: var(--color-text, #0f172a);
}

.btn-secondary:hover {
  background: #f1f5f9;
}

.btn-dismiss {
  background: transparent;
  border-color: var(--color-error-bg, #fee2e2);
  color: var(--color-error, #b91c1c);
}

.btn-dismiss:hover {
  background: var(--color-error-bg, #fee2e2);
}

.btn-accept {
  background: var(--color-primary, #026597);
  color: #ffffff;
}

.btn-accept:hover:not(:disabled) {
  background: var(--color-primary-dark, #014d76);
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
  0% { transform: rotate(0deg); }
  100% { transform: rotate(360deg); }
}
</style>
