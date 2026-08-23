<template>
  <div class="dct-view">
    <TelehealthDtpConsole :study-id="studyId" />
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue';
import { useRoute } from 'vue-router';
import TelehealthDtpConsole from '../components/clinical/TelehealthDtpConsole.vue';

const route = useRoute();
const studyId = computed(() => (route.query.study_id as string) || 'STUDY-DCT-001');
</script>

<style scoped>
.dct-view {
  padding: 24px;
  max-width: 1600px;
  margin: 0 auto;
}
</style>
