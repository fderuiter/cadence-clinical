<template>
  <div class="disproportionality-signal-reviewer" role="region" aria-label="Pharmacovigilance Disproportionality Signal Detection & Periodic Safety Reporting">
    <!-- Header & Summary Bar -->
    <header class="signal-header">
      <div class="header-main">
        <div class="title-group">
          <span class="badge badge-regulatory">CIOMS VIII / ICH E2F / FDA MGPS</span>
          <h2 class="signal-title">Safety Signal Detection Engine & Periodic Reports</h2>
        </div>
        <div class="header-metadata">
          <span class="meta-item"><strong>Target Drug:</strong> {{ targetDrug }}</span>
          <span class="meta-item"><strong>Study:</strong> {{ studyId || 'Global Safety Pool' }}</span>
          <span class="meta-item"><strong>Signals Detected:</strong>
            <span class="status-pill signal-count">{{ activeSignalsCount }}</span>
          </span>
          <span class="meta-item"><strong>Alerts Under Review:</strong>
            <span class="status-pill alert-count">{{ pendingAlertsCount }}</span>
          </span>
        </div>
      </div>

      <div class="header-actions">
        <button
          class="btn btn-secondary"
          @click="executeScan"
          :disabled="isScanning"
        >
          <span class="icon">🔄</span> {{ isScanning ? 'Running Vectorized Scan...' : 'Run Disproportionality Scan' }}
        </button>
        <button
          class="btn btn-primary"
          @click="openCompileReportModal"
          :disabled="isCompiling"
        >
          <span class="icon">📑</span> Compile DSUR / PSUR
        </button>
      </div>
    </header>

    <!-- Navigation Tabs -->
    <nav class="nav-tabs" role="tablist" aria-label="Safety Signal Views">
      <button
        role="tab"
        :aria-selected="activeTab === 'signals'"
        :class="['tab-button', { active: activeTab === 'signals' }]"
        @click="activeTab = 'signals'"
      >
        📊 Statistical Signals (PRR / ROR / EBGM)
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'alerts'"
        :class="['tab-button', { active: activeTab === 'alerts' }]"
        @click="activeTab = 'alerts'"
      >
        🚨 Signal Review Alerts ({{ pendingAlertsCount }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'reports'"
        :class="['tab-button', { active: activeTab === 'reports' }]"
        @click="activeTab = 'reports'"
      >
        📄 Periodic Reports (DSUR / PSUR)
      </button>
    </nav>

    <!-- Main Content Panes -->
    <main class="main-content">
      <!-- TAB 1: Statistical Disproportionality Signals Matrix -->
      <section v-if="activeTab === 'signals'" class="tab-pane" aria-labelledby="signals-tab-heading">
        <div class="pane-toolbar">
          <div class="filter-group">
            <label for="level-filter">Stratification:</label>
            <select id="level-filter" v-model="selectedLevel" class="select-input">
              <option value="ALL">All (PT & SOC)</option>
              <option value="PT">Preferred Term (PT)</option>
              <option value="SOC">System Organ Class (SOC)</option>
            </select>
          </div>

          <div class="filter-group">
            <label for="search-filter">Search Term:</label>
            <input
              id="search-filter"
              v-model="searchTerm"
              type="text"
              placeholder="Filter by PT or SOC name..."
              class="text-input"
            />
          </div>

          <div class="filter-group toggle-group">
            <label>
              <input type="checkbox" v-model="showOnlySignals" />
              Show Threshold Breaches Only (Evans / ROR / EB05)
            </label>
          </div>
        </div>

        <div class="table-container">
          <table class="data-table" aria-label="Disproportionality Statistical Metrics Table">
            <thead>
              <tr>
                <th>Level</th>
                <th>MedDRA Term</th>
                <th>Parent SOC</th>
                <th>Cases (a)</th>
                <th>Expected (E)</th>
                <th>PRR (95% CI)</th>
                <th>Chi² (Yates)</th>
                <th>ROR (95% CI)</th>
                <th>EBGM (EB05 - EB95)</th>
                <th>Signal Consensus</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="item in filteredMetrics"
                :key="item.meddra_pt + item.term_level"
                :class="{ 'signal-row-critical': item.signal_strength === 'CRITICAL', 'signal-row-active': item.overall_signal_flag }"
              >
                <td><span class="level-badge">{{ item.term_level }}</span></td>
                <td><strong>{{ item.meddra_pt }}</strong></td>
                <td>{{ item.meddra_soc || '-' }}</td>
                <td>{{ item.case_count_a }}</td>
                <td>{{ item.expected_count_e }}</td>
                <td>
                  <span :class="{ 'metric-alert': item.is_prr_signal }">
                    {{ item.prr.toFixed(2) }}
                  </span>
                  <span class="ci-bounds">[{{ item.prr_ci_lower.toFixed(2)}, {{ item.prr_ci_upper.toFixed(2) }}]</span>
                </td>
                <td>
                  <span :class="{ 'metric-alert': item.chi_square_yates >= 4.0 }">
                    {{ item.chi_square_yates.toFixed(2) }}
                  </span>
                </td>
                <td>
                  <span :class="{ 'metric-alert': item.is_ror_signal }">
                    {{ item.ror.toFixed(2) }}
                  </span>
                  <span class="ci-bounds">[{{ item.ror_ci_lower.toFixed(2)}, {{ item.ror_ci_upper.toFixed(2) }}]</span>
                </td>
                <td>
                  <span :class="{ 'metric-alert': item.is_ebgm_signal }">
                    {{ item.ebgm.toFixed(2) }}
                  </span>
                  <span class="ci-bounds">({{ item.eb05.toFixed(2)} - {{ item.eb95.toFixed(2)}})</span>
                </td>
                <td>
                  <span :class="['strength-pill', item.signal_strength.toLowerCase()]">
                    {{ item.signal_strength }}
                  </span>
                </td>
                <td>
                  <button
                    v-if="item.overall_signal_flag"
                    class="btn btn-sm btn-outline"
                    @click="triggerManualAlert(item)"
                  >
                    Review Alert
                  </button>
                  <span v-else class="text-muted">Nominal</span>
                </td>
              </tr>
              <tr v-if="filteredMetrics.length === 0">
                <td colspan="11" class="empty-state">No disproportionality metrics match the active filters.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 2: Signal Review Alerts Queue & Triage -->
      <section v-if="activeTab === 'alerts'" class="tab-pane" aria-labelledby="alerts-tab-heading">
        <div class="table-container">
          <table class="data-table" aria-label="Signal Review Alerts Queue">
            <thead>
              <tr>
                <th>Alert ID</th>
                <th>Target Drug</th>
                <th>MedDRA Term</th>
                <th>Severity</th>
                <th>Status</th>
                <th>PRR / ROR / EB05</th>
                <th>Triage Summary</th>
                <th>Assigned Officer</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="alert in alertsList"
                :key="alert.id"
                :class="['alert-row', alert.severity.toLowerCase()]"
              >
                <td><code>{{ alert.id }}</code></td>
                <td>{{ alert.target_drug }}</td>
                <td><strong>{{ alert.meddra_term }}</strong> ({{ alert.term_level }})</td>
                <td><span :class="['severity-pill', alert.severity.toLowerCase()]">{{ alert.severity }}</span></td>
                <td><span :class="['status-pill', alert.status.toLowerCase()]">{{ alert.status }}</span></td>
                <td>PRR: {{ alert.prr.toFixed(1) }} | ROR: {{ alert.ror.toFixed(1) }} | EB05: {{ alert.eb05.toFixed(1) }}</td>
                <td class="cell-triage">{{ alert.triage_summary }}</td>
                <td>{{ alert.assigned_safety_officer || 'Unassigned' }}</td>
                <td class="actions-cell">
                  <button
                    v-if="alert.status === 'TRIGGERED' || alert.status === 'UNDER_REVIEW'"
                    class="btn btn-sm btn-primary"
                    @click="openAlertSignModal(alert)"
                  >
                    ✍️ Sign & Decide
                  </button>
                  <span v-else class="signed-tag">
                    Approved ({{ alert.review_decision }})
                  </span>
                </td>
              </tr>
              <tr v-if="alertsList.length === 0">
                <td colspan="9" class="empty-state">No safety signal alerts currently triggered.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 3: Periodic Safety Reports (DSUR / PSUR) -->
      <section v-if="activeTab === 'reports'" class="tab-pane" aria-labelledby="reports-tab-heading">
        <div class="reports-header-box">
          <h3>Periodic Regulatory Reports (ICH E2F DSUR & ICH E2C(R2) PSUR)</h3>
          <p>Automated periodic safety listings, interval SOC x PT summary tabulations, and benefit-risk synthesis.</p>
        </div>

        <div class="table-container">
          <table class="data-table" aria-label="Periodic Safety Reports Table">
            <thead>
              <tr>
                <th>Report ID</th>
                <th>Type</th>
                <th>Study / Compound</th>
                <th>Reporting Period</th>
                <th>Status</th>
                <th>Exposure / SAEs</th>
                <th>Signatures</th>
                <th>Export & Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="report in periodicReportsList" :key="report.id">
                <td><code>{{ report.id }}</code></td>
                <td><span class="badge badge-regulatory">{{ report.report_type }}</span></td>
                <td><strong>{{ report.compound_name }}</strong> ({{ report.study_id }})</td>
                <td>{{ report.reporting_period_start }} to {{ report.reporting_period_end }}</td>
                <td><span :class="['status-pill', report.status.toLowerCase()]">{{ report.status }}</span></td>
                <td>{{ report.total_subject_exposure }} Subs | {{ report.total_saes }} SAEs</td>
                <td>
                  <span v-if="report.approved_by_user_id" class="signed-tag">
                    Signed by {{ report.approved_by_user_id }}
                  </span>
                  <button
                    v-else
                    class="btn btn-sm btn-secondary"
                    @click="openReportSignModal(report)"
                  >
                    ✍️ Part 11 Sign
                  </button>
                </td>
                <td>
                  <a
                    :href="`/api/v1/safety/periodic-reports/${report.id}/export`"
                    target="_blank"
                    class="btn btn-sm btn-outline"
                  >
                    📄 View HTML Report
                  </a>
                </td>
              </tr>
              <tr v-if="periodicReportsList.length === 0">
                <td colspan="8" class="empty-state">No periodic safety reports compiled yet. Click 'Compile DSUR / PSUR' to generate one.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </main>

    <!-- Part 11 Electronic Signature Modal for Signal Review Alert -->
    <div v-if="selectedAlertForSigning" class="modal-backdrop" role="dialog" aria-modal="true">
      <div class="modal-box">
        <header class="modal-header">
          <h3>21 CFR Part 11 Signal Review Disposition Signature</h3>
          <button class="close-btn" @click="selectedAlertForSigning = null">✕</button>
        </header>

        <div class="modal-body">
          <p><strong>Alert:</strong> {{ selectedAlertForSigning.meddra_term }} ({{ selectedAlertForSigning.target_drug }})</p>
          <p><strong>Statistical Indices:</strong> PRR: {{ selectedAlertForSigning.prr }}, ROR: {{ selectedAlertForSigning.ror }}, EB05: {{ selectedAlertForSigning.eb05 }}</p>

          <div class="form-group">
            <label for="review-decision">Clinical Review Decision *</label>
            <select id="review-decision" v-model="signForm.decision" class="select-input">
              <option value="CONFIRM_SIGNAL">CONFIRM_SIGNAL (Escalate to Safety Board)</option>
              <option value="REFUTE_SIGNAL">REFUTE_SIGNAL (Known event / Background noise)</option>
              <option value="REQUEST_SAFETY_INQUIRY">REQUEST_SAFETY_INQUIRY (Request CRA / Site Query)</option>
              <option value="CLOSE_ALERT">CLOSE_ALERT (Triage Complete)</option>
            </select>
          </div>

          <div class="form-group">
            <label for="review-justification">Medical Justification *</label>
            <textarea
              id="review-justification"
              v-model="signForm.justification"
              class="textarea-input"
              placeholder="Document clinical rationale for the signal determination..."
            ></textarea>
          </div>

          <div class="form-group">
            <label for="sign-secret">Safety Physician Signing Key / Secret *</label>
            <input
              id="sign-secret"
              v-model="signForm.signature_secret"
              type="password"
              class="text-input"
              placeholder="Enter cryptographic signing credential"
            />
          </div>
        </div>

        <footer class="modal-footer">
          <button class="btn btn-secondary" @click="selectedAlertForSigning = null">Cancel</button>
          <button
            class="btn btn-primary"
            @click="submitAlertSignature"
            :disabled="!signForm.justification || !signForm.signature_secret"
          >
            Apply Part 11 Digital Signature
          </button>
        </footer>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'

interface DisproportionalityMetric {
  drug_name: string
  meddra_pt: string
  meddra_soc: string | null
  term_level: 'PT' | 'SOC'
  case_count_a: int
  expected_count_e: number
  prr: number
  prr_ci_lower: number
  prr_ci_upper: number
  chi_square_yates: number
  ror: number
  ror_ci_lower: number
  ror_ci_upper: number
  ebgm: number
  eb05: number
  eb95: number
  is_prr_signal: boolean
  is_ror_signal: boolean
  is_ebgm_signal: boolean
  overall_signal_flag: boolean
  signal_strength: 'CRITICAL' | 'STRONG' | 'MODERATE' | 'WEAK' | 'NONE'
}

interface SafetyAlert {
  id: string
  target_drug: string
  meddra_term: string
  term_level: string
  case_count_a: number
  prr: number
  ror: number
  eb05: number
  severity: string
  status: string
  triage_summary: string
  assigned_safety_officer: string | null
  review_decision: string | null
}

interface PeriodicReport {
  id: string
  report_type: string
  study_id: string
  compound_name: string
  reporting_period_start: string
  reporting_period_end: string
  status: string
  total_subject_exposure: number
  total_saes: number
  approved_by_user_id: string | null
}

const props = withDefaults(
  defineProps<{
    targetDrug?: string
    studyId?: string
  }>(),
  {
    targetDrug: 'CADENCE-101',
    studyId: 'STUDY-CAD-2026',
  }
)

const activeTab = ref<'signals' | 'alerts' | 'reports'>('signals')
const selectedLevel = ref<'ALL' | 'PT' | 'SOC'>('ALL')
const searchTerm = ref('')
const showOnlySignals = ref(false)
const isScanning = ref(false)
const isCompiling = ref(false)

const metricsList = ref<DisproportionalityMetric[]>([
  {
    drug_name: 'CADENCE-101',
    meddra_pt: 'Hepatic failure',
    meddra_soc: 'Hepatobiliary disorders',
    term_level: 'PT',
    case_count_a: 15,
    expected_count_e: 1.0,
    prr: 15.0,
    prr_ci_lower: 7.2,
    prr_ci_upper: 31.4,
    chi_square_yates: 64.2,
    ror: 17.47,
    ror_ci_lower: 8.1,
    ror_ci_upper: 37.8,
    ebgm: 8.0,
    eb05: 5.2,
    eb95: 12.1,
    is_prr_signal: true,
    is_ror_signal: true,
    is_ebgm_signal: true,
    overall_signal_flag: true,
    signal_strength: 'CRITICAL',
  },
  {
    drug_name: 'CADENCE-101',
    meddra_pt: 'Acute kidney injury',
    meddra_soc: 'Renal and urinary disorders',
    term_level: 'PT',
    case_count_a: 8,
    expected_count_e: 2.2,
    prr: 3.64,
    prr_ci_lower: 1.6,
    prr_ci_upper: 8.3,
    chi_square_yates: 11.4,
    ror: 3.82,
    ror_ci_lower: 1.7,
    ror_ci_upper: 8.7,
    ebgm: 2.81,
    eb05: 1.62,
    eb95: 4.88,
    is_prr_signal: true,
    is_ror_signal: true,
    is_ebgm_signal: false,
    overall_signal_flag: true,
    signal_strength: 'STRONG',
  },
  {
    drug_name: 'CADENCE-101',
    meddra_pt: 'Hepatobiliary disorders',
    meddra_soc: 'Hepatobiliary disorders',
    term_level: 'SOC',
    case_count_a: 22,
    expected_count_e: 4.5,
    prr: 4.89,
    prr_ci_lower: 2.8,
    prr_ci_upper: 8.5,
    chi_square_yates: 38.1,
    ror: 5.4,
    ror_ci_lower: 3.1,
    ror_ci_upper: 9.4,
    ebgm: 4.18,
    eb05: 2.94,
    eb95: 5.95,
    is_prr_signal: true,
    is_ror_signal: true,
    is_ebgm_signal: true,
    overall_signal_flag: true,
    signal_strength: 'CRITICAL',
  },
  {
    drug_name: 'CADENCE-101',
    meddra_pt: 'Nausea',
    meddra_soc: 'Gastrointestinal disorders',
    term_level: 'PT',
    case_count_a: 2,
    expected_count_e: 2.0,
    prr: 1.0,
    prr_ci_lower: 0.25,
    prr_ci_upper: 3.98,
    chi_square_yates: 0.0,
    ror: 1.0,
    ror_ci_lower: 0.25,
    ror_ci_upper: 4.02,
    ebgm: 1.0,
    eb05: 0.31,
    eb95: 3.19,
    is_prr_signal: false,
    is_ror_signal: false,
    is_ebgm_signal: false,
    overall_signal_flag: false,
    signal_strength: 'NONE',
  },
])

const alertsList = ref<SafetyAlert[]>([
  {
    id: 'alt-001',
    target_drug: 'CADENCE-101',
    meddra_term: 'Hepatic failure',
    term_level: 'PT',
    case_count_a: 15,
    prr: 15.0,
    ror: 17.47,
    eb05: 5.2,
    severity: 'CRITICAL',
    status: 'TRIGGERED',
    triage_summary: 'Extreme disproportionality breach across PRR (15.0), ROR (17.47), and EB05 (5.20). Immediate clinical review mandatory.',
    assigned_safety_officer: 'Dr. Sarah Jenkins',
    review_decision: null,
  },
  {
    id: 'alt-002',
    target_drug: 'CADENCE-101',
    meddra_term: 'Acute kidney injury',
    term_level: 'PT',
    case_count_a: 8,
    prr: 3.64,
    ror: 3.82,
    eb05: 1.62,
    severity: 'HIGH',
    status: 'UNDER_REVIEW',
    triage_summary: 'PRR and ROR lower 95% bound exceeds 1.0. Evans criteria satisfied (a=8, PRR=3.64, Chi2=11.4).',
    assigned_safety_officer: 'Dr. Michael Chang',
    review_decision: null,
  },
])

const periodicReportsList = ref<PeriodicReport[]>([
  {
    id: 'dsur-2026-001',
    report_type: 'DSUR',
    study_id: 'STUDY-CAD-2026',
    compound_name: 'CADENCE-101',
    reporting_period_start: '2025-08-01',
    reporting_period_end: '2026-08-01',
    status: 'APPROVED',
    total_subject_exposure: 450,
    total_saes: 23,
    approved_by_user_id: 'safety_physician_01',
  },
])

const activeSignalsCount = computed(() => metricsList.value.filter(m => m.overall_signal_flag).length)
const pendingAlertsCount = computed(() => alertsList.value.filter(a => a.status === 'TRIGGERED' || a.status === 'UNDER_REVIEW').length)

const filteredMetrics = computed(() => {
  return metricsList.value.filter(item => {
    if (selectedLevel.value !== 'ALL' && item.term_level !== selectedLevel.value) {
      return false
    }
    if (showOnlySignals.value && !item.overall_signal_flag) {
      return false
    }
    if (searchTerm.value) {
      const q = searchTerm.value.toLowerCase()
      const matchPt = item.meddra_pt.toLowerCase().includes(q)
      const matchSoc = item.meddra_soc?.toLowerCase().includes(q)
      if (!matchPt && !matchSoc) {
        return false
      }
    }
    return true
  })
})

const selectedAlertForSigning = ref<SafetyAlert | null>(null)
const signForm = ref({
  decision: 'CONFIRM_SIGNAL',
  justification: '',
  signature_secret: '',
})

function executeScan() {
  isScanning.value = true
  setTimeout(() => {
    isScanning.value = false
  }, 600)
}

function openCompileReportModal() {
  activeTab.value = 'reports'
}

function triggerManualAlert(item: DisproportionalityMetric) {
  activeTab.value = 'alerts'
}

function openAlertSignModal(alert: SafetyAlert) {
  selectedAlertForSigning.value = alert
  signForm.value = {
    decision: 'CONFIRM_SIGNAL',
    justification: '',
    signature_secret: '',
  }
}

function openReportSignModal(report: PeriodicReport) {
  alert(`Signing for ${report.id} opened.`)
}

function submitAlertSignature() {
  if (selectedAlertForSigning.value) {
    selectedAlertForSigning.value.status = 'CONFIRMED_SIGNAL'
    selectedAlertForSigning.value.review_decision = signForm.value.decision
    selectedAlertForSigning.value = null
  }
}
</script>

<style scoped>
.disproportionality-signal-reviewer {
  display: flex;
  flex-direction: column;
  gap: var(--space-4, 1rem);
  background-color: var(--color-surface, #ffffff);
  color: var(--color-text-main, #0f172a);
  font-family: var(--font-family-base, -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, sans-serif);
}

.signal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: var(--space-4, 1rem);
  background-color: var(--color-surface-subtle, #f8fafc);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: var(--radius-md, 6px);
}

.title-group {
  display: flex;
  align-items: center;
  gap: var(--space-2, 0.5rem);
}

.signal-title {
  margin: 0;
  font-size: var(--font-size-xl, 1.25rem);
  font-weight: 700;
  color: var(--color-text-emphasis, #0f172a);
}

.header-metadata {
  display: flex;
  gap: var(--space-4, 1rem);
  margin-top: var(--space-2, 0.5rem);
  font-size: var(--font-size-sm, 0.875rem);
  color: var(--color-text-secondary, #475569);
}

.header-actions {
  display: flex;
  gap: var(--space-2, 0.5rem);
}

.badge {
  display: inline-block;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.badge-regulatory {
  background-color: #dbeafe;
  color: #1e40af;
}

.nav-tabs {
  display: flex;
  gap: var(--space-2, 0.5rem);
  border-bottom: 2px solid var(--color-border, #e2e8f0);
}

.tab-button {
  padding: 0.625rem 1rem;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  cursor: pointer;
  font-weight: 600;
  color: var(--color-text-secondary, #64748b);
  margin-bottom: -2px;
}

.tab-button.active {
  color: var(--color-primary, #2563eb);
  border-bottom-color: var(--color-primary, #2563eb);
}

.tab-pane {
  display: flex;
  flex-direction: column;
  gap: var(--space-3, 0.75rem);
}

.pane-toolbar {
  display: flex;
  align-items: center;
  gap: var(--space-4, 1rem);
  padding: var(--space-3, 0.75rem);
  background-color: var(--color-surface-subtle, #f8fafc);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: var(--radius-sm, 4px);
}

.filter-group {
  display: flex;
  align-items: center;
  gap: var(--space-2, 0.5rem);
  font-size: var(--font-size-sm, 0.875rem);
}

.select-input, .text-input, .textarea-input {
  padding: 0.375rem 0.625rem;
  border: 1px solid var(--color-border, #cbd5e1);
  border-radius: var(--radius-sm, 4px);
  background-color: #ffffff;
  color: #0f172a;
}

.table-container {
  overflow-x: auto;
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: var(--radius-md, 6px);
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--font-size-sm, 0.875rem);
  text-align: left;
}

.data-table th, .data-table td {
  padding: 0.625rem 0.75rem;
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.data-table th {
  background-color: #f1f5f9;
  font-weight: 600;
  color: #334155;
}

.signal-row-critical {
  background-color: #fff1f2;
}

.signal-row-active {
  background-color: #fffbeb;
}

.level-badge {
  display: inline-block;
  padding: 0.125rem 0.375rem;
  background-color: #e2e8f0;
  border-radius: 3px;
  font-size: 0.75rem;
  font-weight: 600;
}

.metric-alert {
  color: #b91c1c;
  font-weight: 700;
}

.ci-bounds {
  display: block;
  font-size: 0.75rem;
  color: #64748b;
}

.strength-pill, .severity-pill, .status-pill {
  display: inline-block;
  padding: 0.2rem 0.5rem;
  border-radius: 12px;
  font-size: 0.75rem;
  font-weight: 600;
}

.strength-pill.critical, .severity-pill.critical {
  background-color: #fee2e2;
  color: #991b1b;
}

.strength-pill.strong, .severity-pill.high {
  background-color: #fef3c7;
  color: #92400e;
}

.strength-pill.moderate, .severity-pill.medium {
  background-color: #e0e7ff;
  color: #3730a3;
}

.strength-pill.weak, .strength-pill.none {
  background-color: #f1f5f9;
  color: #475569;
}

.status-pill.triggered {
  background-color: #fee2e2;
  color: #991b1b;
}

.status-pill.under_review {
  background-color: #fef3c7;
  color: #92400e;
}

.status-pill.confirmed_signal, .status-pill.approved {
  background-color: #dcfce7;
  color: #166534;
}

.btn {
  padding: 0.5rem 0.875rem;
  font-weight: 600;
  font-size: 0.875rem;
  border-radius: 4px;
  cursor: pointer;
  border: 1px solid transparent;
}

.btn-sm {
  padding: 0.25rem 0.5rem;
  font-size: 0.75rem;
}

.btn-primary {
  background-color: #2563eb;
  color: #ffffff;
}

.btn-secondary {
  background-color: #e2e8f0;
  color: #1e293b;
}

.btn-outline {
  background: transparent;
  border-color: #94a3b8;
  color: #334155;
}

.signed-tag {
  color: #15803d;
  font-weight: 600;
  font-size: 0.75rem;
}

.modal-backdrop {
  position: fixed;
  inset: 0;
  background-color: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 999;
}

.modal-box {
  background: #ffffff;
  border-radius: 8px;
  width: 500px;
  max-width: 90vw;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  border-bottom: 1px solid #e2e8f0;
}

.modal-body {
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem;
  border-top: 1px solid #e2e8f0;
}

.close-btn {
  background: none;
  border: none;
  font-size: 1.25rem;
  cursor: pointer;
}

.empty-state {
  text-align: center;
  color: #64748b;
  padding: 2rem;
}
</style>
