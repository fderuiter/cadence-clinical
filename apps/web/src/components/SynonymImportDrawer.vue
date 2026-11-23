<template>
  <ClinicalDrawer
    :model-value="codingStore.isSynonymDrawerOpen"
    :teleport="false"
    size="lg"
    :dismissible="!codingStore.synonymImportLoading"
    overlay-class="synonym-drawer-overlay"
    panel-class="synonym-drawer-panel"
    aria-labelledby="synonym-drawer-title"
    @close="close"
  >
    <template #header>
      <div class="drawer-header">
        <div class="header-title-wrap">
          <FolderDown :size="18" class="header-icon" aria-hidden="true" />
          <h2 id="synonym-drawer-title" class="drawer-title">
            Bulk Synonym Dictionary Import
          </h2>
        </div>
        <button
          type="button"
          aria-label="Close synonym import drawer"
          class="close-btn"
          :disabled="codingStore.synonymImportLoading"
          @click="close"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div class="synonym-drawer-content">
      <p class="drawer-subtitle">
        Ingest MedDRA and WHODrug verbatim synonym dictionaries with pre-flight
        validation and Part 11 audit lineage.
      </p>

      <!-- Navigation Tabs -->
      <div
        class="synonym-tabs"
        role="tablist"
        aria-label="Synonym Import Views"
      >
        <button
          type="button"
          role="tab"
          :aria-selected="activeTab === 'import'"
          class="synonym-tab-btn"
          :class="{ active: activeTab === 'import' }"
          @click="activeTab = 'import'"
        >
          <FolderInput :size="15" aria-hidden="true" />
          <span>Import New Rules</span>
        </button>
        <button
          type="button"
          role="tab"
          :aria-selected="activeTab === 'history'"
          class="synonym-tab-btn"
          :class="{ active: activeTab === 'history' }"
          @click="switchToHistoryTab"
        >
          <History :size="15" aria-hidden="true" />
          <span>Audit History</span>
        </button>
        <button
          type="button"
          role="tab"
          :aria-selected="activeTab === 'rules'"
          class="synonym-tab-btn"
          :class="{ active: activeTab === 'rules' }"
          @click="switchToRulesTab"
        >
          <Zap :size="15" aria-hidden="true" />
          <span>Active Rules ({{ codingStore.synonymRules.length }})</span>
        </button>
      </div>

      <!-- Access Restricted Banner -->
      <div v-if="!isAuthorized" class="access-restricted-banner">
        <div class="banner-heading">
          <AlertTriangle :size="16" aria-hidden="true" />
          <span>Access Restricted</span>
        </div>
        <p class="banner-body">
          Bulk synonym dictionary import requires
          <strong>DATA_MANAGER</strong> or
          <strong>SPONSOR_DESIGNER</strong> authorization. You can view existing
          history and active rules in read-only mode.
        </p>
      </div>

      <!-- TAB 1: IMPORT NEW RULES -->
      <div v-if="activeTab === 'import'" class="tab-panel">
        <!-- Configuration Card -->
        <div class="section-card">
          <h3 class="section-heading">1. Scope &amp; Target Configuration</h3>

          <div class="config-grid">
            <div class="field-group">
              <label for="synonym-scope" class="field-label">
                Import Scope
              </label>
              <select
                id="synonym-scope"
                v-model="scope"
                class="form-control scope-select"
                :disabled="!isAuthorized || codingStore.synonymImportLoading"
              >
                <option value="STUDY">STUDY (Study-Specific Rule Store)</option>
                <option value="ENTERPRISE">
                  ENTERPRISE (Cross-Study Master Rule Store)
                </option>
              </select>
            </div>

            <div v-if="scope === 'STUDY'" class="field-group">
              <label for="synonym-study-id" class="field-label">
                Study ID
              </label>
              <input
                id="synonym-study-id"
                v-model="studyId"
                type="text"
                placeholder="e.g. STUDY-101"
                class="form-control study-id-input"
                :disabled="!isAuthorized || codingStore.synonymImportLoading"
              />
            </div>
          </div>
        </div>

        <!-- File Dropzone -->
        <div class="section-card">
          <h3 class="section-heading">2. Select Synonym Dictionary File</h3>

          <ClinicalDropzone
            class="dropzone"
            accept=".csv,.xlsx"
            :disabled="!isAuthorized || codingStore.synonymImportLoading"
            :has-selection="!!selectedFile"
            :button-label="
              selectedFile
                ? 'Replace Synonym File'
                : 'Select Synonym Dictionary File'
            "
            input-aria-label="Synonym dictionary file"
            removable
            remove-label="Remove File"
            :remove-context="selectedFile ? selectedFile.name : ''"
            @file-selected="onSynonymFileSelected"
            @remove="clearSelectedFile"
          >
            <template #prompt>
              <div v-if="!selectedFile" class="dropzone-prompt">
                <UploadCloud :size="20" aria-hidden="true" />
                <span>
                  Drag and drop a <strong>.csv</strong> or
                  <strong>.xlsx</strong> synonym dictionary file here
                </span>
              </div>
            </template>
            <template #hint>
              <span v-if="!selectedFile" class="dropzone-hint">
                Must include headers: <code>verbatim_text</code>,
                <code>dictionary_type</code>, <code>dictionary_version</code>,
                <code>target_code</code>, <code>target_term</code>
              </span>
            </template>
            <template #selected>
              <div v-if="selectedFile" class="selected-file-summary">
                <FileSpreadsheet :size="18" aria-hidden="true" />
                <div>
                  <div class="selected-file-name">
                    {{ selectedFile.name }}
                  </div>
                  <div class="selected-file-size">
                    {{ formatFileSize(selectedFile.size) }}
                  </div>
                </div>
              </div>
            </template>
          </ClinicalDropzone>

          <div class="card-actions-end">
            <button
              type="button"
              class="validate-btn btn btn-primary"
              :disabled="
                !selectedFile ||
                !isAuthorized ||
                codingStore.synonymImportLoading
              "
              @click="runPreflightValidation"
            >
              <Search :size="15" aria-hidden="true" />
              <span>{{
                codingStore.synonymImportLoading
                  ? "Validating File..."
                  : "Run Pre-flight Validation"
              }}</span>
            </button>
          </div>
        </div>

        <!-- Error Alert -->
        <div v-if="codingStore.synonymImportError" class="import-error-alert">
          <div class="banner-heading">
            <OctagonAlert :size="16" aria-hidden="true" />
            <span>Import Error</span>
          </div>
          <p class="banner-body">
            {{ codingStore.synonymImportError }}
          </p>
        </div>

        <!-- Pre-flight Validation Summary Report -->
        <div v-if="report" class="preflight-report-card section-card">
          <div class="report-header-row">
            <h3 class="section-heading">
              3. Pre-flight Validation Summary Report
            </h3>
            <span
              class="preflight-status-pill"
              :class="report.canImport ? 'pill-passed' : 'pill-failed'"
            >
              <CheckCircle2
                v-if="report.canImport"
                :size="14"
                aria-hidden="true"
              />
              <XCircle v-else :size="14" aria-hidden="true" />
              <span>{{
                report.canImport ? "Pre-flight Passed" : "Validation Failed"
              }}</span>
            </span>
          </div>

          <!-- Metrics Badges Grid -->
          <div class="report-metrics-grid">
            <div class="report-metric metric-neutral">
              <div class="report-metric-value">{{ report.totalRows }}</div>
              <div class="report-metric-label">Total Rows</div>
            </div>
            <div class="report-metric metric-valid">
              <div class="report-metric-value">{{ report.validCount }}</div>
              <div class="report-metric-label">Valid Rows</div>
            </div>
            <div class="report-metric metric-dup">
              <div class="report-metric-value">{{ report.duplicateCount }}</div>
              <div class="report-metric-label">Duplicates</div>
            </div>
            <div class="report-metric metric-invalid">
              <div class="report-metric-value">{{ report.invalidCount }}</div>
              <div class="report-metric-label">Invalid Rows</div>
            </div>
          </div>

          <!-- Invalid Rows Error Table -->
          <div
            v-if="report.invalidRows && report.invalidRows.length > 0"
            class="invalid-breakdown"
          >
            <div class="invalid-breakdown-title">
              Invalid Row Breakdown ({{ report.invalidRows.length }} rows
              require correction):
            </div>
            <div class="table-scroll-box">
              <table class="synonym-table stacked-mobile-table">
                <thead>
                  <tr>
                    <th>Row #</th>
                    <th>Verbatim</th>
                    <th>Dict</th>
                    <th>Error Detail</th>
                  </tr>
                </thead>
                <tbody>
                  <tr
                    v-for="errRow in report.invalidRows"
                    :key="errRow.rowNumber"
                  >
                    <td data-label="Row #" class="mono-cell">
                      #{{ errRow.rowNumber }}
                    </td>
                    <td data-label="Verbatim" class="strong-cell">
                      {{ errRow.verbatimText || "—" }}
                    </td>
                    <td data-label="Dict">
                      {{ errRow.dictionaryType || "—" }}
                    </td>
                    <td data-label="Error Detail" class="error-cell">
                      {{ errRow.errorReason }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>

          <!-- 21 CFR Part 11 Reason For Change Input & Confirm Button -->
          <div class="confirm-section">
            <div class="field-group">
              <label for="synonym-reason-for-change" class="field-label">
                21 CFR Part 11 Reason for Change / Ingestion Justification
                <span class="required-mark">*</span>
              </label>
              <input
                id="synonym-reason-for-change"
                v-model="reasonForChange"
                type="text"
                placeholder="e.g. Ingesting MedDRA 26.0 protocol verbatim synonym dictionary mappings"
                class="form-control reason-for-change-input"
                :disabled="!isAuthorized || codingStore.synonymImportLoading"
              />
            </div>

            <div class="confirm-actions">
              <button
                type="button"
                class="btn btn-secondary"
                @click="clearSelectedFile"
              >
                Cancel
              </button>
              <button
                type="button"
                class="confirm-import-btn btn btn-success"
                :disabled="
                  !report.canImport ||
                  !reasonForChange.trim() ||
                  !isAuthorized ||
                  codingStore.synonymImportLoading
                "
                @click="executeImport"
              >
                <CheckCircle2 :size="15" aria-hidden="true" />
                <span>{{
                  codingStore.synonymImportLoading
                    ? "Ingesting Synonym Rules..."
                    : `Confirm & Ingest ${report.validCount} Valid Rules`
                }}</span>
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- TAB 2: AUDIT HISTORY -->
      <div v-else-if="activeTab === 'history'" class="tab-panel">
        <div class="panel-toolbar-row">
          <h3 class="section-heading">Bulk Synonym Import Job Ledger</h3>
          <button
            type="button"
            class="refresh-btn"
            @click="
              codingStore.fetchSynonymImportJobs(
                scope === 'STUDY' ? studyId : null
              )
            "
          >
            <RefreshCw :size="14" aria-hidden="true" />
            <span>Refresh</span>
          </button>
        </div>

        <div
          v-if="codingStore.synonymImportJobs.length === 0"
          class="empty-ledger-card"
        >
          No previous bulk synonym import jobs recorded for this scope.
        </div>

        <div v-else class="table-scroll-box">
          <table class="synonym-table stacked-mobile-table">
            <thead>
              <tr>
                <th>File / Hash</th>
                <th>Scope</th>
                <th>Metrics</th>
                <th>Status</th>
                <th>Created By / Date</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="job in codingStore.synonymImportJobs" :key="job.id">
                <td data-label="File / Hash">
                  <div class="strong-cell">{{ job.fileName }}</div>
                  <div class="hash-subtext">{{ job.fileHash }}</div>
                </td>
                <td data-label="Scope">
                  <span
                    class="scope-badge"
                    :class="
                      job.scope === 'ENTERPRISE'
                        ? 'scope-enterprise'
                        : 'scope-study'
                    "
                  >
                    {{ job.scope }}{{ job.studyId ? ` (${job.studyId})` : "" }}
                  </span>
                </td>
                <td data-label="Metrics">
                  <div class="job-metrics-inline">
                    <span class="text-valid">{{ job.validRows }} valid</span> /
                    <span class="text-dup">{{ job.duplicateRows }} dup</span> /
                    <span class="text-invalid">{{ job.invalidRows }} err</span>
                  </div>
                  <div class="subtle-meta">Total: {{ job.totalRows }} rows</div>
                </td>
                <td data-label="Status">
                  <span
                    class="preflight-status-pill"
                    :class="
                      job.status === 'COMPLETED' ? 'pill-passed' : 'pill-failed'
                    "
                  >
                    {{ job.status }}
                  </span>
                </td>
                <td data-label="Created By / Date">
                  <div>{{ job.createdBy }}</div>
                  <div class="subtle-meta">{{ formatDate(job.createdAt) }}</div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- TAB 3: ACTIVE RULES -->
      <div v-else-if="activeTab === 'rules'" class="tab-panel">
        <div class="panel-toolbar-row">
          <h3 class="section-heading">Active Synonym Rules Store</h3>
          <button
            type="button"
            class="refresh-btn"
            @click="codingStore.fetchActiveSynonymRules(studyId)"
          >
            <RefreshCw :size="14" aria-hidden="true" />
            <span>Refresh Rules</span>
          </button>
        </div>

        <div
          v-if="codingStore.synonymRules.length === 0"
          class="empty-ledger-card"
        >
          No active synonym rules configured for this study or enterprise scope.
        </div>

        <div v-else class="table-scroll-box">
          <table class="synonym-table stacked-mobile-table">
            <thead>
              <tr>
                <th>Verbatim</th>
                <th>Target Term &amp; Code</th>
                <th>Scope</th>
                <th>Dict</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="rule in codingStore.synonymRules" :key="rule.id">
                <td data-label="Verbatim" class="strong-cell">
                  {{ rule.verbatim_text || rule.verbatimText }}
                </td>
                <td data-label="Target Term & Code">
                  <div class="strong-cell">
                    {{ rule.target_term || rule.targetTerm }}
                  </div>
                  <div class="mono-cell subtle-meta">
                    {{ rule.target_code || rule.targetCode }}
                    {{
                      rule.target_level || rule.targetLevel
                        ? `(${rule.target_level || rule.targetLevel})`
                        : ""
                    }}
                  </div>
                </td>
                <td data-label="Scope">
                  <span
                    class="scope-badge"
                    :class="
                      (rule.scope || '').toUpperCase() === 'ENTERPRISE'
                        ? 'scope-enterprise'
                        : 'scope-study'
                    "
                  >
                    {{ rule.scope
                    }}{{
                      rule.study_id || rule.studyId
                        ? ` (${rule.study_id || rule.studyId})`
                        : ""
                    }}
                  </span>
                </td>
                <td data-label="Dict">
                  {{ rule.dictionary_type || rule.dictionaryType }}
                  {{ rule.dictionary_version || rule.dictionaryVersion }}
                </td>
                <td data-label="Action">
                  <button
                    type="button"
                    class="deactivate-rule-btn"
                    :disabled="
                      !isAuthorized || codingStore.synonymImportLoading
                    "
                    @click="deactivateRule(rule)"
                  >
                    Deactivate
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>
  </ClinicalDrawer>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { ClinicalDrawer, ClinicalDropzone } from "ui";
import {
  AlertTriangle,
  CheckCircle2,
  FileSpreadsheet,
  FolderDown,
  FolderInput,
  History,
  OctagonAlert,
  RefreshCw,
  Search,
  UploadCloud,
  X,
  XCircle,
  Zap,
} from "lucide-vue-next";
import { useConfirmDialog } from "../composables/useConfirmDialog.js";
import { useCodingStore } from "../stores/coding.js";
import { useAuthStore } from "../stores/auth.js";
import { useToastStore } from "../stores/toasts.js";

const codingStore = useCodingStore();
const authStore = useAuthStore();
const toastStore = useToastStore();
const { promptDialog } = useConfirmDialog();

const activeTab = ref("import"); // 'import' | 'history' | 'rules'
const scope = ref("STUDY");
const studyId = ref("STUDY-101");
const selectedFile = ref(null);
const reasonForChange = ref("");

const isAuthorized = computed(() => {
  if (authStore.isDemoMode) return true;
  return (
    authStore.hasRole("data_manager") ||
    authStore.hasRole("sponsor_designer") ||
    authStore.hasRole("sponsor_admin")
  );
});

const report = computed(() => codingStore.synonymPreflightReport);

function close() {
  codingStore.closeSynonymDrawer();
}

function onSynonymFileSelected(file) {
  if (!isAuthorized.value || codingStore.synonymImportLoading || !file) return;
  selectedFile.value = file;
  codingStore.synonymPreflightReport = null;
}

function clearSelectedFile() {
  selectedFile.value = null;
  codingStore.synonymPreflightReport = null;
}

async function runPreflightValidation() {
  if (!selectedFile.value) return;
  try {
    await codingStore.validateSynonymFile({
      file: selectedFile.value,
      scope: scope.value,
      studyId: scope.value === "STUDY" ? studyId.value : null,
    });
  } catch {
    // Error captured in store
  }
}

async function executeImport() {
  if (!selectedFile.value || !reasonForChange.value.trim()) return;
  try {
    await codingStore.importSynonymRules({
      file: selectedFile.value,
      scope: scope.value,
      studyId: scope.value === "STUDY" ? studyId.value : null,
      reasonForChange: reasonForChange.value.trim(),
    });

    clearSelectedFile();
    reasonForChange.value = "";
    activeTab.value = "rules";
  } catch {
    // Error captured in store
  }
}

async function switchToHistoryTab() {
  activeTab.value = "history";
  await codingStore.fetchSynonymImportJobs(
    scope.value === "STUDY" ? studyId.value : null
  );
}

async function switchToRulesTab() {
  activeTab.value = "rules";
  await codingStore.fetchActiveSynonymRules(studyId.value);
}

async function deactivateRule(rule) {
  const ruleId = rule.id;
  const verb = rule.verbatim_text || rule.verbatimText || "rule";
  const reason = await promptDialog({
    title: "Deactivate Synonym Rule",
    message: `Provide a 21 CFR Part 11 reason for deactivating synonym rule '${verb}'.`,
    inputLabel: `21 CFR Part 11 Reason for Deactivating Synonym Rule '${verb}'`,
    placeholder: "Enter justification for deactivating this synonym rule",
    confirmLabel: "Deactivate Rule",
    cancelLabel: "Cancel",
    variant: "danger",
  });
  if (!reason || !reason.trim()) return;

  try {
    await codingStore.deactivateSynonymRule(
      ruleId,
      reason.trim(),
      studyId.value
    );
    toastStore.push({
      tone: "success",
      title: "Synonym Rule Deactivated",
      message: `Synonym rule '${verb}' was deactivated.`,
    });
  } catch {
    // Error captured in store
  }
}

function formatFileSize(bytes) {
  if (!bytes) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
}

function formatDate(dateStr) {
  if (!dateStr) return "—";
  try {
    return new Date(dateStr).toLocaleString();
  } catch {
    return dateStr;
  }
}

onMounted(() => {
  codingStore.fetchActiveSynonymRules(studyId.value);
});
</script>

<style scoped>
.drawer-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  width: 100%;
  min-width: 0;
}

.header-title-wrap {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 0;
  flex: 1 1 auto;
}

.header-icon {
  flex-shrink: 0;
  color: var(--semantic-color-primary);
}

.drawer-title {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}

.close-btn {
  background: transparent;
  border: 1px solid transparent;
  border-radius: 6px;
  color: var(--semantic-color-text-secondary);
  min-width: 44px;
  min-height: 44px;
  padding: 0;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
}

.close-btn:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
}

.close-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

@media (max-height: 540px) {
  .close-btn {
    min-width: 36px;
    min-height: 36px;
  }
}

.synonym-drawer-content {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
  font-variant-numeric: tabular-nums;
}

.drawer-subtitle {
  margin: 0;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
}

.synonym-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 8px;
}

.synonym-tab-btn {
  flex: 1 1 140px;
  min-height: 44px;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-size: 0.8125rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  transition: all 0.15s ease;
}

.synonym-tab-btn.active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-inverse);
}

.access-restricted-banner,
.import-error-alert {
  border-radius: 8px;
  padding: 12px 14px;
  font-size: 0.8125rem;
}

.access-restricted-banner {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  color: var(--semantic-color-warning-dark);
}

.import-error-alert {
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
}

.banner-heading {
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 6px;
  margin-bottom: 4px;
}

.banner-body {
  margin: 0;
  line-height: 1.45;
}

.tab-panel {
  display: flex;
  flex-direction: column;
  gap: 16px;
  min-width: 0;
}

.section-card {
  border: 1px solid var(--semantic-color-border);
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  min-width: 0;
}

.section-heading {
  margin: 0;
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--semantic-color-text-secondary);
}

.config-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.field-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

.field-label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.required-mark {
  color: var(--semantic-color-error);
}

.form-control {
  width: 100%;
  min-height: 44px;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-size: 1rem;
  box-sizing: border-box;
}

.dropzone {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 18px;
  text-align: center;
  background-color: var(--semantic-color-surface-muted);
}

.dropzone-prompt {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 6px;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-body);
}

.dropzone-hint {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  word-break: break-word;
}

.selected-file-summary {
  display: inline-flex;
  align-items: center;
  gap: 8px;
  text-align: left;
}

.selected-file-name {
  font-size: 0.8125rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  word-break: break-all;
}

.selected-file-size {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.card-actions-end {
  display: flex;
  justify-content: flex-end;
}

.btn {
  min-height: 44px;
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 0.8125rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  border: 1px solid transparent;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-inverse);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.btn-success {
  background-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.report-header-row,
.panel-toolbar-row {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  flex-wrap: wrap;
}

.preflight-status-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  border-radius: 999px;
  padding: 4px 10px;
  font-size: 0.75rem;
  font-weight: 700;
}

.pill-passed {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.pill-failed {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.report-metrics-grid {
  display: grid;
  grid-template-columns: repeat(4, minmax(0, 1fr));
  gap: 10px;
  text-align: center;
}

.report-metric {
  border-radius: 8px;
  padding: 10px;
  border: 1px solid var(--semantic-color-border);
}

.metric-neutral {
  background-color: var(--semantic-color-surface-muted);
}

.metric-valid {
  background-color: var(--semantic-color-success-bg-soft);
}

.metric-dup {
  background-color: var(--semantic-color-warning-bg-soft);
}

.metric-invalid {
  background-color: var(--semantic-color-error-bg-soft);
}

.report-metric-value {
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.report-metric-label {
  font-size: 0.6875rem;
  text-transform: uppercase;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.invalid-breakdown {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.invalid-breakdown-title {
  font-size: 0.8125rem;
  font-weight: 700;
  color: var(--semantic-color-error-text-dark);
}

.table-scroll-box {
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  overflow-x: auto;
  max-width: 100%;
}

.synonym-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
  text-align: left;
}

.synonym-table th,
.synonym-table td {
  padding: 8px 10px;
  border-bottom: 1px solid var(--semantic-color-border);
  vertical-align: top;
}

.synonym-table th {
  background-color: var(--semantic-color-surface-muted);
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.mono-cell {
  font-family: var(--font-mono, monospace);
}

.strong-cell {
  font-weight: 700;
  color: var(--semantic-color-text);
  word-break: break-word;
}

.error-cell {
  color: var(--semantic-color-error-text-dark);
}

.hash-subtext {
  font-family: var(--font-mono, monospace);
  font-size: 0.6875rem;
  color: var(--semantic-color-text-secondary);
  max-width: 160px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.scope-badge {
  display: inline-block;
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.6875rem;
  font-weight: 700;
}

.scope-enterprise {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
}

.scope-study {
  background-color: var(--semantic-color-info-bg);
  color: var(--semantic-color-primary);
}

.job-metrics-inline {
  font-size: 0.75rem;
}

.text-valid {
  font-weight: 700;
  color: var(--semantic-color-success-dark);
}

.text-dup {
  color: var(--semantic-color-warning-dark);
}

.text-invalid {
  color: var(--semantic-color-error-text-dark);
}

.subtle-meta {
  font-size: 0.6875rem;
  color: var(--semantic-color-text-secondary);
}

.confirm-section {
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.confirm-actions {
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  flex-wrap: wrap;
}

.refresh-btn,
.deactivate-rule-btn {
  min-height: 44px;
  padding: 6px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border-control);
  background-color: var(--semantic-color-surface);
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.refresh-btn {
  color: var(--semantic-color-primary);
}

.deactivate-rule-btn {
  color: var(--semantic-color-error);
}

.deactivate-rule-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.empty-ledger-card {
  border: 1px dashed var(--semantic-color-border-control);
  background-color: var(--semantic-color-surface-muted);
  border-radius: 8px;
  padding: 24px 16px;
  text-align: center;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-secondary);
}

@media (max-width: 640px) {
  .config-grid {
    grid-template-columns: 1fr;
  }

  .report-metrics-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  .confirm-actions,
  .card-actions-end {
    flex-direction: column;
    align-items: stretch;
  }

  .confirm-actions .btn,
  .card-actions-end .btn {
    width: 100%;
  }

  .stacked-mobile-table,
  .stacked-mobile-table tbody,
  .stacked-mobile-table tr,
  .stacked-mobile-table td {
    display: block;
    width: 100%;
    box-sizing: border-box;
  }

  .stacked-mobile-table thead {
    display: none;
  }

  .stacked-mobile-table tr {
    padding: 10px;
    border-bottom: 1px solid var(--semantic-color-border);
  }

  .stacked-mobile-table td {
    border-bottom: none;
    padding: 4px 0;
  }

  .stacked-mobile-table td::before {
    content: attr(data-label) ": ";
    font-weight: 700;
    color: var(--semantic-color-text-secondary);
  }
}
</style>
