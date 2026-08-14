<template>
  <div class="amendment-view-container">
    <!-- Header Section -->
    <div class="view-header">
      <div class="header-content">
        <div class="title-row">
          <h2 class="view-title">
            Protocol Amendments &amp; In-Flight Subject Migration
          </h2>
          <span class="badge badge-primary">Zero-Downtime Engine</span>
        </div>
        <p class="view-description">
          Graph-native immutable versioning and dynamic subject schema
          projection. Compare version graphs, inspect field deltas, and track
          in-flight patient re-consent compliance.
        </p>
      </div>

      <div class="header-actions">
        <button
          id="btn-create-amendment"
          class="btn btn-primary"
          @click="showCreateModal = true"
        >
          <span class="btn-icon">➕</span>
          <span>Draft New Amendment</span>
        </button>
      </div>
    </div>

    <!-- Protocol Version Selection & Diff Controls -->
    <div class="controls-panel">
      <div class="version-selectors">
        <div class="selector-group">
          <label for="base-version-select" class="selector-label"
            >Base Version (Frozen):</label
          >
          <select
            id="base-version-select"
            v-model="selectedBaseVersion"
            class="form-select"
          >
            <option value="1.0.0">v1.0.0 (Approved / Locked)</option>
            <option value="1.1.0">v1.1.0 (Locked)</option>
          </select>
        </div>

        <div class="diff-arrow">➔</div>

        <div class="selector-group">
          <label for="amended-version-select" class="selector-label"
            >Amended Target Version:</label
          >
          <select
            id="amended-version-select"
            v-model="selectedAmendedVersion"
            class="form-select"
          >
            <option value="2.0.0">v2.0.0-AMENDMENT (Approved / Active)</option>
            <option value="2.1.0-DRAFT">v2.1.0-DRAFT (Drafting)</option>
          </select>
        </div>
      </div>

      <div class="version-meta-tags">
        <span class="meta-tag">
          <strong>Re-Consent Mandated:</strong>
          <span
            class="tag-status"
            :class="requiresReconsent ? 'status-alert' : 'status-ok'"
          >
            {{ requiresReconsent ? "YES (PRD-SUB-007)" : "NO" }}
          </span>
        </span>
        <span class="meta-tag">
          <strong>Graph Status:</strong>
          <span class="tag-status status-immutable">IMMUTABLE BRANCH</span>
        </span>
      </div>
    </div>

    <!-- Section 1: Subject Impact Analyzer Dashboard -->
    <div class="dashboard-section">
      <div class="section-header">
        <h3 class="section-title">
          📊 In-Flight Subject Migration &amp; Re-Consent Analyzer
        </h3>
        <span class="subject-total-counter"
          >Total In-Flight Cohort:
          <strong>{{ activeSubjectCount }} Subjects</strong></span
        >
      </div>

      <div class="impact-metrics-grid">
        <!-- Migrated & Re-Consented -->
        <div class="metric-card metric-green">
          <div class="card-header">
            <span class="card-badge badge-green"
              >MIGRATED &amp; RE-CONSENTED</span
            >
            <span class="metric-count">{{ impactStats.migrated.length }}</span>
          </div>
          <p class="metric-label">
            Subjects executing on Target Schema v{{ selectedAmendedVersion }}
          </p>
          <div class="progress-bar-container">
            <div
              class="progress-bar bar-green"
              :style="{
                width: getPercentage(impactStats.migrated.length) + '%',
              }"
            />
          </div>
        </div>

        <!-- Pending Re-Consent -->
        <div class="metric-card metric-yellow">
          <div class="card-header">
            <span class="card-badge badge-yellow">PENDING RE-CONSENT</span>
            <span class="metric-count">{{ impactStats.pending.length }}</span>
          </div>
          <p class="metric-label">
            eCRF Data Entry Gated until ICF Signed (PRD-SUB-007)
          </p>
          <div class="progress-bar-container">
            <div
              class="progress-bar bar-yellow"
              :style="{
                width: getPercentage(impactStats.pending.length) + '%',
              }"
            />
          </div>
        </div>

        <!-- Completed under Previous Version -->
        <div class="metric-card metric-gray">
          <div class="card-header">
            <span class="card-badge badge-gray">COMPLETED UNDER PREVIOUS</span>
            <span class="metric-count">{{
              impactStats.completedPrev.length
            }}</span>
          </div>
          <p class="metric-label">
            Historical Visits Preserved under v{{ selectedBaseVersion }} Schema
          </p>
          <div class="progress-bar-container">
            <div
              class="progress-bar bar-gray"
              :style="{
                width: getPercentage(impactStats.completedPrev.length) + '%',
              }"
            />
          </div>
        </div>
      </div>

      <!-- Subject Table Breakdown -->
      <div class="subject-table-wrapper">
        <table class="data-table">
          <thead>
            <tr>
              <th>Subject ID</th>
              <th>Current Status</th>
              <th>Active Protocol Tag</th>
              <th>Consent Status</th>
              <th>Data Entry Gating State</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="sub in subjectsList"
              :key="sub.id"
              :class="'row-' + sub.category"
            >
              <td class="cell-id">
                <strong>{{ sub.id }}</strong>
              </td>
              <td>
                <span class="state-pill">{{ sub.status }}</span>
              </td>
              <td>
                <span class="version-tag"
                  >v{{ sub.active_protocol_version }}</span
                >
              </td>
              <td>
                <span :class="['consent-badge', 'badge-' + sub.consentColor]">
                  {{ sub.consentText }}
                </span>
              </td>
              <td>
                <span v-if="sub.isGated" class="gating-pill pill-locked">
                  🔒 Gated (Re-Consent Required)
                </span>
                <span v-else class="gating-pill pill-unlocked">
                  ✅ Active &amp; Projected
                </span>
              </td>
              <td>
                <button
                  v-if="sub.isGated"
                  class="btn btn-sm btn-action"
                  @click="openReconsentModal(sub)"
                >
                  Clear Re-Consent Gate
                </button>
                <span v-else class="text-muted">Compliant</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Section 2: Visual Graph & Schema Diff Visualizer -->
    <div class="diff-section">
      <div class="section-header">
        <h3 class="section-title">🔀 Side-by-Side Protocol Graph Diff</h3>
        <div class="legend-box">
          <span class="legend-item"
            ><span class="color-dot dot-green" /> Added Visits/Activities</span
          >
          <span class="legend-item"
            ><span class="color-dot dot-yellow" /> Modified Constraints</span
          >
          <span class="legend-item"
            ><span class="color-dot dot-red" /> Deprecated Procedures</span
          >
        </div>
      </div>

      <div class="graph-diff-grid">
        <!-- Base Version Column -->
        <div class="graph-column">
          <div class="column-header base-header">
            <h4>Base Protocol Version (v{{ selectedBaseVersion }})</h4>
            <span class="badge badge-locked">LOCKED IMMUTABLE</span>
          </div>

          <div class="nodes-container">
            <div
              v-for="item in graphDiff.baseNodes"
              :key="item.id"
              class="graph-node node-base"
              :class="item.statusClass"
            >
              <div class="node-title-row">
                <span class="node-type-badge">{{ item.type }}</span>
                <span class="node-name">{{ item.name }}</span>
              </div>
              <div class="node-details">
                <span class="node-spec">{{ item.spec }}</span>
                <span class="node-schedule">{{ item.schedule }}</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Comparison Separator -->
        <div class="graph-divider">
          <div class="diff-line" />
        </div>

        <!-- Amended Version Column -->
        <div class="graph-column">
          <div class="column-header amended-header">
            <h4>Amended Protocol Version (v{{ selectedAmendedVersion }})</h4>
            <span class="badge badge-active">ACTIVE PROJECTION</span>
          </div>

          <div class="nodes-container">
            <div
              v-for="item in graphDiff.amendedNodes"
              :key="item.id"
              class="graph-node"
              :class="item.diffType"
            >
              <div class="node-title-row">
                <span class="node-type-badge">{{ item.type }}</span>
                <span class="node-name">{{ item.name }}</span>
                <span class="diff-badge" :class="'diff-badge-' + item.diffType">
                  {{ item.diffBadgeText }}
                </span>
              </div>
              <div class="node-details">
                <span class="node-spec">{{ item.spec }}</span>
                <span class="node-schedule">{{ item.schedule }}</span>
              </div>
              <div v-if="item.deltaNote" class="delta-annotation">
                <span class="delta-icon">ℹ️</span> {{ item.deltaNote }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Modal: Draft New Amendment -->
    <div
      v-if="showCreateModal"
      class="modal-overlay"
      @click.self="showCreateModal = false"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h3 class="modal-title">Draft Protocol Amendment</h3>
          <button class="modal-close" @click="showCreateModal = false">
            &times;
          </button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label class="form-label">Base Study Version:</label>
            <input type="text" class="form-control" value="1.0.0" disabled />
          </div>
          <div class="form-group">
            <label class="form-label">Amendment Classification:</label>
            <select v-model="newAmendment.amendment_type" class="form-select">
              <option value="major">
                Major Amendment (Structural / Safety Changes)
              </option>
              <option value="minor">
                Minor Amendment (Administrative / Clarification)
              </option>
            </select>
          </div>
          <div class="form-group checkbox-group">
            <label class="checkbox-label">
              <input
                v-model="newAmendment.requires_reconsent"
                type="checkbox"
              />
              <span
                ><strong
                  >Requires Subject Re-Consent (PRD-SUB-007)</strong
                ></span
              >
            </label>
            <small class="form-hint">
              When checked, site data entry for in-flight subjects is locked
              until a signed ICF matching the new version is registered.
            </small>
          </div>
          <div class="form-group">
            <label class="form-label"
              >GxP Justification &amp; Change Reason:</label
            >
            <textarea
              v-model="newAmendment.change_reason"
              class="form-control text-area"
              placeholder="e.g. Protocol Amendment 2.0 introducing optional PK visit and updating dosing cohort safety rules..."
              rows="3"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showCreateModal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="!newAmendment.change_reason.trim() || isSubmitting"
            @click="submitCreateAmendment"
          >
            {{
              isSubmitting
                ? "Cloning Graph Hierarchy..."
                : "Clone & Create Amendment"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- Modal: Re-Consent Resolution -->
    <div
      v-if="showReconsentModal"
      class="modal-overlay"
      @click.self="showReconsentModal = false"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h3 class="modal-title">
            Clear Subject Re-Consent Gate (PRD-SUB-007)
          </h3>
          <button class="modal-close" @click="showReconsentModal = false">
            &times;
          </button>
        </div>
        <div class="modal-body">
          <p>
            Subject <strong>{{ activeModalSubject?.id }}</strong> is currently
            locked from data entry on upcoming visits under Protocol Amendment
            <strong>v{{ selectedAmendedVersion }}</strong
            >.
          </p>
          <div class="reconsent-options">
            <div
              class="option-card"
              :class="{ selected: reconsentMode === 'ECONSENT' }"
              @click="reconsentMode = 'ECONSENT'"
            >
              <h4>✍️ Execute eConsent</h4>
              <p>
                Register electronic signature verified with 21 CFR Part 11
                cryptographic seal.
              </p>
            </div>
            <div
              class="option-card"
              :class="{ selected: reconsentMode === 'PAPER' }"
              @click="reconsentMode = 'PAPER'"
            >
              <h4>📄 Upload Signed Paper ICF</h4>
              <p>
                Record site PI verified paper ICF signed and dated by the
                patient.
              </p>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showReconsentModal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="isSubmitting"
            @click="submitReconsent"
          >
            {{
              isSubmitting
                ? "Registering & Unlocking..."
                : "Register Signed Consent & Unlock eCRF"
            }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";

// State
const selectedBaseVersion = ref("1.0.0");
const selectedAmendedVersion = ref("2.0.0");
const requiresReconsent = ref(true);
const showCreateModal = ref(false);
const showReconsentModal = ref(false);
const isSubmitting = ref(false);
const reconsentMode = ref("ECONSENT");
const activeModalSubject = ref(null);

const newAmendment = ref({
  amendment_type: "major",
  requires_reconsent: true,
  change_reason: "",
});

// Subject Cohort
const subjectsList = ref([
  {
    id: "SUBJ-101",
    status: "ACTIVE",
    active_protocol_version: "2.0.0",
    consentText: "Signed ICF v2.0.0",
    consentColor: "green",
    category: "migrated",
    isGated: false,
  },
  {
    id: "SUBJ-102",
    status: "ACTIVE",
    active_protocol_version: "1.0.0",
    consentText: "Pending ICF v2.0.0",
    consentColor: "yellow",
    category: "pending",
    isGated: true,
  },
  {
    id: "SUBJ-103",
    status: "ENROLLED",
    active_protocol_version: "1.0.0",
    consentText: "Pending ICF v2.0.0",
    consentColor: "yellow",
    category: "pending",
    isGated: true,
  },
  {
    id: "SUBJ-104",
    status: "COMPLETED",
    active_protocol_version: "1.0.0",
    consentText: "Historical v1.0.0",
    consentColor: "gray",
    category: "completedPrev",
    isGated: false,
  },
  {
    id: "SUBJ-105",
    status: "ACTIVE",
    active_protocol_version: "2.0.0",
    consentText: "Signed ICF v2.0.0",
    consentColor: "green",
    category: "migrated",
    isGated: false,
  },
]);

const activeSubjectCount = computed(() => subjectsList.value.length);

const impactStats = computed(() => {
  return {
    migrated: subjectsList.value.filter((s) => s.category === "migrated"),
    pending: subjectsList.value.filter((s) => s.category === "pending"),
    completedPrev: subjectsList.value.filter(
      (s) => s.category === "completedPrev"
    ),
  };
});

function getPercentage(count) {
  if (!activeSubjectCount.value) return 0;
  return Math.round((count / activeSubjectCount.value) * 100);
}

// Graph Diff Data
const graphDiff = ref({
  baseNodes: [
    {
      id: "b-arm1",
      type: "Study Arm",
      name: "Arm A: Active Dose",
      spec: "Cohort: 100mg Daily",
      schedule: "4 Epochs",
      statusClass: "node-unchanged",
    },
    {
      id: "b-v1",
      type: "Visit Encounter",
      name: "Visit 1: Screening",
      spec: "eCRF Forms: Demographics, Eligibility",
      schedule: "Day -7",
      statusClass: "node-unchanged",
    },
    {
      id: "b-v2",
      type: "Visit Encounter",
      name: "Visit 2: Baseline",
      spec: "eCRF Forms: Vitals, ECG, Labs",
      schedule: "Day 1",
      statusClass: "node-unchanged",
    },
    {
      id: "b-v3",
      type: "Visit Encounter",
      name: "Visit 3: Treatment Cycle 1",
      spec: "eCRF Forms: Dosing, Safety Labs",
      schedule: "Day 14",
      statusClass: "node-unchanged",
    },
    {
      id: "b-act1",
      type: "Procedure",
      name: "Standard Safety Chemistry",
      spec: "Assay: CBC + Chem Panel",
      schedule: "Bi-weekly",
      statusClass: "node-unchanged",
    },
  ],
  amendedNodes: [
    {
      id: "a-arm1",
      type: "Study Arm",
      name: "Arm A: Active Dose",
      spec: "Cohort: 100mg Daily",
      schedule: "4 Epochs",
      diffType: "node-unchanged",
      diffBadgeText: "Preserved",
    },
    {
      id: "a-v1",
      type: "Visit Encounter",
      name: "Visit 1: Screening",
      spec: "eCRF Forms: Demographics, Eligibility",
      schedule: "Day -7",
      diffType: "node-unchanged",
      diffBadgeText: "Preserved",
    },
    {
      id: "a-v2",
      type: "Visit Encounter",
      name: "Visit 2: Baseline",
      spec: "eCRF Forms: Vitals, ECG, Labs",
      schedule: "Day 1",
      diffType: "node-unchanged",
      diffBadgeText: "Preserved",
    },
    {
      id: "a-v3",
      type: "Visit Encounter",
      name: "Visit 3: Treatment Cycle 1",
      spec: "eCRF Forms: Dosing, Safety Labs, PK Blood Draw",
      schedule: "Day 14",
      diffType: "node-modified",
      diffBadgeText: "Modified",
      deltaNote:
        "Added PK Blood Draw form and expanded safety lab range criteria.",
    },
    {
      id: "a-v4",
      type: "Visit Encounter",
      name: "Visit 3.5: Interim PK Assessment",
      spec: "eCRF Forms: Pharmacokinetics, Biomarkers",
      schedule: "Day 21",
      diffType: "node-added",
      diffBadgeText: "Added",
      deltaNote: "New mid-cycle pharmacokinetic visit added in Amendment 2.0.",
    },
    {
      id: "a-act1",
      type: "Procedure",
      name: "Standard Safety Chemistry",
      spec: "Assay: CBC + Chem Panel + Biomarkers",
      schedule: "Bi-weekly",
      diffType: "node-modified",
      diffBadgeText: "Modified",
      deltaNote: "Added high-sensitivity troponin biomarker requirement.",
    },
  ],
});

// Actions
function openReconsentModal(subject) {
  activeModalSubject.value = subject;
  showReconsentModal.value = true;
}

async function submitReconsent() {
  if (!activeModalSubject.value) return;
  isSubmitting.value = true;
  try {
    // Simulate API registration
    await new Promise((resolve) => setTimeout(resolve, 600));

    const targetSub = subjectsList.value.find(
      (s) => s.id === activeModalSubject.value.id
    );
    if (targetSub) {
      targetSub.active_protocol_version = selectedAmendedVersion.value;
      targetSub.consentText = `Signed ICF v${selectedAmendedVersion.value}`;
      targetSub.consentColor = "green";
      targetSub.category = "migrated";
      targetSub.isGated = false;
    }
    showReconsentModal.value = false;
  } finally {
    isSubmitting.value = false;
  }
}

async function submitCreateAmendment() {
  isSubmitting.value = true;
  try {
    await new Promise((resolve) => setTimeout(resolve, 800));
    showCreateModal.value = false;
    newAmendment.value.change_reason = "";
  } finally {
    isSubmitting.value = false;
  }
}
</script>

<style scoped>
.amendment-view-container {
  padding: 1.5rem 2rem;
  max-width: 100%;
  box-sizing: border-box;
  color: var(--text-main, #1e293b);
}

.view-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 1.5rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.view-title {
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0;
}

.view-description {
  color: var(--text-muted, #64748b);
  margin-top: 0.25rem;
  font-size: 0.925rem;
  max-width: 900px;
}

.controls-panel {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 1rem 1.25rem;
  margin-bottom: 1.5rem;
}

.version-selectors {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.selector-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.selector-label {
  font-size: 0.8rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--text-muted, #64748b);
}

.diff-arrow {
  font-size: 1.25rem;
  color: var(--primary, #2563eb);
  margin-top: 1rem;
}

.form-select,
.form-control {
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border, #cbd5e1);
  border-radius: var(--radius-sm, 6px);
  background: #ffffff;
  font-size: 0.9rem;
}

.version-meta-tags {
  display: flex;
  gap: 1rem;
}

.meta-tag {
  font-size: 0.85rem;
  padding: 0.4rem 0.75rem;
  background: var(--surface-alt, #f8fafc);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-sm, 6px);
}

.status-alert {
  color: #dc2626;
  font-weight: 700;
}

.status-ok {
  color: #16a34a;
  font-weight: 700;
}

.status-immutable {
  color: #475569;
  font-weight: 600;
}

/* Dashboard Section */
.dashboard-section,
.diff-section {
  background: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 1.25rem;
  margin-bottom: 1.75rem;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--border, #f1f5f9);
}

.section-title {
  font-size: 1.15rem;
  font-weight: 600;
  margin: 0;
}

.subject-total-counter {
  font-size: 0.9rem;
  color: var(--text-muted, #64748b);
}

.impact-metrics-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
  margin-bottom: 1.25rem;
}

.metric-card {
  padding: 1rem;
  border-radius: var(--radius-md, 8px);
  border: 1px solid var(--border, #e2e8f0);
}

.metric-green {
  background: #f0fdf4;
  border-color: #bbf7d0;
}

.metric-yellow {
  background: #fefce8;
  border-color: #fef08a;
}

.metric-gray {
  background: #f8fafc;
  border-color: #e2e8f0;
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
}

.card-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.badge-green {
  background: #dcfce7;
  color: #166534;
}
.badge-yellow {
  background: #fef9c3;
  color: #854d0e;
}
.badge-gray {
  background: #e2e8f0;
  color: #475569;
}

.metric-count {
  font-size: 1.5rem;
  font-weight: 700;
}

.metric-label {
  font-size: 0.85rem;
  color: var(--text-muted, #64748b);
  margin-bottom: 0.75rem;
}

.progress-bar-container {
  height: 6px;
  background: #e2e8f0;
  border-radius: 3px;
  overflow: hidden;
}

.progress-bar {
  height: 100%;
  transition: width 0.3s ease;
}
.bar-green {
  background: #22c55e;
}
.bar-yellow {
  background: #eab308;
}
.bar-gray {
  background: #94a3b8;
}

/* Data Table */
.subject-table-wrapper {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.data-table th,
.data-table td {
  padding: 0.75rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--border, #f1f5f9);
}

.data-table th {
  background: var(--surface-alt, #f8fafc);
  font-weight: 600;
  color: var(--text-muted, #64748b);
}

.state-pill {
  font-size: 0.75rem;
  padding: 0.2rem 0.5rem;
  background: #f1f5f9;
  border-radius: 4px;
  font-weight: 600;
}

.version-tag {
  font-weight: 600;
  color: var(--primary, #2563eb);
}

.consent-badge {
  font-size: 0.8rem;
  font-weight: 600;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.gating-pill {
  font-size: 0.8rem;
  font-weight: 600;
  padding: 0.25rem 0.6rem;
  border-radius: 4px;
}

.pill-locked {
  background: #fee2e2;
  color: #991b1b;
}

.pill-unlocked {
  background: #dcfce7;
  color: #166534;
}

/* Graph Diff Grid */
.graph-diff-grid {
  display: grid;
  grid-template-columns: 1fr 40px 1fr;
  gap: 1rem;
}

.graph-column {
  background: var(--surface-alt, #f8fafc);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-md, 8px);
  padding: 1rem;
}

.column-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.column-header h4 {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
}

.badge-locked {
  background: #e2e8f0;
  color: #475569;
}
.badge-active {
  background: #dbeafe;
  color: #1e40af;
}

.nodes-container {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.graph-node {
  background: #ffffff;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: var(--radius-sm, 6px);
  padding: 0.75rem 1rem;
  transition: all 0.2s ease;
}

.node-unchanged {
  border-left: 4px solid #94a3b8;
}

.node-modified {
  border-left: 4px solid #eab308;
  background: #fefce8;
}

.node-added {
  border-left: 4px solid #22c55e;
  background: #f0fdf4;
}

.node-deprecated {
  border-left: 4px solid #ef4444;
  background: #fef2f2;
}

.node-title-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.4rem;
}

.node-type-badge {
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  background: #f1f5f9;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
  color: #475569;
}

.node-name {
  font-weight: 600;
  font-size: 0.95rem;
}

.diff-badge {
  margin-left: auto;
  font-size: 0.7rem;
  font-weight: 700;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
}

.diff-badge-node-added {
  background: #dcfce7;
  color: #166534;
}
.diff-badge-node-modified {
  background: #fef9c3;
  color: #854d0e;
}
.diff-badge-node-unchanged {
  background: #f1f5f9;
  color: #64748b;
}

.node-details {
  display: flex;
  justify-content: space-between;
  font-size: 0.85rem;
  color: var(--text-muted, #64748b);
}

.delta-annotation {
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px dashed #cbd5e1;
  font-size: 0.825rem;
  color: #475569;
}

.legend-box {
  display: flex;
  gap: 1rem;
  font-size: 0.825rem;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.color-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
}

.dot-green {
  background: #22c55e;
}
.dot-yellow {
  background: #eab308;
}
.dot-red {
  background: #ef4444;
}

.graph-divider {
  display: flex;
  justify-content: center;
  align-items: center;
}

.diff-line {
  width: 2px;
  height: 100%;
  background: var(--border, #e2e8f0);
}

/* Modals */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-card {
  background: #ffffff;
  border-radius: var(--radius-md, 8px);
  width: 540px;
  max-width: 90vw;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
  overflow: hidden;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.modal-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 600;
}

.modal-close {
  background: none;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
}

.modal-body {
  padding: 1.25rem;
}

.form-group {
  margin-bottom: 1rem;
}

.form-label {
  display: block;
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 0.35rem;
}

.checkbox-group {
  margin: 1.25rem 0;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.form-hint {
  display: block;
  color: var(--text-muted, #64748b);
  margin-top: 0.25rem;
  font-size: 0.8rem;
}

.text-area {
  width: 100%;
  box-sizing: border-box;
  resize: vertical;
}

.reconsent-options {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  margin-top: 1rem;
}

.option-card {
  border: 2px solid var(--border, #e2e8f0);
  border-radius: var(--radius-sm, 6px);
  padding: 1rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.option-card h4 {
  margin: 0 0 0.5rem 0;
  font-size: 0.95rem;
}

.option-card p {
  margin: 0;
  font-size: 0.8rem;
  color: var(--text-muted, #64748b);
}

.option-card.selected {
  border-color: var(--primary, #2563eb);
  background: #eff6ff;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--border, #e2e8f0);
  background: var(--surface-alt, #f8fafc);
}

.btn {
  padding: 0.5rem 1rem;
  border-radius: var(--radius-sm, 6px);
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.btn-primary {
  background: var(--primary, #2563eb);
  color: #ffffff;
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-secondary {
  background: #ffffff;
  border-color: var(--border, #cbd5e1);
  color: var(--text-main, #1e293b);
}

.btn-sm {
  padding: 0.25rem 0.6rem;
  font-size: 0.8rem;
}

.btn-action {
  background: #fee2e2;
  border-color: #fca5a5;
  color: #991b1b;
}

.badge {
  font-size: 0.75rem;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-weight: 700;
}

.badge-primary {
  background: #dbeafe;
  color: #1e40af;
}

.text-muted {
  color: var(--text-muted, #94a3b8);
  font-size: 0.85rem;
}
</style>
