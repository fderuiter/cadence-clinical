<template>
  <StudyPulse @drill="emit('drill', $event)" />
  <StudySitesPanel
    v-if="hasStudySitesService"
    :key="`home-sites-${studyCode}`"
    :study-code="studyCode"
    :can-manage-sites="canManageSites"
    compact
    @open-participants="emit('open-participants', $event)"
  />
  <NeedsAttention :key="`attention-${studyCode}`" :study-code="studyCode" />
  <WorkQueuesPanel :key="`work-queues-${studyCode}`" :study-code="studyCode" />
  <StudyActivity :key="studyCode" :study-code="studyCode" />
</template>

<script setup lang="ts">
import { computed } from "vue";
import { kernelApi } from "../../api/kernelApi";
import NeedsAttention from "../ai/NeedsAttention.vue";
import StudyActivity from "./StudyActivity.vue";
import StudyPulse from "./StudyPulse.vue";
import StudySitesPanel from "./StudySitesPanel.vue";
import WorkQueuesPanel from "./WorkQueuesPanel.vue";

defineProps<{
  studyCode: string;
  canManageSites: boolean;
}>();

const emit = defineEmits<{
  (e: "drill", status: string): void;
  (e: "open-participants", siteCode: string): void;
}>();

const hasStudySitesService = computed(
  () => typeof kernelApi.listStudySites === "function"
);
</script>
