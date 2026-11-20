<template>
  <div
    v-if="codingStore.isSynonymDrawerOpen"
    class="synonym-drawer-overlay fixed inset-0 z-50 flex justify-end bg-slate-900/60 backdrop-blur-xs transition-opacity"
    role="presentation"
    @click.self="close"
  >
    <div
      ref="drawerRef"
      class="synonym-drawer-panel flex h-full w-full max-w-2xl flex-col bg-white shadow-2xl dark:bg-slate-900"
      role="dialog"
      aria-modal="true"
      aria-labelledby="synonym-drawer-title"
      tabindex="-1"
    >
      <!-- Header -->
      <div
        class="flex items-center justify-between border-b border-slate-200 px-6 py-4 dark:border-slate-800"
      >
        <div>
          <h2
            id="synonym-drawer-title"
            class="text-lg font-bold text-slate-900 dark:text-slate-100 flex items-center gap-2"
          >
            <span>📥</span> Bulk Synonym Dictionary Import
          </h2>
          <p class="text-xs text-slate-500 dark:text-slate-400 mt-0.5">
            Ingest MedDRA and WHODrug verbatim synonym dictionaries with
            pre-flight validation and Part 11 audit lineage.
          </p>
        </div>
        <button
          type="button"
          aria-label="Close synonym import drawer"
          class="close-btn rounded-lg p-1.5 text-slate-400 hover:bg-slate-100 hover:text-slate-600 dark:hover:bg-slate-800 dark:hover:text-slate-200"
          @click="close"
        >
          ✕
        </button>
      </div>

      <!-- Navigation Tabs -->
      <div
        class="flex border-b border-slate-200 px-6 bg-slate-50 dark:border-slate-800 dark:bg-slate-900/50"
      >
        <button
          type="button"
          class="border-b-2 px-4 py-3 text-xs font-semibold transition-colors"
          :class="
            activeTab === 'import'
              ? 'border-indigo-600 text-indigo-600 dark:border-indigo-400 dark:text-indigo-400'
              : 'border-transparent text-slate-500 hover:text-slate-700 dark:text-slate-400'
          "
          @click="activeTab = 'import'"
        >
          📁 Import New Rules
        </button>
        <button
          type="button"
          class="border-b-2 px-4 py-3 text-xs font-semibold transition-colors"
          :class="
            activeTab === 'history'
              ? 'border-indigo-600 text-indigo-600 dark:border-indigo-400 dark:text-indigo-400'
              : 'border-transparent text-slate-500 hover:text-slate-700 dark:text-slate-400'
          "
          @click="switchToHistoryTab"
        >
          📜 Audit History
        </button>
        <button
          type="button"
          class="border-b-2 px-4 py-3 text-xs font-semibold transition-colors"
          :class="
            activeTab === 'rules'
              ? 'border-indigo-600 text-indigo-600 dark:border-indigo-400 dark:text-indigo-400'
              : 'border-transparent text-slate-500 hover:text-slate-700 dark:text-slate-400'
          "
          @click="switchToRulesTab"
        >
          ⚡ Active Rules ({{ codingStore.synonymRules.length }})
        </button>
      </div>

      <!-- Drawer Body -->
      <div class="flex-1 overflow-y-auto p-6 space-y-6">
        <!-- Access Restricted Banner -->
        <div
          v-if="!isAuthorized"
          class="access-restricted-banner rounded-lg border border-amber-200 bg-amber-50 p-4 text-xs text-amber-800 dark:border-amber-900/50 dark:bg-amber-950/40 dark:text-amber-300"
        >
          <div class="font-bold flex items-center gap-1.5 text-sm">
            <span>⚠️</span> Access Restricted
          </div>
          <p class="mt-1">
            Bulk synonym dictionary import requires
            <strong>DATA_MANAGER</strong> or
            <strong>SPONSOR_DESIGNER</strong> authorization. You can view
            existing history and active rules in read-only mode.
          </p>
        </div>

        <!-- TAB 1: IMPORT NEW RULES -->
        <div v-if="activeTab === 'import'" class="space-y-6">
          <!-- Configuration Card -->
          <div
            class="rounded-xl border border-slate-200 bg-white p-4 shadow-xs dark:border-slate-800 dark:bg-slate-900 space-y-4"
          >
            <h3
              class="text-xs font-bold uppercase tracking-wider text-slate-500"
            >
              1. Scope & Target Configuration
            </h3>

            <div class="grid grid-cols-2 gap-4">
              <div>
                <label
                  for="synonym-scope"
                  class="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1"
                >
                  Import Scope
                </label>
                <select
                  id="synonym-scope"
                  v-model="scope"
                  class="scope-select w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs font-medium text-slate-900 focus:border-indigo-500 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
                  :disabled="!isAuthorized || codingStore.synonymImportLoading"
                >
                  <option value="STUDY">
                    STUDY (Study-Specific Rule Store)
                  </option>
                  <option value="ENTERPRISE">
                    ENTERPRISE (Cross-Study Master Rule Store)
                  </option>
                </select>
              </div>

              <div v-if="scope === 'STUDY'">
                <label
                  for="synonym-study-id"
                  class="block text-xs font-medium text-slate-700 dark:text-slate-300 mb-1"
                >
                  Study ID
                </label>
                <input
                  id="synonym-study-id"
                  v-model="studyId"
                  type="text"
                  placeholder="e.g. STUDY-101"
                  class="study-id-input w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs font-medium text-slate-900 focus:border-indigo-500 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
                  :disabled="!isAuthorized || codingStore.synonymImportLoading"
                />
              </div>
            </div>
          </div>

          <!-- File Dropzone -->
          <div
            class="rounded-xl border border-slate-200 bg-white p-4 shadow-xs dark:border-slate-800 dark:bg-slate-900 space-y-4"
          >
            <h3
              class="text-xs font-bold uppercase tracking-wider text-slate-500"
            >
              2. Select Synonym Dictionary File
            </h3>

            <div
              class="dropzone border-2 border-dashed rounded-xl p-6 text-center transition-colors cursor-pointer"
              :class="[
                isDragging
                  ? 'border-indigo-500 bg-indigo-50/50 dark:bg-indigo-950/20'
                  : selectedFile
                    ? 'border-emerald-400 bg-emerald-50/30 dark:bg-emerald-950/10'
                    : 'border-slate-300 hover:border-slate-400 dark:border-slate-700',
                !isAuthorized || codingStore.synonymImportLoading
                  ? 'opacity-60 cursor-not-allowed'
                  : '',
              ]"
              @dragover.prevent="isDragging = true"
              @dragleave.prevent="isDragging = false"
              @drop.prevent="handleFileDrop"
              role="button"
              tabindex="0"
              aria-label="Select synonym dictionary file"
              @click="triggerFileInput"
              @keydown.enter.prevent="triggerFileInput"
              @keydown.space.prevent="triggerFileInput"
            >
              <input
                ref="fileInputRef"
                type="file"
                aria-label="Synonym dictionary file"
                accept=".csv,.xlsx"
                class="hidden"
                :disabled="!isAuthorized || codingStore.synonymImportLoading"
                @change="handleFileSelect"
              />

              <div v-if="selectedFile" class="space-y-2">
                <div class="text-2xl">📄</div>
                <div
                  class="text-xs font-bold text-slate-800 dark:text-slate-200"
                >
                  {{ selectedFile.name }}
                </div>
                <div class="text-[11px] text-slate-500">
                  {{ formatFileSize(selectedFile.size) }}
                </div>
                <button
                  type="button"
                  class="text-xs text-rose-600 hover:underline dark:text-rose-400 mt-2"
                  @click.stop="clearSelectedFile"
                >
                  Remove File
                </button>
              </div>

              <div v-else class="space-y-2">
                <div class="text-2xl text-slate-400">📤</div>
                <div
                  class="text-xs font-medium text-slate-700 dark:text-slate-300"
                >
                  Drag and drop a <strong>.csv</strong> or
                  <strong>.xlsx</strong> synonym dictionary file here
                </div>
                <div class="text-[11px] text-slate-400">
                  Must include headers: <code>verbatim_text</code>,
                  <code>dictionary_type</code>, <code>dictionary_version</code>,
                  <code>target_code</code>, <code>target_term</code>
                </div>
              </div>
            </div>

            <div class="flex justify-end">
              <button
                type="button"
                class="validate-btn btn rounded-lg bg-indigo-600 px-4 py-2 text-xs font-semibold text-white shadow-xs hover:bg-indigo-700 disabled:opacity-50"
                :disabled="
                  !selectedFile ||
                  !isAuthorized ||
                  codingStore.synonymImportLoading
                "
                @click="runPreflightValidation"
              >
                {{
                  codingStore.synonymImportLoading
                    ? "Validating File..."
                    : "🔍 Run Pre-flight Validation"
                }}
              </button>
            </div>
          </div>

          <!-- Error Alert -->
          <div
            v-if="codingStore.synonymImportError"
            class="rounded-lg border border-rose-200 bg-rose-50 p-4 text-xs text-rose-800 dark:border-rose-900/50 dark:bg-rose-950/40 dark:text-rose-300"
          >
            <div class="font-bold flex items-center gap-1.5">
              <span>🛑</span> Import Error
            </div>
            <p class="mt-1">{{ codingStore.synonymImportError }}</p>
          </div>

          <!-- Pre-flight Validation Summary Report -->
          <div
            v-if="report"
            class="preflight-report-card rounded-xl border border-slate-200 bg-white p-4 shadow-xs dark:border-slate-800 dark:bg-slate-900 space-y-4"
          >
            <div class="flex items-center justify-between">
              <h3
                class="text-xs font-bold uppercase tracking-wider text-slate-500"
              >
                3. Pre-flight Validation Summary Report
              </h3>
              <span
                class="inline-flex items-center gap-1 rounded-full px-2.5 py-1 text-[11px] font-bold"
                :class="
                  report.canImport
                    ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300'
                    : 'bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300'
                "
              >
                {{
                  report.canImport
                    ? "✅ Pre-flight Passed"
                    : "❌ Validation Failed"
                }}
              </span>
            </div>

            <!-- Metrics Badges Grid -->
            <div class="grid grid-cols-4 gap-3 text-center">
              <div class="rounded-lg bg-slate-50 p-3 dark:bg-slate-800/60">
                <div
                  class="text-lg font-bold text-slate-900 dark:text-slate-100"
                >
                  {{ report.totalRows }}
                </div>
                <div class="text-[10px] uppercase font-semibold text-slate-500">
                  Total Rows
                </div>
              </div>
              <div class="rounded-lg bg-emerald-50 p-3 dark:bg-emerald-950/30">
                <div
                  class="text-lg font-bold text-emerald-700 dark:text-emerald-400"
                >
                  {{ report.validCount }}
                </div>
                <div
                  class="text-[10px] uppercase font-semibold text-emerald-600"
                >
                  Valid Rows
                </div>
              </div>
              <div class="rounded-lg bg-amber-50 p-3 dark:bg-amber-950/30">
                <div
                  class="text-lg font-bold text-amber-700 dark:text-amber-400"
                >
                  {{ report.duplicateCount }}
                </div>
                <div class="text-[10px] uppercase font-semibold text-amber-600">
                  Duplicates
                </div>
              </div>
              <div class="rounded-lg bg-rose-50 p-3 dark:bg-rose-950/30">
                <div class="text-lg font-bold text-rose-700 dark:text-rose-400">
                  {{ report.invalidCount }}
                </div>
                <div class="text-[10px] uppercase font-semibold text-rose-600">
                  Invalid Rows
                </div>
              </div>
            </div>

            <!-- Invalid Rows Error Table -->
            <div
              v-if="report.invalidRows && report.invalidRows.length > 0"
              class="space-y-2"
            >
              <div class="text-xs font-bold text-rose-700 dark:text-rose-400">
                Invalid Row Breakdown ({{ report.invalidRows.length }} rows
                require correction):
              </div>
              <div
                class="max-h-48 overflow-y-auto rounded-lg border border-slate-200 dark:border-slate-800"
              >
                <table class="w-full text-left text-[11px]">
                  <thead
                    class="bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 font-semibold"
                  >
                    <tr>
                      <th class="px-3 py-1.5">Row #</th>
                      <th class="px-3 py-1.5">Verbatim</th>
                      <th class="px-3 py-1.5">Dict</th>
                      <th class="px-3 py-1.5">Error Detail</th>
                    </tr>
                  </thead>
                  <tbody
                    class="divide-y divide-slate-100 dark:divide-slate-800"
                  >
                    <tr
                      v-for="errRow in report.invalidRows"
                      :key="errRow.rowNumber"
                      class="hover:bg-slate-50 dark:hover:bg-slate-800/40"
                    >
                      <td
                        class="px-3 py-1.5 font-mono font-bold text-slate-600 dark:text-slate-400"
                      >
                        #{{ errRow.rowNumber }}
                      </td>
                      <td
                        class="px-3 py-1.5 font-medium text-slate-800 dark:text-slate-200"
                      >
                        {{ errRow.verbatimText || "—" }}
                      </td>
                      <td
                        class="px-3 py-1.5 text-slate-600 dark:text-slate-400"
                      >
                        {{ errRow.dictionaryType || "—" }}
                      </td>
                      <td class="px-3 py-1.5 text-rose-600 dark:text-rose-400">
                        {{ errRow.errorReason }}
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            <!-- 21 CFR Part 11 Reason For Change Input & Confirm Button -->
            <div
              class="pt-2 border-t border-slate-200 dark:border-slate-800 space-y-3"
            >
              <div>
                <label
                  for="synonym-reason-for-change"
                  class="block text-xs font-bold text-slate-700 dark:text-slate-300 mb-1"
                >
                  21 CFR Part 11 Reason for Change / Ingestion Justification
                  <span class="text-rose-500">*</span>
                </label>
                <input
                  id="synonym-reason-for-change"
                  v-model="reasonForChange"
                  type="text"
                  placeholder="e.g. Ingesting MedDRA 26.0 protocol verbatim synonym dictionary mappings"
                  class="reason-for-change-input w-full rounded-lg border border-slate-300 bg-white px-3 py-2 text-xs text-slate-900 focus:border-indigo-500 focus:outline-none dark:border-slate-700 dark:bg-slate-800 dark:text-slate-100"
                  :disabled="!isAuthorized || codingStore.synonymImportLoading"
                />
              </div>

              <div class="flex justify-end gap-2">
                <button
                  type="button"
                  class="btn rounded-lg border border-slate-300 px-4 py-2 text-xs font-semibold text-slate-700 hover:bg-slate-50 dark:border-slate-700 dark:text-slate-300 dark:hover:bg-slate-800"
                  @click="clearSelectedFile"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  class="confirm-import-btn btn rounded-lg bg-emerald-600 px-5 py-2 text-xs font-bold text-white shadow-xs hover:bg-emerald-700 disabled:opacity-50"
                  :disabled="
                    !report.canImport ||
                    !reasonForChange.trim() ||
                    !isAuthorized ||
                    codingStore.synonymImportLoading
                  "
                  @click="executeImport"
                >
                  {{
                    codingStore.synonymImportLoading
                      ? "Ingesting Synonym Rules..."
                      : `🚀 Confirm & Ingest ${report.validCount} Valid Rules`
                  }}
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- TAB 2: AUDIT HISTORY -->
        <div v-else-if="activeTab === 'history'" class="space-y-4">
          <div class="flex items-center justify-between">
            <h3
              class="text-xs font-bold uppercase tracking-wider text-slate-500"
            >
              Bulk Synonym Import Job Ledger
            </h3>
            <button
              type="button"
              class="text-xs text-indigo-600 hover:underline dark:text-indigo-400"
              @click="
                codingStore.fetchSynonymImportJobs(
                  scope === 'STUDY' ? studyId : null
                )
              "
            >
              🔄 Refresh
            </button>
          </div>

          <div
            v-if="codingStore.synonymImportJobs.length === 0"
            class="rounded-xl border border-slate-200 bg-slate-50 p-8 text-center text-xs text-slate-500 dark:border-slate-800 dark:bg-slate-900/50"
          >
            No previous bulk synonym import jobs recorded for this scope.
          </div>

          <div
            v-else
            class="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800"
          >
            <table class="w-full text-left text-xs">
              <thead
                class="bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 font-semibold"
              >
                <tr>
                  <th class="px-3 py-2">File / Hash</th>
                  <th class="px-3 py-2">Scope</th>
                  <th class="px-3 py-2">Metrics</th>
                  <th class="px-3 py-2">Status</th>
                  <th class="px-3 py-2">Created By / Date</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-200 dark:divide-slate-800">
                <tr
                  v-for="job in codingStore.synonymImportJobs"
                  :key="job.id"
                  class="hover:bg-slate-50 dark:hover:bg-slate-800/40"
                >
                  <td class="px-3 py-2.5">
                    <div class="font-bold text-slate-900 dark:text-slate-100">
                      {{ job.fileName }}
                    </div>
                    <div
                      class="font-mono text-[10px] text-slate-400 truncate max-w-[160px]"
                    >
                      {{ job.fileHash }}
                    </div>
                  </td>
                  <td class="px-3 py-2.5">
                    <span
                      class="inline-block rounded-xs px-1.5 py-0.5 text-[10px] font-bold"
                      :class="
                        job.scope === 'ENTERPRISE'
                          ? 'bg-purple-100 text-purple-800 dark:bg-purple-950/60 dark:text-purple-300'
                          : 'bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300'
                      "
                    >
                      {{ job.scope
                      }}{{ job.studyId ? ` (${job.studyId})` : "" }}
                    </span>
                  </td>
                  <td class="px-3 py-2.5">
                    <div class="text-[11px]">
                      <span class="font-bold text-emerald-600"
                        >{{ job.validRows }} valid</span
                      >
                      /
                      <span class="text-amber-600"
                        >{{ job.duplicateRows }} dup</span
                      >
                      /
                      <span class="text-rose-600"
                        >{{ job.invalidRows }} err</span
                      >
                    </div>
                    <div class="text-[10px] text-slate-400">
                      Total: {{ job.totalRows }} rows
                    </div>
                  </td>
                  <td class="px-3 py-2.5">
                    <span
                      class="inline-block rounded-full px-2 py-0.5 text-[10px] font-bold"
                      :class="
                        job.status === 'COMPLETED'
                          ? 'bg-emerald-100 text-emerald-800 dark:bg-emerald-950/60 dark:text-emerald-300'
                          : 'bg-rose-100 text-rose-800 dark:bg-rose-950/60 dark:text-rose-300'
                      "
                    >
                      {{ job.status }}
                    </span>
                  </td>
                  <td
                    class="px-3 py-2.5 text-slate-600 dark:text-slate-400 text-[11px]"
                  >
                    <div>{{ job.createdBy }}</div>
                    <div class="text-[10px] text-slate-400">
                      {{ formatDate(job.createdAt) }}
                    </div>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- TAB 3: ACTIVE RULES -->
        <div v-else-if="activeTab === 'rules'" class="space-y-4">
          <div class="flex items-center justify-between">
            <h3
              class="text-xs font-bold uppercase tracking-wider text-slate-500"
            >
              Active Synonym Rules Store
            </h3>
            <button
              type="button"
              class="text-xs text-indigo-600 hover:underline dark:text-indigo-400"
              @click="codingStore.fetchActiveSynonymRules(studyId)"
            >
              🔄 Refresh Rules
            </button>
          </div>

          <div
            v-if="codingStore.synonymRules.length === 0"
            class="rounded-xl border border-slate-200 bg-slate-50 p-8 text-center text-xs text-slate-500 dark:border-slate-800 dark:bg-slate-900/50"
          >
            No active synonym rules configured for this study or enterprise
            scope.
          </div>

          <div
            v-else
            class="overflow-x-auto rounded-xl border border-slate-200 dark:border-slate-800"
          >
            <table class="w-full text-left text-xs">
              <thead
                class="bg-slate-100 dark:bg-slate-800 text-slate-700 dark:text-slate-300 font-semibold"
              >
                <tr>
                  <th class="px-3 py-2">Verbatim</th>
                  <th class="px-3 py-2">Target Term & Code</th>
                  <th class="px-3 py-2">Scope</th>
                  <th class="px-3 py-2">Dict</th>
                  <th class="px-3 py-2 text-right">Action</th>
                </tr>
              </thead>
              <tbody class="divide-y divide-slate-200 dark:divide-slate-800">
                <tr
                  v-for="rule in codingStore.synonymRules"
                  :key="rule.id"
                  class="hover:bg-slate-50 dark:hover:bg-slate-800/40"
                >
                  <td
                    class="px-3 py-2.5 font-bold text-slate-900 dark:text-slate-100"
                  >
                    {{ rule.verbatim_text || rule.verbatimText }}
                  </td>
                  <td class="px-3 py-2.5">
                    <div class="font-medium text-slate-800 dark:text-slate-200">
                      {{ rule.target_term || rule.targetTerm }}
                    </div>
                    <div class="font-mono text-[10px] text-slate-400">
                      {{ rule.target_code || rule.targetCode }}
                      {{
                        rule.target_level || rule.targetLevel
                          ? `(${rule.target_level || rule.targetLevel})`
                          : ""
                      }}
                    </div>
                  </td>
                  <td class="px-3 py-2.5">
                    <span
                      class="inline-block rounded-xs px-1.5 py-0.5 text-[10px] font-bold"
                      :class="
                        (rule.scope || '').toUpperCase() === 'ENTERPRISE'
                          ? 'bg-purple-100 text-purple-800 dark:bg-purple-950/60 dark:text-purple-300'
                          : 'bg-blue-100 text-blue-800 dark:bg-blue-950/60 dark:text-blue-300'
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
                  <td
                    class="px-3 py-2.5 text-slate-600 dark:text-slate-400 text-[11px]"
                  >
                    {{ rule.dictionary_type || rule.dictionaryType }}
                    {{ rule.dictionary_version || rule.dictionaryVersion }}
                  </td>
                  <td class="px-3 py-2.5 text-right">
                    <button
                      type="button"
                      class="deactivate-rule-btn text-xs font-semibold text-rose-600 hover:underline dark:text-rose-400 disabled:opacity-50"
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
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import { useFocusTrap } from "ui";
import { useCodingStore } from "../stores/coding.js";
import { useAuthStore } from "../stores/auth.js";

const codingStore = useCodingStore();
const authStore = useAuthStore();
const drawerRef = ref(null);

const activeTab = ref("import"); // 'import' | 'history' | 'rules'
const scope = ref("STUDY");
const studyId = ref("STUDY-101");
const selectedFile = ref(null);
const isDragging = ref(false);
const fileInputRef = ref(null);
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

useFocusTrap(drawerRef, {
  isOpen: () => codingStore.isSynonymDrawerOpen,
  onEscape: close,
});

function triggerFileInput() {
  if (!isAuthorized.value || codingStore.synonymImportLoading) return;
  fileInputRef.value?.click();
}

function handleFileSelect(event) {
  const files = event.target.files;
  if (files && files.length > 0) {
    selectedFile.value = files[0];
    codingStore.synonymPreflightReport = null;
  }
}

function handleFileDrop(event) {
  isDragging.value = false;
  if (!isAuthorized.value || codingStore.synonymImportLoading) return;
  const files = event.dataTransfer?.files;
  if (files && files.length > 0) {
    selectedFile.value = files[0];
    codingStore.synonymPreflightReport = null;
  }
}

function clearSelectedFile() {
  selectedFile.value = null;
  codingStore.synonymPreflightReport = null;
  if (fileInputRef.value) fileInputRef.value.value = "";
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
  const reason = window.prompt(
    `21 CFR Part 11 Reason for Deactivating Synonym Rule '${verb}':`,
    "Deactivated outdated synonym rule in workbench"
  );
  if (!reason || !reason.trim()) return;

  try {
    await codingStore.deactivateSynonymRule(
      ruleId,
      reason.trim(),
      studyId.value
    );
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
