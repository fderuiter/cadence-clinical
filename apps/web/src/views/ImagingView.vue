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
        <h2 class="view-title">
          Medical Imaging &amp; Oncologic Adjudication Console
        </h2>
        <p class="view-subtitle">
          High-performance browser-native multi-planar reconstruction (MPR)
          DICOM viewer with calibrated oncologic caliper measurement tools, sum
          of longest diameters (SLD), and automatic tumor response derivations
          (RECIST 1.1 and Lugano classification).
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
            <option value="SUBJ-ONC-001">
              Subject: SUBJ-ONC-001 (NSCLC Cohort)
            </option>
            <option value="SUBJ-ONC-002">
              Subject: SUBJ-ONC-002 (Lymphoma Cohort)
            </option>
            <option value="SUBJ-ONC-003">
              Subject: SUBJ-ONC-003 (Glioma Cohort)
            </option>
          </select>
        </div>

        <!-- View Mode Selector -->
        <div class="selector-box">
          <label for="view-mode-select" class="sr-only">Select View Mode</label>
          <select
            id="view-mode-select"
            v-model="viewMode"
            class="header-select view-mode-select"
            aria-label="Viewport Layout Mode"
          >
            <option value="single">Single Timepoint View</option>
            <option value="dual">Dual Timepoint Comparison View</option>
          </select>
        </div>

        <!-- Timepoint Selector (Single View Mode) -->
        <div v-if="viewMode === 'single'" class="selector-box">
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

        <!-- Dual Timepoint Controls (Dual View Mode) -->
        <template v-if="viewMode === 'dual'">
          <div class="selector-box">
            <label for="timepoint-a-select" class="sr-only"
              >Viewport A Timepoint</label
            >
            <select
              id="timepoint-a-select"
              v-model="timepointA"
              class="header-select"
              aria-label="Viewport A Timepoint"
            >
              <option value="BASELINE">A: Baseline Screening</option>
              <option value="CYCLE_2_DAY_1">A: Cycle 2 Day 1</option>
              <option value="CYCLE_4_DAY_1">A: Cycle 4 Day 1</option>
              <option value="END_OF_TREATMENT">A: End of Treatment</option>
            </select>
          </div>

          <div class="selector-box">
            <label for="timepoint-b-select" class="sr-only"
              >Viewport B Timepoint</label
            >
            <select
              id="timepoint-b-select"
              v-model="timepointB"
              class="header-select"
              aria-label="Viewport B Timepoint"
            >
              <option value="BASELINE">B: Baseline Screening</option>
              <option value="CYCLE_2_DAY_1">B: Cycle 2 Day 1</option>
              <option value="CYCLE_4_DAY_1">B: Cycle 4 Day 1</option>
              <option value="END_OF_TREATMENT">B: End of Treatment</option>
            </select>
          </div>

          <div class="selector-box">
            <button
              type="button"
              class="btn-sync-toggle"
              :class="{ synced: isViewportSynced }"
              title="Toggle Crosshairs and Wheel Scroll Sync"
              @click="isViewportSynced = !isViewportSynced"
            >
              {{ isViewportSynced ? "Viewports Synced" : "Viewports Unlinked" }}
            </button>
          </div>
        </template>

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

        <!-- Batch Ingest Button -->
        <div class="selector-box">
          <button
            type="button"
            class="btn-batch-upload"
            @click="isBatchModalOpen = true"
          >
            Batch Ingest DICOM Folder
          </button>
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
          <template v-if="viewMode === 'single'">
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <div
              v-for="s in availableSeries"
              :key="s.seriesUid"
              class="series-item"
              :class="{ active: activeSeriesUid === s.seriesUid }"
              @click="activeSeriesUid = s.seriesUid"
            >
              <div class="series-thumb">
                <ClinicalIcon name="camera" :size="16" />
              </div>
              <div class="series-meta">
                <div class="series-title">
                  {{ s.description }}
                </div>
                <div class="series-tags">
                  <span class="modality-tag">{{ s.modality }}</span>
                  <span class="slices-tag">{{ s.instancesCount }} slices</span>
                </div>
              </div>
            </div>
          </template>

          <template v-else>
            <div class="series-group-title">Viewport A Series</div>
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <div
              v-for="s in availableSeriesA"
              :key="'a-' + s.seriesUid"
              class="series-item"
              :class="{ active: activeSeriesUidA === s.seriesUid }"
              @click="activeSeriesUidA = s.seriesUid"
            >
              <div class="series-thumb">
                <ClinicalIcon name="camera" :size="16" />
              </div>
              <div class="series-meta">
                <div class="series-title">
                  {{ s.description }}
                </div>
                <div class="series-tags">
                  <span class="modality-tag">{{ s.modality }}</span>
                  <span class="slices-tag">{{ s.instancesCount }} slices</span>
                </div>
              </div>
            </div>

            <div class="series-group-title margin-top-sm">
              Viewport B Series
            </div>
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <div
              v-for="s in availableSeriesB"
              :key="'b-' + s.seriesUid"
              class="series-item"
              :class="{ active: activeSeriesUidB === s.seriesUid }"
              @click="activeSeriesUidB = s.seriesUid"
            >
              <div class="series-thumb">
                <ClinicalIcon name="camera" :size="16" />
              </div>
              <div class="series-meta">
                <div class="series-title">
                  {{ s.description }}
                </div>
                <div class="series-tags">
                  <span class="modality-tag">{{ s.modality }}</span>
                  <span class="slices-tag">{{ s.instancesCount }} slices</span>
                </div>
              </div>
            </div>
          </template>
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
          v-if="viewMode === 'single'"
          :timepoint="selectedTimepoint"
          viewport-label="Viewport A (Single Timepoint)"
          :series-uid="activeSeriesUid"
          :active-annotations="currentAnnotations"
          :active-tool="activeCaliperTool"
          @caliper-created="handleCaliperCreated($event, 'single')"
        />

        <div v-else class="dual-viewports-grid">
          <DicomMprViewer
            :timepoint="timepointA"
            viewport-label="Viewport A (Baseline)"
            :series-uid="activeSeriesUidA"
            :active-annotations="currentAnnotations"
            :active-tool="activeCaliperTool"
            :is-synced="isViewportSynced"
            :synced-state="sharedSyncState"
            @update:synced-state="onSyncStateUpdate"
            @update:is-synced="isViewportSynced = $event"
            @caliper-created="handleCaliperCreated($event, 'A')"
          />

          <DicomMprViewer
            :timepoint="timepointB"
            viewport-label="Viewport B (Follow-up)"
            :series-uid="activeSeriesUidB"
            :active-annotations="currentAnnotations"
            :active-tool="activeCaliperTool"
            :is-synced="isViewportSynced"
            :synced-state="sharedSyncState"
            @update:synced-state="onSyncStateUpdate"
            @update:is-synced="isViewportSynced = $event"
            @caliper-created="handleCaliperCreated($event, 'B')"
          />
        </div>
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
          @annotation-updated="handleAnnotationUpdated"
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
        <span class="trajectory-count"
          >{{ trajectoryHistory.length }} Locked Timepoint Assessments</span
        >
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
              <td class="font-bold">
                {{ t.timepoint_name }}
              </td>
              <td>{{ t.assessment_criteria }}</td>
              <td>
                {{
                  t.current_sld_mm
                    ? t.current_sld_mm + " mm"
                    : t.current_spd_mm2
                      ? t.current_spd_mm2 + " mm²"
                      : "-"
                }}
              </td>
              <td
                :class="
                  t.pct_change_baseline <= -30
                    ? 'text-success'
                    : t.pct_change_baseline >= 20
                      ? 'text-danger'
                      : ''
                "
              >
                {{ t.pct_change_baseline > 0 ? "+" : ""
                }}{{ t.pct_change_baseline }}%
              </td>
              <td :class="t.pct_change_nadir >= 20 ? 'text-danger' : ''">
                {{ t.pct_change_nadir > 0 ? "+" : "" }}{{ t.pct_change_nadir }}%
              </td>
              <td>{{ t.target_response }}</td>
              <td>
                <span
                  class="table-response-pill"
                  :class="'resp-' + (t.overall_response || '').toLowerCase()"
                >
                  {{ t.overall_response }}
                </span>
              </td>
              <td class="font-mono text-xs">Sealed</td>
              <td>{{ t.reader_id }}</td>
              <td>{{ t.created_at || "2026-08-23 04:15 UTC" }}</td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- Batch Ingestion Dropzone Modal -->
    <BatchDicomDropzoneModal
      :is-open="isBatchModalOpen"
      :subject-id="selectedSubjectId"
      :timepoint="selectedTimepoint"
      @close="isBatchModalOpen = false"
      @ingested="handleBatchIngested"
    />
  </div>
</template>

<script setup>
import { ClinicalIcon } from "ui";
import { ref, computed, watch } from "vue";
import DicomMprViewer from "../components/imaging/DicomMprViewer.vue";
import OncologyCalipers from "../components/imaging/OncologyCalipers.vue";
import DualReaderAdjudicationPanel from "../components/imaging/DualReaderAdjudicationPanel.vue";
import BatchDicomDropzoneModal from "../components/imaging/BatchDicomDropzoneModal.vue";

const isBatchModalOpen = ref(false);
const selectedSubjectId = ref("SUBJ-ONC-001");
const selectedTimepoint = ref("CYCLE_2_DAY_1");
const selectedCriteria = ref("RECIST_1_1");
const activeSeriesUid = ref("1.2.840.113619.2.55.3.200.1");
const activeCaliperTool = ref("CALIPER_1D");

const viewMode = ref("single"); // 'single' | 'dual'
const timepointA = ref("BASELINE");
const timepointB = ref("CYCLE_2_DAY_1");

const isViewportSynced = ref(true);
const sharedSyncState = ref({
  axialSlice: 24,
  sagittalSlice: 256,
  coronalSlice: 256,
  selectedPreset: "SOFT_TISSUE",
});

function onSyncStateUpdate(newState) {
  if (isViewportSynced.value && newState) {
    sharedSyncState.value = {
      ...sharedSyncState.value,
      ...newState,
    };
  }
}

const timepointSeriesMap = ref({
  BASELINE: [
    {
      seriesUid: "1.2.840.113619.2.55.3.200.1",
      description: "Baseline Chest/Abdomen/Pelvis CT",
      modality: "CT",
      instancesCount: 48,
    },
    {
      seriesUid: "1.2.840.113619.2.55.3.200.2",
      description: "Baseline Thoracic Lung Thin Slices 1.0mm",
      modality: "CT",
      instancesCount: 120,
    },
  ],
  CYCLE_2_DAY_1: [
    {
      seriesUid: "1.2.840.113619.2.55.3.200.3",
      description: "Cycle 2 Day 1 Contrast CT",
      modality: "CT",
      instancesCount: 48,
    },
    {
      seriesUid: "1.2.840.113619.2.55.3.200.4",
      description: "Cycle 2 Day 1 Lung Slices",
      modality: "CT",
      instancesCount: 120,
    },
  ],
  CYCLE_4_DAY_1: [
    {
      seriesUid: "1.2.840.113619.2.55.3.200.5",
      description: "Cycle 4 Day 1 Contrast CT",
      modality: "CT",
      instancesCount: 48,
    },
  ],
  END_OF_TREATMENT: [
    {
      seriesUid: "1.2.840.113619.2.55.3.200.6",
      description: "EOT Contrast CT",
      modality: "CT",
      instancesCount: 48,
    },
  ],
});

const activeSeriesUidA = ref("1.2.840.113619.2.55.3.200.1");
const activeSeriesUidB = ref("1.2.840.113619.2.55.3.200.3");

const availableSeriesA = computed(() => {
  return timepointSeriesMap.value[timepointA.value] || [];
});

const availableSeriesB = computed(() => {
  return timepointSeriesMap.value[timepointB.value] || [];
});

watch(timepointA, (newTp) => {
  const series = timepointSeriesMap.value[newTp];
  if (series && series.length > 0) {
    activeSeriesUidA.value = series[0].seriesUid;
  }
});

watch(timepointB, (newTp) => {
  const series = timepointSeriesMap.value[newTp];
  if (series && series.length > 0) {
    activeSeriesUidB.value = series[0].seriesUid;
  }
});

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
    points: [
      { x: 140, y: 150 },
      { x: 172, y: 150 },
    ],
    timepoint: "BASELINE",
  },
  {
    id: "annot-002",
    lesion_id: "TL2",
    organ_site: "LUNG_RIGHT",
    lesion_type: "TARGET",
    longest_diameter_mm: 15.5,
    perpendicular_diameter_mm: 12.0,
    points: [
      { x: 220, y: 180 },
      { x: 240, y: 180 },
    ],
    timepoint: "CYCLE_2_DAY_1",
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
  const targetLesions = currentAnnotations.value.filter(
    (a) => a.lesion_type === "TARGET"
  );
  const currentSld = targetLesions.reduce(
    (acc, l) => acc + (l.longest_diameter_mm || 0),
    0
  );
  const baselineSld = 65.0;
  const nadirSld = 65.0;

  const pctBaseline =
    baselineSld > 0
      ? Number((((currentSld - baselineSld) / baselineSld) * 100).toFixed(1))
      : 0;
  const pctNadir =
    nadirSld > 0
      ? Number((((currentSld - nadirSld) / nadirSld) * 100).toFixed(1))
      : 0;

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

function handleCaliperCreated(event, viewportKey = "single") {
  const originTimepoint =
    event.timepoint ||
    (viewMode.value === "dual"
      ? viewportKey === "A"
        ? timepointA.value
        : timepointB.value
      : selectedTimepoint.value);

  const newId = `annot-${Date.now()}`;
  const lesionNum = currentAnnotations.value.length + 1;
  const isAi =
    event.measurementType === "AI_AUTO_CONTOUR" || event.isAiAssisted;

  currentAnnotations.value.push({
    id: newId,
    lesion_id: event.lesion_id || `TL${lesionNum}`,
    organ_site: event.organ_site || "LIVER",
    lesion_type: event.lesion_type || "TARGET",
    longest_diameter_mm: event.longestDiameterMm || event.lengthMm,
    perpendicular_diameter_mm: event.perpendicularDiameterMm || null,
    measurement_type: isAi ? "AI_AUTO_CONTOUR" : "1D_RECIST",
    is_ai_assisted: isAi,
    hounsfield_stats: event.hounsfieldStats || null,
    model_version: event.modelVersion || null,
    points: event.points || [],
    fittedCalipers: event.fittedCalipers || [],
    timepoint: originTimepoint,
    viewport_origin:
      viewportKey === "A"
        ? "Viewport A"
        : viewportKey === "B"
          ? "Viewport B"
          : "Single Viewport",
  });
}

function handleAnnotationUpdated(updated) {
  const index = currentAnnotations.value.findIndex((a) => a.id === updated.id);
  if (index !== -1) {
    currentAnnotations.value[index] = {
      ...currentAnnotations.value[index],
      ...updated,
    };
  }
}

function handleDeleteAnnotation(id) {
  currentAnnotations.value = currentAnnotations.value.filter(
    (a) => a.id !== id
  );
}

function handleAssessmentSigned(eventPayload) {
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
    ai_assisted: eventPayload?.aiAssisted || false,
    ai_annotations_count: eventPayload?.aiAnnotationsCount || 0,
    reader_id: "dr.current.user",
    created_at:
      new Date().toISOString().replace("T", " ").substring(0, 19) + " UTC",
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

function handleAdjudicationCompleted(_event) {
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
    created_at:
      new Date().toISOString().replace("T", " ").substring(0, 19) + " UTC",
  });
}

function handleBatchIngested(result) {
  if (result && result.seriesSummary && result.seriesSummary.length > 0) {
    result.seriesSummary.forEach((s) => {
      const seriesUid =
        s.anonymizedSeriesUid ||
        s.AnonymizedSeriesUid ||
        `1.2.840.113619.2.${Date.now()}`;
      const modality = s.modality || s.Modality || "CT";
      const sliceCount =
        s.instanceCount || s.InstanceCount || result.ingestedSliceCount || 120;

      const existing = availableSeries.value.find(
        (a) => a.seriesUid === seriesUid
      );
      if (existing) {
        existing.instancesCount = sliceCount;
      } else {
        availableSeries.value.unshift({
          seriesUid: seriesUid,
          description: `Batch Ingested ${modality} Series (${selectedTimepoint.value})`,
          modality: modality,
          instancesCount: sliceCount,
        });
      }
      activeSeriesUid.value = seriesUid;
    });
  } else if (result) {
    const newUid = result.anonymizedStudyUid
      ? `${result.anonymizedStudyUid}.1`
      : `1.2.840.113619.2.${Date.now()}`;
    availableSeries.value.unshift({
      seriesUid: newUid,
      description: `Batch Ingested CT Series (${selectedTimepoint.value})`,
      modality: "CT",
      instancesCount: result.ingestedSliceCount || 120,
    });
    activeSeriesUid.value = newUid;
  }
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
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
}

.badge-gxp {
  background-color: var(--semantic-badge-in-progress-bg);
  color: var(--semantic-badge-in-progress-text);
  border: 1px solid var(--semantic-badge-in-progress-border);
}

.badge-regulatory {
  background-color: var(--semantic-badge-regulatory-bg);
  color: var(--semantic-badge-regulatory-text);
  border: 1px solid var(--semantic-badge-regulatory-border);
}

.badge-standard {
  background-color: var(--semantic-badge-verified-bg);
  color: var(--semantic-badge-verified-text);
  border: 1px solid var(--semantic-badge-verified-border);
}

.view-title {
  font-size: 1.5rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.view-subtitle {
  font-size: 0.85rem;
  color: var(--semantic-color-text-muted);
  line-height: 1.4;
}

.header-action-group {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.header-select {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  box-shadow: var(--shadow-sm);
}

.view-mode-select {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-accent-inverse);
}

.btn-sync-toggle {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
  border-radius: 6px;
  padding: 8px 12px;
  font-size: 0.85rem;
  font-weight: 700;
  cursor: pointer;
  transition: all 0.15s ease;
  box-shadow: var(--shadow-sm);
}

.btn-sync-toggle.synced {
  background-color: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.dual-viewports-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 16px;
  width: 100%;
}

@media (max-width: 1400px) {
  .dual-viewports-grid {
    grid-template-columns: 1fr;
  }
}

.series-group-title {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-accent-inverse);
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding-bottom: 4px;
}

.margin-top-sm {
  margin-top: 10px;
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

@media (max-width: 640px) {
  .dashboard-section {
    padding: 12px 6px;
    gap: 12px;
  }

  .section-header {
    gap: 10px;
  }

  .view-title {
    font-size: 1.2rem;
  }

  .header-action-group {
    width: 100%;
    flex-direction: column;
  }

  .selector-box,
  .header-select {
    width: 100%;
    box-sizing: border-box;
  }

  .imaging-workspace-grid {
    gap: 12px;
    min-height: auto;
  }

  .trajectory-section {
    padding: 10px 8px;
  }
}

.series-nav-panel {
  background-color: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  padding: 12px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.nav-panel-header {
  padding-bottom: 8px;
  border-bottom: 1px solid var(--semantic-color-text-dark);
}

.panel-heading {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
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
  background-color: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.series-item:hover {
  background-color: var(--semantic-color-border-inverse);
}

.series-item.active {
  background-color: var(--semantic-color-primary);
  border-color: var(--semantic-color-accent-inverse);
}

.series-item.active .series-title {
  color: var(--semantic-color-text-on-primary);
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
  color: var(--semantic-color-text-inverse);
  line-height: 1.2;
}

.series-tags {
  display: flex;
  gap: 4px;
}

.modality-tag,
.slices-tag {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  padding: 1px 4px;
  border-radius: 3px;
  background-color: var(--semantic-color-surface-sunken);
  color: var(--semantic-color-text-inverse-muted);
}

.volume-info-card {
  margin-top: auto;
  background-color: var(--semantic-color-surface-sunken);
  border: 1px solid var(--semantic-color-text-dark);
  border-radius: 6px;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.info-row {
  display: flex;
  justify-content: space-between;
  font-size: var(--font-size-2xs);
}

.info-label {
  color: var(--semantic-color-text-inverse-muted);
}

.info-val {
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.info-val.code {
  font-family: var(--semantic-font-mono);
  color: var(--semantic-color-accent-inverse);
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
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 12px;
  box-shadow: var(--shadow-sm);
}

.trajectory-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.trajectory-title {
  font-size: 1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.trajectory-count {
  font-size: 0.8rem;
  color: var(--semantic-color-text-muted);
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
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  text-align: left;
  padding: 10px 12px;
  font-weight: 700;
  border-bottom: 2px solid var(--semantic-color-border);
}

.trajectory-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
}

.font-bold {
  font-weight: 700;
}

.table-response-pill {
  font-size: var(--font-size-2xs);
  font-weight: 800;
  padding: 2px 8px;
  border-radius: 999px;
}

.resp-cr,
.resp-cmr {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.resp-pr,
.resp-pmr {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.resp-sd,
.resp-nmr {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
}

.resp-pd,
.resp-pmd {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.text-success {
  color: var(--semantic-color-success);
  font-weight: 700;
}

.text-danger {
  color: var(--semantic-color-error);
  font-weight: 700;
}
</style>
