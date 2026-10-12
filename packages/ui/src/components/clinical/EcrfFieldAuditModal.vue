<template>
  <ClinicalModal
    :id="`audit-modal-${fieldId}`"
    :model-value="isOpen"
    :title="modalTitle"
    size="lg"
    @close="handleClose"
  >
    <div class="field-audit-modal-content">
      <!-- Compliance & Regulatory Banner -->
      <div class="audit-compliance-banner">
        <div class="banner-left">
          <span class="banner-icon">📜</span>
          <span class="banner-title">21 CFR Part 11 eCRF Field Audit History</span>
        </div>
        <span class="compliance-badge" title="GxP Audit Trail Compliant">
          🔒 Part 11 Sealed
        </span>
      </div>

      <!-- Loading State -->
      <div v-if="loading" class="audit-loading-state" role="status">
        <div class="spinner"></div>
        <span>Loading field audit history...</span>
      </div>

      <!-- Error State -->
      <div v-else-if="error" class="audit-error-state" role="alert">
        <span class="error-icon">⚠️</span>
        <span class="error-message">{{ error }}</span>
      </div>

      <!-- Empty State -->
      <div
        v-else-if="!auditHistory || auditHistory.length === 0"
        class="audit-empty-state"
      >
        <div class="empty-icon">📂</div>
        <p class="empty-title">No Audit Records Found</p>
        <p class="empty-desc">
          There are no recorded historical changes for field
          <strong>{{ fieldLabel || fieldId }}</strong>.
        </p>
      </div>

      <!-- Audit Revisions Timeline -->
      <div v-else class="audit-timeline">
        <div
          v-for="(entry, index) in normalizedAuditHistory"
          :key="entry.versionIndex ?? index"
          class="audit-card"
        >
          <!-- Card Header -->
          <div class="audit-card-header">
            <div class="header-left">
              <span class="version-badge">
                Rev #{{ entry.versionIndex }}
              </span>
              <span v-if="entry.action" class="action-tag">
                {{ entry.action }}
              </span>
              <span class="user-id">
                👤 <strong>{{ entry.userId || 'System' }}</strong>
              </span>
            </div>
            <div class="header-right">
              <time class="timestamp" :datetime="entry.timestamp">
                🕒 {{ formatTimestamp(entry.timestamp) }}
              </time>
            </div>
          </div>

          <!-- Reason for Change -->
          <div v-if="entry.changeReason" class="change-reason-box">
            <span class="reason-label">Reason for Change:</span>
            <span class="reason-text">{{ entry.changeReason }}</span>
          </div>

          <!-- Value Visual Diff -->
          <div class="value-diff-grid">
            <div class="diff-col diff-old">
              <div class="diff-header">Old Value</div>
              <div class="diff-body old-value">
                <span v-if="entry.oldValue != null && entry.oldValue !== ''">
                  {{ entry.oldValue }}
                </span>
                <span v-else class="null-value">(None / Empty)</span>
              </div>
            </div>

            <div class="diff-divider">➔</div>

            <div class="diff-col diff-new">
              <div class="diff-header">New Value</div>
              <div class="diff-body new-value">
                <span v-if="entry.newValue != null && entry.newValue !== ''">
                  {{ entry.newValue }}
                </span>
                <span v-else class="null-value">(None / Empty)</span>
              </div>
            </div>
          </div>

          <!-- Cryptographic Integrity (Merkle Hashes) -->
          <div class="merkle-hashes-box">
            <div class="hash-row">
              <span class="hash-label">Block Hash:</span>
              <code class="hash-code">{{ entry.blockHash || 'N/A' }}</code>
            </div>
            <div v-if="entry.predecessorHash" class="hash-row">
              <span class="hash-label">Predecessor Hash:</span>
              <code class="hash-code">{{ entry.predecessorHash }}</code>
            </div>
          </div>
        </div>
      </div>
    </div>

    <template #footer>
      <button class="btn btn-secondary" @click="handleClose">
        Close
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { computed } from "vue";
import ClinicalModal from "./ClinicalModal.vue";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  fieldId: {
    type: String,
    default: "",
  },
  fieldLabel: {
    type: String,
    default: "",
  },
  formId: {
    type: String,
    default: "",
  },
  auditHistory: {
    type: Array,
    default: () => [],
  },
  loading: {
    type: Boolean,
    default: false,
  },
  error: {
    type: String,
    default: null,
  },
});

const emit = defineEmits(["close"]);

const modalTitle = computed(() => {
  const label = props.fieldLabel || props.fieldId || "Field";
  return `Field Audit History: ${label}`;
});

/** Normalize snake_case or camelCase properties from API payload */
const normalizedAuditHistory = computed(() => {
  return props.auditHistory.map((entry) => ({
    versionIndex: entry.versionIndex ?? entry.version_index ?? 0,
    timestamp: entry.timestamp ?? "",
    userId: entry.userId ?? entry.user_id ?? "System",
    changeReason: entry.changeReason ?? entry.change_reason ?? null,
    oldValue: entry.oldValue ?? entry.old_value ?? null,
    newValue: entry.newValue ?? entry.new_value ?? null,
    blockHash: entry.blockHash ?? entry.block_hash ?? "",
    predecessorHash: entry.predecessorHash ?? entry.predecessor_hash ?? null,
    action: entry.action ?? null,
  }));
});

function handleClose() {
  emit("close");
}

function formatTimestamp(ts) {
  if (!ts) return "";
  try {
    const d = new Date(ts);
    if (isNaN(d.getTime())) return ts;
    return d.toISOString().replace("T", " ").replace(/\.\d+Z$/, " UTC");
  } catch {
    return ts;
  }
}
</script>

<style scoped>
.field-audit-modal-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.audit-compliance-banner {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--color-surface-muted, #f8fafc);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 6px;
  padding: 10px 14px;
}

.banner-left {
  display: flex;
  align-items: center;
  gap: 8px;
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.compliance-badge {
  background-color: #dcfce7;
  color: #166534;
  border: 1px solid #bbf7d0;
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 12px;
}

.audit-loading-state {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 12px;
  padding: 32px 0;
  color: var(--color-text-muted, #64748b);
}

.spinner {
  width: 20px;
  height: 20px;
  border: 2px solid #e2e8f0;
  border-top-color: var(--color-primary, #026597);
  border-radius: 50%;
  animation: spin 0.8s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.audit-error-state {
  display: flex;
  align-items: center;
  gap: 8px;
  background-color: #fef2f2;
  border: 1px solid #f87171;
  color: #991b1b;
  padding: 12px 16px;
  border-radius: 6px;
}

.audit-empty-state {
  text-align: center;
  padding: 32px 16px;
  color: var(--color-text-muted, #64748b);
}

.empty-icon {
  font-size: 2rem;
  margin-bottom: 8px;
}

.empty-title {
  font-weight: 600;
  font-size: 1rem;
  margin: 0 0 4px 0;
  color: var(--color-text, #0f172a);
}

.empty-desc {
  font-size: 0.875rem;
  margin: 0;
}

.audit-timeline {
  display: flex;
  flex-direction: column;
  gap: 14px;
  max-height: 55vh;
  overflow-y: auto;
  padding-right: 4px;
}

.audit-card {
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 6px;
  padding: 14px;
  background-color: var(--color-surface, #ffffff);
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.audit-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 8px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 8px;
}

.version-badge {
  background-color: #e0f2fe;
  color: #0369a1;
  font-weight: 700;
  font-size: 0.75rem;
  padding: 2px 8px;
  border-radius: 4px;
}

.action-tag {
  background-color: #f3e8ff;
  color: #6b21a8;
  font-weight: 600;
  font-size: 0.7rem;
  padding: 2px 6px;
  border-radius: 4px;
  text-transform: uppercase;
}

.user-id {
  font-size: 0.85rem;
  color: var(--color-text, #0f172a);
}

.timestamp {
  font-size: 0.8rem;
  color: var(--color-text-muted, #64748b);
}

.change-reason-box {
  background-color: #fffbeb;
  border-left: 3px solid #f59e0b;
  padding: 6px 10px;
  border-radius: 4px;
  font-size: 0.85rem;
  display: flex;
  gap: 6px;
}

.reason-label {
  font-weight: 600;
  color: #92400e;
}

.reason-text {
  color: #78350f;
}

.value-diff-grid {
  display: flex;
  align-items: center;
  gap: 12px;
  background-color: #f8fafc;
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid #f1f5f9;
}

.diff-col {
  flex: 1;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.diff-header {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  color: var(--color-text-muted, #64748b);
  letter-spacing: 0.5px;
}

.diff-body {
  font-size: 0.9rem;
  padding: 6px 8px;
  border-radius: 4px;
  word-break: break-word;
}

.diff-old .old-value {
  background-color: #fef2f2;
  color: #991b1b;
  text-decoration: line-through;
  border: 1px solid #fecaca;
}

.diff-new .new-value {
  background-color: #f0fdf4;
  color: #166534;
  font-weight: 600;
  border: 1px solid #bbf7d0;
}

.null-value {
  font-style: italic;
  color: #94a3b8;
  text-decoration: none !important;
}

.diff-divider {
  color: #94a3b8;
  font-weight: bold;
}

.merkle-hashes-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
  background-color: #0f172a;
  color: #f8fafc;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.75rem;
}

.hash-row {
  display: flex;
  align-items: center;
  gap: 8px;
}

.hash-label {
  color: #94a3b8;
  font-weight: 600;
  min-width: 100px;
}

.hash-code {
  font-family: monospace;
  color: #38bdf8;
  word-break: break-all;
}
</style>
