<template>
  <div class="grid-header">
    <div class="header-titles">
      <h2 id="document-grid-heading" tabindex="-1">
        Artifact Documents Registry
      </h2>
      <p v-if="selectedArtifactCode" class="active-artifact-subtitle">
        Showing documents for Artifact:
        <strong>{{ selectedArtifactCode }}</strong>
      </p>
      <p v-else class="active-artifact-subtitle">
        Please select an artifact from the binder tree to view documents.
      </p>
    </div>
    <div class="grid-header-actions">
      <button
        v-if="selectedCount > 0"
        type="button"
        class="btn btn-outline-primary export-selected-btn"
        :disabled="!canExport"
        :title="!canExport ? ETMF_EXPORT_ROLE_REASON : undefined"
        @click="emit('exportSelected')"
      >
        <span aria-hidden="true"
          ><ClinicalIcon name="folder" :size="16"
        /></span>
        Download Selected ({{ selectedCount }})<span class="sr-only">
          documents as ZIP package</span
        >
      </button>
      <button
        type="button"
        class="btn btn-secondary batch-upload-trigger-btn"
        :disabled="!canStage"
        :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
        @click="emit('openBatch')"
      >
        <span aria-hidden="true"
          ><ClinicalIcon name="upload" :size="16"
        /></span>
        Batch Ingestion<span class="sr-only"> multi-file upload</span>
      </button>
      <button
        type="button"
        class="btn btn-primary upload-trigger-btn"
        :disabled="!canStage || !selectedArtifactCode"
        :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
        @click="emit('openUpload')"
      >
        <span aria-hidden="true"
          ><ClinicalIcon name="file-text" :size="16"
        /></span>
        Upload Single Document<span v-if="selectedArtifactCode" class="sr-only">
          for artifact {{ selectedArtifactCode }}</span
        >
      </button>
    </div>
  </div>

  <div
    v-if="!canStage || !canTag"
    id="document-grid-role-note"
    class="grid-role-note"
    role="note"
  >
    <span v-if="!canStage">{{ ETMF_STAGE_ROLE_REASON }}</span>
    <span v-if="!canStage && !canTag"> • </span>
    <span v-if="!canTag">{{ ETMF_TAG_ROLE_REASON }}</span>
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import {
  ETMF_EXPORT_ROLE_REASON,
  ETMF_STAGE_ROLE_REASON,
  ETMF_TAG_ROLE_REASON,
} from "../../../canvas/authority.ts";

defineProps({
  selectedArtifactCode: {
    type: String,
    default: "",
  },
  selectedCount: {
    type: Number,
    default: 0,
  },
  canStage: {
    type: Boolean,
    default: true,
  },
  canTag: {
    type: Boolean,
    default: true,
  },
  canExport: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["exportSelected", "openBatch", "openUpload"]);
</script>

<style scoped>
.grid-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-start;
  gap: 12px;
  min-width: 0;
  margin-bottom: 24px;
  border-bottom: 1px solid var(--semantic-color-surface-header); /* deid-ignore */
  padding-bottom: 16px;
}

.header-titles {
  min-width: 0;
}

.header-titles h2 {
  font-size: 1.4rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0 0 6px 0;
}

.active-artifact-subtitle {
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

.grid-header-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  min-width: 0;
  max-width: 100%;
}

.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  padding: 8px 16px;
  font-size: 0.9rem;
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
}

.upload-trigger-btn {
  padding: 10px 16px;
  font-weight: 600;
}

.btn-primary {
  background-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-interactive-primary);
}

.btn-primary:disabled {
  background-color: var(--semantic-color-divider-accent);
  border-color: var(--semantic-color-divider-accent);
  cursor: not-allowed;
}

.btn-outline-primary {
  background-color: transparent;
  color: var(--semantic-color-interactive-primary);
  border: 1px solid var(--semantic-color-interactive-primary);
}

.btn-outline-primary:hover:not(:disabled) {
  background-color: var(--semantic-color-info-bg);
}

.grid-role-note {
  margin-bottom: 12px;
  padding: 8px 12px;
  border-radius: 6px;
  font-size: 0.8rem;
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
}
</style>
