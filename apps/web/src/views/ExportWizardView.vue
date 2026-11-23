<template>
  <main id="export-wizard-view" class="canvas-consolidated-view">
    <header class="consolidated-header">
      <div>
        <h1>Data Export Wizard</h1>
        <p class="consolidated-subtitle">
          Reproducible clinical datasets, statistical bundles, and regulatory
          study archives stream directly from the Clinical Kernel.
        </p>
      </div>
      <div class="consolidated-controls">
        <label for="export-wizard-study-select">Study</label>
        <select
          id="export-wizard-study-select"
          v-model="selectedStudyCode"
          data-testid="export-wizard-study-select"
          :disabled="studiesLoading"
        >
          <option value="">
            {{ studiesLoading ? "Loading studies…" : "Select a study…" }}
          </option>
          <option
            v-for="study in studies"
            :key="study.ref || study.code"
            :value="study.code"
          >
            {{ study.code }} — {{ study.title }}
          </option>
        </select>
        <router-link
          v-if="selectedStudyCode"
          class="btn btn-secondary"
          data-testid="open-canvas-exports-link"
          :to="{
            name: 'study',
            params: { studyCode: selectedStudyCode },
            query: { stage: 'submit', section: 'exports' },
          }"
        >
          Open in Clinical Canvas
        </router-link>
      </div>
    </header>

    <p v-if="studiesError" class="status-line error-banner" role="alert">
      {{ studiesError }}
    </p>
    <p
      v-else-if="!selectedStudyCode && !studiesLoading"
      class="status-line"
      data-testid="export-wizard-choose-study"
    >
      Choose a study to stream reproducible exports and certified archives.
    </p>
    <ExportsSection
      v-else-if="selectedStudyCode"
      :study-code="selectedStudyCode"
    />
  </main>
</template>

<script setup lang="ts">
import { onMounted, ref } from "vue";
import { kernelApi, type KernelStudySummary } from "../api/kernelApi";
import ExportsSection from "../components/canvas/ExportsSection.vue";
import { useStudyCanvasStore } from "../stores/studyCanvas";

const canvasStore = useStudyCanvasStore();
const studies = ref<KernelStudySummary[]>([]);
const studiesLoading = ref(false);
const studiesError = ref<string | null>(null);
const selectedStudyCode = ref(canvasStore.studyCode || "");

onMounted(async () => {
  if (typeof kernelApi.listStudies !== "function") return;
  studiesLoading.value = true;
  studiesError.value = null;
  try {
    const res = await kernelApi.listStudies();
    studies.value = res ?? [];
    if (!selectedStudyCode.value && studies.value.length > 0) {
      selectedStudyCode.value = studies.value[0].code;
    }
  } catch (err) {
    studiesError.value = err instanceof Error ? err.message : String(err);
  } finally {
    studiesLoading.value = false;
  }
});
</script>

<style scoped>
.canvas-consolidated-view {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px;
}

.consolidated-header {
  display: flex;
  flex-wrap: wrap;
  justify-content: space-between;
  align-items: flex-end;
  gap: 12px;
}

.consolidated-header h1 {
  margin: 0;
  font-size: 1.35rem;
}

.consolidated-subtitle {
  margin: 4px 0 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-body);
}

.consolidated-controls {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 8px;
}

.consolidated-controls label {
  font-size: 0.84rem;
  font-weight: 600;
}

.consolidated-controls select {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
  box-sizing: border-box;
}

.status-line {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-body);
}

.error-banner {
  padding: 8px 12px;
  border-radius: 6px;
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-dark);
}
</style>
