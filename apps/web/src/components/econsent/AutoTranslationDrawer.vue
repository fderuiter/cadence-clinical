<template>
  <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
  <div v-if="isOpen" class="drawer-overlay" @click.self="$emit('close')">
    <aside
      ref="drawerRef"
      class="auto-translation-drawer"
      :class="{ open: isOpen }"
      role="dialog"
      aria-modal="true"
      aria-labelledby="drawer-title"
    >
      <div class="drawer-header">
        <div class="drawer-title-group">
          <span class="drawer-icon">🌐</span>
          <h3 id="drawer-title">
            eConsent Auto-Translation & Vendor XLIFF Exchange
          </h3>
        </div>
        <button
          type="button"
          class="btn-close"
          aria-label="Close Auto Translation Drawer"
          title="Close Drawer"
          @click="$emit('close')"
        >
          ✕
        </button>
      </div>

      <div class="drawer-subtitle">
        Generate automated machine-translated drafts for target locales or
        exchange standard XLIFF 1.2/2.0 localization packages with translation
        vendors.
      </div>

      <!-- Mode Selector Tabs -->
      <div class="drawer-tabs" role="tablist">
        <button
          type="button"
          role="tab"
          :aria-selected="executionMode === 'neural'"
          :class="['drawer-tab', { active: executionMode === 'neural' }]"
          @click="executionMode = 'neural'"
        >
          🤖 Auto-Translate (Neural)
        </button>
        <button
          type="button"
          role="tab"
          :aria-selected="executionMode === 'export'"
          :class="['drawer-tab', { active: executionMode === 'export' }]"
          @click="executionMode = 'export'"
        >
          📤 Export XLIFF Package
        </button>
        <button
          type="button"
          role="tab"
          :aria-selected="executionMode === 'import'"
          :class="['drawer-tab', { active: executionMode === 'import' }]"
          @click="executionMode = 'import'"
        >
          📥 Import Vendor XLIFF
        </button>
      </div>

      <div class="drawer-body">
        <!-- Status / Alert Message Banner -->
        <div
          v-if="statusMessage"
          :class="['status-banner', `status-${statusType}`]"
          role="status"
          aria-live="polite"
        >
          <span class="banner-icon">
            <template v-if="statusType === 'success'">✅</template>
            <template v-else-if="statusType === 'warning'">⚠️</template>
            <template v-else>❌</template>
          </span>
          <div class="banner-content">
            <p>{{ statusMessage }}</p>
            <div v-if="mismatchedKeys.length > 0" class="mismatched-details">
              <strong>Mismatched Section Keys Ignored:</strong>
              <code>{{ mismatchedKeys.join(", ") }}</code>
            </div>
          </div>
        </div>

        <!-- Mode 1: Neural Machine Auto-Translation -->
        <div v-if="executionMode === 'neural'" class="mode-panel">
          <div class="form-section">
            <span class="section-label">Target Locales</span>
            <p class="section-desc">
              Select target languages for automated translation draft
              generation.
            </p>
            <div class="locale-checkbox-grid">
              <label
                v-for="lang in availableTargetLanguages"
                :key="lang.code"
                class="checkbox-label"
              >
                <input
                  v-model="selectedLocales"
                  type="checkbox"
                  :value="lang.code"
                />
                <span class="flag-icon">{{ lang.flag }}</span>
                <span>{{ lang.name }} ({{ lang.code.toUpperCase() }})</span>
              </label>
            </div>
          </div>

          <div class="form-section">
            <span class="section-label">Section Scope</span>
            <div class="radio-group">
              <label class="radio-label">
                <input v-model="sectionScope" type="radio" value="unedited" />
                <span>Unedited Sections Only (Recommended)</span>
              </label>
              <label class="radio-label">
                <input v-model="sectionScope" type="radio" value="all" />
                <span>All Consent Sections</span>
              </label>
            </div>
          </div>

          <div class="form-section highlight-box">
            <label class="checkbox-label overwrite-checkbox">
              <input v-model="overwriteExisting" type="checkbox" />
              <strong>Overwrite Existing Translations</strong>
            </label>
            <p class="help-text">
              By default, approved or human-edited translations are protected.
              Checking this will overwrite existing target language content.
            </p>
          </div>

          <div class="info-note">
            ℹ️ All generated sections will be marked with a
            <strong>"Draft - Pending Review"</strong> status tag until validated
            by a medical translator.
          </div>
        </div>

        <!-- Mode 2: Export XLIFF Package -->
        <div v-else-if="executionMode === 'export'" class="mode-panel">
          <div class="form-section">
            <label for="export-locale-select" class="section-label"
              >Target Language Locale</label
            >
            <select
              id="export-locale-select"
              v-model="exportLocale"
              class="form-control"
            >
              <option
                v-for="lang in availableTargetLanguages"
                :key="lang.code"
                :value="lang.code"
              >
                {{ lang.name }} ({{ lang.code.toUpperCase() }})
              </option>
            </select>
          </div>

          <div class="form-section">
            <label for="xliff-version-select" class="section-label"
              >XLIFF Specification Standard</label
            >
            <select
              id="xliff-version-select"
              v-model="xliffVersion"
              class="form-control"
            >
              <option value="1.2">XLIFF 1.2 (Standard XML Package)</option>
              <option value="2.0">
                XLIFF 2.0 (OASIS Modern Specification)
              </option>
            </select>
          </div>

          <div class="info-note">
            📄 Exporting generates an XML bundle containing master source
            section strings, titles, and IDs for localization vendors.
          </div>
        </div>

        <!-- Mode 3: Import Vendor XLIFF Package -->
        <div v-else-if="executionMode === 'import'" class="mode-panel">
          <div class="form-section">
            <label for="xliff-file-input" class="section-label"
              >Select Vendor XLIFF File</label
            >
            <input
              id="xliff-file-input"
              type="file"
              accept=".xliff,.xlf,.xml"
              class="file-input"
              @change="handleFileChange"
            />
            <p class="help-text">
              Supports standard XLIFF 1.2 and XLIFF 2.0 XML files.
            </p>
          </div>

          <div class="info-note">
            🛡️ Imports parse localized translation units, validate XML
            structure, map section keys, update Pinia store state, and set
            status to <strong>"Vendor Verified"</strong>. Invalid section keys
            will allow partial import.
          </div>
        </div>
      </div>

      <!-- Action Footer -->
      <div class="drawer-footer">
        <button type="button" class="btn btn-secondary" @click="$emit('close')">
          Cancel
        </button>

        <button
          v-if="executionMode === 'neural'"
          type="button"
          class="btn btn-primary btn-generate-drafts"
          :disabled="selectedLocales.length === 0 || isProcessing"
          @click="runAutoTranslate"
        >
          <span v-if="isProcessing">⏳ Generating...</span>
          <span v-else>✨ Generate Translation Drafts</span>
        </button>

        <button
          v-else-if="executionMode === 'export'"
          type="button"
          class="btn btn-primary btn-export-xliff"
          :disabled="isProcessing"
          @click="runExportXliff"
        >
          📥 Export XLIFF Bundle
        </button>

        <button
          v-else-if="executionMode === 'import'"
          type="button"
          class="btn btn-primary btn-import-xliff"
          :disabled="!selectedFile || isProcessing"
          @click="runImportXliff"
        >
          <span v-if="isProcessing">⏳ Importing...</span>
          <span v-else>📤 Import XLIFF Package</span>
        </button>
      </div>
    </aside>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";
import { useEconsentStore } from "../../stores/econsent.js";
import { useFocusTrap } from "../../composables/useFocusTrap.js";
import { useEscapeClose } from "../../composables/useEscapeClose.js";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["close", "translated"]);

const econsentStore = useEconsentStore();
const drawerRef = ref(null);

useFocusTrap(drawerRef, () => props.isOpen);
useEscapeClose(
  () => props.isOpen,
  () => emit("close")
);

const executionMode = ref("neural"); // 'neural' | 'export' | 'import'
const selectedLocales = ref(["es", "fr", "de"]);
const sectionScope = ref("unedited");
const overwriteExisting = ref(false);
const exportLocale = ref("es");
const xliffVersion = ref("1.2");
const selectedFile = ref(null);

const statusMessage = ref("");
const statusType = ref(""); // 'success' | 'warning' | 'error'
const mismatchedKeys = ref([]);
const isProcessing = ref(false);

const availableTargetLanguages = [
  { code: "es", name: "Spanish", flag: "🇪🇸" },
  { code: "fr", name: "French", flag: "🇫🇷" },
  { code: "de", name: "German", flag: "🇩🇪" },
];

watch(
  () => props.isOpen,
  (val) => {
    if (val) {
      statusMessage.value = "";
      statusType.value = "";
      mismatchedKeys.value = [];
      selectedFile.value = null;
    }
  }
);

const handleFileChange = (e) => {
  const file = e.target.files?.[0];
  selectedFile.value = file || null;
  statusMessage.value = "";
  statusType.value = "";
  mismatchedKeys.value = [];
};

const runAutoTranslate = async () => {
  if (selectedLocales.value.length === 0) return;
  isProcessing.value = true;
  statusMessage.value = "";
  statusType.value = "";
  mismatchedKeys.value = [];

  try {
    econsentStore.autoTranslateDrafts({
      targetLocales: selectedLocales.value,
      scope: sectionScope.value,
      overwriteExisting: overwriteExisting.value,
    });

    statusType.value = "success";
    statusMessage.value = `Successfully generated automated translation drafts for ${selectedLocales.value.map((c) => c.toUpperCase()).join(", ")}. All populated sections marked as "Draft - Pending Review".`;
    emit("translated");
  } catch (err) {
    statusType.value = "error";
    statusMessage.value =
      err.message || "Failed to generate translation drafts.";
  } finally {
    isProcessing.value = false;
  }
};

const runExportXliff = () => {
  isProcessing.value = true;
  statusMessage.value = "";
  try {
    const xmlContent = econsentStore.exportXliff({
      targetLocale: exportLocale.value,
      version: xliffVersion.value,
    });

    const blob = new Blob([xmlContent], {
      type: "application/x-xliff+xml;charset=utf-8",
    });
    const url = URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = url;
    link.download = `icf-master-${econsentStore.currentIcf?.id || "icf-001"}-${exportLocale.value}-v${xliffVersion.value}.xliff`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);

    statusType.value = "success";
    statusMessage.value = `Successfully exported XLIFF ${xliffVersion.value} package for locale '${exportLocale.value.toUpperCase()}'.`;
  } catch (err) {
    statusType.value = "error";
    statusMessage.value = err.message || "Failed to export XLIFF bundle.";
  } finally {
    isProcessing.value = false;
  }
};

const runImportXliff = () => {
  if (!selectedFile.value) return;
  isProcessing.value = true;
  statusMessage.value = "";
  statusType.value = "";
  mismatchedKeys.value = [];

  const reader = new FileReader();
  reader.onload = (e) => {
    try {
      const xmlText = e.target.result;
      const res = econsentStore.importXliff(xmlText);

      if (!res.success) {
        statusType.value = "error";
        statusMessage.value = res.error;
      } else {
        mismatchedKeys.value = res.mismatchedKeys || [];
        if (res.mismatchedKeys && res.mismatchedKeys.length > 0) {
          if (res.updatedCount > 0) {
            statusType.value = "warning";
            statusMessage.value = `Partial import completed for locale '${res.targetLocale.toUpperCase()}': ${res.updatedCount} sections updated. Mismatched section keys were ignored.`;
          } else {
            statusType.value = "warning";
            statusMessage.value = `XLIFF file parsed, but no matching section keys were found for locale '${res.targetLocale.toUpperCase()}'.`;
          }
        } else {
          statusType.value = "success";
          statusMessage.value = `Successfully imported vendor XLIFF package for locale '${res.targetLocale.toUpperCase()}'. ${res.updatedCount} sections updated to "Vendor Verified".`;
        }
        emit("translated");
      }
    } catch (err) {
      statusType.value = "error";
      statusMessage.value =
        err.message || "An unexpected error occurred while parsing XLIFF file.";
    } finally {
      isProcessing.value = false;
    }
  };

  reader.onerror = () => {
    statusType.value = "error";
    statusMessage.value = "Failed to read the selected file.";
    isProcessing.value = false;
  };

  reader.readAsText(selectedFile.value);
};
</script>

<style scoped>
.drawer-overlay {
  position: fixed;
  top: 0;
  left: 0;
  width: 100vw;
  height: 100vh;
  background-color: rgba(15, 23, 42, 0.45);
  backdrop-filter: blur(2px);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
}

.auto-translation-drawer {
  width: 500px;
  max-width: 100vw;
  height: 100vh;
  background-color: white;
  box-shadow: -4px 0 24px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
  animation: slideInRight 0.25s ease-out;
}

@keyframes slideInRight {
  from {
    transform: translateX(100%);
  }
  to {
    transform: translateX(0);
  }
}

.drawer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 18px 24px 12px 24px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.drawer-title-group {
  display: flex;
  align-items: center;
  gap: 10px;
}

.drawer-icon {
  font-size: 1.4rem;
}

.drawer-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--neutral-dark, #0f172a);
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: #64748b;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-close:hover {
  background-color: #f1f5f9;
  color: #0f172a;
}

.drawer-subtitle {
  padding: 0 24px 14px 24px;
  font-size: 0.85rem;
  color: #64748b;
  line-height: 1.4;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.drawer-tabs {
  display: flex;
  background-color: #f8fafc;
  padding: 8px 16px;
  gap: 6px;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.drawer-tab {
  flex: 1;
  padding: 8px 10px;
  font-size: 0.8rem;
  font-weight: 600;
  border: 1px solid transparent;
  border-radius: 6px;
  background-color: transparent;
  color: #64748b;
  cursor: pointer;
  transition: all 0.2s;
  text-align: center;
}

.drawer-tab:hover {
  background-color: #e2e8f0;
  color: #0f172a;
}

.drawer-tab.active {
  background-color: white;
  border-color: var(--border, #cbd5e1);
  color: var(--primary, #2563eb);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.drawer-body {
  flex: 1;
  overflow-y: auto;
  padding: 20px 24px;
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.status-banner {
  display: flex;
  gap: 12px;
  align-items: flex-start;
  padding: 12px 16px;
  border-radius: 8px;
  font-size: 0.85rem;
  line-height: 1.5;
}

.status-success {
  background-color: #f0fdf4;
  border: 1px solid #bbf7d0;
  color: #166534;
}

.status-warning {
  background-color: #fffbeb;
  border: 1px solid #fef3c7;
  color: #b45309;
}

.status-error {
  background-color: #fef2f2;
  border: 1px solid #fecaca;
  color: #991b1b;
}

.banner-content p {
  margin: 0;
}

.mismatched-details {
  margin-top: 6px;
  font-size: 0.8rem;
}

.mismatched-details code {
  background-color: rgba(0, 0, 0, 0.06);
  padding: 2px 6px;
  border-radius: 4px;
  font-family: monospace;
}

.mode-panel {
  display: flex;
  flex-direction: column;
  gap: 18px;
}

.form-section {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.section-label {
  font-weight: 600;
  font-size: 0.88rem;
  color: #1e293b;
}

.section-desc {
  margin: 0;
  font-size: 0.8rem;
  color: #64748b;
}

.locale-checkbox-grid {
  display: flex;
  flex-direction: column;
  gap: 8px;
  background-color: #f8fafc;
  padding: 12px;
  border-radius: 8px;
  border: 1px solid #e2e8f0;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: #334155;
  cursor: pointer;
}

.flag-icon {
  font-size: 1.1rem;
}

.radio-group {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.radio-label {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.85rem;
  color: #334155;
  cursor: pointer;
}

.highlight-box {
  background-color: #f0f9ff;
  border: 1px solid #bae6fd;
  padding: 12px;
  border-radius: 8px;
}

.overwrite-checkbox {
  color: #0369a1;
}

.help-text {
  margin: 4px 0 0 0;
  font-size: 0.78rem;
  color: #64748b;
}

.info-note {
  background-color: #f8fafc;
  border-left: 3px solid #3b82f6;
  padding: 10px 14px;
  font-size: 0.82rem;
  color: #334155;
  border-radius: 0 6px 6px 0;
  line-height: 1.4;
}

.form-control {
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.88rem;
}

.file-input {
  padding: 8px;
  border: 1px dashed #cbd5e1;
  border-radius: 6px;
  background-color: #f8fafc;
  font-size: 0.85rem;
}

.drawer-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 16px 24px;
  border-top: 1px solid var(--border, #e2e8f0);
  background-color: #f8fafc;
}
</style>
