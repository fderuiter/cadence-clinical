<template>
  <div id="section-rules" class="dashboard-section active">
    <div class="section-header">
      <h2>Interactive Rules Designer</h2>
      <p>
        Declarative visual rule builder supporting skip logic, constraint
        checks, and cross-form edit validations, plus centralized Query
        Life-Cycle management.
      </p>
    </div>

    <!-- Authorization Gating check -->
    <div
      v-if="!hasEditAccess"
      class="card rules-gating-banner"
      style="
        border-left: 4px solid var(--error);
        background-color: var(--error-bg);
        padding: 24px;
      "
    >
      <div
        class="rules-gating-content"
        style="display: flex; gap: 16px; align-items: flex-start"
      >
        <span class="rules-gating-icon" style="font-size: 2rem"
          ><ClinicalIcon name="x-circle" :size="16"
        /></span>
        <div>
          <h3 class="rules-gating-title">
            21 CFR Part 11 Role Gating - Access Denied
          </h3>
          <p class="rules-gating-text">
            You do not have the required <strong>STUDY_DESIGNER</strong> or
            <strong>DATA_MANAGER</strong> role to view or interact with clinical
            rules and queries. Please authenticate with an authorized token or
            consult your system administrator.
          </p>
        </div>
      </div>
    </div>

    <!-- Authorized Rules Panel -->
    <div v-else>
      <!-- Sub-Issue 11 Tab Navigation -->
      <div
        class="tabs-navigation"
        style="
          display: flex;
          flex-wrap: wrap;
          gap: var(--spacing-sm);
          margin-bottom: var(--spacing-lg);
          border-bottom: 2px solid var(--border);
          padding-bottom: 10px;
        "
      >
        <button
          class="btn tab-btn-rules"
          :style="
            activeTab === 'rules'
              ? 'background-color: var(--primary); color: var(--semantic-color-text-on-primary);'
              : 'background-color: var(--semantic-color-surface-header); color: var(--semantic-color-text-secondary);'
          "
          @click="activeTab = 'rules'"
        >
          Rules Designer Workspace
        </button>
        <button
          class="btn tab-btn-queries"
          :style="
            activeTab === 'queries'
              ? 'background-color: var(--primary); color: var(--semantic-color-text-on-primary);'
              : 'background-color: var(--semantic-color-surface-header); color: var(--semantic-color-text-secondary);'
          "
          @click="activeTab = 'queries'"
        >
          Data Manager Query Dashboard
        </button>
      </div>

      <!-- Tab 1: Rules Designer -->
      <div v-if="activeTab === 'rules'" class="grid-2-responsive">
        <!-- Active Ruleset List -->
        <div
          class="card"
          style="display: flex; flex-direction: column; height: fit-content"
        >
          <div
            class="card-title"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              margin-bottom: 16px;
            "
          >
            <span>Study Active Ruleset</span>
            <span
              v-if="loadingRules"
              style="
                font-size: 0.85rem;
                color: var(--semantic-color-text-secondary);
                font-weight: normal;
              "
              >Loading...</span
            >
          </div>

          <!-- Connection Error Banner if any -->
          <div
            v-if="connectionError"
            style="
              background-color: var(--warning-bg);
              border: 1px solid var(--warning);
              color: var(--warning);
              padding: 10px;
              border-radius: 6px;
              margin-bottom: 12px;
              font-size: 0.85rem;
            "
          >
            <strong>API Mode Degraded:</strong> Running in Local Sandbox VM.
            Sync with GxP Server offline.
          </div>

          <div
            style="
              flex: 1;
              max-height: 600px;
              overflow-y: auto;
              margin-bottom: 16px;
              padding-right: 4px;
            "
          >
            <div
              v-if="activeRules.length === 0"
              style="
                color: var(--semantic-color-text-secondary);
                font-style: italic;
                padding: 12px 0;
                text-align: center;
              "
            >
              No active rules configured. Click "Create New Rule" to get
              started.
            </div>
            <div v-else>
              <div
                v-for="rule in activeRules"
                :key="rule.id"
                class="rule-card rule-card-item"
              >
                <div class="rule-card-header">
                  <strong
                    style="
                      color: var(--accent);
                      font-size: 0.95rem;
                      font-family: var(--semantic-font-mono);
                    "
                    >{{ rule.id }}</strong
                  >
                  <div style="display: flex; gap: 6px">
                    <button
                      class="btn"
                      style="
                        padding: 4px 10px;
                        font-size: 0.75rem;
                        cursor: pointer;
                        border: 1px solid var(--border);
                        border-radius: 4px;
                        background: var(--semantic-color-surface);
                        color: var(--text-main);
                      "
                      @click="openRuleEditor(rule)"
                    >
                      Edit
                    </button>
                    <button
                      class="btn"
                      style="
                        padding: 4px 10px;
                        font-size: 0.75rem;
                        background-color: var(
                          --semantic-color-error-interactive
                        );
                        color: var(--semantic-color-text-on-solid);
                        border: none;
                        border-radius: 4px;
                        cursor: pointer;
                      "
                      @click="promptDeleteRule(rule.id)"
                    >
                      Delete
                    </button>
                  </div>
                </div>
                <div
                  style="
                    font-size: 0.85rem;
                    color: var(--neutral-dark);
                    margin-bottom: 8px;
                    line-height: 1.4;
                  "
                >
                  <div style="margin-bottom: 4px">
                    <strong>Type:</strong>
                    <span
                      class="badge"
                      style="
                        background-color: var(--semantic-badge-draft-bg);
                        color: var(--semantic-badge-draft-text);
                        border: 1px solid var(--semantic-badge-draft-border);
                        font-size: var(--font-size-2xs);
                        padding: 2px 6px;
                      "
                      >{{ rule.type }}</span
                    >
                  </div>
                  <div v-if="rule.type === 'skip_logic'">
                    <strong>Action:</strong> {{ rule.action }} field
                    <code>{{ rule.target_field }}</code>
                  </div>
                  <div v-else-if="rule.type === 'constraint'">
                    <strong>Target Field:</strong>
                    <code>{{ rule.target_field }}</code> <br />
                    <strong>Discrepancy Message:</strong>
                    <span style="font-style: italic; color: var(--text-main)"
                      >"{{ rule.query_message }}"</span
                    >
                  </div>
                  <div v-else-if="rule.type === 'cross_form_check'">
                    <strong>Discrepancy Message:</strong>
                    <span style="font-style: italic; color: var(--text-main)"
                      >"{{ rule.query_message }}"</span
                    >
                  </div>
                </div>
                <div
                  style="
                    font-family: var(--semantic-font-mono);
                    font-size: 0.75rem;
                    color: var(--text-main);
                    background-color: var(--semantic-color-info-bg);
                    padding: 6px 10px;
                    border-radius: 4px;
                    border: 1px solid var(--semantic-color-info-border);
                    word-break: break-all;
                  "
                >
                  <strong>XPath:</strong>
                  {{ rule.compiled_xpath || rule.xpath || "(Not compiled)" }}
                </div>
              </div>
            </div>
          </div>

          <div style="display: flex; gap: 8px">
            <button
              class="btn btn-primary"
              style="
                width: 100%;
                display: flex;
                align-items: center;
                justify-content: center;
                gap: 8px;
              "
              @click="openRuleEditor(null)"
            >
              <span><ClinicalIcon name="plus" :size="16" /></span> Create New
              Rule
            </button>
          </div>
        </div>

        <!-- Rules Editor Workspace -->
        <div
          v-if="showEditor"
          class="card"
          style="display: flex; flex-direction: column"
        >
          <div class="card-title" style="margin-bottom: 16px">
            {{ editingRuleId ? "Edit Clinical Rule" : "Compose Clinical Rule" }}
          </div>

          <div
            style="
              display: flex;
              flex-direction: column;
              gap: 16px;
              overflow-y: auto;
              max-height: 700px;
              padding-right: 4px;
            "
          >
            <!-- Reusable Rule Editor Widget -->
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <div
              class="rule-editor-wrapper"
              @click="handleEditorClick"
              @change="handleEditorChange"
              @input="handleEditorInput"
              v-html="ruleEditorHtml"
            />

            <!-- Live Compilation & GxP Verification Preview -->
            <fieldset
              style="
                border: 1px solid var(--border);
                border-radius: 8px;
                padding: 16px;
              "
            >
              <legend
                style="padding: 0 8px; font-weight: bold; color: var(--accent)"
              >
                Live Compilation &amp; GxP Verification Preview
              </legend>
              <div
                style="
                  background-color: var(--semantic-color-surface-header);
                  padding: 12px;
                  border-radius: 8px;
                  font-family: var(--semantic-font-mono);
                  font-size: 0.8rem;
                  border: 1px solid var(--border);
                  line-height: 1.5;
                "
              >
                <div
                  style="
                    font-weight: 700;
                    color: var(--text-main);
                    margin-bottom: 4px;
                  "
                >
                  Compiled XPath Expression:
                </div>
                <div
                  style="
                    color: var(--text-main);
                    margin-bottom: 12px;
                    word-break: break-all;
                    min-height: 1.5em;
                  "
                >
                  {{ previewXpath || "(No conditions added)" }}
                </div>

                <div
                  style="
                    font-weight: 700;
                    color: var(--text-main);
                    margin-bottom: 4px;
                  "
                >
                  Validation Feedback:
                </div>
                <div
                  v-if="previewFailures.length > 0"
                  style="
                    color: var(--error);
                    font-weight: 600;
                    margin-bottom: 8px;
                  "
                >
                  <div v-for="(f, i) in previewFailures" :key="i">
                    {{ f }}
                  </div>
                </div>
                <div
                  v-else
                  style="
                    color: var(--success);
                    font-weight: 600;
                    margin-bottom: 8px;
                  "
                >
                  CDASH Field &amp; Pydantic Schema Aligned.
                </div>

                <div
                  v-if="previewCircularCycles.length > 0"
                  style="color: var(--error); font-weight: 600"
                >
                  <div v-for="(c, i) in previewCircularCycles" :key="i">
                    {{ c }}
                  </div>
                </div>
              </div>
            </fieldset>
          </div>

          <!-- Editor Actions -->
          <div
            style="
              margin-top: 20px;
              display: flex;
              justify-content: flex-end;
              gap: 8px;
              border-top: 1px solid var(--border);
              padding-top: 16px;
            "
          >
            <button
              class="btn"
              style="padding: 8px 16px; cursor: pointer"
              @click="showEditor = false"
            >
              Cancel
            </button>
            <button
              class="btn btn-primary"
              style="padding: 8px 16px; cursor: pointer"
              @click="promptSaveRule"
            >
              Save Signed Rule
            </button>
          </div>
        </div>
      </div>

      <!-- Tab 2: Query Life-Cycle Dashboard & History Viewer (Sub-Issue 11) -->
      <div v-else-if="activeTab === 'queries'" class="card">
        <div
          class="card-title"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            margin-bottom: 16px;
          "
        >
          <span>Study Query Life-Cycle Dashboard</span>
          <div style="display: flex; gap: 8px">
            <button
              class="btn btn-secondary"
              style="font-size: 0.85rem; padding: 6px 12px"
              @click="exportQueries('JSON')"
            >
              Export JSON
            </button>
            <button
              class="btn btn-primary"
              style="font-size: 0.85rem; padding: 6px 12px"
              @click="exportQueries('CSV')"
            >
              Export CSV
            </button>
          </div>
        </div>
        <p
          style="
            font-size: 0.85rem;
            color: var(--semantic-color-text-secondary);
            margin-bottom: 16px;
          "
        >
          Review, manage, and audit clinical query states (OPEN, ANSWERED,
          REOPENED, CLOSED) across all trial subjects.
        </p>

        <!-- Coding Dictionary Lookup Area -->
        <div
          style="
            background-color: var(--semantic-color-surface-muted);
            border: 1px solid var(--border);
            border-radius: 8px;
            padding: 16px;
            margin-bottom: 24px;
          "
        >
          <h3
            style="
              font-weight: bold;
              font-size: 0.95rem;
              color: var(--text-main);
              margin-bottom: 8px;
            "
          >
            Coding Dictionary Lookup Tool
          </h3>
          <div style="display: flex; gap: 8px; margin-bottom: 12px">
            <input
              id="dm-dict-lookup-input"
              v-model="dictQuery"
              type="text"
              placeholder="Search dictionaries (MedDRA/WHODrug/NCI EVS)..."
              style="
                flex: 1;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 4px;
                font-size: 0.85rem;
              "
            />
            <button
              class="btn btn-primary"
              style="font-size: 0.85rem"
              @click="searchDict"
            >
              Search Dictionary
            </button>
          </div>
          <div
            v-if="dictSearching || codingStore.isSearching"
            style="
              font-size: 0.8rem;
              color: var(--semantic-color-text-secondary);
            "
          >
            Querying dictionaries...
          </div>
          <div
            v-else-if="codingStore.error"
            class="dict-search-error-alert card"
            style="
              padding: 8px 12px;
              background-color: var(--error-bg);
              border-left: 4px solid var(--error);
              border-radius: 4px;
              color: var(--semantic-color-error);
              font-size: 0.85rem;
              margin-bottom: 8px;
            "
          >
            {{ codingStore.error }}
          </div>
          <div
            v-else-if="dictResults.length > 0"
            style="
              max-height: 150px;
              overflow-y: auto;
              background: var(--semantic-color-surface);
              border: 1px solid var(--border);
              border-radius: 4px;
              padding: 8px;
            "
          >
            <div
              v-for="r in dictResults"
              :key="r.code"
              style="
                padding: 4px 0;
                border-bottom: 1px solid var(--semantic-color-surface-header);
                font-size: 0.8rem;
              "
            >
              <strong style="color: var(--accent)">{{ r.code }}</strong> -
              {{ r.name }} ({{ r.dictionary }})
            </div>
          </div>
        </div>

        <!-- Anomaly Detection Trigger & Query Filter Bar -->
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            flex-wrap: wrap;
            gap: 12px;
            margin-bottom: 16px;
          "
        >
          <div style="display: flex; gap: 6px; flex-wrap: wrap">
            <button
              class="btn btn-sm"
              :class="
                queryFilterStatus === 'ALL' ? 'btn-primary' : 'btn-secondary'
              "
              style="font-size: 0.75rem; padding: 4px 10px"
              @click="queryFilterStatus = 'ALL'"
            >
              All Queries ({{ dashboardQueries.length }})
            </button>
            <button
              class="btn btn-sm"
              :class="
                queryFilterStatus === 'CANDIDATE'
                  ? 'btn-primary'
                  : 'btn-secondary'
              "
              style="font-size: 0.75rem; padding: 4px 10px"
              @click="queryFilterStatus = 'CANDIDATE'"
            >
              Staged Anomaly Candidates ({{ countCandidateQueries }})
            </button>
            <button
              class="btn btn-sm"
              :class="
                queryFilterStatus === 'OPEN' ? 'btn-primary' : 'btn-secondary'
              "
              style="font-size: 0.75rem; padding: 4px 10px"
              @click="queryFilterStatus = 'OPEN'"
            >
              Open ({{ countOpenQueries }})
            </button>
            <button
              class="btn btn-sm"
              :class="
                queryFilterStatus === 'ANSWERED'
                  ? 'btn-primary'
                  : 'btn-secondary'
              "
              style="font-size: 0.75rem; padding: 4px 10px"
              @click="queryFilterStatus = 'ANSWERED'"
            >
              Answered ({{ countAnsweredQueries }})
            </button>
            <button
              class="btn btn-sm"
              :class="
                queryFilterStatus === 'CLOSED' ? 'btn-primary' : 'btn-secondary'
              "
              style="font-size: 0.75rem; padding: 4px 10px"
              @click="queryFilterStatus = 'CLOSED'"
            >
              Closed ({{ countClosedQueries }})
            </button>
          </div>

          <button
            class="btn btn-sm btn-primary"
            style="
              font-size: 0.8rem;
              display: flex;
              align-items: center;
              gap: 6px;
            "
            @click="triggerAnomalyScan"
          >
            <span> Run Cross-Domain Anomaly Scan</span>
          </button>
        </div>

        <!-- Queries Table -->
        <table class="clinical-visit-matrix" style="width: 100%">
          <thead>
            <tr>
              <th scope="col">ID</th>
              <th scope="col">Subject / Visit</th>
              <th scope="col">Field</th>
              <th scope="col">Discrepancy Message / Responses</th>
              <th scope="col">Status</th>
              <th scope="col">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="q in filteredQueries" :key="q.id">
              <td>
                <strong style="font-family: var(--semantic-font-mono)">{{
                  q.id
                }}</strong>
                <div v-if="q.origin === 'AI_ASSISTED'" style="margin-top: 4px">
                  <AiLabel label="AI-assisted" />
                </div>
                <div
                  v-else-if="
                    q.origin === 'ANOMALY_DETECTOR' ||
                    q.query_type === 'CROSS_DOMAIN_ANOMALY'
                  "
                  style="margin-top: 4px"
                >
                  <span class="clinical-badge clinical-badge--warning">
                    <ClinicalIcon name="alert-triangle" :size="16" />
                    CROSS-DOMAIN
                  </span>
                </div>
              </td>
              <td>
                {{ q.subjectId || q.subject_id }} /
                {{ q.visitId || q.visit_id || "N/A" }}
              </td>
              <td>
                <code>{{ q.fieldId || q.test_code }}</code>
              </td>
              <td>
                <div><strong>Query:</strong> {{ q.message }}</div>
                <div
                  v-if="q.explanation && q.explanation !== q.message"
                  style="
                    font-size: 0.8rem;
                    color: var(--semantic-color-text-secondary);
                    margin-top: 2px;
                  "
                >
                  <em>Details:</em> {{ q.explanation }}
                </div>
                <div
                  v-if="q.response"
                  style="color: var(--text-main); margin-top: 4px"
                >
                  <strong>Response:</strong> "{{ q.response }}" (Responded by:
                  {{ q.respondedBy }} on {{ q.respondedAt }})
                </div>
                <div
                  v-if="q.closedBy"
                  style="color: var(--semantic-color-success); margin-top: 4px"
                >
                  <strong>Closed:</strong> by {{ q.closedBy }} on
                  {{ q.closedAt }}
                </div>
              </td>
              <td>
                <span class="badge" :class="getBadgeClass(q.status)">{{
                  q.status
                }}</span>
              </td>
              <td>
                <div
                  v-if="q.status === 'CANDIDATE'"
                  style="display: flex; gap: 6px"
                >
                  <button
                    class="btn btn-sm btn-primary"
                    style="padding: 4px 8px; font-size: 0.75rem"
                    @click="promptAdjudicateCandidate"
                  >
                    Approve (Open)
                  </button>
                  <button
                    class="btn btn-sm"
                    style="
                      padding: 4px 8px;
                      font-size: 0.75rem;
                      background-color: var(--error-bg);
                      color: var(--error);
                    "
                    @click="promptAdjudicateCandidate"
                  >
                    Dismiss
                  </button>
                </div>
                <div
                  v-else-if="q.status === 'ANSWERED'"
                  style="display: flex; gap: 6px"
                >
                  <button
                    class="btn btn-sm btn-success"
                    style="padding: 4px 8px; font-size: 0.75rem"
                    @click="promptUpdateQueryState(q, 'CLOSED')"
                  >
                    Close
                  </button>
                  <button
                    class="btn btn-sm"
                    style="padding: 4px 8px; font-size: 0.75rem"
                    @click="promptUpdateQueryState(q, 'REOPENED')"
                  >
                    Reopen
                  </button>
                </div>
                <div
                  v-else-if="q.status === 'OPEN'"
                  style="display: flex; gap: 6px"
                >
                  <button
                    class="btn btn-sm btn-secondary"
                    style="padding: 4px 8px; font-size: 0.75rem"
                    @click="promptUpdateQueryState(q, 'ANSWERED')"
                  >
                    Add Response
                  </button>
                </div>
                <div v-else>-</div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Reason for Change Modal Dialog -->
    <ReasonModal
      :show="showReasonModal"
      title="Reason for Change Required"
      description="To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for changing this clinical data check rule."
      :options="rulesReasonOptions"
      default-option="Initial Entry"
      @confirm="confirmChangeReason"
      @cancel="closeReasonModal"
    />
  </div>
</template>

<script setup>
import { encodeChangeReason } from "../api/changeReason";
import { ref, computed, onMounted, watch } from "vue";
import AiLabel from "../components/ai/AiLabel.vue";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import { useCodingStore } from "../stores/coding";
import { useToastStore } from "../stores/toasts.js";
import { useErrorPresenter } from "../composables/useErrorPresenter";
import { apiClient } from "@/api/apiClient";
import ReasonModal from "../components/ReasonModal.vue";
import {
  allowUnbackedAction,
  refuseUnbackedAction,
} from "../utils/unbackedActions.js";
import {
  createRuleEditorHTML,
  serializeConditionsTree,
  deserializeConditionsTree,
  canonicalSerialize,
  sha256,
  ClinicalIcon,
} from "ui";

const rulesReasonOptions = [
  { value: "Initial Entry", text: "Initial Data Entry" },
  { value: "Typographical Error", text: "Correction of typographical error" },
  { value: "Re-measurement", text: "Re-measurement of vitals" },
  { value: "Transcription Error", text: "Correction of transcription error" },
  { value: "Protocol Amendment", text: "Protocol Amendment / Update" },
  { value: "Other", text: "Other (specify below)" },
];

const store = useClinicalStore();
const authStore = useAuthStore();
const codingStore = useCodingStore();
const toasts = useToastStore();
const { presentError } = useErrorPresenter();

// Sub-Issue 11 States
const activeTab = ref("rules"); // 'rules', 'queries'
const dictQuery = ref("");
const dictSearching = ref(false);
const dictResults = ref([]);
const queryFilterStatus = ref("ALL"); // 'ALL', 'CANDIDATE', 'OPEN', 'ANSWERED', 'CLOSED'

const dashboardQueries = ref([
  {
    id: "q-cand-1",
    subjectId: "SUBJ-003",
    visitId: "Week4",
    fieldId: "AESEV",
    status: "CANDIDATE",
    origin: "ANOMALY_DETECTOR",
    query_type: "CROSS_DOMAIN_ANOMALY",
    message:
      "Severe Adverse Event 'Hepatotoxicity' recorded without concomitant medication.",
    explanation:
      "Subject has severe AE without corresponding CM domain record to manage condition.",
    createdBy: "ANOMALY_DETECTOR_WORKER",
    createdAt: new Date().toISOString().slice(0, 10),
  },
  {
    id: "q-1",
    subjectId: "SUBJ-001",
    visitId: "Screening",
    fieldId: "pulse",
    status: "OPEN",
    message: "Pulse is 190. Please re-measure.",
    createdBy: "CRA",
    createdAt: new Date().toISOString().slice(0, 10),
  },
  {
    id: "q-2",
    subjectId: "SUBJ-002",
    visitId: "Week2",
    fieldId: "vssbp",
    status: "ANSWERED",
    message: "Systolic BP out of logic constraint range.",
    response: "Re-measured and confirmed correct.",
    respondedBy: "CRC",
    respondedAt: new Date().toISOString().slice(0, 10),
  },
  {
    id: "q-3",
    subjectId: "SUBJ-001",
    visitId: "Screening",
    fieldId: "vsdpb",
    status: "CLOSED",
    message: "Diastolic value missing.",
    response: "Entered 80 mmHg.",
    closedBy: "DM",
    closedAt: new Date().toISOString().slice(0, 10),
  },
]);

const countCandidateQueries = computed(() => {
  return dashboardQueries.value.filter((q) => q.status === "CANDIDATE").length;
});

const countOpenQueries = computed(() => {
  return dashboardQueries.value.filter((q) => q.status === "OPEN").length;
});

const countAnsweredQueries = computed(() => {
  return dashboardQueries.value.filter((q) => q.status === "ANSWERED").length;
});

const countClosedQueries = computed(() => {
  return dashboardQueries.value.filter((q) => q.status === "CLOSED").length;
});

const filteredQueries = computed(() => {
  if (queryFilterStatus.value === "ALL") return dashboardQueries.value;
  return dashboardQueries.value.filter(
    (q) => q.status === queryFilterStatus.value
  );
});

function getBadgeClass(status) {
  if (status === "CANDIDATE") return "lookup-candidate";
  if (status === "OPEN") return "lookup-invalid";
  if (status === "ANSWERED") return "lookup-degraded";
  if (status === "CLOSED") return "lookup-valid";
  return "";
}

// State-machine transition & GxP Change capture
const pendingQueryStateTransition = ref(null);

function promptUpdateQueryState(query, nextState) {
  pendingQueryStateTransition.value = { query, nextState };
  showReasonModal.value = true;
}

function promptAdjudicateCandidate() {
  // Candidate queries are adjudicated by the anomaly service, which no Cadence service serves yet: nothing
  // changes, in any build (#4829).
  refuseUnbackedAction("Adjudicating a candidate query");
}

function triggerAnomalyScan() {
  // No Cadence service runs the cross-domain anomaly scan yet; nothing is scanned in any build (#4829).
  refuseUnbackedAction("Scanning for cross-domain anomalies");
}

// Search Terminology / Coding Dictionary lookups
async function searchDict() {
  if (!dictQuery.value || !dictQuery.value.trim()) {
    dictResults.value = [];
    return;
  }
  dictSearching.value = true;
  try {
    await codingStore.searchDictionary({
      term: dictQuery.value.trim(),
      version: "26.0",
    });
    if (codingStore.error) {
      dictResults.value = [];
    } else {
      const matches = codingStore.dictionarySearchResults || [];
      dictResults.value = matches.map((m) => ({
        code: m.code || m.llt_code || m.pt_code || "",
        name: m.name || m.llt_name || m.pt_name || m.preferred_name || "",
        dictionary:
          m.dictionary || (m.version ? `MedDRA v${m.version}` : "MedDRA v26.0"),
      }));
    }
  } catch (err) {
    console.warn("Dictionary search failed:", err);
    dictResults.value = [];
  } finally {
    dictSearching.value = false;
  }
}

function exportQueries(format) {
  const filename = `query_history_export.${format.toLowerCase()}`;
  let content;
  let mimeType;

  if (format === "JSON") {
    content = JSON.stringify(dashboardQueries.value, null, 2);
    mimeType = "application/json";
  } else {
    // CSV export
    const headers = [
      "ID",
      "Subject",
      "Visit",
      "Field",
      "Status",
      "Message",
      "Response",
    ];
    const rows = dashboardQueries.value.map((q) => [
      q.id,
      q.subjectId,
      q.visitId,
      q.fieldId,
      q.status,
      q.message,
      q.response || "",
    ]);
    content = [
      headers.join(","),
      ...rows.map((r) =>
        r.map((v) => `"${String(v).replace(/"/g, '""')}"`).join(",")
      ),
    ].join("\n");
    mimeType = "text/csv";
  }

  const blob = new Blob([content], { type: mimeType });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
  toasts.push({
    tone: "success",
    title: "Queries Exported",
    message: `Queries exported as ${format} successfully!`,
  });
}

// Standard CDASH mock structures aligned with index.js legacy logic and backend visits
const mockStudyForms = [
  { id: "form_dm", name: "Demographics" },
  { id: "form_vs", name: "Vital Signs" },
  { id: "form_ae", name: "Adverse Events" },
  { id: "visit_1", name: "Visit 1" },
];

const mockStudyFields = [
  { id: "act_1", name: "Blood Draw (act_1)", formId: "visit_1" },
  { id: "act_2", name: "Vitals (act_2)", formId: "visit_1" },
  { id: "brthdt", name: "Date of Birth (brthdt)", formId: "form_dm" },
  { id: "sex", name: "Sex at Birth (sex)", formId: "form_dm" },
  { id: "vssbp", name: "Systolic BP (vssbp)", formId: "form_vs" },
  { id: "vsdpb", name: "Diastolic BP (vsdpb)", formId: "form_vs" },
  { id: "pulse", name: "Pulse Rate (pulse)", formId: "form_vs" },
  { id: "aeterm", name: "Adverse Event Term (aeterm)", formId: "form_ae" },
];

// Authorization gating
const hasEditAccess = computed(() => {
  // Check if user has sponsor_designer, data_manager, sponsor_admin or admin roles
  return authStore.normalizedRoles.some(
    (role) =>
      role === "sponsor_designer" ||
      role === "data_manager" ||
      role === "sponsor_admin" ||
      role === "admin"
  );
});

// Component state
const activeRules = ref([]);
const loadingRules = ref(false);
const connectionError = ref(false);
const showEditor = ref(false);

// Editor Form state
const editingRuleId = ref(null);
const ruleType = ref("skip_logic");
const targetField = ref("");
const ruleAction = ref("show");
const targetForm = ref("");
const queryMessage = ref("");
const matchOperator = ref("and");
const conditions = ref([]);

// Preview state
const previewXpath = ref("");
const previewFailures = ref([]);
const previewCircularCycles = ref([]);

// Reason Modal state
const showReasonModal = ref(false);
const pendingAction = ref(null); // { type: 'save' | 'delete' | 'query_state', ruleId?: string, payload?: any }

// Reusable rule-builder widget reactive state bridge
const ruleEditorHtml = computed(() => {
  return createRuleEditorHTML(mockStudyForms, mockStudyFields, {
    conditions: conditions.value,
    matchOperator: matchOperator.value,
    ruleType: ruleType.value,
    targetField: targetField.value,
    ruleAction: ruleAction.value,
    targetForm: targetForm.value,
    queryMessage: queryMessage.value,
  });
});

function handleEditorClick(e) {
  const target = e.target;
  const action = target.getAttribute("data-action");
  const indexAttr = target.getAttribute("data-index");
  const index = indexAttr !== null ? parseInt(indexAttr, 10) : null;

  if (action === "add-condition") {
    addConditionRow();
  } else if (action === "remove-condition" && index !== null && !isNaN(index)) {
    removeConditionRow(index);
  }
}

function handleEditorChange(e) {
  const target = e.target;
  const indexAttr = target.getAttribute("data-index");
  const index = indexAttr !== null ? parseInt(indexAttr, 10) : null;

  if (target.classList.contains("rule-type-selector")) {
    ruleType.value = target.value;
    handleTypeChange();
  } else if (target.classList.contains("target-field-selector")) {
    targetField.value = target.value;
  } else if (target.classList.contains("rule-action-selector")) {
    ruleAction.value = target.value;
  } else if (target.classList.contains("target-form-selector")) {
    targetForm.value = target.value;
  } else if (target.classList.contains("match-operator-selector")) {
    matchOperator.value = target.value;
  } else if (index !== null && !isNaN(index) && conditions.value[index]) {
    if (target.classList.contains("cond-form")) {
      conditions.value[index].formId = target.value;
    } else if (target.classList.contains("cond-field")) {
      conditions.value[index].fieldId = target.value;
    } else if (target.classList.contains("cond-operator")) {
      conditions.value[index].operator = target.value;
    } else if (target.classList.contains("cond-right-type")) {
      conditions.value[index].rightType = target.value;
    } else if (target.classList.contains("cond-right-field")) {
      conditions.value[index].rightFieldId = target.value;
    }
  }
}

function handleEditorInput(e) {
  const target = e.target;
  const indexAttr = target.getAttribute("data-index");
  const index = indexAttr !== null ? parseInt(indexAttr, 10) : null;

  if (target.classList.contains("query-message-input")) {
    queryMessage.value = target.value;
  } else if (index !== null && !isNaN(index) && conditions.value[index]) {
    if (target.classList.contains("cond-right-value")) {
      conditions.value[index].rightValue = target.value;
    }
  }
}
/**
 * Headers for a rules API call. The browser sends only the user's change reason: the gateway strips every
 * identity header (X-User-Id, X-User-Roles, X-Gateway-Signature, ...) from inbound requests and re-issues them,
 * HMAC-signed, from the validated OIDC token (#4523). A browser must never hold the gateway signing secret.
 */
function changeReasonHeaders(changeReason = "") {
  return changeReason
    ? { "X-Change-Reason": encodeChangeReason(changeReason) }
    : {};
}

// Fetch active rules from backend REST API
async function fetchRules() {
  loadingRules.value = true;
  connectionError.value = false;
  try {
    const response = await apiClient.get(`/api/v1/studies/study_1/rules`, {
      headers: changeReasonHeaders(),
    });
    activeRules.value = response;
  } catch (err) {
    console.warn(
      "Failed to fetch active rules from REST backend, falling back to empty:",
      err
    );
    connectionError.value = true;
    activeRules.value = [];
  } finally {
    loadingRules.value = false;
  }
}

// Conditions management
function addConditionRow() {
  conditions.value.push({
    formId: "",
    fieldId: "",
    operator: "==",
    rightType: "constant",
    rightValue: "",
    rightFieldId: "",
  });
}

function removeConditionRow(index) {
  conditions.value.splice(index, 1);
  if (conditions.value.length === 0) {
    addConditionRow();
  }
}

function handleTypeChange() {
  if (ruleType.value === "skip_logic") {
    queryMessage.value = "";
    ruleAction.value = "show";
  } else {
    ruleAction.value = "show";
    targetForm.value = "";
    if (ruleType.value === "constraint" && !queryMessage.value) {
      queryMessage.value = "Value is out of range.";
    } else if (ruleType.value === "cross_form_check" && !queryMessage.value) {
      queryMessage.value = "Discrepancy detected across study forms.";
    }
  }
}

// Serialize vue forms to Pydantic Expression tree
function serializeConditions() {
  return serializeConditionsTree(conditions.value, matchOperator.value);
}

// Dry-run preview REST API compilation
async function triggerPreview() {
  const payload = {
    type: ruleType.value,
    condition: serializeConditions(),
    target_field:
      ruleType.value !== "cross_form_check" ? targetField.value || null : null,
    target_form:
      ruleType.value === "skip_logic" ? targetForm.value || null : null,
    action: ruleType.value === "skip_logic" ? ruleAction.value : null,
    query_message:
      ruleType.value !== "skip_logic" ? queryMessage.value || null : null,
  };

  try {
    const signedHeaders = changeReasonHeaders("Rule compilation preview");

    try {
      await apiClient.post(`/api/v1/studies/study_1/rules/validate`, payload, {
        headers: signedHeaders,
      });
    } catch (vErr) {
      console.warn("Live-validation endpoint returned errors:", vErr);
    }

    const data = await apiClient.post(
      `/api/v1/studies/study_1/rules/preview`,
      payload,
      { headers: signedHeaders }
    );
    previewXpath.value = data.xpath;
    previewFailures.value = data.failures || [];
    previewCircularCycles.value = data.circular_cycles || [];
  } catch (err) {
    // Graceful degraded local compiling sandbox
    console.warn(
      "Dry-run preview API failed, performing fallback local compilation:",
      err
    );
    compileLocalFallback(payload);
  }
}

function compileLocalFallback(payload) {
  function compileNode(node) {
    if (!node) return "";
    if (node.type === "constant") {
      return typeof node.value === "string"
        ? `'${node.value}'`
        : String(node.value);
    }
    if (node.type === "field_ref") {
      return `/clinical_data/${node.field_ref.form_id ? node.field_ref.form_id + "/" : ""}${node.field_ref.field_id}`;
    }
    if (node.type === "function") {
      const fnName = node.operator === "is_empty" ? "empty" : "not(empty";
      const closing = node.operator === "is_not_empty" ? ")" : "";
      return `${fnName}(${compileNode(node.operands[0])})${closing}`;
    }
    if (node.type === "comparison") {
      return `(${compileNode(node.operands[0])} ${node.operator === "==" ? "=" : node.operator} ${compileNode(node.operands[1])})`;
    }
    if (node.type === "logical") {
      const ops = node.operands
        .map(compileNode)
        .join(` ${node.operator.toUpperCase()} `);
      return `(${ops})`;
    }
    return "";
  }

  previewXpath.value = compileNode(payload.condition);
  previewFailures.value = [];
  previewCircularCycles.value = [];

  // Basic circular validation
  if (payload.type === "skip_logic" && payload.target_field) {
    const refs = traverseRefs(payload.condition);
    if (refs.includes(payload.target_field)) {
      previewCircularCycles.value.push(
        `Circular dependency: ${payload.target_field} -> ${payload.target_field}`
      );
    }
  }
}

function traverseRefs(node) {
  if (!node) return [];
  if (node.type === "field_ref") return [node.field_ref.field_id];
  let refs = [];
  if (node.operands) {
    node.operands.forEach((op) => {
      refs = refs.concat(traverseRefs(op));
    });
  }
  return refs;
}

function openRuleEditor(rule = null) {
  if (rule) {
    editingRuleId.value = rule.id;
    ruleType.value = rule.type;
    targetField.value = rule.target_field || "";
    ruleAction.value = rule.action || "show";
    targetForm.value = rule.target_form || "";
    queryMessage.value = rule.query_message || "";

    const deserialized = deserializeConditionsTree(rule.condition);
    conditions.value = deserialized.conditions;
    matchOperator.value = deserialized.matchOperator;

    if (conditions.value.length === 0) {
      addConditionRow();
    }
  } else {
    editingRuleId.value = null;
    ruleType.value = "skip_logic";
    targetField.value = "";
    ruleAction.value = "show";
    targetForm.value = "";
    queryMessage.value = "";
    conditions.value = [];
    matchOperator.value = "and";
    addConditionRow();
  }
  showEditor.value = true;
  triggerPreview();
}

// GxP Change Modal Actions
function promptSaveRule() {
  if (ruleType.value !== "cross_form_check" && !targetField.value) {
    toasts.push({
      tone: "warning",
      title: "Target Field Required",
      message: "Please select a target field!",
    });
    return;
  }
  if (ruleType.value === "constraint" && !queryMessage.value) {
    toasts.push({
      tone: "warning",
      title: "Query Message Required",
      message: "Please provide an auto-query message!",
    });
    return;
  }
  if (ruleType.value === "cross_form_check" && !queryMessage.value) {
    toasts.push({
      tone: "warning",
      title: "Query Message Required",
      message: "Please provide an auto-query message!",
    });
    return;
  }

  const payload = {
    type: ruleType.value,
    condition: serializeConditions(),
    target_field:
      ruleType.value !== "cross_form_check" ? targetField.value || null : null,
    target_form:
      ruleType.value === "skip_logic" ? targetForm.value || null : null,
    action: ruleType.value === "skip_logic" ? ruleAction.value : null,
    query_message:
      ruleType.value !== "skip_logic" ? queryMessage.value || null : null,
  };

  pendingAction.value = {
    type: "save",
    payload,
  };
  showReasonModal.value = true;
}

function promptDeleteRule(ruleId) {
  pendingAction.value = {
    type: "delete",
    ruleId,
  };
  showReasonModal.value = true;
}

function closeReasonModal() {
  showReasonModal.value = false;
  pendingAction.value = null;
  pendingQueryStateTransition.value = null;
}

async function confirmChangeReason(reasonText) {
  const action = pendingAction.value;

  // Handle Query State Transition inside confirmation
  if (pendingQueryStateTransition.value) {
    const { query, nextState } = pendingQueryStateTransition.value;
    // #4827: these queries are not the Execution service's, so a transition here would be recorded nowhere.
    if (!allowUnbackedAction(`Moving query ${query.id} to ${nextState}`)) {
      pendingQueryStateTransition.value = null;
      showReasonModal.value = false;
      return;
    }
    query.status = nextState;

    if (nextState === "CLOSED") {
      query.closedBy = authStore.userId || "dm.lead";
      query.closedAt = new Date().toISOString().slice(0, 10);
    } else if (nextState === "ANSWERED") {
      query.response = "Response added by Data Manager.";
      query.respondedBy = authStore.userId || "dm.lead";
      query.respondedAt = new Date().toISOString().slice(0, 10);
    }

    await store.addLedgerBlock(
      "QUERY_TRANSITION",
      { queryId: query.id, nextState },
      reasonText
    );

    pendingQueryStateTransition.value = null;
    showReasonModal.value = false;
    toasts.push({
      tone: "success",
      title: "Query Status Updated",
      message: `Query status successfully transitioned to ${nextState} and logged!`,
    });
    return;
  }

  if (!action) return;

  try {
    if (action.type === "save") {
      const isEdit = !!editingRuleId.value;
      const url = isEdit
        ? `/api/v1/studies/study_1/rules/${editingRuleId.value}`
        : `/api/v1/studies/study_1/rules`;

      const signedHeaders = changeReasonHeaders(reasonText);

      let saved;
      try {
        if (isEdit) {
          saved = await apiClient.put(url, action.payload, {
            headers: signedHeaders,
          });
        } else {
          saved = await apiClient.post(url, action.payload, {
            headers: signedHeaders,
          });
        }
      } catch (err) {
        // Never record a rule the backend did not store (#4523); the editor and dialog stay open for a retry.
        toasts.apiError("Rule save failed", err);
        return;
      }

      // A content digest, not a signature: the browser holds no signing key (#4765). The server-side audit
      // trail is the authoritative, signed record of this change.
      const payloadSha256 = await sha256(canonicalSerialize(action.payload));

      // Sync verified record into compliance ledger
      await store.addLedgerBlock(
        "RULE_SAVE",
        {
          ruleId: saved.id,
          type: saved.type,
          xpath: saved.compiled_xpath || previewXpath.value,
          payloadSha256,
          payload: action.payload,
          headers: signedHeaders,
        },
        reasonText
      );

      toasts.push({
        tone: "success",
        title: "Rule Saved",
        message: "Rule saved.",
      });
    } else if (action.type === "delete") {
      const url = `/api/v1/studies/study_1/rules/${action.ruleId}`;
      const signedHeaders = changeReasonHeaders(reasonText);
      try {
        await apiClient.delete(url, { headers: signedHeaders });
      } catch (err) {
        // The rule was not deleted: no RULE_DELETE ledger block, no success toast (#4523).
        toasts.apiError("Rule deletion failed", err);
        return;
      }

      // Sync deletion block
      await store.addLedgerBlock(
        "RULE_DELETE",
        {
          ruleId: action.ruleId,
          headers: signedHeaders,
        },
        reasonText
      );

      toasts.push({
        tone: "success",
        title: "Rule Deleted",
        message: "Rule successfully soft-deleted!",
      });
    }

    showEditor.value = false;
    showReasonModal.value = false;
    pendingAction.value = null;
    await fetchRules();
  } catch (err) {
    presentError(err, {
      title: "GxP Transaction Aborted",
      fallback: "GxP transaction aborted.",
    });
  }
}

// Dynamic watchers to trigger live preview
watch(
  () => conditions.value,
  () => {
    triggerPreview();
  },
  { deep: true }
);

watch(
  [ruleType, targetField, ruleAction, targetForm, queryMessage, matchOperator],
  () => {
    triggerPreview();
  }
);

onMounted(async () => {
  if (hasEditAccess.value) {
    await fetchRules();
  }
});
</script>

<style scoped>
.rule-card:hover {
  border-color: var(--accent) !important;
  box-shadow: var(--shadow-sm);
}

.rules-gating-banner {
  border-left: 4px solid var(--error);
  background-color: var(--error-bg);
  padding: 24px;
}

.rules-gating-content {
  display: flex;
  gap: 16px;
  align-items: flex-start;
}

.rules-gating-icon {
  font-size: 2rem;
}

.rules-gating-title {
  color: var(--error);
  font-weight: bold;
  margin-bottom: 8px;
}

.rules-gating-text {
  color: var(--neutral-dark);
  font-size: 0.95rem;
  line-height: 1.6;
}

.rule-card-item {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  margin-bottom: 12px;
  background-color: var(--neutral-light);
  transition:
    background-color 0.2s,
    border-color 0.2s;
}

.rule-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 8px;
}
</style>
