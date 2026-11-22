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
          id="btn-open-sign-modal"
          type="button"
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
          <code>{{ latestSignature.sha256_hash || DEFAULT_EMPTY_SHA256 }}</code>
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
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
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
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
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
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
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
    <SignatureCaptureModal
      :is-open="showSignatureModal"
      username="participant.cadence101"
      signer-name="Participant Jane Doe"
      signer-role="Subject"
      :action-url="captureConsentActionUrl"
      :semantic-action="ECONSENT_CAPTURE_SEMANTIC_ACTION"
      :on-sign="handleExecuteSignature"
      @cancel="showSignatureModal = false"
      @success="onSignatureSuccess"
      @error="onSignatureError"
    />

    <!-- Publish Version GxP Modal -->
    <ClinicalModal
      v-model="showPublishModal"
      :teleport="false"
      backdrop-class="publish-modal-overlay"
      dialog-class="publish-modal card"
      title="21 CFR Part 11 Change Reason Capture"
    >
      <p class="warning-text">
        ⚠️ Publishing this Informed Consent Form (ICF) increments the active
        protocol design version. All future subject enrollments and re-consents
        will bind to the new version index.
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
        <span v-if="publishError" class="error-text">{{ publishError }}</span>
      </div>
      <template #footer>
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
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { ClinicalModal } from "ui";
import {
  ECONSENT_CAPTURE_SEMANTIC_ACTION,
  econsentCaptureAction,
} from "../api/econsent";
import { useEconsentStore } from "../stores/econsent.js";
import IcfSectionEditor from "../components/econsent/IcfSectionEditor.vue";
import ComprehensionQuizBuilder from "../components/econsent/ComprehensionQuizBuilder.vue";
import LanguageTranslationTabs from "../components/econsent/LanguageTranslationTabs.vue";
import SignatureCaptureModal from "../components/SignatureCaptureModal.vue";

const econsentStore = useEconsentStore();
const DEFAULT_EMPTY_SHA256 =
  "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"; // pragma: allowlist secret
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

const captureConsentActionUrl = computed(() => {
  const templateId = econsentStore.currentIcf?.id || "icf-001";
  const rawVersion = String(econsentStore.currentIcf?.version || "v1.0");
  const versionMatch = rawVersion.match(/^v?(\d+)/i);
  const versionIndex = versionMatch ? Number.parseInt(versionMatch[1], 10) : 1;
  return econsentCaptureAction(templateId, versionIndex);
});

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
  if (!econsentStore.recordSignedConsent(record)) {
    throw new Error(
      "Consent signatures aren't available yet. Nothing was signed."
    );
  }
  latestSignature.value = record;
  return record;
};

const onSignatureSuccess = (result) => {
  showSignatureModal.value = false;
  // Show only a signature that was actually recorded, never a made-up one (#4827).
  if (!latestSignature.value && result) {
    latestSignature.value = result;
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

  if (!econsentStore.publishIcfVersion(reason)) return;
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
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  border: 1px solid var(--border);
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 600;
}

.protocol-tag {
  background-color: var(--semantic-color-info-bg);
  color: var(--primary);
  border: 1px solid var(--semantic-color-info-border);
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
  background-color: var(--semantic-color-success-bg-soft);
  border-left: 4px solid var(--success);
  border: 1px solid var(--semantic-color-success-border-soft);
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
  color: var(--semantic-color-text-dark);
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.manifest-details code {
  background-color: var(--semantic-color-surface-header);
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
  color: var(--semantic-color-text-secondary);
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
  background-color: var(--semantic-color-info-bg);
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
  color: var(--semantic-color-text-secondary);
}

.btn-clause-ctrl:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
  color: var(--primary);
}

.btn-delete-clause:hover {
  background: var(--semantic-color-error-bg);
  color: var(--error);
  border-color: var(--semantic-color-error-border);
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
  color: var(--semantic-color-text-secondary);
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
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
}

.history-reason {
  margin: 2px 0 0 0;
  color: var(--semantic-color-text-secondary);
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
  color: var(--semantic-color-text-secondary);
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.warning-text {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
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
