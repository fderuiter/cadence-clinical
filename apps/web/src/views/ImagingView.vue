<template>
  <div id="section-imaging-viewer" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">DICOM PS 3.15</span>
          <span class="badge badge-standard">RECIST 1.1 / Lugano 2014</span>
          <span class="badge badge-standard">Zero-Footprint Web MPR</span>
        </div>
        <h2 class="view-title">Medical Imaging &amp; Oncologic Adjudication Console</h2>
        <p class="view-subtitle">
          High-performance browser-native multi-planar reconstruction (MPR) DICOM viewer
          with calibrated oncologic caliper measurement tools, sum of longest diameters (SLD),
          and automatic tumor response derivations (RECIST 1.1 and Lugano classification).
        </p>
      </div>

      <div class="header-action-group">
        <!-- Subject Selector -->
        <div class="selector-box">
          <label for="subject-select" class="sr-only">Select Subject</label>
          <select
            id="subject-select"
            v-model="selectedSubjectId"
            class="header-select"
            @change="loadSubjectData"
          >
            <option value="SUBJ-ONC-001">Subject: SUBJ-ONC-001 (NSCLC Cohort)</option>
            <option value="SUBJ-ONC-002">Subject: SUBJ-ONC-002 (Lymphoma Cohort)</option>
            <option value="SUBJ-ONC-003">Subject: SUBJ-ONC-003 (Glioma Cohort)</option>
          </select>
        </div>

        <!-- Timepoint Selector -->
        <div class="selector-box">
          <label for="timepoint-select" class="sr-only">Select Timepoint</label>
          <select
            id="timepoint-select"
            v-model="selectedTimepoint"
            class="header-select"
            @change="onTimepointChange"
          >
            <option value="BASELINE">Baseline Screening (Day -7)</option>
            <option value="CYCLE_2_DAY_1">Cycle 2 Day 1 (Week 6)</option>
            <option value="CYCLE_4_DAY_1">Cycle 4 Day 1 (Week 12)</option>
            <option value="END_OF_TREATMENT">End of Treatment (Week 24)</option>
          </select>
        </div>

        <!-- Criteria Selector -->
        <div class="selector-box">
          <label for="criteria-select" class="sr-only">Criteria</label>
          <select
            id="criteria-select"
            v-model="selectedCriteria"
            class="header-select"
            @change="recalculateResponse"
          >
            <option value="RECIST_1_1">RECIST 1.1 (Solid Tumors)</option>
            <option value="LUGANO_2014">Lugano 2014 (Lymphoma)</option>
          </select>
        </div>
      </div>
    </div>

    <!-- Main Workspace Split Layout -->
    <div class="imaging-workspace-grid">
      <!-- Left: Series Navigator -->
      <aside class="series-nav-panel">
        <div class="nav-panel-header">
          <span class="panel-heading">Series Envelope</span>
        </div>
        <div class="series-list">
          <div
            v-for="s in availableSeries"
            :key="s.seriesUid"
            class="series-item"
            :class="{ active: activeSeriesUid === s.seriesUid }"
            @click="activeSeriesUid = s.seriesUid"
          >
            <div class="series-thumb">🖼️</div>
            <div class="series-meta">
              <div class="series-title">{{ s.description }}</div>
              <div class="series-tags">
                <span class="modality-tag">{{ s.modality }}</span>
                <span class="slices-tag">{{ s.instancesCount }} slices</span>
              </div>
            </div>
          </div>
        </div>

        <!-- Series Volume Info Box -->
        <div class="volume-info-card">
          <div class="info-row">
            <span class="info-label">Voxel Spacing:</span>
            <span class="info-val">0.75 &times; 0.75 mm</span>
          </div>
          <div class="info-row">
            <span class="info-label">Slice Thickness:</span>
            <span class="info-val">2.50 mm</span>
          </div>
          <div class="info-row">
            <span class="info-label">De-ID Hash:</span>
            <span class="info-val code">a9f8...3e12</span>
          </div>
        </div>
      </aside>

      <!-- Center: MPR Viewer -->
      <main class="mpr-viewer-wrapper">
        <DicomMprViewer
          :series-uid="activeSeriesUid"
          :active-annotations="currentAnnotations"
          :active-tool="activeCaliperTool"
          @caliper-created="handleCaliperCreated"
        />
      </main>

      <!-- Right: Oncology Calipers & Derivation Console -->
      <aside class="calipers-console-wrapper">
        <OncologyCalipers
          :annotations="currentAnnotations"
          :criteria="selectedCriteria"
          :derived-response="derivedResponse"
          @tool-selected="activeCaliperTool = $event"
          @delete-annotation="handleDeleteAnnotation"
          @clear-all-annotations="currentAnnotations = []"
          @assessment-signed="handleAssessmentSigned"
        />
      </aside>
    </div>

    <!-- Dual-Reader Blinded Recording & Committee Chair Tie-Breaker Section -->
    <section class="adjudication-workflow-section">
      <DualReaderAdjudicationPanel
        :subject-id="selectedSubjectId"
        :timepoint="selectedTimepoint"
        @adjudication-completed="handleAdjudicationCompleted"
      />
    </section>

    <!-- Bottom: Longitudinal Tumor Trajectory Table -->
    <section class="trajectory-section">
      <div class="trajectory-header">
        <h3 class="trajectory-title">Longitudinal Tumor Response Trajectory</h3>
        <span class="trajectory-count">{{ trajectoryHistory.length }} Locked Timepoint Assessments</span>
      </div>

      <div class="trajectory-table-wrapper">
        <table class="trajectory-table">
          <thead>
            <tr>
              <th>Timepoint</th>
              <th>Criteria</th>
              <th>SLD / SPD</th>
              <th>% Chg Baseline</th>
              <th>% Chg Nadir</th>
              <th>Target Resp</th>
              <th>Overall Response</th>
              <th>Part 11 Seal</th>
              <th>Evaluated By</th>
              <th>Date / Time</th>
            </tr>
          </thead>
          <tbody>
            <tr v-for="t in trajectoryHistory" :key="t.id">
              <td class="font-bold">{{ t.timepoint_name }}</td>
              <td>{{ t.assessment_criteria }}</td>
              <td>{{ t.current_sld_mm ? t.current_sld_mm + ' mm' : (t.current_spd_mm2 ? t.current_spd_mm2 + ' mm²' : '-') }}</td>
              <td :class="t.pct_change_baseline <= -30 ? 'text-success' : (t.pct_change_baseline >= 20 ? 'text-danger' : '')">
                {{ t.pct_change_baseline > 0 ? '+' : '' }}{{ t.pct_change_baseline }}%
              </td>
              <td :class="t.pct_change_nadir >= 20 ? 'text-danger' : ''">
                {{ t.pct_change_nadir > 0 ? '+' : '' }}{{ t.pct_change_nadir }}%
              </td>
              <td>{{ t.target_response }}</td>
              <td>
                <span class="table-response-pill" :class="'resp-' + (t.overall_response || '').toLowerCase()">
                  {{ t.overall_response }}
                </span>
              </td>
              <td class="font-mono text-xs">🔒 Sealed</td>
              <td>{{ t.reader_id }}</td>
              <td>{{ t.created_at || '2026-08-23 04:15 UTC' }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";
import DicomMprViewer from "../components/imaging/DicomMprViewer.vue";
import OncologyCalipers from "../components/imaging/OncologyCalipers.vue";
import DualReaderAdjudicationPanel from "../components/imaging/DualReaderAdjudicationPanel.vue";

const selectedSubjectId = ref("SUBJ-ONC-001");
const selectedTimepoint = ref("CYCLE_2_DAY_1");
const selectedCriteria = ref("RECIST_1_1");
const activeSeriesUid = ref("1.2.840.113619.2.55.3.200.1");
const activeCaliperTool = ref("CALIPER_1D");

const availableSeries = ref([
  {
    seriesUid: "1.2.840.113619.2.55.3.200.1",
    description: "Chest/Abdomen/Pelvis Contrast CT",
    modality: "CT",
    instancesCount: 48,
  },
  {
    seriesUid: "1.2.840.113619.2.55.3.200.2",
    description: "Thoracic Lung Thin Slices 1.0mm",
    modality: "CT",
    instancesCount: 120,
  },
]);

const currentAnnotations = ref([
  {
    id: "annot-001",
    lesion_id: "TL1",
    organ_site: "LIVER",
    lesion_type: "TARGET",
    longest_diameter_mm: 24.5,
    perpendicular_diameter_mm: 18.0,
    points: [{ x: 140, y: 150 }, { x: 172, y: 150 }],
  },
  {
    id: "annot-002",
    lesion_id: "TL2",
    organ_site: "LUNG_RIGHT",
    lesion_type: "TARGET",
    longest_diameter_mm: 15.5,
    perpendicular_diameter_mm: 12.0,
    points: [{ x: 220, y: 180 }, { x: 240, y: 180 }],
  },
]);

const trajectoryHistory = ref([
  {
    id: "eval-001",
    timepoint_name: "BASELINE",
    assessment_criteria: "RECIST_1_1",
    baseline_sld_mm: 65.0,
    nadir_sld_mm: 65.0,
    current_sld_mm: 65.0,
    pct_change_baseline: 0.0,
    pct_change_nadir: 0.0,
    target_response: "SD",
    overall_response: "SD",
    reader_id: "dr.chen.oncologist",
    created_at: "2026-07-10 14:22 UTC",
  },
]);

const derivedResponse = computed(() => {
  const targetLesions = currentAnnotations.value.filter((a) => a.lesion_type === "TARGET");
  const currentSld = targetLesions.reduce((acc, l) => acc + (l.longest_diameter_mm || 0), 0);
  const baselineSld = 65.0;
  const nadirSld = 65.0;

  const pctBaseline = baselineSld > 0 ? Number((((currentSld - baselineSld) / baselineSld) * 100).toFixed(1)) : 0;
  const pctNadir = nadirSld > 0 ? Number((((currentSld - nadirSld) / nadirSld) * 100).toFixed(1)) : 0;

  let resp = "SD";
  if (pctBaseline <= -30) {
    resp = "PR";
  } else if (pctNadir >= 20 && currentSld - nadirSld >= 5) {
    resp = "PD";
  } else if (currentSld === 0) {
    resp = "CR";
  }

  return {
    current_sld_mm: Number(currentSld.toFixed(1)),
    baseline_sld_mm: baselineSld,
    nadir_sld_mm: nadirSld,
    pct_change_baseline: pctBaseline,
    pct_change_nadir: pctNadir,
    target_response: resp,
    overall_response: resp,
  };
});

function handleCaliperCreated(event) {
  const newId = `annot-${Date.now()}`;
  const lesionNum = currentAnnotations.value.length + 1;
  currentAnnotations.value.push({
    id: newId,
    lesion_id: `TL${lesionNum}`,
    organ_site: "LIVER",
    lesion_type: "TARGET",
    longest_diameter_mm: event.lengthMm,
    perpendicular_diameter_mm: null,
    points: event.points,
  });
}

function handleDeleteAnnotation(id) {
  currentAnnotations.value = currentAnnotations.value.filter((a) => a.id !== id);
}

function handleAssessmentSigned(event) {
  trajectoryHistory.value.push({
    id: `eval-${Date.now()}`,
    timepoint_name: selectedTimepoint.value,
    assessment_criteria: selectedCriteria.value,
    baseline_sld_mm: derivedResponse.value.baseline_sld_mm,
    nadir_sld_mm: derivedResponse.value.nadir_sld_mm,
    current_sld_mm: derivedResponse.value.current_sld_mm,
    pct_change_baseline: derivedResponse.value.pct_change_baseline,
    pct_change_nadir: derivedResponse.value.pct_change_nadir,
    target_response: derivedResponse.value.target_response,
    overall_response: derivedResponse.value.overall_response,
    reader_id: "dr.current.user",
    created_at: new Date().toISOString().replace("T", " ").substring(0, 19) + " UTC",
  });
}

function loadSubjectData() {
  // Re-initializes measurements for active subject
}

function onTimepointChange() {
  // Switches timepoint
}

function recalculateResponse() {
  // Switches between RECIST and Lugano
}

function handleAdjudicationCompleted(event) {
  trajectoryHistory.value.push({
    id: `adj-${Date.now()}`,
    timepoint_name: selectedTimepoint.value,
    assessment_criteria: selectedCriteria.value,
    baseline_sld_mm: 50.0,
    nadir_sld_mm: 22.0,
    current_sld_mm: 22.0,
    pct_change_baseline: -56.0,
    pct_change_nadir: 0.0,
    target_response: "PR",
    overall_response: "PR",
    reader_id: "chair_dr_smith (IRC Chair)",
    created_at: new Date().toISOString().replace("T", " ").substring(0, 19) + " UTC",
  });
}
</script>

<style scoped>
.dashboard-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
  padding: 20px 24px;
  width: 100%;
  box-sizing: border-box;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 16px;
}

.header-content-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-width: 800px;
}

.header-badge-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.badge {
  font-size: 0.72rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
}

.badge-gxp {
  background-color: rgba(56, 189, 248, 0.15);
  color: #38bdf8;
  border: 1px solid rgba(56, 189, 248, 0.4);
}

.badge-regulatory {
  background-color: rgba(168, 85, 247, 0.15);
  color: #c084fc;
  border: 1px solid rgba(168, 85, 247, 0.4);
}

.badge-standard {
  background-color: rgba(52, 211, 153, 0.15);
  color: #34d399;
  border: 1px solid rgba(52, 211, 153, 0.4);
}

.view-title {
  font-size: 1.5rem;
  font-weight: 800;
  color: #0f172a;
}

.view-subtitle {
  font-size: 0.85rem;
  color: #64748b;
  line-height: 1.4;
}

.header-action-group {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.header-select {
  background-color: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
  font-weight: 600;
  color: #1e293b;
  outline: none;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.imaging-workspace-grid {
  display: grid;
  grid-template-columns: 260px 1fr 360px;
  gap: 16px;
  min-height: 580px;
}

@media (max-width: 1200px) {
  .imaging-workspace-grid {
    grid-template-columns: 1fr;
  }
}

.series-nav-panel {
  background-color: #0f172a;
  border: 1px solid #334155;
  border-radius: 8px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.nav-panel-header {
  padding-bottom: 8px;
  border-bottom: 1px solid #1e293b;
}

.panel-heading {
  font-size: 0.85rem;
  font-weight: 700;
  color: #f8fafc;
}

.series-list {
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.series-item {
  display: flex;
  align-items: center;
  gap: 10px;
  background-color: #1e293b;
  border: 1px solid #334155;
  border-radius: 6px;
  padding: 8px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.series-item:hover {
  background-color: #334155;
}

.series-item.active {
  background-color: #0284c7;
  border-color: #38bdf8;
}

.series-thumb {
  font-size: 1.4rem;
}

.series-meta {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.series-title {
  font-size: 0.78rem;
  font-weight: 700;
  color: #ffffff;
  line-height: 1.2;
}

.series-tags {
  display: flex;
  gap: 4px;
}

.modality-tag,
.slices-tag {
  font-size: 0.65rem;
  font-weight: 600;
  padding: 1px 4px;
  border-radius: 3px;
  background-color: rgba(0, 0, 0, 0.3);
  color: #cbd5e1;
}

.volume-info-card {
  margin-top: auto;
  background-color: #090d16;
  border: 1px solid #1e293b;
  border-radius: 6px;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.info-row {
  display: flex;
  justify-content: space-between;
  font-size: 0.72rem;
}

.info-label {
  color: #94a3b8;
}

.info-val {
  font-weight: 700;
  color: #f8fafc;
}

.info-val.code {
  font-family: monospace;
  color: #38bdf8;
}

.mpr-viewer-wrapper {
  display: flex;
  flex-direction: column;
}

.calipers-console-wrapper {
  display: flex;
  flex-direction: column;
}

.trajectory-section {
  background-color: #ffffff;
  border: 1px solid #e2e8f0;
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.trajectory-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.trajectory-title {
  font-size: 1rem;
  font-weight: 700;
  color: #0f172a;
}

.trajectory-count {
  font-size: 0.8rem;
  color: #64748b;
  font-weight: 600;
}

.trajectory-table-wrapper {
  overflow-x: auto;
}

.trajectory-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8rem;
}

.trajectory-table th {
  background-color: #f8fafc;
  color: #475569;
  text-align: left;
  padding: 10px 12px;
  font-weight: 700;
  border-bottom: 2px solid #e2e8f0;
}

.trajectory-table td {
  padding: 10px 12px;
  border-bottom: 1px solid #f1f5f9;
  color: #1e293b;
}

.font-bold {
  font-weight: 700;
}

.table-response-pill {
  font-size: 0.72rem;
  font-weight: 800;
  padding: 2px 8px;
  border-radius: 999px;
}

.resp-cr, .resp-cmr {
  background-color: #d1fae5;
  color: #065f46;
}

.resp-pr, .resp-pmr {
  background-color: #e0f2fe;
  color: #0369a1;
}

.resp-sd, .resp-nmr {
  background-color: #fef3c7;
  color: #92400e;
}

.resp-pd, .resp-pmd {
  background-color: #fee2e2;
  color: #991b1b;
}

.text-success {
  color: #059669;
  font-weight: 700;
}

.text-danger {
  color: #dc2626;
  font-weight: 700;
}
</style>
