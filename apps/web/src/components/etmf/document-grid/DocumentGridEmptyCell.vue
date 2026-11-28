<template>
  <div
    v-if="scopeForbiddenGuidance"
    id="grid-scope-forbidden-guidance"
    class="scope-forbidden-guidance"
    role="alert"
  >
    <span class="scope-forbidden-icon" aria-hidden="true"
      ><ClinicalIcon name="lock" :size="16"
    /></span>
    <h3>Study or Site Scope Assignment Required</h3>
    <p>{{ scopeForbiddenGuidance }}</p>
  </div>
  <div
    v-else-if="documentsError"
    id="grid-documents-error"
    class="scope-forbidden-guidance"
    role="alert"
  >
    <span class="scope-forbidden-icon" aria-hidden="true"
      ><ClinicalIcon name="alert-triangle" :size="16"
    /></span>
    <h3>Unable to Load Regulated Documents</h3>
    <p>{{ documentsError }}</p>
  </div>
  <EmptyState
    v-else
    icon="file-text"
    title="No Regulated Documents Found"
    message="No documents have been uploaded for this artifact yet. Upload a single document or run batch ingestion to populate the eTMF."
  >
    <template #action>
      <button
        type="button"
        class="btn btn-primary btn-sm upload-empty-btn"
        :disabled="!canStage || !selectedArtifactCode"
        :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
        @click="emit('openUpload')"
      >
        <span><ClinicalIcon name="file-text" :size="16" /></span>
        Upload Single Document
      </button>
      <button
        type="button"
        class="btn btn-secondary btn-sm batch-empty-btn"
        :disabled="!canStage"
        :title="!canStage ? ETMF_STAGE_ROLE_REASON : undefined"
        @click="emit('openBatch')"
      >
        <span><ClinicalIcon name="upload" :size="16" /></span> Batch Ingestion
      </button>
    </template>
  </EmptyState>
</template>

<script setup>
import { ClinicalIcon, EmptyState } from "ui";
import { ETMF_STAGE_ROLE_REASON } from "../../../canvas/authority/etmfTraining";

defineProps({
  scopeForbiddenGuidance: {
    type: String,
    default: "",
  },
  documentsError: {
    type: String,
    default: "",
  },
  canStage: {
    type: Boolean,
    default: true,
  },
  selectedArtifactCode: {
    type: String,
    default: "",
  },
});

const emit = defineEmits(["openUpload", "openBatch"]);
</script>

<style scoped>
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  padding: 4px 10px;
  font-size: 0.8rem;
  font-weight: 500;
  border-radius: 6px;
  cursor: pointer;
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

.scope-forbidden-guidance {
  padding: 32px 24px;
  text-align: center;
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 8px;
  color: var(--semantic-color-error-text-dark);
}

.scope-forbidden-guidance h3 {
  margin: 8px 0 6px;
  font-size: 1rem;
  font-weight: 700;
}

.scope-forbidden-guidance p {
  margin: 0;
  font-size: 0.85rem;
  line-height: 1.45;
}

.scope-forbidden-icon {
  font-size: 1.6rem;
}
</style>
