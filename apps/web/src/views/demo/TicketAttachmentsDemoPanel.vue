<template>
  <div
    id="ticket-drawer-panel-attachments"
    role="tabpanel"
    aria-labelledby="ticket-drawer-tab-attachments"
    class="tab-pane"
  >
    <div class="attachment-upload-box">
      <h4 class="card-title">Upload Audited Evidence Blob</h4>
      <p class="upload-hint">
        Upload logs, temperature traces, source certificates, or PDFs (21 CFR
        Part 11 &amp; DEID scanned).
      </p>
      <div class="upload-inputs">
        <input
          ref="fileInput"
          type="file"
          aria-label="Evidence attachment file"
          class="file-input"
          @change="onFileSelected"
        />
        <input
          v-model="attachmentReason"
          type="text"
          aria-label="Reason for attaching evidence"
          class="form-control"
          placeholder="Mandatory GxP reason for attachment..."
        />
        <button
          type="button"
          class="btn btn-primary"
          :disabled="
            !selectedFile || !attachmentReason.trim() || uploadSubmitting
          "
          @click="uploadAttachment"
        >
          📎 Attach Evidence
        </button>
      </div>
    </div>

    <div class="attachments-list">
      <div v-for="att in attachments" :key="att.id" class="attachment-card">
        <div class="attachment-icon">📄</div>
        <div class="attachment-details">
          <div class="attachment-filename">
            {{ att.filename }}
          </div>
          <div class="attachment-meta">
            <span>{{ (att.file_size_bytes / 1024).toFixed(1) }} KB</span>
            • <span>Uploaded by {{ att.uploaded_by }}</span> •
            <span>{{ formatDate(att.uploaded_at) }}</span>
          </div>
          <div class="sha-hash" :title="att.sha256_hash">
            SHA-256:
            <code>{{ att.sha256_hash?.substring(0, 16) }}...</code>
          </div>
        </div>
        <div class="attachment-badges">
          <span v-if="att.deid_scrubbed" class="badge badge-deid"
            >🛡️ DEID Verified</span
          >
        </div>
      </div>

      <div v-if="attachments.length === 0" class="empty-placeholder">
        <EmptyState
          icon="📎"
          title="No Evidence Attached"
          message="No evidence files have been attached to this ticket yet."
        />
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref } from "vue";
import { EmptyState } from "ui";

defineOptions({
  name: "TicketAttachmentsDemoPanel",
});

const props = defineProps({
  ticketId: {
    type: String,
    default: "",
  },
  attachments: {
    type: Array,
    default: () => [],
  },
});

const emit = defineEmits(["attachment"]);

const fileInput = ref(null);
const selectedFile = ref(null);
const attachmentReason = ref("Supporting regulatory evidence attachment");
const uploadSubmitting = ref(false);

const formatDate = (d) => {
  if (!d) return "N/A";
  try {
    return new Date(d).toLocaleString();
  } catch {
    return String(d);
  }
};

const onFileSelected = (e) => {
  selectedFile.value = e.target.files?.[0] || null;
};

const uploadAttachment = () => {
  if (!selectedFile.value || !attachmentReason.value.trim()) return;
  emit("attachment", {
    ticketId: props.ticketId,
    file: selectedFile.value,
    reason_for_change: attachmentReason.value,
  });
  selectedFile.value = null;
  if (fileInput.value) fileInput.value.value = "";
};
</script>

<style scoped>
.tab-pane {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.card-title {
  font-size: 0.9rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 10px 0;
}

.attachment-upload-box {
  background: var(--semantic-color-surface);
  border: 1px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 16px;
}

.upload-hint {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 12px;
}

.upload-inputs {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.attachments-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.attachment-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 12px 14px;
  display: flex;
  align-items: center;
  gap: 12px;
}

.attachment-icon {
  font-size: 1.5rem;
}

.attachment-details {
  flex: 1;
}

.attachment-filename {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.attachment-meta {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
  margin: 2px 0;
}

.sha-hash {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
}

.badge {
  font-size: 0.72rem;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-deid {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.empty-placeholder {
  padding: 24px;
  text-align: center;
  color: var(--semantic-color-text-secondary);
  font-size: 0.85rem;
}

.btn {
  padding: 6px 12px;
  border-radius: 6px;
  font-size: 0.82rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease-in-out;
  min-height: 44px;
  min-width: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
}

.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
}

.btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.form-control {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.82rem;
  color: var(--semantic-color-text-dark);
  background: var(--semantic-color-surface);
}

.form-control:focus-visible {
  border-color: var(--semantic-color-interactive-primary);
}
</style>
