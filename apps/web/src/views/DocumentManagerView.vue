<!-- apps/web/src/views/DocumentManagerView.vue -->
<template>
  <div class="document-manager-layout">
    <aside class="sidebar-binder-tree">
      <TmfBinderTree
        :tree="etmfStore.binderTree"
        @select-artifact="handleArtifactSelect"
      />
    </aside>
    <main class="document-content-area">
      <DocumentGrid
        :documents="etmfStore.documents"
        @preview="handlePreviewDocument"
      />
    </main>
    <PdfPreviewModal
      v-if="selectedDoc"
      :document="selectedDoc"
      @close="selectedDoc = null"
    />
  </div>
</template>

<script setup>
import { ref, onMounted } from 'vue';
import { useEtmfStore } from '../stores/etmf';
import TmfBinderTree from '../components/etmf/TmfBinderTree.vue';
import DocumentGrid from '../components/etmf/DocumentGrid.vue';
import PdfPreviewModal from '../components/etmf/PdfPreviewModal.vue';

const etmfStore = useEtmfStore();
const selectedDoc = ref(null);

function handleArtifactSelect(artifactCode) {
  etmfStore.fetchDocuments(artifactCode);
}

function handlePreviewDocument(doc) {
  selectedDoc.value = doc;
}

onMounted(() => {
  etmfStore.fetchBinderTree();
});
</script>

<style scoped>
.document-manager-layout {
  display: flex;
  height: calc(100vh - 120px);
  gap: 20px;
  background-color: #f8fafc;
  margin: -20px;
  padding: 20px;
  overflow: hidden;
}

.sidebar-binder-tree {
  width: 340px;
  min-width: 300px;
  background-color: #ffffff;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.document-content-area {
  flex: 1;
  background-color: #ffffff;
  border-radius: 8px;
  overflow: hidden;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}
</style>
