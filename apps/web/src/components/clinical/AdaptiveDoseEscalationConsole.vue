<template>
  <div class="adaptive-console">
    <!-- Header -->
    <div class="console-header">
      <div class="header-left">
        <div class="header-badge-row">
          <span
            class="badge-design"
            :class="`design-${designType.toLowerCase()}`"
          >
            {{ designTypeLabel }}
          </span>
          <span
            v-if="isActive"
            class="badge-status status-active"
          >ACTIVE PROTOCOL</span>
          <span
            v-else
            class="badge-status status-inactive"
          >INACTIVE</span>
        </div>
        <h3 class="title">
          Adaptive Oncology Dose Escalation &amp; Randomization Console
        </h3>
        <p class="subtitle">
          Real-time DLT monitoring, Bayesian interval decision engine, and 21
          CFR Part 11 cohort gating.
        </p>
      </div>

      <div class="header-metrics">
        <div class="metric-box">
          <span class="metric-label">Target Toxicity (&phi;)</span>
          <span class="metric-val">{{ (targetToxicityRate * 100).toFixed(1) }}%</span>
        </div>
        <div
          v-if="designType === 'BOIN'"
          class="metric-box"
        >
          <span class="metric-label">BOIN Boundaries</span>
          <span class="metric-val">&lambda;<sub>e</sub> {{ lambdaE.toFixed(3) }} | &lambda;<sub>d</sub>
            {{ lambdaD.toFixed(3) }}</span>
        </div>
        <div class="metric-box">
          <span class="metric-label">Current Cohort</span>
          <span class="metric-val highlight">Cohort #{{ activeCohortNumber }}</span>
        </div>
      </div>
    </div>

    <!-- Active Cohort Recommendation Banner -->
    <div
      class="recommendation-banner"
      :class="`banner-${recommendationAction.toLowerCase()}`"
    >
      <div class="banner-icon-col">
        <span class="action-icon">{{ actionIcon(recommendationAction) }}</span>
      </div>
      <div class="banner-content">
        <div class="action-row">
          <span class="action-tag">RECOMMENDED ACTION:</span>
          <span class="action-name">{{ recommendationAction }}</span>
          <span
            v-if="recommendedDoseName"
            class="rec-dose-pill"
          >Target: {{ recommendedDoseName }}</span>
        </div>
        <p class="banner-rationale">
          {{ rationale || "Awaiting cohort evaluation." }}
        </p>
      </div>
      <div class="banner-cta">
        <button
          type="button"
          class="btn-primary-action"
          :class="`btn-${recommendationAction.toLowerCase()}`"
          :disabled="isSubmitting"
          @click="openTransitionModal"
        >
          Execute Cohort Decision
        </button>
      </div>
    </div>

    <!-- Dose Levels Table & Bayesian Toxicity Graph -->
    <div class="dose-grid-container">
      <h4 class="section-title">
        Dose Level Escalation Tracker &amp; Posterior Estimates
      </h4>
      <div class="table-wrapper">
        <table class="dose-table">
          <thead>
            <tr>
              <th>Level</th>
              <th>Dose Label</th>
              <th>Enrolled (N)</th>
              <th>DLTs (Y)</th>
              <th>Observed Rate</th>
              <th>Posterior Rate</th>
              <th>Overdose Prob</th>
              <th>Safety Status</th>
              <th>Action State</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="dose in doseSummaries"
              :key="dose.dose_level_index"
              :class="{
                'row-active': dose.dose_level_index === currentDoseLevelIndex,
                'row-eliminated': dose.is_eliminated_for_safety,
              }"
            >
              <td>
                <span class="dose-idx-badge">{{ dose.dose_level_index }}</span>
              </td>
              <td class="cell-bold">
                {{ dose.dose_name }}
              </td>
              <td>{{ dose.total_enrolled }}</td>
              <td>
                <span :class="{ 'dlt-alert': dose.total_dlts > 0 }">{{
                  dose.total_dlts
                }}</span>
              </td>
              <td>
                <div class="rate-bar-container">
                  <div
                    class="rate-bar"
                    :style="{
                      width: `${Math.min(dose.observed_dlt_rate * 100, 100)}%`,
                    }"
                    :class="rateColorClass(dose.observed_dlt_rate)"
                  />
                  <span class="rate-text">{{ (dose.observed_dlt_rate * 100).toFixed(1) }}%</span>
                </div>
              </td>
              <td>
                <span
                  v-if="
                    dose.posterior_dlt_rate !== null &&
                      dose.posterior_dlt_rate !== undefined
                  "
                >
                  {{ (dose.posterior_dlt_rate * 100).toFixed(1) }}%
                </span>
                <span
                  v-else
                  class="text-muted"
                >-</span>
              </td>
              <td>
                <span
                  v-if="
                    dose.overdose_probability !== null &&
                      dose.overdose_probability !== undefined
                  "
                  :class="{
                    'overdose-warning': dose.overdose_probability > 0.8,
                  }"
                >
                  {{ (dose.overdose_probability * 100).toFixed(1) }}%
                </span>
                <span
                  v-else
                  class="text-muted"
                >-</span>
              </td>
              <td>
                <span
                  v-if="dose.is_eliminated_for_safety"
                  class="pill-eliminated"
                >ELIMINATED</span>
                <span
                  v-else
                  class="pill-safe"
                >TOLERABLE</span>
              </td>
              <td>
                <span
                  v-if="dose.dose_level_index === currentDoseLevelIndex"
                  class="pill-current"
                >ACTIVE DOSE</span>
                <span
                  v-else-if="
                    dose.dose_level_index === recommendedDoseLevelIndex
                  "
                  class="pill-target"
                >NEXT TARGET</span>
                <span
                  v-else
                  class="text-muted"
                >STANDBY</span>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Response-Adaptive Randomization (RAR) Multi-Arm Allocator (if RAR active) -->
    <div
      v-if="designType === 'RAR' || rarArms.length > 0"
      class="rar-section"
    >
      <div class="rar-header">
        <h4 class="section-title">
          Response-Adaptive Randomization (RAR) Allocation Weights
        </h4>
        <span class="pill highlight">{{ rarArms.length }} Treatment Arms</span>
      </div>
      <div class="rar-cards-grid">
        <div
          v-for="arm in rarArms"
          :key="arm.arm_id"
          class="rar-arm-card"
          :class="`rar-status-${arm.status.toLowerCase()}`"
        >
          <div class="arm-card-header">
            <span class="arm-name">{{ arm.arm_name }}</span>
            <span
              v-if="arm.is_control"
              class="control-badge"
            >CONTROL</span>
            <span class="status-pill">{{ arm.status }}</span>
          </div>
          <div class="arm-metrics">
            <div class="arm-metric-item">
              <span class="metric-lbl">Enrolled</span>
              <span class="metric-num">{{ arm.enrolled_count }}</span>
            </div>
            <div class="arm-metric-item">
              <span class="metric-lbl">Responses</span>
              <span class="metric-num">{{ arm.response_count }}</span>
            </div>
            <div class="arm-metric-item">
              <span class="metric-lbl">Posterior &theta;</span>
              <span class="metric-num">{{ (arm.posterior_mean * 100).toFixed(1) }}%</span>
            </div>
          </div>
          <div class="arm-prob-section">
            <div class="prob-label-row">
              <span class="prob-title">Dynamic Allocation Weight:</span>
              <span class="prob-val">{{ (arm.allocation_probability * 100).toFixed(1) }}%</span>
            </div>
            <div class="prob-track">
              <div
                class="prob-fill"
                :style="{ width: `${arm.allocation_probability * 100}%` }"
              />
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Cohort Transition Sign-off Modal -->
    <div
      v-if="showTransitionModal"
      class="modal-backdrop"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h4 class="modal-title">
            21 CFR Part 11 Cohort Transition Sign-off
          </h4>
          <button
            type="button"
            class="btn-close"
            @click="closeTransitionModal"
          >
            &times;
          </button>
        </div>
        <div class="modal-body">
          <div class="decision-summary-box">
            <div class="decision-row">
              <span class="lbl">Cohort Number:</span>
              <span class="val">Cohort #{{ activeCohortNumber }}</span>
            </div>
            <div class="decision-row">
              <span class="lbl">Algorithmic Recommendation:</span>
              <span class="val bold">{{ recommendationAction }}</span>
            </div>
            <div class="decision-row">
              <span class="lbl">Target Dose Level:</span>
              <span class="val">{{ selectedTargetDoseName }} (Level
                {{ selectedTargetDoseIndex }})</span>
            </div>
          </div>

          <div class="form-group">
            <label
              class="form-label"
              for="decision-action-select"
            >Confirmed Action:</label>
            <select
              id="decision-action-select"
              v-model="selectedAction"
              class="form-select"
            >
              <option value="ESCALATE">
                ESCALATE (Advance to Next Higher Dose)
              </option>
              <option value="DE_ESCALATE">
                DE_ESCALATE (Reduce to Lower Dose)
              </option>
              <option value="STAY">
                STAY (Continue Current Dose Level)
              </option>
              <option value="EXPAND_COHORT">
                EXPAND_COHORT (Enroll 3 Additional Subjects)
              </option>
              <option value="STOP_SAFETY">
                STOP_SAFETY (Halt Trial for Toxicity)
              </option>
              <option value="STOP_SUCCESS">
                STOP_SUCCESS / MTD_FOUND (Declare MTD)
              </option>
            </select>
          </div>

          <div class="form-group">
            <label
              class="form-label"
              for="target-dose-select"
            >Target Dose Level Index:</label>
            <select
              id="target-dose-select"
              v-model.number="selectedTargetDoseIndex"
              class="form-select"
            >
              <option
                v-for="dose in doseSummaries"
                :key="dose.dose_level_index"
                :value="dose.dose_level_index"
                :disabled="dose.is_eliminated_for_safety"
              >
                Level {{ dose.dose_level_index }} - {{ dose.dose_name }}
                <template v-if="dose.is_eliminated_for_safety">
                  (ELIMINATED FOR SAFETY)
                </template>
              </option>
            </select>
          </div>

          <div
            v-if="isOverride"
            class="form-group alert-group"
          >
            <label
              class="form-label required"
              for="override-reason-input"
            >
              Safety Committee Override Justification (21 CFR Part 11):
            </label>
            <textarea
              id="override-reason-input"
              v-model="overrideReason"
              class="form-textarea"
              rows="3"
              placeholder="Enter detailed clinical rationale and Safety Committee sign-off ID..."
              required
            />
          </div>

          <div class="form-group">
            <label
              class="form-label required"
              for="esignature-input"
            >
              Electronic Signature (PIN or Full Name):
            </label>
            <input
              id="esignature-input"
              v-model="electronicSignature"
              type="text"
              class="form-input"
              placeholder="e.g. Dr. Jane Smith, MD"
              required
            >
          </div>
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn-secondary"
            @click="closeTransitionModal"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn-submit"
            :disabled="!isFormValid || isSubmitting"
            @click="submitCohortTransition"
          >
            {{ isSubmitting ? "Authorizing..." : "Authorize & Advance Cohort" }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, ref } from "vue";

export interface DoseLevelSummaryDTO {
  dose_level_index: number;
  dose_name: string;
  dose_value: number;
  total_enrolled: number;
  total_dlts: number;
  observed_dlt_rate: number;
  posterior_dlt_rate?: number | null;
  is_eliminated_for_safety: boolean;
  overdose_probability?: number | null;
}

export interface RARArmDTO {
  arm_id: string;
  arm_name: string;
  is_control: boolean;
  status: string;
  enrolled_count: number;
  response_count: number;
  posterior_mean: number;
  allocation_probability: number;
  superiority_vs_control_prob?: number | null;
}

const props = withDefaults(
  defineProps<{
    studyId: string;
    designType?: string;
    targetToxicityRate?: number;
    lambdaE?: number;
    lambdaD?: number;
    activeCohortNumber?: number;
    currentDoseLevelIndex?: number;
    currentDoseName?: string;
    recommendationAction?: string;
    recommendedDoseLevelIndex?: number | null;
    recommendedDoseName?: string | null;
    rationale?: string;
    isActive?: boolean;
    doseSummaries?: DoseLevelSummaryDTO[];
    rarArms?: RARArmDTO[];
  }>(),
  {
    designType: "BOIN",
    targetToxicityRate: 0.25,
    lambdaE: 0.197,
    lambdaD: 0.298,
    activeCohortNumber: 1,
    currentDoseLevelIndex: 1,
    currentDoseName: "Dose 1",
    recommendationAction: "STAY",
    recommendedDoseLevelIndex: 1,
    recommendedDoseName: "Dose 1",
    rationale: "",
    isActive: true,
    doseSummaries: () => [],
    rarArms: () => [],
  }
);

const emit = defineEmits<{
  (
    e: "transition-cohort",
    payload: {
      study_id: string;
      cohort_number: number;
      decision_action: string;
      target_dose_level_index: number;
      override_reason?: string;
      electronic_signature?: string;
    }
  ): void;
}>();

const showTransitionModal = ref(false);
const isSubmitting = ref(false);
const selectedAction = ref(props.recommendationAction);
const selectedTargetDoseIndex = ref(
  props.recommendedDoseLevelIndex || props.currentDoseLevelIndex
);
const overrideReason = ref("");
const electronicSignature = ref("");

const designTypeLabel = computed(() => {
  switch (props.designType) {
    case "BOIN":
      return "Bayesian Optimal Interval (BOIN)";
    case "CRM":
      return "Continual Reassessment Method (CRM)";
    case "THREE_PLUS_THREE":
      return "Classic 3+3 Rule-Based";
    case "RAR":
      return "Response-Adaptive Randomization (RAR)";
    default:
      return props.designType;
  }
});

const isOverride = computed(() => {
  return (
    selectedAction.value !== props.recommendationAction ||
    selectedTargetDoseIndex.value !== props.recommendedDoseLevelIndex
  );
});

const selectedTargetDoseName = computed(() => {
  const match = props.doseSummaries.find(
    (d) => d.dose_level_index === selectedTargetDoseIndex.value
  );
  return match ? match.dose_name : `Dose ${selectedTargetDoseIndex.value}`;
});

const isFormValid = computed(() => {
  if (!electronicSignature.value.trim()) return false;
  if (isOverride.value && !overrideReason.value.trim()) return false;
  return true;
});

function actionIcon(action: string): string {
  switch (action) {
    case "ESCALATE":
      return "▲";
    case "DE_ESCALATE":
      return "▼";
    case "STAY":
    case "EXPAND_COHORT":
      return "■";
    case "STOP_SAFETY":
      return "✖";
    case "STOP_SUCCESS":
    case "MTD_FOUND":
      return "✔";
    default:
      return "●";
  }
}

function rateColorClass(rate: number): string {
  if (rate <= props.lambdaE) return "bar-green";
  if (rate >= props.lambdaD) return "bar-red";
  return "bar-amber";
}

function openTransitionModal(): void {
  selectedAction.value = props.recommendationAction;
  selectedTargetDoseIndex.value =
    props.recommendedDoseLevelIndex || props.currentDoseLevelIndex;
  overrideReason.value = "";
  electronicSignature.value = "";
  showTransitionModal.value = true;
}

function closeTransitionModal(): void {
  showTransitionModal.value = false;
}

function submitCohortTransition(): void {
  if (!isFormValid.value) return;
  isSubmitting.value = true;

  emit("transition-cohort", {
    study_id: props.studyId,
    cohort_number: props.activeCohortNumber,
    decision_action: selectedAction.value,
    target_dose_level_index: selectedTargetDoseIndex.value,
    override_reason: isOverride.value ? overrideReason.value.trim() : undefined,
    electronic_signature: electronicSignature.value.trim(),
  });

  showTransitionModal.value = false;
  isSubmitting.value = false;
}
</script>

<style scoped>
.adaptive-console {
  background-color: var(--bg-surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 24px;
  font-family: var(
    --font-family,
    -apple-system,
    BlinkMacSystemFont,
    "Segoe UI",
    Roboto,
    sans-serif
  );
  color: var(--text-primary, #0f172a);
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 24px;
  margin-bottom: 20px;
  flex-wrap: wrap;
}

.header-badge-row {
  display: flex;
  gap: 8px;
  align-items: center;
  margin-bottom: 6px;
}

.badge-design {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
  background-color: #e0e7ff;
  color: #3730a3;
}

.badge-status {
  font-size: 0.7rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.status-active {
  background-color: #dcfce7;
  color: #15803d;
}

.status-inactive {
  background-color: #f1f5f9;
  color: #64748b;
}

.title {
  margin: 0 0 4px 0;
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--text-heading, #0f172a);
}

.subtitle {
  margin: 0;
  font-size: 0.85rem;
  color: var(--text-muted, #64748b);
}

.header-metrics {
  display: flex;
  gap: 12px;
  flex-wrap: wrap;
}

.metric-box {
  background-color: var(--bg-subtle, #f8fafc);
  border: 1px solid var(--border-light, #e2e8f0);
  border-radius: 6px;
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
}

.metric-label {
  font-size: 0.7rem;
  text-transform: uppercase;
  color: var(--text-muted, #64748b);
  font-weight: 600;
}

.metric-val {
  font-size: 1rem;
  font-weight: 700;
  color: var(--text-primary, #0f172a);
}

.highlight {
  color: #2563eb;
}

/* Recommendation Banner */
.recommendation-banner {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 16px 20px;
  border-radius: 6px;
  margin-bottom: 24px;
  border: 1px solid transparent;
}

.banner-escalate {
  background-color: #f0fdf4;
  border-color: #bbf7d0;
  color: #166534;
}

.banner-de_escalate {
  background-color: #fef2f2;
  border-color: #fecaca;
  color: #991b1b;
}

.banner-stay,
.banner-expand_cohort {
  background-color: #eff6ff;
  border-color: #bfdbfe;
  color: #1e40af;
}

.banner-stop_safety {
  background-color: #450a0a;
  border-color: #7f1d1d;
  color: #fef2f2;
}

.banner-stop_success,
.banner-mtd_found {
  background-color: #ecfdf5;
  border-color: #6ee7b7;
  color: #065f46;
}

.banner-icon-col {
  font-size: 1.75rem;
  line-height: 1;
}

.banner-content {
  flex: 1;
}

.action-row {
  display: flex;
  align-items: center;
  gap: 8px;
  margin-bottom: 4px;
  flex-wrap: wrap;
}

.action-tag {
  font-size: 0.75rem;
  font-weight: 700;
  letter-spacing: 0.5px;
}

.action-name {
  font-size: 1.1rem;
  font-weight: 800;
}

.rec-dose-pill {
  font-size: 0.75rem;
  font-weight: 600;
  background-color: rgba(0, 0, 0, 0.08);
  padding: 2px 8px;
  border-radius: 12px;
}

.banner-rationale {
  margin: 0;
  font-size: 0.85rem;
  opacity: 0.9;
}

.btn-primary-action {
  font-size: 0.85rem;
  font-weight: 700;
  padding: 8px 16px;
  border-radius: 6px;
  border: none;
  cursor: pointer;
  background-color: #0f172a;
  color: #ffffff;
  transition: opacity 0.15s ease;
}

.btn-primary-action:hover {
  opacity: 0.9;
}

/* Dose Levels Table */
.dose-grid-container {
  margin-bottom: 28px;
}

.section-title {
  font-size: 1rem;
  font-weight: 700;
  margin: 0 0 12px 0;
  color: var(--text-heading, #1e293b);
}

.table-wrapper {
  overflow-x: auto;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
}

.dose-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  text-align: left;
}

.dose-table th {
  background-color: var(--bg-subtle, #f8fafc);
  padding: 10px 12px;
  font-weight: 600;
  color: var(--text-secondary, #475569);
  border-bottom: 1px solid var(--border, #e2e8f0);
}

.dose-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--border-light, #f1f5f9);
}

.row-active {
  background-color: #f0f9ff;
}

.row-eliminated {
  background-color: #fff1f2;
  opacity: 0.75;
}

.dose-idx-badge {
  display: inline-block;
  width: 22px;
  height: 22px;
  line-height: 22px;
  text-align: center;
  border-radius: 50%;
  background-color: #e2e8f0;
  font-weight: 700;
  font-size: 0.75rem;
}

.cell-bold {
  font-weight: 600;
}

.dlt-alert {
  color: #dc2626;
  font-weight: 700;
}

.rate-bar-container {
  display: flex;
  align-items: center;
  gap: 8px;
  min-width: 110px;
}

.rate-bar {
  height: 6px;
  border-radius: 3px;
  flex: 1;
}

.bar-green {
  background-color: #22c55e;
}

.bar-amber {
  background-color: #f59e0b;
}

.bar-red {
  background-color: #ef4444;
}

.rate-text {
  font-size: 0.75rem;
  font-weight: 600;
  width: 40px;
}

.overdose-warning {
  color: #b91c1c;
  font-weight: 700;
}

.pill-eliminated {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: #fee2e2;
  color: #991b1b;
}

.pill-safe {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: #dcfce7;
  color: #166534;
}

.pill-current {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: #dbeafe;
  color: #1e40af;
}

.pill-target {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: #fef3c7;
  color: #92400e;
}

.text-muted {
  color: #94a3b8;
}

/* RAR Section */
.rar-section {
  margin-top: 24px;
  padding-top: 20px;
  border-top: 1px solid var(--border, #e2e8f0);
}

.rar-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.rar-cards-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(260px, 1fr));
  gap: 16px;
}

.rar-arm-card {
  background-color: var(--bg-subtle, #f8fafc);
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 14px;
}

.arm-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.arm-name {
  font-weight: 700;
  font-size: 0.9rem;
}

.control-badge {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 5px;
  border-radius: 3px;
  background-color: #e2e8f0;
  color: #334155;
}

.status-pill {
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background-color: #dbeafe;
  color: #1e40af;
}

.arm-metrics {
  display: flex;
  justify-content: space-between;
  margin-bottom: 12px;
  background-color: #ffffff;
  padding: 6px 10px;
  border-radius: 4px;
  border: 1px solid var(--border-light, #f1f5f9);
}

.arm-metric-item {
  display: flex;
  flex-direction: column;
}

.metric-lbl {
  font-size: 0.65rem;
  color: #64748b;
  text-transform: uppercase;
}

.metric-num {
  font-size: 0.85rem;
  font-weight: 700;
}

.arm-prob-section {
  margin-top: 8px;
}

.prob-label-row {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  margin-bottom: 4px;
}

.prob-title {
  color: #475569;
}

.prob-val {
  font-weight: 700;
  color: #2563eb;
}

.prob-track {
  height: 6px;
  background-color: #e2e8f0;
  border-radius: 3px;
  overflow: hidden;
}

.prob-fill {
  height: 100%;
  background-color: #2563eb;
  border-radius: 3px;
}

/* Modal */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 999;
}

.modal-card {
  background-color: #ffffff;
  border-radius: 8px;
  width: 100%;
  max-width: 520px;
  padding: 24px;
  box-shadow:
    0 20px 25px -5px rgba(0, 0, 0, 0.1),
    0 8px 10px -6px rgba(0, 0, 0, 0.1);
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 16px;
}

.modal-title {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
}

.btn-close {
  background: none;
  border: none;
  font-size: 1.5rem;
  line-height: 1;
  cursor: pointer;
  color: #64748b;
}

.decision-summary-box {
  background-color: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px 14px;
  margin-bottom: 16px;
}

.decision-row {
  display: flex;
  justify-content: space-between;
  font-size: 0.8rem;
  margin-bottom: 4px;
}

.decision-row:last-child {
  margin-bottom: 0;
}

.form-group {
  margin-bottom: 14px;
}

.form-label {
  display: block;
  font-size: 0.8rem;
  font-weight: 600;
  margin-bottom: 4px;
}

.required::after {
  content: " *";
  color: #dc2626;
}

.form-select,
.form-input,
.form-textarea {
  width: 100%;
  padding: 8px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  font-size: 0.85rem;
  box-sizing: border-box;
}

.form-select:focus,
.form-input:focus,
.form-textarea:focus {
  outline: none;
  border-color: #2563eb;
  box-shadow: 0 0 0 1px #2563eb;
}

.alert-group {
  background-color: #fef2f2;
  border: 1px solid #fecaca;
  padding: 10px;
  border-radius: 6px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 20px;
}

.btn-secondary {
  padding: 8px 14px;
  border: 1px solid #cbd5e1;
  background-color: #ffffff;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-submit {
  padding: 8px 16px;
  border: none;
  background-color: #2563eb;
  color: #ffffff;
  border-radius: 6px;
  font-size: 0.85rem;
  font-weight: 700;
  cursor: pointer;
}

.btn-submit:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
