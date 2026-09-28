<template>
  <div class="sensor-view">
    <SensorTelemetryConsole :study-id="studyId" />
  </div>
</template>

<script setup lang="ts">
import { computed } from "vue";
import { useRoute } from "vue-router";
import SensorTelemetryConsole from "../components/clinical/SensorTelemetryConsole.vue";

const route = useRoute();
const studyId = computed(() => (route.query.study_id as string) || "STUDY-901");
</script>

<style scoped>
.sensor-view {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
}
</style>
