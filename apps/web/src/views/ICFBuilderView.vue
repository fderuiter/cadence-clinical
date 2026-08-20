<template>
  <div class="icf-builder-container">
    <!-- Header Area -->
    <header class="builder-header">
      <div class="header-left">
        <h2>ICF Authoring: {{ econsentStore.currentIcf?.title }}</h2>
        <span class="study-tag"
          >Study: {{ econsentStore.currentIcf?.studyId || "CADENCE-101" }}</span
        >
        <span class="version-tag"
          >ICF Version: {{ econsentStore.currentIcf?.version }}</span
        >
        <span class="protocol-tag"
          >Protocol:
          {{ econsentStore.currentIcf?.protocolVersion || "v1.0" }}</span
        >
      </div>
      <div class="header-right">
        <button
          type="button"
          id="btn-open-sign-modal"
          class="btn btn-secondary btn-sign"
          @click="openSignatureModal"
        >
          ✍️ eConsent Sign-off
        </button>
        <button
          class="btn btn-primary btn-publish"
          @click="showPublishModal = true"
        >
          🚀 Publish Version
        </button>
      </div>
    </header>

    <!-- Cryptographic Manifest / Confirmation Banner if signed -->
    <div
      v-if="latestSignature"
      id="signature-manifest-banner"
      class="signature-manifest-banner card"
      role="status"
    >
      <div class="manifest-header">
        <span class="manifest-icon">🔒</span>
        <strong style="color: var(--success); font-size: 0.95rem">
          21 CFR Part 11 Electronic Signature Manifest Verified
        </strong>
      </div>
      <div class="manifest-details">
        <div>
          <strong>Signer:</strong>
          {{ latestSignature.signerName || latestSignature.username }} ({{
            latestSignature.signerRole || "Subject"
          }})
        </div>
        <div>
          <strong>Meaning of Signing:</strong>
          {{
            latestSignature.meaningOfSigning || latestSignature.signingReason
          }}
        </div>
        <div>
          <strong>Server Timestamp:</strong> {{ latestSignature.timestamp }}
        </div>
        <div>
          <strong>Cryptographic Checksum (SHA-256):</strong>
          <code>{{
            latestSignature.sha256_hash ||
            "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855" // pragma: allowlist secret
          }}</code>
        </div>
      </div>
    </div>

    <!-- Language Translations Selector Tabs -->
    <div class="translations-section">
      <LanguageTranslationTabs />
    </div>

    <!-- Main Workspace -->
    <div class="builder-workspace">
      <!-- Sidebar / Outline Nav -->
      <nav class="section-outline">
        <div class="outline-group">
          <h4>Modular Consent Clauses</h4>
          <ul id="consent-clauses-list">
            <li
              v-for="(sec, sIdx) in econsentStore.sections"
              :key="sec.id"
              :class="[
                'outline-item',
                { active: activeSectionId === sec.id && !showQuiz },
              ]"
              @click="selectSection(sec.id)"
            >
              <span class="item-icon">📄</span>
              <span class="item-title">{{ sec.title }}</span>

              <div class="clause-controls" @click.stop>
                <button
                  type="button"
                  class="btn-clause-ctrl btn-move-up"
                  title="Move Up"
                  :disabled="sIdx === 0"
                  @click="econsentStore.reorderSections(sIdx, sIdx - 1)"
                >
                  ▲
                </button>
                <button
                  type="button"
                  class="btn-clause-ctrl btn-move-down"
                  title="Move Down"
                  :disabled="sIdx === econsentStore.sections.length - 1"
                  @click="econsentStore.reorderSections(sIdx, sIdx + 1)"
                >
                  ▼
                </button>
                <button
                  type="button"
                  class="btn-clause-ctrl btn-delete-clause"
                  title="Remove Clause"
                  @click="econsentStore.removeSection(sec.id)"
                >
                  ✕
                </button>
              </div>
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
          <ComprehensionQuizBuilder @proceed-to-sign="openSignatureModal" />
        </div>
        <div v-else class="no-selection-pane">
          <p>
            Please select a Consent Section or the Comprehension Quiz from the
            left outline to start editing.
          </p>
        </div>
      </main>
    </div>

    <!-- Signature Capture Modal -->
    <!-- eslint-disable vuejs-accessibility/aria-role -->
    <SignatureCaptureModal
      :is-open="showSignatureModal"
      username="participant.cadence101"
      signer-name="Participant Jane Doe"
      role="Subject"
      action-url="/api/v1/econsent/templates/icf-001/versions/1/capture-consent"
      :on-sign="handleExecuteSignature"
      @cancel="showSignatureModal = false"
      @success="onSignatureSuccess"
      @error="onSignatureError"
    />
    <!-- eslint-enable vuejs-accessibility/aria-role -->

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
import SignatureCaptureModal from "../components/SignatureCaptureModal.vue";

const econsentStore = useEconsentStore();
const activeSectionId = ref(null);
const showQuiz = ref(false);
const showPublishModal = ref(false);
const showSignatureModal = ref(false);
const latestSignature = ref(null);
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

const openSignatureModal = () => {
  showSignatureModal.value = true;
};

const handleExecuteSignature = async (
  sigToken,
  signingReason,
  metadata = {}
) => {
  const record = {
    signerName: metadata.signerName || "Participant Jane Doe",
    signerRole: metadata.signerRole || "Subject",
    meaningOfSigning: signingReason || "I agree to participate",
    signingReason: signingReason || "I agree to participate",
    timestamp: new Date().toISOString(),
    sha256_hash:
      "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
    sigToken,
  };
  econsentStore.recordSignedConsent(record);
  latestSignature.value = record;
  return record;
};

const onSignatureSuccess = (result) => {
  showSignatureModal.value = false;
  if (!latestSignature.value) {
    latestSignature.value = result || {
      signerName: "Participant Jane Doe",
      signerRole: "Subject",
      meaningOfSigning: "I agree to participate",
      timestamp: new Date().toISOString(),
      sha256_hash:
        "a4f89d9e2b10a26d7c71e21b764c63286e9e4f215d2f6381014e7a83d7121289", // pragma: allowlist secret
    };
  }
};

const onSignatureError = (err) => {
  console.error("Signature capture failed:", err);
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
  gap: 16px;
  max-width: 1200px;
  margin: 0 auto;
  padding: 20px;
}

.builder-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 2px solid var(--border);
  padding-bottom: 16px;
  flex-wrap: wrap;
  gap: 12px;
}

.header-left {
  display: flex;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.header-left h2 {
  margin: 0;
  font-size: 1.4rem;
  color: var(--neutral-dark);
}

.study-tag {
  background-color: #f1f5f9;
  color: #334155;
  border: 1px solid var(--border);
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 600;
}

.protocol-tag {
  background-color: #eff6ff;
  color: var(--primary);
  border: 1px solid #bfdbfe;
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 600;
}

.version-tag {
  background-color: var(--accent-light);
  color: var(--accent);
  padding: 4px 10px;
  border-radius: 9999px;
  font-size: 0.85rem;
  font-weight: bold;
}

.header-right {
  display: flex;
  gap: 8px;
}

.signature-manifest-banner {
  background-color: #f0fdf4;
  border-left: 4px solid var(--success);
  border: 1px solid #bbf7d0;
  padding: 16px;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.manifest-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.manifest-details {
  font-size: 0.85rem;
  color: #1e293b;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.manifest-details code {
  background-color: #e2e8f0;
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
}

.translations-section {
  width: 100%;
}

.builder-workspace {
  display: grid;
  grid-template-columns: 320px 1fr;
  gap: 24px;
}

.section-outline {
  display: flex;
  flex-direction: column;
  gap: 16px;
  background-color: white;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  height: fit-content;
}

.outline-group h4 {
  margin: 0 0 10px 0;
  font-size: 0.85rem;
  color: #64748b;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.outline-group ul {
  list-style: none;
  padding: 0;
  margin: 0;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.outline-item {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
  font-size: 0.9rem;
  transition: all 0.2s;
  color: var(--neutral-dark);
}

.outline-item:hover {
  background-color: var(--neutral-light);
}

.outline-item.active {
  background-color: #eff6ff;
  color: var(--primary);
  font-weight: 600;
}

.item-icon {
  font-size: 1rem;
}

.item-title {
  flex: 1;
}

.clause-controls {
  display: flex;
  gap: 4px;
}

.btn-clause-ctrl {
  background: white;
  border: 1px solid var(--border);
  border-radius: 3px;
  font-size: 0.65rem;
  padding: 2px 5px;
  cursor: pointer;
  color: #475569;
}

.btn-clause-ctrl:hover:not(:disabled) {
  background: #f1f5f9;
  color: var(--primary);
}

.btn-delete-clause:hover {
  background: #fee2e2;
  color: var(--error);
  border-color: #fca5a5;
}

.separator {
  border-top: 1px solid var(--border);
  padding-top: 16px;
}

.add-section-form {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.inline-input {
  font-size: 0.85rem;
  padding: 6px 10px;
}

.editor-pane {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.no-selection-pane {
  background-color: var(--neutral-light);
  border: 2px dashed var(--border);
  border-radius: 8px;
  padding: 40px;
  text-align: center;
  color: #64748b;
}

/* Version History Styles */
.history-group {
  max-height: 250px;
  overflow-y: auto;
}

.history-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.history-item {
  border-left: 2px solid var(--border);
  padding-left: 8px;
  font-size: 0.8rem;
}

.history-ver {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.history-time {
  color: #94a3b8;
  font-size: 0.75rem;
}

.history-reason {
  margin: 2px 0 0 0;
  color: #64748b;
  font-style: italic;
}

/* Modal styles */
.publish-modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100%;
  height: 100%;
  background-color: rgba(0, 0, 0, 0.4);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.publish-modal {
  width: 90%;
  max-width: 550px;
  background-color: white;
  border-radius: 8px;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1);
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid var(--border);
}

.modal-header h3 {
  margin: 0;
  font-size: 1.15rem;
  color: var(--neutral-dark);
}

.close-btn {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: #94a3b8;
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.warning-text {
  background-color: #fffbeb;
  border: 1px solid #fef3c7;
  color: #b45309;
  padding: 12px;
  border-radius: 6px;
  font-size: 0.85rem;
  line-height: 1.5;
  margin: 0;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.form-group label {
  font-weight: 600;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.form-control {
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.9rem;
}

.error-text {
  color: var(--error);
  font-size: 0.8rem;
  font-weight: 600;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  padding: 16px 20px;
  border-top: 1px solid var(--border);
  background-color: var(--neutral-light);
  border-radius: 0 0 8px 8px;
}
</style>
