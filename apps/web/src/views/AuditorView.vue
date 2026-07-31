<!-- apps/web/src/views/AuditorView.vue -->
<template>
  <div
    class="auditor-workspace"
    style="padding: 20px;"
  >
    <div
      class="inspection-banner"
      style="background: rgba(0, 123, 255, 0.08); padding: 12px 16px; border-left: 4px solid var(--accent); border-radius: 4px; margin-bottom: 20px; display: flex; justify-content: space-between; align-items: center; font-size: 13px;"
    >
      <span
        class="badge badge-info"
        style="font-weight: bold; background: var(--accent); color: white; padding: 4px 8px; border-radius: 4px;"
      >REGULATORY INSPECTION MODE</span>
      <span>Session Expires: {{ auditorStore.inspectionSession?.expiresAt }}</span>
    </div>
    <header
      class="workspace-header"
      style="display: flex; justify-content: space-between; align-items: center; margin-bottom: 20px;"
    >
      <h2 style="margin: 0; font-size: 20px; font-weight: 600;">
        Regulatory Audit &amp; Inspection Trail
      </h2>
      <button
        class="btn btn-secondary"
        @click="showExportModal = true"
      >
        Export Inspection Log
      </button>
    </header>
    <div class="workspace-body">
      <AuditTrailViewer :events="auditorStore.auditEvents" />
    </div>
    <AuditorExportModal
      v-if="showExportModal"
      @close="showExportModal = false"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';
import { useAuditorStore } from '@/stores/auditor';
import AuditTrailViewer from '@/components/auditor/AuditTrailViewer.vue';
import AuditorExportModal from '@/components/auditor/AuditorExportModal.vue';

const auditorStore = useAuditorStore();
const showExportModal = ref(false);

onMounted(() => {
  auditorStore.fetchAuditLogs();
});
</script>
