<template>
  <main :id="viewId" class="canvas-consolidated-view">
    <header class="consolidated-header">
      <div>
        <h1>{{ title }}</h1>
        <p class="consolidated-subtitle">
          <slot name="subtitle" />
        </p>
      </div>
      <div class="consolidated-controls">
        <label :for="selectId">Study</label>
        <select
          :id="selectId"
          v-model="selectedStudyCode"
          :data-testid="selectId"
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
        <slot name="extra-controls" />
        <router-link
          v-if="selectedStudyCode"
          class="btn btn-secondary"
          :data-testid="canvasLinkTestId"
          :to="canvasLocation"
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
      :data-testid="chooseStudyTestId"
    >
      {{ chooseStudyMessage }}
    </p>
    <slot v-else-if="selectedStudyCode" :study-code="selectedStudyCode" />

    <slot name="modals" />
  </main>
</template>

<script setup lang="ts">
import { computed, watch } from "vue";
import { useConsolidatedStudyList } from "../../composables/useConsolidatedStudyList";
import {
  useScopedNavigation,
  type UseScopedStudyLocationOptions,
} from "../../composables/useScopedNavigation";

const props = defineProps<{
  viewId: string;
  title: string;
  selectId: string;
  chooseStudyTestId: string;
  chooseStudyMessage: string;
  canvasLinkTestId: string;
  canvasStage: NonNullable<UseScopedStudyLocationOptions["stage"]>;
  canvasSection: NonNullable<UseScopedStudyLocationOptions["section"]>;
  canvasNode?: string;
}>();

const emit = defineEmits<{
  (e: "study-change", studyCode: string): void;
}>();

const { studies, studiesLoading, studiesError, selectedStudyCode } =
  useConsolidatedStudyList();
const { scopedStudyLocation } = useScopedNavigation(
  () => selectedStudyCode.value
);

watch(selectedStudyCode, (code) => {
  emit("study-change", code);
});

const canvasLocation = computed(() =>
  scopedStudyLocation({
    stage: props.canvasStage,
    section: props.canvasSection,
    ...(props.canvasNode ? { node: props.canvasNode } : {}),
  })
);
</script>

<style scoped src="../../views/consolidatedCanvasView.css"></style>
