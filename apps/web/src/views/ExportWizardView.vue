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
import ExportsSection from "../components/canvas/ExportsSection.vue";
import { useConsolidatedStudyList } from "../composables/useConsolidatedStudyList";

const { studies, studiesLoading, studiesError, selectedStudyCode } =
  useConsolidatedStudyList();
</script>

<style scoped src="./consolidatedCanvasView.css"></style>
