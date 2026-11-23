<template>
  <DocumentManagerView
    v-if="canReadDocuments"
    :key="`docs-${studyCode}`"
    :study-id="studyCode"
    :site-id="siteId"
  />
  <p v-else id="etmf-read-role-guidance" class="stage-empty" role="status">
    {{ ETMF_READ_ROLE_REASON }}
  </p>
</template>

<script setup lang="ts">
import { ETMF_READ_ROLE_REASON } from "../../canvas/authority";
import DocumentManagerView from "../../views/DocumentManagerView.vue";

defineProps<{
  studyCode: string;
  siteId?: string;
  canReadDocuments: boolean;
}>();
</script>

<style scoped>
.stage-empty {
  color: var(--neutral-dark);
}
</style>
