<template>
  <div class="inspector-details-pane">
    <p
      v-if="loading"
      id="inspector-generic-loading"
      class="inspector-empty"
      role="status"
    >
      Loading record details…
    </p>
    <p
      v-else-if="display.masked"
      id="inspector-generic-masked"
      class="inspector-empty"
    >
      Details for this {{ display.kindLabel.toLowerCase() }} are hidden ({{
        display.maskReason
      }}).
    </p>
    <dl v-else id="inspector-generic" class="details">
      <dt>Record type</dt>
      <dd id="inspector-generic-kind">{{ display.kindLabel }}</dd>
      <template v-if="display.code">
        <dt>Code</dt>
        <dd id="inspector-generic-code" class="inspector-mono">
          {{ display.code }}
        </dd>
      </template>
      <template v-if="display.title && display.title !== display.code">
        <dt>Title</dt>
        <dd id="inspector-generic-title">{{ display.title }}</dd>
      </template>
      <template v-if="display.status">
        <dt>Status</dt>
        <dd id="inspector-generic-status">{{ display.status }}</dd>
      </template>
      <dt>Study</dt>
      <dd class="inspector-mono">{{ studyCode }}</dd>
    </dl>

    <InspectorRecordLinks
      :record-location="recordLocation"
      :data-entry-location="dataEntryLocation"
      :handoff-location="handoffLocation"
    />
  </div>
</template>

<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";
import type { NodeDisplaySummary } from "../../canvas/nodeResolver";
import InspectorRecordLinks from "./InspectorRecordLinks.vue";

defineProps<{
  studyCode: string;
  display: NodeDisplaySummary;
  loading?: boolean;
  recordLocation?: RouteLocationRaw | null;
  dataEntryLocation?: RouteLocationRaw | null;
  handoffLocation?: RouteLocationRaw | null;
}>();
</script>

<style scoped>
.inspector-mono {
  font-family: var(--semantic-font-mono);
}
</style>
