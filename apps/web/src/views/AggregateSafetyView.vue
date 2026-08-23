<template>
  <div class="safety-view">
    <AggregateSafetyConsole
      :initial-study-id="studyId"
      :initial-compound="compoundName"
    />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import AggregateSafetyConsole from '../components/clinical/AggregateSafetyConsole.vue';

const route = useRoute();
const studyId = computed(() => (route.query.study_id as string) || 'STUDY-CAD-301');
const compoundName = computed(() => (route.query.compound as string) || 'CADENCE-101');
</script>

<style scoped>
.safety-view {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
}
</style>
