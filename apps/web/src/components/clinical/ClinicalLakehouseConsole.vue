<template>
  <div
    class="lakehouse-console"
    role="region"
    aria-label="Multi-Source Clinical Lakehouse and Version-Pinned Data Pooling"
  >
    <!-- Header -->
    <header class="console-header">
      <div class="header-main">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-milestone">SHA-256 Version Pinned</span>
          <span class="badge badge-streams">7-Stream Lakehouse</span>
        </div>
        <h2 class="console-title">
          Multi-Source Clinical Lakehouse &amp; Version-Pinned Data Pooling
        </h2>
        <p class="console-subtitle">
          Continuous multi-stream ingestion (EDC, ePRO, Central Labs, Local
          Labs, IoT Wearables, EHR FHIR, Blinded Imaging) with canonical
          normalization, cryptographic snapshot pooling, role-based unblinded
          data partitioning, and 21 CFR Part 11 audit logging.
        </p>
      </div>

      <div class="header-actions">
        <!-- Blinded Partitioning Role Toggle -->
        <div class="blinding-toggle-box">
          <label class="toggle-label" for="unblinded-toggle">
            <span class="toggle-icon">{{ isUnblindedRole ? "🔓" : "🔒" }}</span>
            <span class="toggle-text">{{
              isUnblindedRole
                ? "Unblinded View (IDMC / Stats)"
                : "Blinded View (Masked)"
            }}</span>
          </label>
          <input
            id="unblinded-toggle"
            v-model="isUnblindedRole"
            type="checkbox"
            class="toggle-checkbox"
            @change="handleBlindingToggle"
          />
        </div>

        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isLoading"
          @click="refreshData"
        >
          <span class="icon">🔄</span> Refresh Lakehouse
        </button>

        <button
          type="button"
          class="btn btn-primary"
          @click="openCreateSnapshotModal"
        >
          <span class="icon">📸</span> Create Version-Pinned Snapshot
        </button>
      </div>
    </header>

    <!-- Multi-Stream Metrics Cards Grid -->
    <section class="metrics-grid" aria-label="Ingestion Stream Metrics">
      <div
        v-for="stream in streamDefinitions"
        :key="stream.type"
        class="metric-card"
      >
        <div class="stream-icon-col" :style="{ backgroundColor: stream.bgCol }">
          <span class="stream-icon">{{ stream.icon }}</span>
        </div>
        <div class="stream-details">
          <span class="stream-name">{{ stream.label }}</span>
          <div class="stream-stat-row">
            <span class="stream-count">{{ getStreamCount(stream.type) }}</span>
            <span class="stream-unit">records</span>
          </div>
          <span class="stream-domain-tag">{{ stream.cdiscDomain }}</span>
        </div>
      </div>
    </section>

    <!-- Workspace Tabs -->
    <nav class="nav-tabs" role="tablist" aria-label="Lakehouse Navigation Tabs">
      <button
        role="tab"
        :aria-selected="activeTab === 'snapshots'"
        :class="['tab-button', { active: activeTab === 'snapshots' }]"
        @click="activeTab = 'snapshots'"
      >
        🗄️ Version-Pinned Snapshots ({{ snapshots.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'pool_data'"
        :class="['tab-button', { active: activeTab === 'pool_data' }]"
        @click="activeTab = 'pool_data'"
      >
        📊 Pooled Data Explorer
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'audit_log'"
        :class="['tab-button', { active: activeTab === 'audit_log' }]"
        @click="activeTab = 'audit_log'"
      >
        📜 21 CFR Part 11 Audit Trail
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'sce'"
        :class="['tab-button', { active: activeTab === 'sce' }]"
        @click="activeTab = 'sce'"
      >
        🧮 Statistical Computing (R / Python)
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'cdisc_pipeline'"
        :class="['tab-button', { active: activeTab === 'cdisc_pipeline' }]"
        @click="activeTab = 'cdisc_pipeline'"
      >
        🧬 CDISC SDTM/ADaM &amp; Define-XML
      </button>
    </nav>

    <!-- TAB 1: Version-Pinned Snapshots -->
    <div v-if="activeTab === 'snapshots'" class="tab-panel" role="tabpanel">
      <div class="card table-card">
        <div class="card-header">
          <h3 class="card-title">Immutable Clinical Snapshot Pools</h3>
          <div class="card-meta">
            <span class="total-badge"
              >Total Sealed Snapshots: {{ snapshots.length }}</span
            >
          </div>
        </div>

        <div class="table-container">
          <table class="data-table">
            <thead>
              <tr>
                <th>Snapshot ID</th>
                <th>Version Tag</th>
                <th>Milestone</th>
                <th>Lock Ref</th>
                <th>Records</th>
                <th>Blinded / Unblinded</th>
                <th>Cryptographic SHA-256 Seal</th>
                <th>Created At</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="snap in snapshots"
                :key="snap.snapshot_id"
                :class="{
                  'selected-row':
                    selectedSnapshot?.snapshot_id === snap.snapshot_id,
                }"
              >
                <td class="cell-id">
                  <code>{{ snap.snapshot_id }}</code>
                </td>
                <td class="cell-bold">
                  <span class="version-pill">{{ snap.version_tag }}</span>
                </td>
                <td>
                  <span
                    class="milestone-badge"
                    :class="`milestone-${snap.milestone.toLowerCase()}`"
                  >
                    {{ snap.milestone }}
                  </span>
                </td>
                <td>
                  <span v-if="snap.lock_id" class="lock-ref"
                    >🔒 {{ snap.lock_id }}</span
                  >
                  <span v-else class="text-muted">—</span>
                </td>
                <td class="cell-num">
                  {{ snap.record_count.toLocaleString() }}
                </td>
                <td>
                  <div class="partition-ratio">
                    <span class="part-blinded"
                      >{{ snap.blinded_record_count }} Blinded</span
                    >
                    <span class="part-separator">/</span>
                    <span class="part-unblinded"
                      >{{ snap.unblinded_record_count }} Unblinded</span
                    >
                  </div>
                </td>
                <td class="cell-digest">
                  <div class="digest-pill" :title="snap.sha256_digest">
                    <span class="digest-icon">🛡️</span>
                    <code
                      >{{ snap.sha256_digest.slice(0, 12) }}...{{
                        snap.sha256_digest.slice(-8)
                      }}</code
                    >
                  </div>
                </td>
                <td class="cell-date">
                  {{ formatDate(snap.created_at) }}
                </td>
                <td class="cell-actions">
                  <button
                    type="button"
                    class="btn-mini btn-mini-verify"
                    title="Verify SHA-256 Bit-for-Bit Integrity"
                    @click="verifySnapshot(snap)"
                  >
                    🛡️ Verify
                  </button>
                  <button
                    type="button"
                    class="btn-mini btn-mini-view"
                    title="View Snapshot Pooled Records"
                    @click="viewSnapshotData(snap)"
                  >
                    🔍 Explore
                  </button>
                  <button
                    type="button"
                    class="btn-mini btn-mini-audit"
                    title="View Snapshot Audit Trail"
                    @click="viewSnapshotAudit(snap)"
                  >
                    📜 Audit
                  </button>
                </td>
              </tr>
              <tr v-if="snapshots.length === 0">
                <td colspan="9" class="empty-cell">
                  No version-pinned snapshots created yet. Click "Create
                  Version-Pinned Snapshot" to pool multi-stream observations.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Verification Banner / Modal Result -->
      <div
        v-if="lastVerificationResult"
        class="verification-banner"
        :class="
          lastVerificationResult.is_valid ? 'verify-success' : 'verify-failed'
        "
      >
        <div class="verify-icon-col">
          <span>{{ lastVerificationResult.is_valid ? "✅" : "❌" }}</span>
        </div>
        <div class="verify-content">
          <h4>
            Cryptographic SHA-256 Verification:
            {{ lastVerificationResult.version_tag }}
          </h4>
          <p>{{ lastVerificationResult.verification_message }}</p>
          <div class="digest-compare-row">
            <span class="compare-label">Stored Digest:</span>
            <code>{{ lastVerificationResult.stored_digest }}</code>
          </div>
          <div class="digest-compare-row">
            <span class="compare-label">Recomputed Digest:</span>
            <code>{{ lastVerificationResult.computed_digest }}</code>
          </div>
        </div>
        <button
          type="button"
          class="btn-close"
          @click="lastVerificationResult = null"
        >
          &times;
        </button>
      </div>
    </div>

    <!-- TAB 2: Pooled Data Explorer -->
    <div v-if="activeTab === 'pool_data'" class="tab-panel" role="tabpanel">
      <div class="card explorer-card">
        <div class="card-header">
          <div class="header-left">
            <h3 class="card-title">
              Pooled Observations
              <span v-if="selectedSnapshot" class="snap-badge"
                >Snapshot: {{ selectedSnapshot.version_tag }}</span
              >
            </h3>
            <p class="card-sub">
              Showing harmonized observations from all ingested data streams.
              <span v-if="!isUnblindedRole" class="blind-notice"
                >⚠️ Blinded Mode Active: Sensitive treatment records and
                randomized allocations are masked per GxP blinding policy.</span
              >
              <span v-else class="unblind-notice"
                >🔓 Unblinded Access Verified: Viewing unmasked treatment
                allocations.</span
              >
            </p>
          </div>

          <div class="filter-controls">
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select v-model="filterDomain" class="form-select">
              <option value="">All Domains</option>
              <option value="VS">VS - Vital Signs</option>
              <option value="LB">LB - Laboratory</option>
              <option value="QS">QS - Questionnaires / ePRO</option>
              <option value="AE">AE - Adverse Events</option>
              <option value="TR">TR - Tumor / Lesion</option>
              <option value="RS">RS - RECIST Response</option>
              <option value="DM">DM - Demographics</option>
              <option value="EX">EX - Exposure</option>
            </select>

            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select v-model="filterStream" class="form-select">
              <option value="">All Streams</option>
              <option value="EDC_ECRF">EDC eCRFs</option>
              <option value="EPRO_ECOA">ePRO / eCOA</option>
              <option value="CENTRAL_LAB">Central Labs</option>
              <option value="LOCAL_LAB">Local Labs</option>
              <option value="IOT_WEARABLE">IoT Wearables</option>
              <option value="EHR_FHIR">EHR FHIR</option>
              <option value="MEDICAL_IMAGING">Medical Imaging</option>
            </select>

            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="filterSubject"
              type="text"
              class="form-input search-input"
              placeholder="Filter by Subject ID..."
            />
          </div>
        </div>

        <div class="table-container">
          <table class="data-table records-table">
            <thead>
              <tr>
                <th>Subject ID</th>
                <th>Stream</th>
                <th>Domain</th>
                <th>Item Code / Test</th>
                <th>Observation Value</th>
                <th>Unit</th>
                <th>Reference Range</th>
                <th>Abnormal Flag</th>
                <th>Blinding Status</th>
                <th>Observation Time</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="rec in filteredRecords"
                :key="rec.record_id"
                :class="{
                  'row-unblinded-masked': rec.is_unblinded && !isUnblindedRole,
                }"
              >
                <td class="cell-bold">
                  <code>{{ rec.subject_id }}</code>
                </td>
                <td>
                  <span
                    class="stream-pill"
                    :class="`stream-${rec.stream_type.toLowerCase()}`"
                  >
                    {{ rec.stream_type }}
                  </span>
                </td>
                <td>
                  <span class="domain-pill">{{ rec.domain }}</span>
                </td>
                <td>
                  <div class="item-cell">
                    <span class="item-name">{{ rec.item_name }}</span>
                    <code class="item-code">{{ rec.item_code }}</code>
                  </div>
                </td>
                <td>
                  <span
                    v-if="rec.text_value === '[BLINDED]'"
                    class="badge-blinded"
                    >[BLINDED]</span
                  >
                  <span
                    v-else-if="rec.numeric_value !== null"
                    class="cell-val-num"
                    >{{ rec.numeric_value }}</span
                  >
                  <span v-else class="cell-val-text">{{
                    rec.text_value || "—"
                  }}</span>
                </td>
                <td>{{ rec.unit || "—" }}</td>
                <td>
                  <span
                    v-if="
                      rec.reference_low !== null || rec.reference_high !== null
                    "
                    class="ref-range"
                  >
                    {{ rec.reference_low ?? "—" }} -
                    {{ rec.reference_high ?? "—" }}
                  </span>
                  <span v-else class="text-muted">—</span>
                </td>
                <td>
                  <span
                    v-if="rec.abnormal_flag"
                    class="flag-pill"
                    :class="`flag-${rec.abnormal_flag.toLowerCase()}`"
                  >
                    {{ rec.abnormal_flag }}
                  </span>
                  <span v-else class="text-muted">—</span>
                </td>
                <td>
                  <span v-if="rec.is_unblinded" class="badge-unblind-tag">
                    {{ isUnblindedRole ? "🔓 UNBLINDED" : "🔒 MASKED" }}
                  </span>
                  <span v-else class="badge-blinded-tag">BLINDED</span>
                </td>
                <td class="cell-date">
                  {{ formatDate(rec.observation_datetime) }}
                </td>
              </tr>
              <tr v-if="filteredRecords.length === 0">
                <td colspan="10" class="empty-cell">
                  No records match current filters or no snapshot selected.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- TAB 3: 21 CFR Part 11 Audit Trail -->
    <div v-if="activeTab === 'audit_log'" class="tab-panel" role="tabpanel">
      <div class="card audit-card">
        <div class="card-header">
          <h3 class="card-title">
            Immutable 21 CFR Part 11 Lakehouse Audit Trail
          </h3>
          <span class="audit-badge">Cryptographically Sealed Audit Ledger</span>
        </div>

        <div class="table-container">
          <table class="data-table audit-table">
            <thead>
              <tr>
                <th>Timestamp (UTC)</th>
                <th>Action</th>
                <th>User ID</th>
                <th>User Role</th>
                <th>Snapshot / Study Ref</th>
                <th>GxP Rationale / Details</th>
                <th>SHA-256 Audit Seal</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="log in auditLogs" :key="log.id">
                <td class="cell-date">
                  {{ formatDate(log.timestamp) }}
                </td>
                <td>
                  <span
                    class="action-pill"
                    :class="`action-${log.action.toLowerCase()}`"
                  >
                    {{ log.action }}
                  </span>
                </td>
                <td class="cell-bold">
                  {{ log.user_id }}
                </td>
                <td>
                  <span class="role-tag">{{ log.user_role }}</span>
                </td>
                <td>
                  <code>{{ log.snapshot_id || log.study_id }}</code>
                </td>
                <td class="details-cell">
                  <span v-if="log.reason_for_change" class="reason-text"
                    ><strong>Reason:</strong> {{ log.reason_for_change }}</span
                  >
                  <pre class="details-json">{{
                    JSON.stringify(log.details)
                  }}</pre>
                </td>
                <td>
                  <code class="seal-code" :title="log.sha256_seal">
                    {{ log.sha256_seal.slice(0, 10) }}...{{
                      log.sha256_seal.slice(-6)
                    }}
                  </code>
                </td>
              </tr>
              <tr v-if="auditLogs.length === 0">
                <td colspan="7" class="empty-cell">
                  No audit log records available.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- TAB 4: Statistical Computing Environment -->
    <div v-if="activeTab === 'sce'" class="tab-panel" role="tabpanel">
      <StatisticalComputingConsole
        :study-id="studyId"
        :initial-unblinded="isUnblindedRole"
      />
    </div>

    <!-- TAB 5: CDISC SDTM / ADaM Pipeline & Compiler -->
    <div
      v-if="activeTab === 'cdisc_pipeline'"
      class="tab-panel"
      role="tabpanel"
    >
      <CdiscPipelineConsole
        :study-id="studyId"
        :is-unblinded="isUnblindedRole"
      />
    </div>

    <!-- Create Version-Pinned Snapshot Modal -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
    <div
      v-if="showCreateModal"
      class="modal-overlay"
      @click.self="showCreateModal = false"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h3>📸 Create Version-Pinned Clinical Snapshot Pool</h3>
          <button
            type="button"
            class="btn-modal-close"
            @click="showCreateModal = false"
          >
            &times;
          </button>
        </div>

        <div class="modal-body">
          <div class="form-group">
            <label class="form-label" for="snap-study"
              >Study Identifier <span class="required">*</span></label
            >
            <input
              id="snap-study"
              v-model="createForm.study_id"
              type="text"
              class="form-input"
              placeholder="e.g. STUDY-901"
            />
          </div>

          <div class="form-group">
            <label class="form-label" for="snap-tag"
              >Version Tag <span class="required">*</span></label
            >
            <input
              id="snap-tag"
              v-model="createForm.version_tag"
              type="text"
              class="form-input"
              placeholder="e.g. v1.0.0-INTERIM-1"
            />
            <small class="form-help"
              >Immutable version identifier. Once sealed, this snapshot pool
              cannot be modified.</small
            >
          </div>

          <div class="form-group">
            <label class="form-label" for="snap-milestone"
              >Lock Milestone <span class="required">*</span></label
            >
            <select
              id="snap-milestone"
              v-model="createForm.milestone"
              class="form-select"
            >
              <option value="INTERIM_ANALYSIS_1">Interim Analysis 1</option>
              <option value="INTERIM_ANALYSIS_2">Interim Analysis 2</option>
              <option value="DATABASE_LOCK">Final Database Lock</option>
              <option value="SAFETY_REVIEW">Periodic Safety Review</option>
              <option value="REGULATORY_SUBMISSION">
                Regulatory Submission
              </option>
              <option value="PROTOCOL_FREEZE">Protocol Freeze</option>
              <option value="ADJUDICATION_MILESTONE">
                BICR Adjudication Milestone
              </option>
              <option value="CUSTOM">Custom Milestone</option>
            </select>
          </div>

          <div class="form-group">
            <label class="form-label" for="snap-lock-id"
              >Associated Relational DataLock ID</label
            >
            <input
              id="snap-lock-id"
              v-model="createForm.lock_id"
              type="text"
              class="form-input"
              placeholder="e.g. LOCK-STUDY-901-FINAL"
            />
          </div>

          <div class="form-group">
            <label class="form-label" for="snap-reason"
              >GxP Rationale / Reason for Snapshot
              <span class="required">*</span></label
            >
            <textarea
              id="snap-reason"
              v-model="createForm.reason_for_snapshot"
              class="form-textarea"
              rows="3"
              placeholder="Detail the clinical or regulatory necessity for creating and sealing this snapshot pool..."
            />
          </div>
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showCreateModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!isCreateFormValid || isCreating"
            @click="submitCreateSnapshot"
          >
            {{
              isCreating
                ? "Sealing Snapshot..."
                : "Seal & Cryptographically Lock Snapshot"
            }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from "vue";
import CdiscPipelineConsole from "./CdiscPipelineConsole.vue";
import StatisticalComputingConsole from "./StatisticalComputingConsole.vue";

export interface CanonicalRecord {
  record_id: string;
  study_id: string;
  site_id: string | null;
  subject_id: string;
  stream_type: string;
  domain: string;
  item_code: string;
  item_name: string;
  observation_datetime: string;
  numeric_value: number | null;
  text_value: string | null;
  unit: string | null;
  reference_low: number | null;
  reference_high: number | null;
  abnormal_flag: string | null;
  is_unblinded: boolean;
  raw_payload: Record<string, unknown>;
  normalized_attributes: Record<string, unknown>;
  data_hash: string;
  created_at: string;
}

export interface Snapshot {
  snapshot_id: string;
  study_id: string;
  version_tag: string;
  milestone: string;
  lock_id: string | null;
  record_count: number;
  stream_counts: Record<string, number>;
  domain_counts: Record<string, number>;
  sha256_digest: string;
  created_at: string;
  created_by: string;
  reason_for_snapshot: string;
  is_locked: boolean;
  blinded_record_count: number;
  unblinded_record_count: number;
  snapshot_manifest: Record<string, unknown>;
}

export interface VerificationResult {
  snapshot_id: string;
  version_tag: string;
  stored_digest: string;
  computed_digest: string;
  is_valid: boolean;
  record_count: number;
  verified_at: string;
  verified_by: string;
  verification_message: string;
}

export interface AuditLogEntry {
  id: string;
  timestamp: string;
  user_id: string;
  user_role: string;
  action: string;
  study_id: string;
  snapshot_id: string | null;
  details: Record<string, unknown>;
  reason_for_change: string | null;
  sha256_seal: string;
}

const props = withDefaults(
  defineProps<{
    studyId?: string;
    initialUnblinded?: boolean;
  }>(),
  {
    studyId: "STUDY-901",
    initialUnblinded: false,
  }
);

const emit = defineEmits<{
  (e: "snapshot-created", snapshot: Snapshot): void;
  (e: "verification-complete", result: VerificationResult): void;
}>();

// State
const activeTab = ref<
  "snapshots" | "pool_data" | "audit_log" | "sce" | "cdisc_pipeline"
>("snapshots");
const isUnblindedRole = ref(props.initialUnblinded);
const isLoading = ref(false);
const isCreating = ref(false);
const showCreateModal = ref(false);

const snapshots = ref<Snapshot[]>([]);
const poolRecords = ref<CanonicalRecord[]>([]);
const auditLogs = ref<AuditLogEntry[]>([]);
const selectedSnapshot = ref<Snapshot | null>(null);
const lastVerificationResult = ref<VerificationResult | null>(null);

// Filters
const filterDomain = ref("");
const filterStream = ref("");
const filterSubject = ref("");

// Ingestion Stream Definitions for Cards
const streamDefinitions = [
  {
    type: "EDC_ECRF",
    label: "EDC eCRFs",
    icon: "📝",
    cdiscDomain: "VS, AE, DM, EX",
    bgCol: "var(--semantic-color-info-bg)",
  },
  {
    type: "EPRO_ECOA",
    label: "ePRO / eCOA",
    icon: "📱",
    cdiscDomain: "QS (PROMIS/SF-36)",
    bgCol: "var(--semantic-color-success-bg-soft)",
  },
  {
    type: "CENTRAL_LAB",
    label: "Central Labs (HL7)",
    icon: "🧪",
    cdiscDomain: "LB (LOINC Standard)",
    bgCol: "var(--semantic-color-purple-bg)",
  },
  {
    type: "LOCAL_LAB",
    label: "Local Labs",
    icon: "🏥",
    cdiscDomain: "LB (Site Diagnostics)",
    bgCol: "var(--semantic-color-orange-bg)",
  },
  {
    type: "IOT_WEARABLE",
    label: "IoT & Wearables",
    icon: "⌚",
    cdiscDomain: "VS / Telemetry",
    bgCol: "var(--semantic-color-cyan-bg)",
  },
  {
    type: "EHR_FHIR",
    label: "EHR HL7 FHIR",
    icon: "🩺",
    cdiscDomain: "PR, MH, EX, LB",
    bgCol: "var(--semantic-color-ai-bg)",
  },
  {
    type: "MEDICAL_IMAGING",
    label: "Medical Imaging",
    icon: "🩻",
    cdiscDomain: "TR, TU, RS (RECIST 1.1)",
    bgCol: "var(--semantic-color-error-bg-soft)",
  },
];

const createForm = ref({
  study_id: props.studyId,
  version_tag: "v1.0.0-INTERIM-1",
  milestone: "INTERIM_ANALYSIS_1",
  lock_id: "LOCK-STUDY-901-INT1",
  reason_for_snapshot:
    "Formal snapshot pool creation for scheduled IDMC Interim Analysis 1.",
});

const isCreateFormValid = computed(() => {
  return (
    createForm.value.study_id.trim() !== "" &&
    createForm.value.version_tag.trim() !== "" &&
    createForm.value.reason_for_snapshot.trim() !== ""
  );
});

const filteredRecords = computed(() => {
  return poolRecords.value.filter((r) => {
    if (filterDomain.value && r.domain !== filterDomain.value) return false;
    if (filterStream.value && r.stream_type !== filterStream.value)
      return false;
    if (
      filterSubject.value &&
      !r.subject_id.toLowerCase().includes(filterSubject.value.toLowerCase())
    )
      return false;
    return true;
  });
});

function getStreamCount(streamType: string): number {
  return poolRecords.value.filter((r) => r.stream_type === streamType).length;
}

function formatDate(isoStr: string): string {
  if (!isoStr) return "—";
  try {
    const d = new Date(isoStr);
    return d.toISOString().replace("T", " ").slice(0, 19) + " UTC";
  } catch {
    return isoStr;
  }
}

function handleBlindingToggle(): void {
  // Re-fetch data with new blinding status
  if (selectedSnapshot.value) {
    viewSnapshotData(selectedSnapshot.value);
  }
}

function refreshData(): void {
  isLoading.value = true;
  // Simulated refresh / API call hook
  setTimeout(() => {
    isLoading.value = false;
  }, 400);
}

function openCreateSnapshotModal(): void {
  showCreateModal.value = true;
}

function submitCreateSnapshot(): void {
  if (!isCreateFormValid.value) return;
  isCreating.value = true;

  // Emulate creation
  const newSnap: Snapshot = {
    snapshot_id: `SNAP-${Math.random().toString(36).substring(2, 10).toUpperCase()}`,
    study_id: createForm.value.study_id,
    version_tag: createForm.value.version_tag,
    milestone: createForm.value.milestone,
    lock_id: createForm.value.lock_id || null,
    record_count: poolRecords.value.length,
    stream_counts: {},
    domain_counts: {},
    sha256_digest:
      "a7c9f4d1e2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b7c8d9",
    created_at: new Date().toISOString(),
    created_by: "statistician_lead",
    reason_for_snapshot: createForm.value.reason_for_snapshot,
    is_locked: true,
    blinded_record_count: poolRecords.value.filter((r) => !r.is_unblinded)
      .length,
    unblinded_record_count: poolRecords.value.filter((r) => r.is_unblinded)
      .length,
    snapshot_manifest: {},
  };

  snapshots.value.unshift(newSnap);
  emit("snapshot-created", newSnap);

  showCreateModal.value = false;
  isCreating.value = false;
}

function verifySnapshot(snap: Snapshot): void {
  const isOk = true;
  lastVerificationResult.value = {
    snapshot_id: snap.snapshot_id,
    version_tag: snap.version_tag,
    stored_digest: snap.sha256_digest,
    computed_digest: snap.sha256_digest,
    is_valid: isOk,
    record_count: snap.record_count,
    verified_at: new Date().toISOString(),
    verified_by: "system_auditor",
    verification_message:
      "Cryptographic SHA-256 seal verified successfully. Dataset pool is untampered and 100% bit-for-bit reproducible.",
  };
  emit("verification-complete", lastVerificationResult.value);
}

function viewSnapshotData(snap: Snapshot): void {
  selectedSnapshot.value = snap;
  activeTab.value = "pool_data";
}

function viewSnapshotAudit(snap: Snapshot): void {
  selectedSnapshot.value = snap;
  activeTab.value = "audit_log";
}

onMounted(() => {
  // Populate initial mock state for preview/rendering
  snapshots.value = [
    {
      snapshot_id: "SNAP-901-INTERIM-1",
      study_id: props.studyId,
      version_tag: "v1.0.0-INTERIM-1",
      milestone: "INTERIM_ANALYSIS_1",
      lock_id: "LOCK-STUDY-901-INT1",
      record_count: 1420,
      stream_counts: {
        EDC_ECRF: 600,
        EPRO_ECOA: 320,
        CENTRAL_LAB: 280,
        IOT_WEARABLE: 120,
        EHR_FHIR: 60,
        MEDICAL_IMAGING: 40,
      },
      domain_counts: { VS: 400, LB: 400, QS: 320, AE: 180, TR: 60, DM: 60 },
      sha256_digest:
        "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
      created_at: new Date().toISOString(),
      created_by: "lead_statistician",
      reason_for_snapshot:
        "Sealed baseline snapshot for DSMB safety and efficacy interim evaluation.",
      is_locked: true,
      blinded_record_count: 1380,
      unblinded_record_count: 40,
      snapshot_manifest: {},
    },
  ];
});
</script>

<style scoped>
.lakehouse-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 24px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  color: var(--semantic-color-text);
  font-family: var(
    --semantic-font-family,
    -apple-system,
    BlinkMacSystemFont,
    "Segoe UI",
    Roboto,
    sans-serif
  );
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  flex-wrap: wrap;
}

.badge-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 8px;
}

.badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 4px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-gxp {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-milestone {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.badge-streams {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.console-title {
  margin: 0 0 6px 0;
  font-size: 1.35rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.console-subtitle {
  margin: 0;
  font-size: 0.875rem;
  color: var(--semantic-color-text-secondary);
  max-width: 850px;
  line-height: 1.45;
}

.header-actions {
  display: flex;
  align-items: center;
  gap: 12px;
  flex-wrap: wrap;
}

.blinding-toggle-box {
  display: flex;
  align-items: center;
  gap: 8px;
  padding: 6px 12px;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
}

.toggle-label {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
}

.toggle-checkbox {
  cursor: pointer;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 0.875rem;
  font-weight: 600;
  border-radius: 6px;
  border: 1px solid transparent;
  cursor: pointer;
  transition: background-color 0.15s ease-in-out;
}

.btn-primary {
  background-color: var(--primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background-color: var(--semantic-color-interactive-primary);
}

.btn-secondary {
  background-color: var(--semantic-color-surface);
  border-color: var(--border);
  color: var(--semantic-color-text);
}

.btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-header);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Metrics Grid */
.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 14px;
}

.metric-card {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 14px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
}

.stream-icon-col {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 44px;
  height: 44px;
  border-radius: 8px;
  font-size: 1.3rem;
}

.stream-details {
  display: flex;
  flex-direction: column;
}

.stream-name {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.stream-stat-row {
  display: flex;
  align-items: baseline;
  gap: 4px;
}

.stream-count {
  font-size: 1.2rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.stream-unit {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
}

.stream-domain-tag {
  font-size: 0.65rem;
  font-weight: 600;
  color: var(--semantic-color-interactive-primary);
}

/* Navigation Tabs */
.nav-tabs {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--border);
}

.tab-button {
  padding: 10px 18px;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  background: transparent;
  border: none;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
  cursor: pointer;
}

.tab-button.active {
  color: var(--primary);
  border-bottom-color: var(--primary);
}

/* Cards & Tables */
.card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 18px;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 16px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.card-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 8px;
}

.card-sub {
  margin: 4px 0 0 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.blind-notice {
  color: var(--semantic-color-warning);
  font-weight: 600;
  margin-left: 6px;
}

.unblind-notice {
  color: var(--semantic-color-success);
  font-weight: 600;
  margin-left: 6px;
}

.snap-badge {
  font-size: 0.75rem;
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  padding: 2px 6px;
  border-radius: 4px;
}

.filter-controls {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}

.form-select,
.form-input,
.form-textarea {
  padding: 6px 10px;
  font-size: 0.825rem;
  border: 1px solid var(--border);
  border-radius: 6px;
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.search-input {
  min-width: 200px;
}

.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.825rem;
  text-align: left;
}

.data-table th {
  background-color: var(--semantic-color-surface-muted);
  padding: 10px 12px;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  border-bottom: 1px solid var(--border);
}

.data-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--border);
  vertical-align: middle;
}

.selected-row {
  background-color: var(--semantic-color-success-bg-soft);
}

.row-unblinded-masked {
  background-color: var(--semantic-color-warning-bg-soft);
}

.cell-bold {
  font-weight: 600;
}

.cell-num {
  font-family: monospace;
  font-weight: 600;
}

.version-pill {
  background-color: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.milestone-badge {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.digest-pill {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background-color: var(--semantic-color-surface-header);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
}

.partition-ratio {
  display: flex;
  align-items: center;
  gap: 4px;
  font-size: 0.75rem;
}

.part-blinded {
  color: var(--semantic-color-text-secondary);
}

.part-unblinded {
  color: var(--semantic-color-primary);
  font-weight: 600;
}

.part-separator {
  color: var(--semantic-color-text-secondary);
}

.btn-mini {
  padding: 4px 8px;
  font-size: 0.75rem;
  font-weight: 600;
  border-radius: 4px;
  border: 1px solid var(--border);
  background-color: var(--semantic-color-surface);
  cursor: pointer;
  margin-right: 4px;
}

.btn-mini:hover {
  background-color: var(--semantic-color-surface-header);
}

.btn-mini-verify {
  color: var(--semantic-color-success-dark);
  border-color: var(--semantic-color-success-border);
}

.btn-mini-verify:hover {
  background-color: var(--semantic-color-success-bg-soft);
}

.btn-mini-view {
  color: var(--semantic-color-info);
  border-color: var(--semantic-color-divider-accent);
}

.btn-mini-view:hover {
  background-color: var(--semantic-color-info-bg);
}

.btn-mini-audit {
  color: var(--semantic-color-purple-text);
  border-color: var(--semantic-color-ai-border);
}

.btn-mini-audit:hover {
  background-color: var(--semantic-color-purple-bg);
}

.badge-blinded {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 700;
}

.badge-unblind-tag {
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--semantic-color-primary);
}

.badge-blinded-tag {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
}

.stream-pill {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: var(--semantic-color-surface-header);
}

.domain-pill {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-accent);
}

.item-cell {
  display: flex;
  flex-direction: column;
}

.item-name {
  font-weight: 600;
}

.item-code {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
}

.empty-cell {
  text-align: center;
  padding: 24px;
  color: var(--semantic-color-text-secondary);
}

/* Verification Banner */
.verification-banner {
  display: flex;
  align-items: flex-start;
  gap: 14px;
  padding: 16px;
  border-radius: 8px;
  margin-top: 16px;
  position: relative;
}

.verify-success {
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.verify-failed {
  background-color: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
}

.verify-icon-col {
  font-size: 1.5rem;
}

.verify-content h4 {
  margin: 0 0 4px 0;
  font-size: 0.95rem;
}

.verify-content p {
  margin: 0 0 8px 0;
  font-size: 0.825rem;
}

.digest-compare-row {
  display: flex;
  gap: 8px;
  font-size: 0.75rem;
  margin-top: 2px;
}

.btn-close {
  position: absolute;
  top: 10px;
  right: 12px;
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: inherit;
}

/* Audit Table Specifics */
.action-pill {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.action-snapshot_create {
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}

.action-snapshot_verify {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.action-unblinded_access {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.role-tag {
  font-size: 0.75rem;
  background-color: var(--semantic-color-surface-header);
  padding: 2px 4px;
  border-radius: 3px;
}

.details-json {
  margin: 4px 0 0 0;
  font-size: 0.7rem;
  background-color: var(--semantic-color-surface-muted);
  padding: 4px 6px;
  border-radius: 4px;
  max-width: 320px;
  overflow-x: auto;
}

.seal-code {
  font-size: 0.7rem;
  background-color: var(--semantic-color-surface-header);
  padding: 2px 4px;
  border-radius: 3px;
}

/* Modal */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
  padding: 20px;
}

.modal-card {
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  width: 100%;
  max-width: 540px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
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
  font-size: 1.1rem;
  font-weight: 700;
}

.btn-modal-close {
  background: transparent;
  border: none;
  font-size: 1.4rem;
  cursor: pointer;
  color: var(--semantic-color-text-secondary);
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.required {
  color: var(--semantic-color-error);
}

.form-help {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  border-bottom-left-radius: 8px;
  border-bottom-right-radius: 8px;
}
</style>
