<template>
  <form class="upload-meta-form" @submit.prevent="emit('submit')">
    <div class="form-group-row">
      <div class="form-group">
        <label for="etmf-upload-study-id">Target Study ID</label>
        <input
          id="etmf-upload-study-id"
          :value="studyId"
          type="text"
          class="form-control"
          readonly
        />
      </div>
      <div class="form-group">
        <label for="etmf-upload-site-id">Site ID (Optional)</label>
        <input
          id="etmf-upload-site-id"
          :value="siteId"
          type="text"
          placeholder="e.g. SITE-01"
          class="form-control"
          @input="emit('update:siteId', $event.target.value)"
        />
      </div>
    </div>

    <div class="form-group-row">
      <div class="form-group">
        <label for="etmf-upload-artifact-code">DIA TMF Artifact Code</label>
        <input
          id="etmf-upload-artifact-code"
          :value="artifactCode"
          type="text"
          class="form-control"
          readonly
        />
      </div>
      <div class="form-group">
        <label for="etmf-upload-artifact-type">Artifact Type (Label)</label>
        <input
          id="etmf-upload-artifact-type"
          :value="artifactType"
          type="text"
          class="form-control"
          readonly
        />
      </div>
    </div>

    <div class="form-group">
      <label for="etmf-upload-reason" class="required-label">
        Reason for Change / Upload Justification
      </label>
      <textarea
        id="etmf-upload-reason"
        :value="reasonForChange"
        rows="3"
        placeholder="Specify the regulatory or scientific justification for ingesting this record version (e.g. 'Initial Approved protocol for site initiation', 'Site license renewal')."
        class="form-control"
        required
        @input="emit('update:reasonForChange', $event.target.value)"
      />
    </div>

    <div v-if="uploadError" class="upload-error-banner">
      {{ uploadError }}
    </div>

    <div class="modal-footer-actions">
      <button
        type="button"
        class="btn btn-outline-secondary"
        :disabled="isSubmitting"
        @click="emit('cancel')"
      >
        Cancel
      </button>
      <button
        type="submit"
        class="btn btn-primary"
        :disabled="isSubmitting || !hasFile || !reasonForChange.trim()"
      >
        <span v-if="isSubmitting">Uploading &amp; Indexing...</span>
        <span v-else> Commit Electronic Record</span>
      </button>
    </div>
  </form>
</template>

<script setup>
defineProps({
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: "",
  },
  artifactCode: {
    type: String,
    default: "",
  },
  artifactType: {
    type: String,
    default: "",
  },
  reasonForChange: {
    type: String,
    default: "",
  },
  uploadError: {
    type: String,
    default: "",
  },
  isSubmitting: {
    type: Boolean,
    default: false,
  },
  hasFile: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits([
  "update:siteId",
  "update:reasonForChange",
  "cancel",
  "submit",
]);
</script>

<style scoped>
.upload-meta-form {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.form-group-row {
  display: flex;
  gap: 16px;
}

.form-group-row .form-group {
  flex: 1;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-group label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.required-label::after {
  content: " *";
  color: var(--semantic-color-error);
}

.upload-meta-form .form-control {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.9rem;
  outline: 2px solid transparent;
  padding: 8px 12px;
  transition: border-color 0.2s;
}

.upload-meta-form .form-control:focus {
  border-color: var(--semantic-color-interactive-primary);
}

.upload-meta-form .form-control[readonly] {
  background-color: var(--semantic-color-surface-header); /* deid-ignore */
  color: var(--semantic-color-text-secondary);
  cursor: not-allowed;
}

.upload-error-banner {
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
  padding: 10px 12px;
  border-radius: 6px;
  font-size: 0.85rem;
}

.modal-footer-actions {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  margin-top: 24px;
  border-top: 1px solid var(--semantic-color-border); /* deid-ignore */
  padding-top: 16px;
}

.modal-footer-actions .btn {
  align-items: center;
  border-radius: 6px;
  cursor: pointer;
  display: inline-flex;
  font-size: 0.9rem;
  font-weight: 500;
  justify-content: center;
  padding: 8px 16px;
}

.modal-footer-actions .btn-outline-secondary {
  background-color: transparent;
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
}

.modal-footer-actions .btn-outline-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-muted);
}

.modal-footer-actions .btn-primary {
  background-color: var(--semantic-color-interactive-primary);
  border: 1px solid var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}

.modal-footer-actions .btn-primary:disabled {
  background-color: var(--semantic-color-divider-accent);
  border-color: var(--semantic-color-divider-accent);
  cursor: not-allowed;
}
</style>
