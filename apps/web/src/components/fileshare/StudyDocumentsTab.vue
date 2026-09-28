<template>
  <div class="study-documents-tab-wrapper">
    <DocumentsHubView :initial-study-id="studyId" />
  </div>
</template>

<script setup>
import DocumentsHubView from "../../views/DocumentsHubView.vue";

defineProps({
  studyId: {
    type: String,
    required: true,
  },
  siteId: {
    type: String,
    default: null,
  },
});
</script>

<style scoped>
.study-documents-tab-wrapper {
  width: 100%;
  height: calc(100vh - 180px);
  position: relative;
  overflow: hidden;
}
</style>
