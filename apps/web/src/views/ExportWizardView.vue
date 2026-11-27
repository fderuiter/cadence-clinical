<template>
  <ConsolidatedStudyHeader
    view-id="export-wizard-view"
    title="Data Export Wizard"
    select-id="export-wizard-study-select"
    choose-study-test-id="export-wizard-choose-study"
    choose-study-message="Choose a study to stream reproducible exports and certified archives."
    canvas-link-test-id="open-canvas-exports-link"
    canvas-stage="submit"
    canvas-section="exports"
  >
    <template #subtitle>
      Reproducible clinical datasets, statistical bundles, and regulatory study
      archives stream directly from the Clinical Kernel.
    </template>
    <template #default="{ studyCode }">
      <ExportsSection :study-code="studyCode" />
    </template>
  </ConsolidatedStudyHeader>
</template>

<script setup lang="ts">
import ConsolidatedStudyHeader from "../components/canvas/ConsolidatedStudyHeader.vue";
import ExportsSection from "../components/canvas/ExportsSection.vue";
</script>
