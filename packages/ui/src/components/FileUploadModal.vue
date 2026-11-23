<script setup>
import { computed, ref, watch } from "vue";
import ClinicalDropzone from "./clinical/ClinicalDropzone.vue";
import ClinicalModal from "./clinical/ClinicalModal.vue";
import ModalErrorAlert from "./ModalErrorAlert.vue";

const props = defineProps({
  modelValue: {
    type: Boolean,
    default: false,
  },
  studyId: {
    type: String,
    required: true,
  },
  siteId: {
    type: String,
    default: null,
  },
  title: {
    type: String,
    default: "Upload Clinical Document",
  },
  maxSizeBytes: {
    type: Number,
    default: 524288000, // 500 MB
  },
  multipartThresholdBytes: {
    type: Number,
    default: 5242880, // 5 MB
  },
  apiEndpoint: {
    type: String,
    default: "/api/v1/files/upload/presigned-url",
  },
});

const emit = defineEmits(["update:modelValue", "close", "uploaded", "error"]);

const selectedFile = ref(null);
const reasonForChange = ref("");
const status = ref("idle"); // idle, hashing, requesting, uploading, verifying, success, error
const progress = ref(0);
const calculatedSha256 = ref(null);
const errorMessage = ref("");

const isUploadLocked = computed(
  () =>
    status.value === "requesting" ||
    status.value === "uploading" ||
    status.value === "verifying"
);

const isSubmitDisabled = computed(() => {
  return (
    !selectedFile.value ||
    !reasonForChange.value ||
    reasonForChange.value.trim().length < 5 ||
    status.value === "hashing" ||
    status.value === "requesting" ||
    status.value === "uploading" ||
    status.value === "verifying"
  );
});

const formattedFileSize = computed(() => {
  if (!selectedFile.value) return "0 B";
  const bytes = selectedFile.value.size;
  if (bytes < 1024) return `${bytes} B`;
  if (bytes < 1048576) return `${(bytes / 1024).toFixed(1)} KB`;
  return `${(bytes / 1048576).toFixed(2)} MB`;
});

const closeModal = () => {
  if (isUploadLocked.value) return;
  emit("update:modelValue", false);
  emit("close");
  resetForm();
};

const resetForm = () => {
  selectedFile.value = null;
  reasonForChange.value = "";
  status.value = "idle";
  progress.value = 0;
  calculatedSha256.value = null;
  errorMessage.value = "";
};

const clearSelectedFile = () => {
  selectedFile.value = null;
  calculatedSha256.value = null;
  status.value = "idle";
  errorMessage.value = "";
};

const handleSelectedFile = async (file) => {
  if (file.size > props.maxSizeBytes) {
    errorMessage.value = `File exceeds maximum allowed size of ${Math.round(props.maxSizeBytes / 1048576)} MB.`;
    return;
  }
  selectedFile.value = file;
  errorMessage.value = "";
  calculatedSha256.value = null;

  // Compute SHA-256 Checksum via Web Crypto API
  try {
    status.value = "hashing";
    const arrayBuffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest("SHA-256", arrayBuffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    calculatedSha256.value = hashArray
      .map((b) => b.toString(16).padStart(2, "0"))
      .join("");
    status.value = "idle";
  } catch {
    status.value = "idle";
    calculatedSha256.value = null;
  }
};

const startUpload = async () => {
  if (isSubmitDisabled.value) return;

  try {
    status.value = "requesting";
    progress.value = 10;

    const isMultipart = selectedFile.value.size > props.multipartThresholdBytes;
    const partsCount = isMultipart
      ? Math.ceil(selectedFile.value.size / props.multipartThresholdBytes)
      : 1;

    // 1. Request presigned upload session
    const payload = {
      study_id: props.studyId,
      site_id: props.siteId,
      filename: selectedFile.value.name,
      mime_type: selectedFile.value.type || "application/octet-stream",
      size_bytes: selectedFile.value.size,
      checksum_sha256: calculatedSha256.value,
      reason_for_change: reasonForChange.value.trim(),
      is_multipart: isMultipart,
      parts_count: partsCount,
    };

    const res = await fetch(props.apiEndpoint, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
      },
      body: JSON.stringify(payload),
    });

    if (!res.ok) {
      throw new Error(`Upload allocation failed: HTTP ${res.status}`);
    }

    const session = await res.json();
    progress.value = 40;
    status.value = "uploading";

    // 2. Perform direct binary upload to presigned PUT URL
    if (!isMultipart && session.upload_url) {
      const uploadHeaders = {};
      if (session.required_headers) {
        Object.assign(uploadHeaders, session.required_headers);
      }
      if (selectedFile.value.type) {
        uploadHeaders["Content-Type"] = selectedFile.value.type;
      }

      const putRes = await fetch(session.upload_url, {
        method: "PUT",
        headers: uploadHeaders,
        body: selectedFile.value,
      });

      if (!putRes.ok) {
        throw new Error(`Direct S3 upload failed: HTTP ${putRes.status}`);
      }
    }

    progress.value = 100;
    status.value = "success";

    emit("uploaded", {
      fileId: session.file_id,
      objectKey: session.object_key,
      filename: selectedFile.value.name,
      checksumSha256: calculatedSha256.value,
    });

    setTimeout(() => {
      closeModal();
    }, 800);
  } catch (err) {
    status.value = "error";
    errorMessage.value = err.message || "Upload failed.";
    emit("error", err);
  }
};

watch(
  () => props.modelValue,
  (isOpen) => {
    if (isOpen) {
      resetForm();
    }
  }
);

defineExpose({
  errorMessage,
  selectedFile,
  reasonForChange,
  status,
  progress,
  startUpload,
  resetForm,
  handleSelectedFile,
});
</script>

<template>
  <ClinicalModal
    :model-value="modelValue"
    :title="title"
    :dismissible="!isUploadLocked"
    dialog-class="modal-dialog file-upload-modal"
    backdrop-class="modal-backdrop"
    close-label="Close dialog"
    @close="closeModal"
  >
    <div class="upload-modal-content">
      <!-- Error alert -->
      <ModalErrorAlert
        :error="errorMessage"
        retry-label="Retry Upload"
        dismiss-label="Clear Error"
        @retry="startUpload"
        @dismiss="
          errorMessage = '';
          status = 'idle';
          selectedFile = null;
        "
      />

      <!-- Dropzone -->
      <ClinicalDropzone
        class="dropzone"
        :has-selection="!!selectedFile"
        :disabled="isUploadLocked"
        :icon="selectedFile ? '📄' : '📁'"
        :prompt-text="
          selectedFile ? '' : 'Select or drag and drop a clinical document file'
        "
        :hint-text="
          selectedFile
            ? ''
            : `Max size: ${Math.round(maxSizeBytes / 1048576)} MB`
        "
        :button-label="selectedFile ? 'Replace File' : 'Browse File'"
        input-aria-label="Upload file"
        removable
        remove-label="Remove File"
        :remove-context="selectedFile ? selectedFile.name : ''"
        @file-selected="handleSelectedFile"
        @remove="clearSelectedFile"
      >
        <template #selected>
          <div v-if="selectedFile" class="dropzone-file-info">
            <div class="file-details">
              <p class="file-name">
                {{ selectedFile.name }}
              </p>
              <p class="file-meta">
                {{ formattedFileSize }} • {{ selectedFile.type || "binary" }}
              </p>
              <p v-if="calculatedSha256" class="file-checksum">
                SHA-256:
                <code>{{ calculatedSha256.substring(0, 16) }}...</code>
              </p>
              <p v-else-if="status === 'hashing'" class="file-hashing">
                ⏳ Calculating cryptographic SHA-256...
              </p>
            </div>
          </div>
        </template>
      </ClinicalDropzone>

      <!-- Reason for change (21 CFR Part 11 Mandate) -->
      <div class="form-group">
        <label for="upload-reason" class="form-label">
          Reason for Change (21 CFR Part 11 Mandate)
          <span class="required-star">*</span>
        </label>
        <textarea
          id="upload-reason"
          v-model="reasonForChange"
          class="form-textarea"
          placeholder="State the regulatory justification for this document upload..."
          rows="3"
          required
        ></textarea>
        <span class="hint-text">Minimum 5 characters.</span>
      </div>

      <!-- Progress Indicator -->
      <div
        v-if="status === 'uploading' || status === 'verifying'"
        class="progress-bar-wrapper"
      >
        <div class="progress-track">
          <div class="progress-fill" :style="{ width: `${progress}%` }"></div>
        </div>
        <span class="progress-text">{{
          status === "uploading" ? "Uploading..." : "Verifying SHA-256..."
        }}</span>
      </div>
    </div>

    <template #footer>
      <button
        type="button"
        class="btn btn-secondary"
        :disabled="isUploadLocked"
        @click="closeModal"
      >
        Cancel
      </button>
      <button
        type="button"
        class="btn btn-primary"
        :disabled="isSubmitDisabled"
        data-testid="upload-submit-btn"
        @click="startUpload"
      >
        {{ status === "uploading" ? "Uploading..." : "Upload Document" }}
      </button>
    </template>
  </ClinicalModal>
</template>

<style scoped>
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(2px);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 9999;
  padding: 16px;
}

.modal-dialog {
  background: var(--semantic-color-surface);
  border-radius: var(--radius-md);
  box-shadow: var(--shadow-xl);
  border: 1px solid var(--semantic-color-border);
  display: flex;
  flex-direction: column;
  max-width: 580px;
  width: 100%;
  max-height: 90vh;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--semantic-color-border);
}

.modal-title {
  margin: 0;
  font-size: 16px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.close-btn {
  background: transparent;
  border: none;
  font-size: 18px;
  color: var(--semantic-color-text-muted);
  cursor: pointer;
}

.upload-modal-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.error-alert {
  padding: 10px 14px;
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
  border-radius: var(--radius-sm);
  font-size: 13px;
}

.dropzone {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: var(--radius-md);
  padding: 24px;
  text-align: center;
  background: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.15s ease;
}

.dropzone:hover,
.dropzone.is-dragging {
  border-color: var(--semantic-color-primary);
  background: var(--semantic-color-info-bg-soft);
}

.hidden-file-input {
  display: none;
}

.dropzone-empty {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
}

.upload-icon {
  font-size: 32px;
}

.dropzone-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.dropzone-hint {
  margin: 0;
  font-size: 12px;
  color: var(--semantic-color-text-muted);
}

.dropzone-file-info {
  display: flex;
  align-items: center;
  gap: 16px;
  text-align: left;
}

.file-icon {
  font-size: 36px;
}

.file-details {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.file-name {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.file-meta,
.file-checksum,
.file-hashing {
  margin: 0;
  font-size: 12px;
  color: var(--semantic-color-text-muted);
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 13px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.required-star {
  color: var(--semantic-color-error);
}

.form-textarea {
  width: 100%;
  padding: 10px 12px;
  border-radius: var(--radius-sm);
  border: 1px solid var(--semantic-color-border-control);
  font-size: 13px;
  font-family: inherit;
  resize: vertical;
  box-sizing: border-box;
}

.form-textarea:focus-visible {
  border-color: var(--semantic-color-accent);
}

.hint-text {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-muted);
}

.progress-bar-wrapper {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.progress-track {
  width: 100%;
  height: 6px;
  background: var(--semantic-color-border);
  border-radius: 3px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  background: var(--semantic-color-primary);
  transition: width 0.2s ease;
}

.progress-text {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-muted);
  text-align: right;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 14px 20px;
  border-top: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  border-bottom-left-radius: var(--radius-md);
  border-bottom-right-radius: var(--radius-md);
}

.btn {
  padding: 8px 16px;
  font-size: 13px;
  font-weight: 500;
  border-radius: var(--radius-sm);
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn-primary {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-primary-dark);
}
</style>
