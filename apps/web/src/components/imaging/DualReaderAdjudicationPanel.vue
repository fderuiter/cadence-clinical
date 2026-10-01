<template>
  <div class="dual-reader-panel">
    <!-- Panel Header & IRC Persona Selector -->
    <div class="panel-header">
      <div class="header-titles">
        <h3 class="panel-title">
          Blinded Dual-Reader Charter &amp; Tie-Breaker Adjudication
        </h3>
        <span class="badge badge-gxp">21 CFR Part 11 &bull; IRC Protocol</span>
      </div>

      <!-- Persona / Role Tabs -->
      <div class="role-selector-tabs">
        <button
          type="button"
          class="role-tab-btn"
          :class="{ active: activePersona === 'READER_1' }"
          @click="switchPersona('READER_1')"
        >
          <span class="role-icon">👤</span> Reader 1 (Blinded)
        </button>
        <button
          type="button"
          class="role-tab-btn"
          :class="{ active: activePersona === 'READER_2' }"
          @click="switchPersona('READER_2')"
        >
          <span class="role-icon">👤</span> Reader 2 (Blinded)
        </button>
        <button
          type="button"
          class="role-tab-btn"
          :class="{ active: activePersona === 'CHAIR' }"
          @click="switchPersona('CHAIR')"
        >
          <span class="role-icon">⚖️</span> Adjudication Chair
        </button>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- VIEW A: Blinded Primary Reader Recording Form (Reader 1 / Reader 2)        -->
    <!-- ========================================================================= -->
    <div
      v-if="activePersona === 'READER_1' || activePersona === 'READER_2'"
      class="blinded-reader-view"
    >
      <!-- Blinding Enclosure Notice -->
      <div class="blinding-alert-box">
        <div class="alert-icon">
          🔒
        </div>
        <div class="alert-content">
          <div class="alert-title">
            Blinded Independent Review Protocol Active
          </div>
          <div class="alert-text">
            Operating as
            <strong>{{
              activePersona === "READER_1"
                ? "Reader 1 (Dr. Sarah Miller)"
                : "Reader 2 (Dr. James Wilson)"
            }}</strong>. Subject treatment arm, clinical trial site, and peer reader
            observations are completely blinded per Charter section 3.1.
          </div>
        </div>
      </div>

      <!-- Blinded Reader Status & Entry Form -->
      <div class="reader-form-card">
        <div class="card-header-row">
          <span class="card-subtitle">Reader Assessment Entry</span>
          <span
            class="status-pill"
            :class="
              currentReaderStatus === 'COMPLETED'
                ? 'status-submitted'
                : 'status-pending'
            "
          >
            Status: {{ currentReaderStatus }}
          </span>
        </div>

        <div class="metrics-preview-row">
          <div class="metric-card">
            <span class="metric-card-label">Target Lesions Count</span>
            <span class="metric-card-val">{{
              currentReaderLesions.length
            }}</span>
          </div>
          <div class="metric-card">
            <span class="metric-card-label">Sum of Diameters (SLD)</span>
            <span class="metric-card-val font-mono">{{ currentReaderSLD }} mm</span>
          </div>
          <div class="metric-card">
            <span class="metric-card-label">Peer Read Status</span>
            <span class="metric-card-val text-muted">BLINDED</span>
          </div>
        </div>

        <!-- Lesion List for Current Reader -->
        <div class="lesion-table-container">
          <table class="reader-lesion-table">
            <thead>
              <tr>
                <th>Lesion ID</th>
                <th>Organ Site</th>
                <th>Longest Axis (mm)</th>
                <th>Short Axis (mm)</th>
                <th>Type</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="l in currentReaderLesions"
                :key="l.lesion_id"
              >
                <td class="font-bold">
                  {{ l.lesion_id }}
                </td>
                <td>{{ l.organ_site }}</td>
                <td class="font-mono">
                  {{ l.longest_diameter_mm }} mm
                </td>
                <td class="font-mono">
                  {{ l.short_axis_mm ? l.short_axis_mm + " mm" : "-" }}
                </td>
                <td><span class="badge badge-target">TARGET</span></td>
              </tr>
              <tr v-if="currentReaderLesions.length === 0">
                <td
                  colspan="5"
                  class="empty-row"
                >
                  No lesions added. Use the MPR Caliper tools to place
                  measurements.
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Qualitative Non-Target & Response Classification -->
        <div class="qualitative-assessment-grid">
          <div class="form-group">
            <label
              for="non-target-select"
              class="form-label"
            >Non-Target Lesion Status:</label>
            <select
              id="non-target-select"
              v-model="currentAssessment.non_target_status"
              class="form-select"
              :disabled="currentReaderStatus === 'COMPLETED'"
            >
              <option value="CR">
                Complete Response (CR)
              </option>
              <option value="NON_CR_NON_PD">
                Non-CR / Non-PD (Stable)
              </option>
              <option value="PD">
                Unequivocal Progression (PD)
              </option>
              <option value="NE">
                Not Evaluable (NE)
              </option>
            </select>
          </div>

          <div class="form-group">
            <label
              for="new-lesions-toggle"
              class="form-label"
            >New Malignant Lesions Present?</label>
            <div class="toggle-group">
              <button
                type="button"
                class="toggle-btn"
                :class="{ active: !currentAssessment.new_lesions_present }"
                :disabled="currentReaderStatus === 'COMPLETED'"
                @click="currentAssessment.new_lesions_present = false"
              >
                No New Lesions
              </button>
              <button
                type="button"
                class="toggle-btn toggle-danger"
                :class="{ active: currentAssessment.new_lesions_present }"
                :disabled="currentReaderStatus === 'COMPLETED'"
                @click="currentAssessment.new_lesions_present = true"
              >
                Yes (New Lesions)
              </button>
            </div>
          </div>

          <div class="form-group full-width">
            <label
              for="overall-response-select"
              class="form-label"
            >Overall Categorical Response:</label>
            <select
              id="overall-response-select"
              v-model="currentAssessment.overall_response"
              class="form-select response-highlight-select"
              :disabled="currentReaderStatus === 'COMPLETED'"
            >
              <option value="CR">
                Complete Response (CR)
              </option>
              <option value="PR">
                Partial Response (PR)
              </option>
              <option value="SD">
                Stable Disease (SD)
              </option>
              <option value="PD">
                Progressive Disease (PD)
              </option>
              <option value="NE">
                Not Evaluable (NE)
              </option>
            </select>
          </div>

          <div class="form-group full-width">
            <label
              for="reader-notes-input"
              class="form-label"
            >Radiologist Interpretation Notes:</label>
            <textarea
              id="reader-notes-input"
              v-model="currentAssessment.assessment_notes"
              class="form-textarea"
              rows="2"
              placeholder="Enter blinded qualitative findings and slice references..."
              :disabled="currentReaderStatus === 'COMPLETED'"
            />
          </div>
        </div>

        <!-- Submit Button -->
        <div class="action-footer">
          <button
            v-if="currentReaderStatus !== 'COMPLETED'"
            type="button"
            class="btn-submit-read"
            @click="submitReaderRead"
          >
            <span class="btn-icon">📤</span> Submit Blinded
            {{ activePersona === "READER_1" ? "Reader 1" : "Reader 2" }}
            Assessment
          </button>
          <button
            v-else
            type="button"
            class="btn-retract-read"
            @click="retractReaderRead"
          >
            <span class="btn-icon">✏️</span> Edit Assessment (Unlock)
          </button>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- VIEW B: Adjudication Committee Chair Tie-Breaker Console                   -->
    <!-- ========================================================================= -->
    <div
      v-else
      class="chair-adjudication-view"
    >
      <!-- Discordance Evaluation Banner -->
      <div
        class="discordance-banner"
        :class="
          isDiscordant
            ? 'banner-discordant'
            : workflowStatus === 'ADJUDICATED'
              ? 'banner-adjudicated'
              : 'banner-concordant'
        "
      >
        <div class="banner-icon">
          {{
            isDiscordant ? "⚠️" : workflowStatus === "ADJUDICATED" ? "✅" : "🤝"
          }}
        </div>
        <div class="banner-info">
          <div class="banner-headline">
            {{
              isDiscordant
                ? "Dual-Reader Discordance Detected — Tie-Breaker Required"
                : workflowStatus === "ADJUDICATED"
                  ? "Case Adjudicated & Sealed with 21 CFR Part 11 Signature"
                  : "Dual-Reader Concordance Achieved"
            }}
          </div>
          <div class="banner-subtext">
            SLD Difference Delta:
            <strong>{{ (sldDelta * 100).toFixed(1) }}%</strong>
            <span
              v-if="sldDelta > 0.2"
              class="threshold-tag"
            >(Exceeds 20.0% Threshold)</span>
            &bull; Workflow Status: <strong>{{ workflowStatus }}</strong>
          </div>
        </div>
      </div>

      <!-- Side-by-Side Dual-Reader Comparison Matrix -->
      <div class="side-by-side-grid">
        <!-- Reader 1 Assessment Card -->
        <div
          class="reader-compare-card"
          :class="{ 'card-highlight': selectedBindingReader === 'READER_1' }"
        >
          <div class="compare-header">
            <span class="reader-label">Reader 1: Dr. Sarah Miller</span>
            <span
              class="response-badge"
              :class="
                'resp-' +
                  (reader1Assessment.overall_response || 'pr').toLowerCase()
              "
            >
              {{ reader1Assessment.overall_response }}
            </span>
          </div>
          <div class="compare-body">
            <div class="compare-metric">
              <span class="lbl">Sum of Diameters (SLD):</span>
              <span class="val font-mono">{{ reader1Assessment.sum_longest_diameters_mm }} mm</span>
            </div>
            <div class="compare-metric">
              <span class="lbl">Non-Target Status:</span>
              <span class="val">{{ reader1Assessment.non_target_status }}</span>
            </div>
            <div class="compare-metric">
              <span class="lbl">New Lesions:</span>
              <span class="val">{{
                reader1Assessment.new_lesions_present
                  ? "Yes (" + reader1Assessment.new_lesions_count + ")"
                  : "None"
              }}</span>
            </div>
            <div class="compare-notes">
              <span class="lbl">Notes:</span>
              <p class="notes-txt">
                {{ reader1Assessment.assessment_notes || "No notes provided." }}
              </p>
            </div>
          </div>
        </div>

        <!-- Reader 2 Assessment Card -->
        <div
          class="reader-compare-card"
          :class="{ 'card-highlight': selectedBindingReader === 'READER_2' }"
        >
          <div class="compare-header">
            <span class="reader-label">Reader 2: Dr. James Wilson</span>
            <span
              class="response-badge"
              :class="
                'resp-' +
                  (reader2Assessment.overall_response || 'pd').toLowerCase()
              "
            >
              {{ reader2Assessment.overall_response }}
            </span>
          </div>
          <div class="compare-body">
            <div class="compare-metric">
              <span class="lbl">Sum of Diameters (SLD):</span>
              <span class="val font-mono">{{ reader2Assessment.sum_longest_diameters_mm }} mm</span>
            </div>
            <div class="compare-metric">
              <span class="lbl">Non-Target Status:</span>
              <span class="val">{{ reader2Assessment.non_target_status }}</span>
            </div>
            <div class="compare-metric">
              <span class="lbl">New Lesions:</span>
              <span
                class="val"
                :class="{
                  'text-danger': reader2Assessment.new_lesions_present,
                }"
              >
                {{
                  reader2Assessment.new_lesions_present
                    ? "Yes (" + reader2Assessment.new_lesions_count + ")"
                    : "None"
                }}
              </span>
            </div>
            <div class="compare-notes">
              <span class="lbl">Notes:</span>
              <p class="notes-txt">
                {{ reader2Assessment.assessment_notes || "No notes provided." }}
              </p>
            </div>
          </div>
        </div>
      </div>

      <!-- Chair Tie-Breaker Resolution Card -->
      <div class="chair-decision-card">
        <div class="card-header-row">
          <span class="card-subtitle">Committee Chair Determination</span>
          <span
            v-if="workflowStatus === 'ADJUDICATED'"
            class="badge badge-gxp"
          >Locked &amp; Signed</span>
        </div>

        <div class="form-row">
          <div class="form-group full-width">
            <label
              for="binding-reader-select"
              class="form-label"
            >Binding Final Assessment Source:</label>
            <select
              id="binding-reader-select"
              v-model="selectedBindingReader"
              class="form-select font-bold"
              :disabled="workflowStatus === 'ADJUDICATED'"
            >
              <option value="READER_1">
                Bind Reader 1 Determination (Partial Response - PR, 24.0mm)
              </option>
              <option value="READER_2">
                Bind Reader 2 Determination (Progressive Disease - PD, 42.0mm)
              </option>
              <option value="ADJUDICATOR">
                Adjudication Chair Independent Determination (PR, 22.0mm)
              </option>
            </select>
          </div>
        </div>

        <div class="form-row">
          <div class="form-group full-width">
            <label
              for="adjudication-rationale-input"
              class="form-label"
            >
              Mandatory Clinical Adjudication Rationale (21 CFR Part 11):
            </label>
            <textarea
              id="adjudication-rationale-input"
              v-model="adjudicationRationale"
              class="form-textarea"
              rows="3"
              placeholder="Detail medical justification for resolving discordance between readers..."
              :disabled="workflowStatus === 'ADJUDICATED'"
            />
          </div>
        </div>

        <!-- Action Buttons -->
        <div class="chair-actions-row">
          <button
            v-if="workflowStatus !== 'ADJUDICATED'"
            type="button"
            class="btn-chair-signoff"
            :disabled="!adjudicationRationale.trim()"
            @click="isSignoffModalOpen = true"
          >
            <span class="btn-icon">✍️</span> Execute 21 CFR Part 11 Adjudication
            Sign-Off
          </button>

          <div
            v-else
            class="post-adjudication-actions"
          >
            <button
              type="button"
              class="btn-export-edc"
              @click="isEdcModalOpen = true"
            >
              <span class="btn-icon">📋</span> View EDC eCRF Binding
            </button>
            <button
              type="button"
              class="btn-export-sdtm"
              @click="isSdtmModalOpen = true"
            >
              <span class="btn-icon">📊</span> Export CDISC SDTM TU/TR/RS
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- MODAL 1: 21 CFR Part 11 Step-Up Electronic Signature Dialog                -->
    <!-- ========================================================================= -->
    <div
      v-if="isSignoffModalOpen"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h4 class="modal-title">
            21 CFR Part 11 Adjudication Chair Sign-off
          </h4>
          <button
            type="button"
            class="btn-close"
            @click="isSignoffModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="modal-warning-box">
            <strong>Legal Manifestation:</strong> Executing this signature
            legally binds the final oncologic response determination and commits
            the tumor assessment to the study trial master record.
          </div>

          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="form-label">Signer Identity:</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              type="text"
              class="form-input"
              value="Dr. Arthur Smith (Adjudication Committee Chair)"
              disabled
            >
          </div>

          <div class="form-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="form-label">Signing Reason:</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              type="text"
              class="form-input"
              value="BLINDED_IRC_ADJUDICATION_SIGN_OFF"
              disabled
            >
          </div>

          <div class="form-group">
            <label
              for="chair-password-input"
              class="form-label"
            >Re-authentication Password / PIN:</label>
            <input
              id="chair-password-input"
              v-model="chairPassword"
              type="password"
              class="form-input"
              placeholder="Enter Part 11 credential..."
            >
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn-secondary"
            @click="isSignoffModalOpen = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn-confirm-sign"
            :disabled="!chairPassword"
            @click="executeChairSignoff"
          >
            Sign &amp; Lock Binding Determination
          </button>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- MODAL 2: Direct EDC eCRF Binding Preview Modal                             -->
    <!-- ========================================================================= -->
    <div
      v-if="isEdcModalOpen"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card modal-large">
        <div class="modal-header">
          <h4 class="modal-title">
            Direct EDC eCRF Binding (Form: eCRF_TUMOR_ASSESSMENT)
          </h4>
          <button
            type="button"
            class="btn-close"
            @click="isEdcModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="json-preview-container">
            <pre
              class="json-code"
            ><code>{{ JSON.stringify(edcBindingData, null, 2) }}</code></pre>
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn-secondary"
            @click="isEdcModalOpen = false"
          >
            Close
          </button>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- MODAL 3: CDISC SDTM TU/TR/RS Export Modal                                 -->
    <!-- ========================================================================= -->
    <div
      v-if="isSdtmModalOpen"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card modal-large">
        <div class="modal-header">
          <h4 class="modal-title">
            CDISC SDTM Dataset Package (TU, TR, RS)
          </h4>
          <button
            type="button"
            class="btn-close"
            @click="isSdtmModalOpen = false"
          >
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="json-preview-container">
            <pre
              class="json-code"
            ><code>{{ JSON.stringify(sdtmExportData, null, 2) }}</code></pre>
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn-secondary"
            @click="isSdtmModalOpen = false"
          >
            Close
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";

const props = defineProps({
  subjectId: {
    type: String,
    default: "SUBJ-ONC-777",
  },
  timepoint: {
    type: String,
    default: "CYCLE_2_DAY_1",
  },
});

const emit = defineEmits(["adjudicationCompleted"]);

// Active Persona / Role Switcher
const activePersona = ref("READER_1"); // 'READER_1', 'READER_2', 'CHAIR'

// Reader 1 State
const reader1Status = ref("COMPLETED");
const reader1Assessment = ref({
  reader_id: "radiologist_01",
  reader_name: "Dr. Sarah Miller",
  reader_role: "READER_1",
  target_lesions: [
    {
      lesion_id: "TL1",
      organ_site: "LIVER",
      longest_diameter_mm: 24.0,
      short_axis_mm: 18.0,
    },
  ],
  sum_longest_diameters_mm: 24.0,
  non_target_status: "NON_CR_NON_PD",
  new_lesions_present: false,
  new_lesions_count: 0,
  overall_response: "PR",
  assessment_notes: "Significant shrinkage in segment IV liver metastasis.",
});

// Reader 2 State
const reader2Status = ref("COMPLETED");
const reader2Assessment = ref({
  reader_id: "radiologist_02",
  reader_name: "Dr. James Wilson",
  reader_role: "READER_2",
  target_lesions: [
    {
      lesion_id: "TL1",
      organ_site: "LIVER",
      longest_diameter_mm: 42.0,
      short_axis_mm: 30.0,
    },
  ],
  sum_longest_diameters_mm: 42.0,
  non_target_status: "NON_CR_NON_PD",
  new_lesions_present: true,
  new_lesions_count: 1,
  overall_response: "PD",
  assessment_notes:
    "Observed new subcapsular nodule in right hepatic lobe. Primary lesion increased in diameter.",
});

// Chair Adjudication State
const workflowStatus = ref("DISCORDANCE_DETECTED"); // 'PENDING', 'CONSENSUS_REACHED', 'DISCORDANCE_DETECTED', 'ADJUDICATED'
const selectedBindingReader = ref("ADJUDICATOR");
const adjudicationRationale = ref(
  "Reviewed axial contrast CT series 2 slice 48. Confirmed Reader 2 misidentified hepatic vein branching artifact as new metastasis. Verified target lesion longest diameter 22.0 mm. Binding response: Partial Response (PR)."
);

// Modals
const isSignoffModalOpen = ref(false);
const isEdcModalOpen = ref(false);
const isSdtmModalOpen = ref(false);
const chairPassword = ref("");

// Computed properties
const currentReaderStatus = computed(() => {
  return activePersona.value === "READER_1"
    ? reader1Status.value
    : reader2Status.value;
});

const currentReaderLesions = computed(() => {
  return activePersona.value === "READER_1"
    ? reader1Assessment.value.target_lesions
    : reader2Assessment.value.target_lesions;
});

const currentReaderSLD = computed(() => {
  return activePersona.value === "READER_1"
    ? reader1Assessment.value.sum_longest_diameters_mm
    : reader2Assessment.value.sum_longest_diameters_mm;
});

const currentAssessment = computed(() => {
  return activePersona.value === "READER_1"
    ? reader1Assessment.value
    : reader2Assessment.value;
});

const sldDelta = computed(() => {
  const sld1 = reader1Assessment.value.sum_longest_diameters_mm;
  const sld2 = reader2Assessment.value.sum_longest_diameters_mm;
  const maxSld = Math.max(sld1, sld2);
  if (maxSld === 0) return 0;
  return Math.abs(sld1 - sld2) / maxSld;
});

const isDiscordant = computed(() => {
  if (
    reader1Status.value !== "COMPLETED" ||
    reader2Status.value !== "COMPLETED"
  )
    return false;
  if (workflowStatus.value === "ADJUDICATED") return false;
  const responseMismatch =
    reader1Assessment.value.overall_response !==
    reader2Assessment.value.overall_response;
  const sldExceeds = sldDelta.value > 0.2;
  const newLesionMismatch =
    reader1Assessment.value.new_lesions_present !==
    reader2Assessment.value.new_lesions_present;
  return responseMismatch || sldExceeds || newLesionMismatch;
});

// EDC eCRF Binding Payload
const edcBindingData = computed(() => ({
  form_id: "eCRF_TUMOR_ASSESSMENT",
  study_id: "TRIAL-SOLID-101",
  site_id: "SITE-NYC-01",
  subject_id: props.subjectId,
  timepoint_name: props.timepoint,
  assessment_criteria: "RECIST_1_1",
  target_lesions: [
    {
      lesion_id: "TL1",
      lesion_type: "TARGET",
      organ_site: "LIVER",
      longest_diameter_mm: 22.0,
      short_axis_mm: 16.5,
      slice_location: -45.0,
      sop_instance_uid: "1.2.840.113619.2.55.3.200.1.1",
    },
  ],
  sum_longest_diameters_mm: 22.0,
  baseline_sld_mm: 50.0,
  nadir_sld_mm: 22.0,
  pct_change_baseline: -56.0,
  pct_change_nadir: 0.0,
  non_target_status: "NON_CR_NON_PD",
  new_lesions_present: false,
  overall_response: "PR",
  adjudication_status: workflowStatus.value,
  binding_source: selectedBindingReader.value,
  manifest_sha256:
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
  signed_by: "chair_dr_smith",
  signed_at: "2026-08-24T14:45:00Z",
}));

// CDISC SDTM Datasets Export Payload
const sdtmExportData = computed(() => ({
  study_id: "TRIAL-SOLID-101",
  subject_id: props.subjectId,
  timepoint_name: props.timepoint,
  TU: [
    {
      STUDYID: "TRIAL-SOLID-101",
      DOMAIN: "TU",
      USUBJID: `TRIAL-SOLID-101-${props.subjectId}`,
      TUSEQ: 1,
      TULNKID: "TL1",
      TUTESTCD: "TUMIDENT",
      TUTEST: "Tumor Identification",
      TUORRES: "TARGET",
      TUSTRESC: "TARGET",
      TUMETHOD: "CT SCAN",
      TULOC: "LIVER",
      VISIT: props.timepoint,
      TUEVAL: "INDEPENDENT ASSESSOR",
      TUEVALID: "chair_dr_smith",
    },
  ],
  TR: [
    {
      STUDYID: "TRIAL-SOLID-101",
      DOMAIN: "TR",
      USUBJID: `TRIAL-SOLID-101-${props.subjectId}`,
      TRSEQ: 1,
      TRLNKID: "TL1",
      TRTESTCD: "LDIAM",
      TRTEST: "Longest Diameter",
      TRORRES: "22.0",
      TRORRESU: "mm",
      TRSTRESN: 22.0,
      TRSTRESU: "mm",
      TRMETHOD: "CT SCAN",
      VISIT: props.timepoint,
      TREVAL: "INDEPENDENT ASSESSOR",
      TREVALID: "chair_dr_smith",
    },
    {
      STUDYID: "TRIAL-SOLID-101",
      DOMAIN: "TR",
      USUBJID: `TRIAL-SOLID-101-${props.subjectId}`,
      TRSEQ: 2,
      TRTESTCD: "SUMDIR",
      TRTEST: "Sum of Longest Diameters",
      TRORRES: "22.0",
      TRORRESU: "mm",
      TRSTRESN: 22.0,
      TRSTRESU: "mm",
      TRMETHOD: "CT SCAN",
      VISIT: props.timepoint,
      TREVAL: "INDEPENDENT ASSESSOR",
      TREVALID: "chair_dr_smith",
    },
  ],
  RS: [
    {
      STUDYID: "TRIAL-SOLID-101",
      DOMAIN: "RS",
      USUBJID: `TRIAL-SOLID-101-${props.subjectId}`,
      RSSEQ: 1,
      RSTESTCD: "OVRLRESP",
      RSTEST: "Overall Response",
      RSORRES: "PR",
      RSSTRESC: "PR",
      RSMETHOD: "RECIST 1.1",
      VISIT: props.timepoint,
      RSEVAL: "INDEPENDENT ASSESSOR",
      RSEVALID: "chair_dr_smith",
      RSEVALSTAT: "FINAL",
    },
  ],
}));

function switchPersona(persona) {
  activePersona.value = persona;
}

function submitReaderRead() {
  if (activePersona.value === "READER_1") {
    reader1Status.value = "COMPLETED";
  } else {
    reader2Status.value = "COMPLETED";
  }
}

function retractReaderRead() {
  if (activePersona.value === "READER_1") {
    reader1Status.value = "PENDING";
  } else {
    reader2Status.value = "PENDING";
  }
}

function executeChairSignoff() {
  workflowStatus.value = "ADJUDICATED";
  isSignoffModalOpen.value = false;
  chairPassword.value = "";
  emit("adjudicationCompleted", {
    bindingReader: selectedBindingReader.value,
    rationale: adjudicationRationale.value,
    status: "ADJUDICATED",
  });
}
</script>

<style scoped>
.dual-reader-panel {
  display: flex;
  flex-direction: column;
  background-color: var(--semantic-color-surface-elevated, #0f172a);
  border: 1px solid var(--semantic-color-border, #334155);
  border-radius: var(--semantic-border-radius-md, 8px);
  padding: 16px;
  gap: 16px;
  color: #f8fafc;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  border-bottom: 1px solid #1e293b;
  padding-bottom: 12px;
}

.header-titles {
  display: flex;
  align-items: center;
  gap: 10px;
}

.panel-title {
  font-size: 1.05rem;
  font-weight: 800;
  color: #ffffff;
}

.role-selector-tabs {
  display: flex;
  gap: 6px;
  background-color: #090d16;
  padding: 4px;
  border-radius: 6px;
  border: 1px solid #1e293b;
}

.role-tab-btn {
  background: none;
  border: none;
  color: #94a3b8;
  font-size: 0.78rem;
  font-weight: 700;
  padding: 6px 12px;
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
  transition: all 0.15s ease;
}

.role-tab-btn:hover {
  color: #f8fafc;
  background-color: rgba(255, 255, 255, 0.05);
}

.role-tab-btn.active {
  background-color: #0284c7;
  color: #ffffff;
  box-shadow: 0 1px 4px rgba(2, 132, 199, 0.4);
}

.blinding-alert-box {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  background-color: rgba(14, 165, 233, 0.1);
  border: 1px solid rgba(14, 165, 233, 0.35);
  border-radius: 6px;
  padding: 10px 14px;
}

.alert-icon {
  font-size: 1.2rem;
}

.alert-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: #38bdf8;
  margin-bottom: 2px;
}

.alert-text {
  font-size: 0.75rem;
  color: #cbd5e1;
  line-height: 1.4;
}

.reader-form-card,
.chair-decision-card {
  background-color: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.card-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.card-subtitle {
  font-size: 0.85rem;
  font-weight: 700;
  color: #f8fafc;
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.status-pill {
  font-size: 0.7rem;
  font-weight: 800;
  padding: 3px 8px;
  border-radius: 999px;
}

.status-submitted {
  background-color: rgba(16, 185, 129, 0.2);
  color: #34d399;
  border: 1px solid rgba(16, 185, 129, 0.4);
}

.status-pending {
  background-color: rgba(245, 158, 11, 0.2);
  color: #fbbf24;
  border: 1px solid rgba(245, 158, 11, 0.4);
}

.metrics-preview-row {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 10px;
}

.metric-card {
  background-color: #0f172a;
  border: 1px solid #334155;
  border-radius: 6px;
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.metric-card-label {
  font-size: 0.7rem;
  color: #94a3b8;
}

.metric-card-val {
  font-size: 1.1rem;
  font-weight: 800;
  color: #ffffff;
}

.lesion-table-container {
  overflow-x: auto;
}

.reader-lesion-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
}

.reader-lesion-table th {
  background-color: #0f172a;
  color: #94a3b8;
  padding: 8px 10px;
  text-align: left;
  border-bottom: 1px solid #334155;
  font-weight: 600;
}

.reader-lesion-table td {
  padding: 8px 10px;
  border-bottom: 1px solid #1e293b;
  color: #f8fafc;
}

.empty-row {
  text-align: center;
  color: #64748b;
  font-style: italic;
  padding: 16px !important;
}

.qualitative-assessment-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 12px;
}

.full-width {
  grid-column: span 2;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: #94a3b8;
}

.form-select,
.form-input,
.form-textarea {
  background-color: #0f172a;
  color: #ffffff;
  border: 1px solid #334155;
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 0.8rem;
  outline: none;
  font-family: inherit;
}

.response-highlight-select {
  font-weight: 700;
  color: #38bdf8;
}

.toggle-group {
  display: flex;
  gap: 6px;
}

.toggle-btn {
  flex: 1;
  background-color: #0f172a;
  color: #94a3b8;
  border: 1px solid #334155;
  border-radius: 4px;
  padding: 6px 10px;
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
}

.toggle-btn.active {
  background-color: #0284c7;
  color: #ffffff;
  border-color: #38bdf8;
}

.toggle-btn.toggle-danger.active {
  background-color: #dc2626;
  color: #ffffff;
  border-color: #ef4444;
}

.action-footer {
  display: flex;
  justify-content: flex-end;
  margin-top: 6px;
}

.btn-submit-read {
  background: linear-gradient(135deg, #0284c7 0%, #2563eb 100%);
  color: #ffffff;
  border: none;
  border-radius: 6px;
  padding: 10px 18px;
  font-size: 0.82rem;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  box-shadow: 0 2px 6px rgba(2, 132, 199, 0.4);
}

.btn-retract-read {
  background-color: #334155;
  color: #f8fafc;
  border: 1px solid #475569;
  border-radius: 6px;
  padding: 8px 14px;
  font-size: 0.8rem;
  font-weight: 600;
  cursor: pointer;
}

/* Chair Adjudication Styles */
.discordance-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  border-radius: 8px;
  padding: 12px 16px;
}

.banner-discordant {
  background-color: rgba(239, 68, 68, 0.15);
  border: 1px solid rgba(239, 68, 68, 0.4);
}

.banner-concordant {
  background-color: rgba(16, 185, 129, 0.15);
  border: 1px solid rgba(16, 185, 129, 0.4);
}

.banner-adjudicated {
  background-color: rgba(14, 165, 233, 0.15);
  border: 1px solid rgba(14, 165, 233, 0.4);
}

.banner-icon {
  font-size: 1.4rem;
}

.banner-headline {
  font-size: 0.9rem;
  font-weight: 800;
  color: #ffffff;
}

.banner-subtext {
  font-size: 0.75rem;
  color: #cbd5e1;
}

.threshold-tag {
  color: #ef4444;
  font-weight: 700;
}

.side-by-side-grid {
  display: grid;
  grid-template-columns: repeat(2, 1fr);
  gap: 14px;
}

.reader-compare-card {
  background-color: #1e293b;
  border: 1px solid #334155;
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.reader-compare-card.card-highlight {
  border-color: #38bdf8;
  box-shadow: 0 0 10px rgba(56, 189, 248, 0.25);
}

.compare-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid #334155;
  padding-bottom: 8px;
}

.reader-label {
  font-size: 0.85rem;
  font-weight: 700;
  color: #f8fafc;
}

.compare-body {
  display: flex;
  flex-direction: column;
  gap: 6px;
  font-size: 0.75rem;
}

.compare-metric {
  display: flex;
  justify-content: space-between;
}

.lbl {
  color: #94a3b8;
}

.val {
  font-weight: 700;
  color: #ffffff;
}

.compare-notes {
  margin-top: 4px;
  background-color: #0f172a;
  border-radius: 4px;
  padding: 6px 8px;
}

.notes-txt {
  font-size: 0.72rem;
  color: #cbd5e1;
  font-style: italic;
  margin-top: 2px;
}

.chair-actions-row {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 4px;
}

.btn-chair-signoff {
  background: linear-gradient(135deg, #059669 0%, #0284c7 100%);
  color: #ffffff;
  border: none;
  border-radius: 6px;
  padding: 10px 20px;
  font-size: 0.85rem;
  font-weight: 800;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  box-shadow: 0 2px 8px rgba(5, 150, 105, 0.4);
}

.btn-chair-signoff:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.post-adjudication-actions {
  display: flex;
  gap: 10px;
}

.btn-export-edc,
.btn-export-sdtm {
  background-color: #0f172a;
  color: #38bdf8;
  border: 1px solid #0284c7;
  border-radius: 6px;
  padding: 8px 14px;
  font-size: 0.8rem;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
}

.btn-export-edc:hover,
.btn-export-sdtm:hover {
  background-color: #0284c7;
  color: #ffffff;
}

/* Modals */
.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(0, 0, 0, 0.75);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal-card {
  background-color: #0f172a;
  border: 1px solid #334155;
  border-radius: 8px;
  width: 90%;
  max-width: 520px;
  padding: 20px;
}

.modal-card.modal-large {
  max-width: 720px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 14px;
}

.modal-title {
  font-size: 1rem;
  font-weight: 700;
  color: #ffffff;
}

.btn-close {
  background: none;
  border: none;
  color: #94a3b8;
  font-size: 1.1rem;
  cursor: pointer;
}

.modal-warning-box {
  background-color: rgba(245, 158, 11, 0.15);
  border: 1px solid rgba(245, 158, 11, 0.4);
  color: #fde68a;
  padding: 10px;
  border-radius: 6px;
  font-size: 0.75rem;
  line-height: 1.4;
  margin-bottom: 12px;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  margin-top: 16px;
}

.btn-secondary {
  background-color: #334155;
  color: #f8fafc;
  border: none;
  padding: 8px 14px;
  border-radius: 4px;
  cursor: pointer;
}

.btn-confirm-sign {
  background-color: #059669;
  color: #ffffff;
  border: none;
  padding: 8px 16px;
  border-radius: 4px;
  font-weight: 700;
  cursor: pointer;
}

.btn-confirm-sign:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.json-preview-container {
  max-height: 350px;
  overflow-y: auto;
  background-color: #030712;
  border: 1px solid #1e293b;
  border-radius: 6px;
  padding: 12px;
}

.json-code {
  font-family: monospace;
  font-size: 0.75rem;
  color: #38bdf8;
  white-space: pre-wrap;
}

.badge-target {
  background-color: rgba(52, 211, 153, 0.15);
  color: #34d399;
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
}

.resp-pr {
  background-color: #0284c7;
  color: #ffffff;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 800;
}

.resp-pd {
  background-color: #dc2626;
  color: #ffffff;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.72rem;
  font-weight: 800;
}
</style>
