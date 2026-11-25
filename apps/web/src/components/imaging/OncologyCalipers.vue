<template>
  <div class="oncology-calipers-panel">
    <div class="panel-header">
      <h3 class="panel-title">Oncologic Calipers &amp; Response Engine</h3>
      <span class="badge badge-gxp">21 CFR Part 11</span>
    </div>

    <!-- Active Measurement Tool Selection -->
    <div class="caliper-tools-section">
      <div class="section-subtitle">Active Caliper Tool</div>
      <div class="tool-btn-grid">
        <button
          type="button"
          class="tool-select-btn"
          :class="{ active: currentTool === 'POINTER' }"
          @click="selectTool('POINTER')"
        >
          <span class="tool-icon"
            ><ClinicalIcon name="search" :size="16"
          /></span>
          <span class="tool-name">Navigate / Pan</span>
        </button>

        <button
          type="button"
          class="tool-select-btn"
          :class="{ active: currentTool === 'CALIPER_1D' }"
          @click="selectTool('CALIPER_1D')"
        >
          <span class="tool-icon"
            ><ClinicalIcon name="file-text" :size="16"
          /></span>
          <span class="tool-name">1D RECIST Caliper</span>
        </button>

        <button
          type="button"
          class="tool-select-btn"
          :class="{ active: currentTool === 'CALIPER_2D' }"
          @click="selectTool('CALIPER_2D')"
        >
          <span class="tool-icon"><ClinicalIcon name="plus" :size="16" /></span>
          <span class="tool-name">2D Lugano Cross</span>
        </button>

        <button
          type="button"
          class="tool-select-btn"
          :class="{ active: currentTool === 'CALIPER_3D' }"
          @click="selectTool('CALIPER_3D')"
        >
          <span class="tool-icon"
            ><ClinicalIcon name="file-text" :size="16"
          /></span>
          <span class="tool-name">3D Ellipsoid Vol</span>
        </button>
      </div>
    </div>

    <!-- Lesion Classification & Metadata Input -->
    <div class="lesion-config-section">
      <div class="section-subtitle">Lesion Tagging &amp; Anatomical Site</div>
      <div class="form-row">
        <div class="form-group">
          <label for="lesion-id-input" class="field-label">Lesion ID:</label>
          <input
            id="lesion-id-input"
            v-model="newLesionForm.lesionId"
            type="text"
            class="field-input"
            placeholder="e.g. TL1"
          />
        </div>

        <div class="form-group">
          <label for="lesion-type-select" class="field-label">Category:</label>
          <select
            id="lesion-type-select"
            v-model="newLesionForm.lesionType"
            class="field-select"
          >
            <option value="TARGET">Target Lesion</option>
            <option value="NON_TARGET">Non-Target Lesion</option>
            <option value="NEW_LESION">New Malignant Lesion</option>
          </select>
        </div>
      </div>

      <div class="form-row">
        <div class="form-group">
          <label for="organ-site-select" class="field-label">Organ Site:</label>
          <select
            id="organ-site-select"
            v-model="newLesionForm.organSite"
            class="field-select"
          >
            <option value="LIVER">Liver</option>
            <option value="LUNG_RIGHT">Right Lung</option>
            <option value="LUNG_LEFT">Left Lung</option>
            <option value="LYMPH_NODE_MEDIASTINAL">
              Mediastinal Lymph Node
            </option>
            <option value="LYMPH_NODE_CERVICAL">Cervical Lymph Node</option>
            <option value="BRAIN">Brain</option>
            <option value="BONE">Bone</option>
            <option value="EXTRANODAL_SPLEEN">Spleen (Extranodal)</option>
            <option value="OTHER">Other Soft Tissue</option>
          </select>
        </div>

        <div v-if="criteria === 'LUGANO_2014'" class="form-group">
          <label for="deauville-select" class="field-label"
            >PET Deauville:</label
          >
          <select
            id="deauville-select"
            v-model.number="newLesionForm.deauvilleScore"
            class="field-select"
            data-tabular-nums="true"
          >
            <option :value="null">None</option>
            <option :value="1">1: No uptake</option>
            <option :value="2">2: &le; Mediastinum</option>
            <option :value="3">3: &le; Liver</option>
            <option :value="4">4: &gt; Liver</option>
            <option :value="5">5: Markedly &gt; Liver</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Active Annotations List -->
    <div class="annotations-list-section">
      <div class="section-header-row">
        <span class="section-subtitle"
          >Active Calipers ({{ annotations.length }})</span
        >
        <button
          v-if="annotations.length > 0"
          type="button"
          class="btn-clear-all"
          @click="$emit('clearAllAnnotations')"
        >
          Clear All
        </button>
      </div>

      <div v-if="annotations.length === 0" class="empty-state">
        Draw calipers directly on the MPR viewer canvas to measure lesions.
      </div>

      <div v-else class="annotations-table-wrapper">
        <table class="annotations-table">
          <thead>
            <tr>
              <th>ID</th>
              <th>Timepoint</th>
              <th>Site</th>
              <th>Type</th>
              <th>Method</th>
              <th>Longest (mm)</th>
              <th>Perp (mm)</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="a in annotations"
              :key="a.id"
              :class="{
                'row-new-lesion': a.lesion_type === 'NEW_LESION',
                'row-ai-assisted':
                  a.measurement_type === 'AI_AUTO_CONTOUR' || a.is_ai_assisted,
              }"
            >
              <td class="font-bold">
                {{ a.lesion_id }}
              </td>
              <td>
                <span
                  class="timepoint-pill"
                  :class="getTimepointClass(a.timepoint)"
                >
                  {{ formatTimepoint(a.timepoint) }}
                </span>
              </td>
              <td>{{ a.organ_site }}</td>
              <td>
                <span class="type-pill" :class="a.lesion_type.toLowerCase()">
                  {{ a.lesion_type }}
                </span>
              </td>
              <td>
                <span
                  v-if="a.measurement_type === 'AI_AUTO_CONTOUR'"
                  class="ai-badge"
                  :title="
                    a.hounsfield_stats
                      ? 'HU Mean: ' +
                        (a.hounsfield_stats.meanHu ||
                          a.hounsfield_stats.mean_hu ||
                          48.5) +
                        ' HU'
                      : 'AI Auto-Segmented Contour'
                  "
                >
                  AI
                </span>
                <span
                  v-else-if="a.measurement_type === 'MANUAL_EDITED'"
                  class="ai-edited-badge"
                  title="AI-assisted, manually converted/edited"
                >
                  AI-Edited
                </span>
                <span v-else class="manual-badge"> Manual</span>
              </td>
              <td>
                <input
                  type="number"
                  step="0.1"
                  class="diameter-input"
                  :value="a.longest_diameter_mm"
                  aria-label="Longest Diameter mm"
                  @change="updateLongestDiameter(a, $event.target.value)"
                />
              </td>
              <td>{{ a.perpendicular_diameter_mm || "-" }}</td>
              <td>
                <div class="action-btn-group">
                  <button
                    v-if="a.measurement_type === 'AI_AUTO_CONTOUR'"
                    type="button"
                    class="btn-convert-manual"
                    title="Convert AI Contour to Editable Manual Caliper"
                    @click="convertToManual(a)"
                  >
                    Convert
                  </button>
                  <ClinicalIconButton
                    class="btn-delete-annot"
                    title="Delete Annotation"
                    :label="`Delete annotation ${a.lesion_id || a.id}`"
                    @click="$emit('deleteAnnotation', a.id)"
                  >
                    ✕
                  </ClinicalIconButton>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>

    <!-- Derivation Summary & Response Card -->
    <div class="derivation-summary-card">
      <div class="summary-header">
        <span class="summary-title">
          {{
            criteria === "RECIST_1_1"
              ? "RECIST 1.1 Derivation"
              : "Lugano 2014 Derivation"
          }}
        </span>
        <span class="response-pill" :class="overallResponseClass">
          {{ derivedResponse.overall_response || "SD" }}
        </span>
      </div>

      <div class="metrics-grid">
        <div class="metric-item">
          <span class="metric-label">
            {{
              criteria === "RECIST_1_1"
                ? "Sum of Diameters (SLD)"
                : "Sum of Products (SPD)"
            }}
          </span>
          <span class="metric-value tabular-nums">
            {{
              criteria === "RECIST_1_1"
                ? (derivedResponse.current_sld_mm || 0) + " mm"
                : (derivedResponse.current_spd_mm2 || 0) + " mm²"
            }}
          </span>
        </div>

        <div class="metric-item">
          <span class="metric-label">% Change Baseline</span>
          <span
            class="metric-value tabular-nums"
            :class="pctChangeClass(derivedResponse.pct_change_baseline)"
          >
            {{ (derivedResponse.pct_change_baseline || 0) > 0 ? "+" : ""
            }}{{ derivedResponse.pct_change_baseline || 0 }}%
          </span>
        </div>

        <div class="metric-item">
          <span class="metric-label">% Change Nadir</span>
          <span
            class="metric-value tabular-nums"
            :class="pctChangeClass(derivedResponse.pct_change_nadir)"
          >
            {{ (derivedResponse.pct_change_nadir || 0) > 0 ? "+" : ""
            }}{{ derivedResponse.pct_change_nadir || 0 }}%
          </span>
        </div>

        <div class="metric-item">
          <span class="metric-label">Target Response</span>
          <span class="metric-value tabular-nums">{{
            derivedResponse.target_response ||
            derivedResponse.ct_response ||
            "SD"
          }}</span>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Audit Sign-off Button -->
    <div class="signoff-section">
      <button
        type="button"
        class="btn-primary-signoff"
        @click="openSignoffModal"
      >
        <span class="btn-icon"
          ><ClinicalIcon name="file-signature" :size="16"
        /></span>
        21 CFR Part 11 Step-Up Sign-Off &amp; Lock
      </button>
    </div>

    <!-- Part 11 Re-Auth Modal -->
    <div
      v-if="isModalOpen"
      ref="signoffModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h4 class="modal-title">
            Electronic Signature - Tumor Assessment Sign-off
          </h4>
          <ClinicalIconButton
            class="btn-close"
            label="Close tumor assessment electronic signature dialog"
            @click="closeSignoffModal"
          >
            ✕
          </ClinicalIconButton>
        </div>
        <div class="modal-body">
          <p class="modal-disclaimer">
            Under 21 CFR Part 11, entering your clinical credential confirms and
            seals the oncologic caliper measurements and calculated response
            classification.
          </p>
          <div class="form-group">
            <label for="change-reason-input" class="field-label"
              >Reason for Assessment / Change:</label
            >
            <input
              id="change-reason-input"
              v-model="signoffReason"
              type="text"
              class="field-input"
              placeholder="e.g. Scheduled Cycle 2 Oncology Protocol Tumor Evaluation"
            />
          </div>
          <div class="form-group">
            <label for="signature-input" class="field-label"
              >Electronic Signature (Full Name / PIN):</label
            >
            <input
              id="signature-input"
              v-model="signaturePin"
              type="password"
              class="field-input"
              placeholder="Enter signature or password..."
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn-secondary"
            @click="closeSignoffModal"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn-confirm-sign"
            :disabled="!signoffReason || !signaturePin"
            @click="submitSignedAssessment"
          >
            Confirm &amp; Lock Assessment
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import { ClinicalIconButton, useFocusTrap, ClinicalIcon } from "ui";

const props = defineProps({
  annotations: {
    type: Array,
    default: () => [],
  },
  criteria: {
    type: String,
    default: "RECIST_1_1",
  },
  derivedResponse: {
    type: Object,
    default: () => ({
      current_sld_mm: 0,
      baseline_sld_mm: 0,
      nadir_sld_mm: 0,
      pct_change_baseline: 0,
      pct_change_nadir: 0,
      target_response: "SD",
      overall_response: "SD",
    }),
  },
});

const emit = defineEmits([
  "toolSelected",
  "deleteAnnotation",
  "clearAllAnnotations",
  "assessmentSigned",
  "annotationUpdated",
]);

const currentTool = ref("CALIPER_1D");
const isModalOpen = ref(false);
const signoffModalRef = ref(null);
const signoffReason = ref("Routine Oncologic Caliper Response Evaluation");
const signaturePin = ref("");

function closeSignoffModal() {
  signaturePin.value = "";
  isModalOpen.value = false;
}

useFocusTrap(signoffModalRef, isModalOpen, {
  onEscape: closeSignoffModal,
});

const newLesionForm = ref({
  lesionId: "TL1",
  lesionType: "TARGET",
  organSite: "LIVER",
  deauvilleScore: null,
});

function selectTool(toolKey) {
  currentTool.value = toolKey;
  emit("toolSelected", toolKey);
}

function openSignoffModal() {
  isModalOpen.value = true;
}

function convertToManual(annotation) {
  annotation.measurement_type = "MANUAL_EDITED";
  annotation.is_ai_assisted = true;
  emit("annotationUpdated", annotation);
}

function updateLongestDiameter(annotation, val) {
  const num = parseFloat(val);
  if (!isNaN(num) && num > 0) {
    annotation.longest_diameter_mm = num;
    annotation.measurement_type = "MANUAL_EDITED";
    annotation.is_ai_assisted = true;
    emit("annotationUpdated", annotation);
  }
}

function submitSignedAssessment() {
  const aiCount = props.annotations.filter(
    (a) => a.measurement_type === "AI_AUTO_CONTOUR" || a.is_ai_assisted
  ).length;
  emit("assessmentSigned", {
    reason: signoffReason.value,
    signature: signaturePin.value,
    aiAssisted: aiCount > 0,
    aiAnnotationsCount: aiCount,
    totalAnnotationsCount: props.annotations.length,
  });
  isModalOpen.value = false;
  signaturePin.value = "";
}

const overallResponseClass = computed(() => {
  const resp = (props.derivedResponse.overall_response || "SD").toUpperCase();
  if (resp === "CR" || resp === "CMR") return "resp-cr";
  if (resp === "PR" || resp === "PMR") return "resp-pr";
  if (resp === "PD" || resp === "PMD") return "resp-pd";
  return "resp-sd";
});

function pctChangeClass(val) {
  if (!val) return "";
  if (val <= -30) return "text-success";
  if (val >= 20) return "text-danger";
  return "text-neutral";
}

function formatTimepoint(tp) {
  if (!tp) return "Baseline";
  const map = {
    BASELINE: "Baseline",
    CYCLE_2_DAY_1: "Cycle 2 Day 1",
    CYCLE_4_DAY_1: "Cycle 4 Day 1",
    END_OF_TREATMENT: "EOT",
  };
  return map[tp] || tp;
}

function getTimepointClass(tp) {
  if (!tp || tp === "BASELINE") return "tp-baseline";
  if (tp === "CYCLE_2_DAY_1") return "tp-c2d1";
  if (tp === "CYCLE_4_DAY_1") return "tp-c4d1";
  return "tp-followup";
}
</script>

<style scoped>
.oncology-calipers-panel {
  display: flex;
  flex-direction: column;
  background-color: var(--semantic-color-surface-inverse);
  border-radius: var(--semantic-border-radius-md, 8px);
  border: 1px solid var(--semantic-color-border);
  padding: 16px;
  gap: 16px;
  color: var(--semantic-color-text-inverse);
  height: 100%;
  overflow-y: auto;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--semantic-color-text-dark);
  padding-bottom: 10px;
}

.panel-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.badge-gxp {
  background-color: var(--semantic-badge-candidate-bg);
  color: var(--semantic-badge-candidate-text);
  border: 1px solid var(--semantic-badge-candidate-border);
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.section-subtitle {
  font-size: 0.78rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  color: var(--semantic-color-text-inverse-muted);
  margin-bottom: 8px;
}

.tool-btn-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
}

.tool-select-btn {
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 10px;
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s ease;
}

.tool-select-btn:hover {
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

.tool-select-btn.active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-accent-inverse);
  color: var(--semantic-color-text-on-primary);
  box-shadow: var(--shadow-focus-ring);
}

.form-row {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
  margin-bottom: 8px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.field-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-inverse-muted);
}

.field-input,
.field-select {
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 6px 8px;
  font-size: 0.8rem;
  box-sizing: border-box;
}

.section-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.btn-clear-all {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 6px;
  border-radius: 4px;
  background: none;
  border: none;
  color: var(--semantic-color-error);
  font-size: var(--font-size-2xs);
  font-weight: 600;
  cursor: pointer;
  box-sizing: border-box;
}

.empty-state {
  font-size: 0.78rem;
  color: var(--semantic-color-text-inverse-muted);
  padding: 12px;
  background-color: var(--semantic-color-surface-sunken);
  border: 1px dashed var(--semantic-color-border-inverse);
  border-radius: 6px;
  text-align: center;
}

.annotations-table-wrapper {
  max-height: 160px;
  overflow-y: auto;
  background-color: var(--semantic-color-surface-sunken);
  border: 1px solid var(--semantic-color-text-dark);
  border-radius: 6px;
}

.annotations-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.75rem;
}

.annotations-table th {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse-muted);
  text-align: left;
  padding: 6px 8px;
  font-weight: 600;
  border-bottom: 1px solid var(--semantic-color-text-dark);
}

.annotations-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--semantic-color-text-dark);
}

.timepoint-pill {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  display: inline-block;
  white-space: nowrap;
}

.tp-baseline {
  background-color: var(--semantic-badge-candidate-bg);
  color: var(--semantic-badge-candidate-text);
  border: 1px solid var(--semantic-badge-candidate-border);
}

.tp-c2d1,
.tp-c4d1,
.tp-followup {
  background-color: var(--semantic-badge-regulatory-bg);
  color: var(--semantic-badge-regulatory-text);
  border: 1px solid var(--semantic-badge-regulatory-border);
}

.type-pill {
  font-size: var(--font-size-2xs);
  padding: 2px 4px;
  border-radius: 3px;
  font-weight: 600;
}

.type-pill.target {
  background-color: var(--semantic-color-emerald-inverse-bg);
  color: var(--semantic-color-emerald-inverse-text);
}

.type-pill.non_target {
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
}

.type-pill.new_lesion {
  background-color: var(--semantic-badge-error-bg);
  color: var(--semantic-badge-error-text);
}

.btn-delete-annot {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 6px;
  border-radius: 4px;
  background: none;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  cursor: pointer;
  font-weight: bold;
  box-sizing: border-box;
}

.btn-delete-annot:hover {
  color: var(--semantic-color-error);
}

.derivation-summary-card {
  background: linear-gradient(
    135deg,
    var(--semantic-color-surface-elevated) 0%,
    var(--semantic-color-surface-inverse) 100%
  );
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  padding: 12px;
}

.summary-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.summary-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.response-pill {
  font-size: 0.8rem;
  font-weight: 800;
  padding: 3px 10px;
  border-radius: 999px;
  letter-spacing: 0.05em;
}

.resp-cr {
  background-color: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}

.resp-pr {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.resp-sd {
  background-color: var(--semantic-color-warning-action);
  color: var(--semantic-color-text-on-solid);
}

.resp-pd {
  background-color: var(--semantic-color-error-interactive);
  color: var(--semantic-color-text-on-solid);
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 8px;
}

.metric-item {
  display: flex;
  flex-direction: column;
}

.metric-label {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-inverse-muted);
}

.metric-value {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.text-success {
  color: var(--semantic-color-emerald-inverse-text);
}

.text-danger {
  color: var(--semantic-color-error);
}

.btn-primary-signoff {
  width: 100%;
  background: linear-gradient(
    135deg,
    var(--semantic-color-primary) 0%,
    var(--semantic-color-interactive-primary) 100%
  );
  color: var(--semantic-color-text-on-primary);
  border: none;
  border-radius: 6px;
  padding: 10px 14px;
  font-size: 0.85rem;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
  box-shadow: var(--shadow-md);
}

.modal-overlay {
  position: fixed;
  inset: 0;
  background-color: var(--semantic-color-backdrop-strong);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-card {
  background-color: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  width: 90%;
  max-width: 480px;
  padding: 20px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.modal-title {
  font-size: 1rem;
  font-weight: 700;
}

.btn-close {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  border-radius: 4px;
  background: none;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  font-size: 1.1rem;
  cursor: pointer;
  box-sizing: border-box;
}

.modal-disclaimer {
  font-size: 0.78rem;
  color: var(--semantic-color-text-inverse-muted);
  background-color: var(--semantic-color-surface-elevated);
  padding: 8px 12px;
  border-radius: 4px;
  margin-bottom: 12px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 16px;
}

.tool-select-btn.btn-ai-segment {
  border-color: var(--semantic-color-accent-inverse);
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-accent-inverse);
}

.tool-select-btn.btn-ai-segment:hover {
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

.ai-badge {
  background-color: var(--semantic-badge-candidate-bg);
  color: var(--semantic-badge-candidate-text);
  border: 1px solid var(--semantic-badge-candidate-border);
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.ai-edited-badge {
  background-color: var(--semantic-badge-queried-bg);
  color: var(--semantic-badge-queried-text);
  border: 1px solid var(--semantic-badge-queried-border);
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.manual-badge {
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse-muted);
  font-size: var(--font-size-2xs);
  padding: 2px 6px;
  border-radius: 4px;
}

.diameter-input {
  width: 56px;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background-color: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 2px 6px;
  font-size: 0.75rem;
  box-sizing: border-box;
}

.action-btn-group {
  display: flex;
  align-items: center;
  gap: 4px;
}

.btn-convert-manual {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  background-color: var(--semantic-badge-queried-bg);
  color: var(--semantic-badge-queried-text);
  border: 1px solid var(--semantic-badge-queried-border);
  font-size: var(--font-size-2xs);
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
  cursor: pointer;
  box-sizing: border-box;
}

.btn-convert-manual:hover {
  background-color: var(--semantic-color-warning-bg-badge);
}

.btn-secondary {
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
  border: none;
  padding: 8px 14px;
  border-radius: 4px;
  cursor: pointer;
}

.btn-confirm-sign {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border: none;
  padding: 8px 14px;
  border-radius: 4px;
  font-weight: 700;
  cursor: pointer;
}

.btn-confirm-sign:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
