<template>
  <div class="icf-builder-container">
    <!-- Header Area -->
    <header class="builder-header">
      <div class="header-left">
        <h2>ICF Authoring: {{ econsentStore.currentIcf?.title }}</h2>
        <span class="version-tag"
          >Version {{ econsentStore.currentIcf?.version }}</span
        >
      </div>
      <div class="header-right">
        <button
          class="btn btn-primary btn-publish"
          @click="showPublishModal = true"
        >
          🚀 Publish Version
        </button>
      </div>
    </header>

    <!-- Language Translations Selector Tabs -->
    <div class="translations-section">
      <LanguageTranslationTabs />
    </div>

    <!-- Main Workspace -->
    <div class="builder-workspace">
      <!-- Sidebar / Outline Nav -->
      <nav class="section-outline">
        <div class="outline-group">
          <h4>Consent Sections</h4>
          <ul>
            <li
              v-for="sec in econsentStore.sections"
              :key="sec.id"
              :class="[
                'outline-item',
                { active: activeSectionId === sec.id && !showQuiz },
              ]"
              @click="selectSection(sec.id)"
            >
              <span class="item-icon">📄</span>
              <span class="item-title">{{ sec.title }}</span>
            </li>
          </ul>

          <!-- Add New Section form -->
          <div class="add-section-form">
            <input
              v-model="newSectionTitle"
              type="text"
              placeholder="New Section Title..."
              class="form-control inline-input"
              @keyup.enter="handleCreateSection"
            />
            <button
              type="button"
              class="btn btn-secondary btn-small"
              @click="handleCreateSection"
            >
              Add Section
            </button>
          </div>
        </div>

        <div class="outline-group separator">
          <h4>Interactive Checks</h4>
          <ul>
            <li
              :class="['outline-item', { active: showQuiz }]"
              @click="selectQuiz"
            >
              <span class="item-icon">📝</span>
              <span class="item-title">Comprehension Quiz</span>
            </li>
          </ul>
        </div>

        <!-- Version History List -->
        <div class="outline-group separator history-group">
          <h4>Version Audit History</h4>
          <div class="history-list">
            <div
              v-for="(hist, idx) in econsentStore.versionHistory"
              :key="idx"
              class="history-item"
            >
              <div class="history-ver">
                <strong>{{ hist.version }}</strong>
                <span class="history-time">{{
                  formatTime(hist.timestamp)
                }}</span>
              </div>
              <p class="history-reason">"{{ hist.reason }}"</p>
            </div>
          </div>
        </div>
      </nav>

      <!-- Main Editor/Workspace Pane -->
      <main class="editor-pane">
        <div v-if="!showQuiz && activeSection">
          <IcfSectionEditor
            :section="activeSection"
            @update="handleUpdateContent"
          />
        </div>
        <div v-else-if="showQuiz">
          <ComprehensionQuizBuilder />
        </div>
        <div v-else class="no-selection-pane">
          <p>
            Please select a Consent Section or the Comprehension Quiz from the
            left outline to start editing.
          </p>
        </div>
      </main>
    </div>

    <!-- Publish Version GxP Modal -->
    <div v-if="showPublishModal" class="publish-modal-overlay">
      <div class="publish-modal card">
        <div class="modal-header">
          <h3>21 CFR Part 11 Change Reason Capture</h3>
          <button class="close-btn" @click="showPublishModal = false">✕</button>
        </div>
        <div class="modal-body">
          <p class="warning-text">
            ⚠️ Publishing this Informed Consent Form (ICF) increments the active
            protocol design version. All future subject enrollments and
            re-consents will bind to the new version index.
          </p>
          <div class="form-group">
            <label for="publish-reason"
              >Mandatory Change Justification / Audit Reason:</label
            >
            <textarea
              id="publish-reason"
              v-model="publishReason"
              placeholder="e.g. Protocol Amendment v2.0 - added genetic screening disclaimer."
              rows="4"
              class="form-control"
            />
            <span v-if="publishError" class="error-text">{{
              publishError
            }}</span>
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showPublishModal = false"
          >
            Cancel
          </button>
          <button type="button" class="btn btn-primary" @click="handlePublish">
            Confirm & Publish
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useEconsentStore } from "../stores/econsent.js";
import IcfSectionEditor from "../components/econsent/IcfSectionEditor.vue";
import ComprehensionQuizBuilder from "../components/econsent/ComprehensionQuizBuilder.vue";
import LanguageTranslationTabs from "../components/econsent/LanguageTranslationTabs.vue";

const econsentStore = useEconsentStore();
const activeSectionId = ref(null);
const showQuiz = ref(false);
const showPublishModal = ref(false);
const publishReason = ref("");
const publishError = ref("");
const newSectionTitle = ref("");

const activeSection = computed(() =>
  econsentStore.sections.find((s) => s.id === activeSectionId.value)
);

onMounted(() => {
  econsentStore.loadIcf();
  // Select first section by default
  if (econsentStore.sections.length > 0) {
    activeSectionId.value = econsentStore.sections[0].id;
  }
});

const selectSection = (id) => {
  showQuiz.value = false;
  activeSectionId.value = id;
};

const selectQuiz = () => {
  showQuiz.value = true;
  activeSectionId.value = null;
};

const handleUpdateContent = (payload) => {
  econsentStore.updateSectionContent(payload.id, payload.html);
};

const handleCreateSection = () => {
  const title = newSectionTitle.value.trim();
  if (title) {
    const newSec = econsentStore.addSection(title);
    selectSection(newSec.id);
    newSectionTitle.value = "";
  }
};

const handlePublish = () => {
  publishError.value = "";
  const reason = publishReason.value.trim();
  if (!reason) {
    publishError.value =
      "Justification is mandatory under GxP compliance guidelines.";
    return;
  }

  econsentStore.publishIcfVersion(reason);
  showPublishModal.value = false;
  publishReason.value = "";
};

const formatTime = (isoString) => {
  try {
    const d = new Date(isoString);
    return d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
  } catch {
    return isoString;
  }
};
</script>

<style scoped>
.icf-builder-container {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  max-width: 1200px;
  margin: 0 auto;
  padding: var(--spacing-lg);
}

.builder-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 2px solid var(--color-border);
  padding-bottom: var(--spacing-md);
}

.header-left {
  display: flex;
  align-items: center;
  gap: var(--spacing-sm);
}

.header-left h2 {
  margin: 0;
  font-size: 1.5rem;
  color: var(--color-text);
}

.version-tag {
  background-color: var(--color-primary-light);
  color: var(--color-accent);
  padding: var(--spacing-2xs) var(--spacing-xs);
  border-radius: 9999px;
  font-size: 0.85rem;
  font-weight: bold;
}

.translations-section {
  width: 100%;
}

.builder-workspace {
  display: grid;
  grid-template-columns: 300px 1fr;
  gap: var(--spacing-xl);
}

.section-outline {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
  background-color: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: var(--spacing-md);
  height: fit-content;
}

.outline-group h4 {
  margin: 0 0 var(--spacing-xs) 0;
  font-size: 0.85rem;
  color: var(--color-text-muted);
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.outline-group ul {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-2xs);
}

.outline-item {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  padding: var(--spacing-xs) var(--spacing-sm);
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.2s;
  color: var(--color-text);
}

.outline-item:hover {
  background-color: var(--color-surface-muted);
}

.outline-item.active {
  background-color: var(--color-primary-light);
  color: var(--color-primary);
  font-weight: 600;
}

.item-icon {
  font-size: 1rem;
}

.separator {
  border-top: 1px solid var(--color-border);
  padding-top: var(--spacing-md);
}

.add-section-form {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  margin-top: var(--spacing-sm);
}

.inline-input {
  font-size: 0.85rem;
  padding: var(--spacing-2xs) var(--spacing-xs);
}

.editor-pane {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.no-selection-pane {
  background-color: var(--color-surface-muted);
  border: 2px dashed var(--color-border);
  border-radius: 8px;
  padding: var(--spacing-3xl);
  text-align: center;
  color: var(--color-text-muted);
}

/* Version History Styles */
.history-group {
  max-height: 250px;
  overflow-y: auto;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.history-item {
  border-left: 2px solid var(--color-border);
  padding-left: var(--spacing-xs);
  font-size: 0.8rem;
}

.history-ver {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.history-time {
  color: var(--color-text-muted);
  font-size: 0.75rem;
}

.history-reason {
  margin: var(--spacing-2xs) 0 0 0;
  color: var(--color-text-muted);
  font-style: italic;
}

/* Modal styles */
.publish-modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background-color: var(--color-text-muted);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.publish-modal {
  width: 90%;
  max-width: 550px;
  background-color: var(--color-surface);
  border-radius: 8px;
  box-shadow: 0 10px 15px -3px var(--color-border);
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--spacing-md) var(--spacing-lg);
  border-bottom: 1px solid var(--color-border);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.15rem;
  color: var(--color-text);
}

.close-btn {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: var(--color-text-muted);
}

.modal-body {
  padding: var(--spacing-lg);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.warning-text {
  background-color: var(--color-warning-bg);
  border: 1px solid var(--color-warning-bg);
  color: var(--color-warning);
  padding: var(--spacing-sm);
  border-radius: 6px;
  font-size: 0.85rem;
  line-height: 1.5;
  margin: 0;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.form-group label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--color-text);
}

.form-control {
  padding: var(--spacing-xs) var(--spacing-sm);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  font-size: 0.9rem;
}

.error-text {
  color: var(--color-error);
  font-size: 0.8rem;
  font-weight: 600;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: var(--spacing-sm);
  padding: var(--spacing-md) var(--spacing-lg);
  border-top: 1px solid var(--color-border);
  background-color: var(--color-surface-muted);
  border-radius: 0 0 8px 8px;
}
</style>
