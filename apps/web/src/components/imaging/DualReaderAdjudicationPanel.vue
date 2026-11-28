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
          <span class="role-icon"
            ><ClinicalIcon name="users" :size="16"
          /></span>
          Reader 1 (Blinded)
        </button>
        <button
          type="button"
          class="role-tab-btn"
          :class="{ active: activePersona === 'READER_2' }"
          @click="switchPersona('READER_2')"
        >
          <span class="role-icon"
            ><ClinicalIcon name="users" :size="16"
          /></span>
          Reader 2 (Blinded)
        </button>
        <button
          type="button"
          class="role-tab-btn"
          :class="{ active: activePersona === 'CHAIR' }"
          @click="switchPersona('CHAIR')"
        >
          <span class="role-icon"
            ><ClinicalIcon name="file-text" :size="16"
          /></span>
          Adjudication Chair
        </button>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- VIEW A: Blinded Primary Reader Recording Form (Reader 1 / Reader 2) -->
    <!-- ========================================================================= -->
    <div
      v-if="activePersona === 'READER_1' || activePersona === 'READER_2'"
      class="blinded-reader-view"
    >
      <!-- Blinding Enclosure Notice -->
      <div class="blinding-alert-box">
        <div class="alert-icon"><ClinicalIcon name="lock" :size="16" /></div>
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
            }}</strong>. Subject treatment arm, clinical trial site, and peer
            reader observations are completely blinded per Charter section 3.1.
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
            <span class="metric-card-val font-mono"
              >{{ currentReaderSLD }} mm</span
            >
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
              <tr v-for="l in currentReaderLesions" :key="l.lesion_id">
                <td class="font-bold">
                  {{ l.lesion_id }}
                </td>
                <td>{{ l.organ_site }}</td>
                <td class="font-mono">{{ l.longest_diameter_mm }} mm</td>
                <td class="font-mono">
                  {{ l.short_axis_mm ? l.short_axis_mm + " mm" : "-" }}
                </td>
                <td><span class="badge badge-target">TARGET</span></td>
              </tr>
              <tr v-if="currentReaderLesions.length === 0">
                <td colspan="5" class="empty-row">
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
            <label for="non-target-select" class="form-label"
              >Non-Target Lesion Status:</label
            >
            <select
              id="non-target-select"
              v-model="currentAssessment.non_target_status"
              class="form-select"
              :disabled="currentReaderStatus === 'COMPLETED'"
            >
              <option value="CR">Complete Response (CR)</option>
              <option value="NON_CR_NON_PD">Non-CR / Non-PD (Stable)</option>
              <option value="PD">Unequivocal Progression (PD)</option>
              <option value="NE">Not Evaluable (NE)</option>
            </select>
          </div>

          <div class="form-group">
            <label for="new-lesions-toggle" class="form-label"
              >New Malignant Lesions Present?</label
            >
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
            <label for="overall-response-select" class="form-label"
              >Overall Categorical Response:</label
            >
            <select
              id="overall-response-select"
              v-model="currentAssessment.overall_response"
              class="form-select response-highlight-select"
              :disabled="currentReaderStatus === 'COMPLETED'"
            >
              <option value="CR">Complete Response (CR)</option>
              <option value="PR">Partial Response (PR)</option>
              <option value="SD">Stable Disease (SD)</option>
              <option value="PD">Progressive Disease (PD)</option>
              <option value="NE">Not Evaluable (NE)</option>
            </select>
          </div>

          <div class="form-group full-width">
            <label for="reader-notes-input" class="form-label"
              >Radiologist Interpretation Notes:</label
            >
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
            <span class="btn-icon"
              ><ClinicalIcon name="upload" :size="16"
            /></span>
            Submit Blinded
            {{ activePersona === "READER_1" ? "Reader 1" : "Reader 2" }}
            Assessment
          </button>
          <button
            v-else
            type="button"
            class="btn-retract-read"
            @click="retractReaderRead"
          >
            <span class="btn-icon"
              ><ClinicalIcon name="edit" :size="16"
            /></span>
            Edit Assessment (Unlock)
          </button>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- VIEW B: Adjudication Committee Chair Tie-Breaker Console -->
    <!-- ========================================================================= -->
    <div v-else class="chair-adjudication-view">
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
            isDiscordant
              ? "Warning"
              : workflowStatus === "ADJUDICATED"
                ? "Verified"
                : "Item"
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
            <span v-if="sldDelta > 0.2" class="threshold-tag"
              >(Exceeds 20.0% Threshold)</span
            >
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
              <span class="val font-mono"
                >{{ reader1Assessment.sum_longest_diameters_mm }} mm</span
              >
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
              <span class="val font-mono"
                >{{ reader2Assessment.sum_longest_diameters_mm }} mm</span
              >
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
          <span v-if="workflowStatus === 'ADJUDICATED'" class="badge badge-gxp"
            >Locked &amp; Signed</span
          >
        </div>

        <div class="form-row">
          <div class="form-group full-width">
            <label for="binding-reader-select" class="form-label"
              >Binding Final Assessment Source:</label
            >
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
            <label for="adjudication-rationale-input" class="form-label">
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
            <span class="btn-icon"
              ><ClinicalIcon name="file-signature" :size="16"
            /></span>
            Execute 21 CFR Part 11 Adjudication Sign-Off
          </button>

          <div v-else class="post-adjudication-actions">
            <button
              type="button"
              class="btn-export-edc"
              @click="isEdcModalOpen = true"
            >
              <span class="btn-icon"
                ><ClinicalIcon name="clipboard-list" :size="16"
              /></span>
              View EDC eCRF Binding
            </button>
            <button
              type="button"
              class="btn-export-sdtm"
              @click="isSdtmModalOpen = true"
            >
              <span class="btn-icon"
                ><ClinicalIcon name="trending-up" :size="16"
              /></span>
              Export CDISC SDTM TU/TR/RS
            </button>
          </div>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- MODAL 1: 21 CFR Part 11 Step-Up Electronic Signature Dialog -->
    <!-- ========================================================================= -->
    <div
      v-if="isSignoffModalOpen"
      ref="signoffModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card">
        <div class="modal-header">
          <h4 class="modal-title">
            21 CFR Part 11 Adjudication Chair Sign-off
          </h4>
          <ClinicalIconButton
            class="btn-close"
            label="Close adjudication chair sign-off dialog"
            @click="closeSignoffModal"
          >
            ✕
          </ClinicalIconButton>
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
            />
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
            />
          </div>

          <div class="form-group">
            <label for="chair-password-input" class="form-label"
              >Re-authentication Password / PIN:</label
            >
            <input
              id="chair-password-input"
              v-model="chairPassword"
              type="password"
              class="form-input"
              placeholder="Enter Part 11 credential..."
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
            :disabled="!chairPassword"
            @click="executeChairSignoff"
          >
            Sign &amp; Lock Binding Determination
          </button>
        </div>
      </div>
    </div>

    <!-- ========================================================================= -->
    <!-- MODAL 2: Direct EDC eCRF Binding Preview Modal -->
    <!-- ========================================================================= -->
    <div
      v-if="isEdcModalOpen"
      ref="edcModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card modal-large">
        <div class="modal-header">
          <h4 class="modal-title">
            Direct EDC eCRF Binding (Form: eCRF_TUMOR_ASSESSMENT)
          </h4>
          <ClinicalIconButton
            class="btn-close"
            label="Close direct EDC eCRF binding dialog"
            @click="isEdcModalOpen = false"
          >
            ✕
          </ClinicalIconButton>
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
    <!-- MODAL 3: CDISC SDTM TU/TR/RS Export Modal -->
    <!-- ========================================================================= -->
    <div
      v-if="isSdtmModalOpen"
      ref="sdtmModalRef"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
    >
      <div class="modal-card modal-large">
        <div class="modal-header">
          <h4 class="modal-title">CDISC SDTM Dataset Package (TU, TR, RS)</h4>
          <ClinicalIconButton
            class="btn-close"
            label="Close CDISC SDTM dataset export dialog"
            @click="isSdtmModalOpen = false"
          >
            ✕
          </ClinicalIconButton>
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
import { ClinicalIconButton, useFocusTrap, ClinicalIcon } from "ui";

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
const signoffModalRef = ref(null);
const edcModalRef = ref(null);
const sdtmModalRef = ref(null);
const chairPassword = ref("");

function closeSignoffModal() {
  chairPassword.value = "";
  isSignoffModalOpen.value = false;
}

useFocusTrap(signoffModalRef, isSignoffModalOpen, {
  onEscape: closeSignoffModal,
});

useFocusTrap(edcModalRef, isEdcModalOpen, {
  onEscape: () => {
    isEdcModalOpen.value = false;
  },
});

useFocusTrap(sdtmModalRef, isSdtmModalOpen, {
  onEscape: () => {
    isSdtmModalOpen.value = false;
  },
});

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
    "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", // pragma: allowlist secret
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
  --focus-ring-color: var(--semantic-color-focus-ring-inverse);
  display: flex;
  flex-direction: column;
  background-color: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--semantic-border-radius-md, 8px);
  padding: 16px;
  gap: 16px;
  color: var(--semantic-color-text-inverse);
  min-width: 0;
  box-sizing: border-box;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  border-bottom: 1px solid var(--semantic-color-text-dark);
  padding-bottom: 12px;
}

.header-titles {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
  min-width: 0;
}

.panel-title {
  font-size: 1.05rem;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
}

.role-selector-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  background-color: var(--semantic-color-surface-sunken);
  padding: 4px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-text-dark);
  max-width: 100%;
  box-sizing: border-box;
}

.role-tab-btn {
  background: none;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  font-size: 0.78rem;
  font-weight: 700;
  padding: 6px 12px;
  border-radius: 4px;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 6px;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease;
}

.role-tab-btn:hover {
  color: var(--semantic-color-text-inverse);
  background-color: var(--semantic-color-inverse-tint);
}

.role-tab-btn.active {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  box-shadow: var(--shadow-sm);
}

.role-tab-btn.active:focus-visible {
  box-shadow: 0 0 0 2px
    var(--focus-ring-halo-color, var(--semantic-color-text-on-solid));
}

.blinding-alert-box {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  background-color: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-info-border-accent);
  border-radius: 6px;
  padding: 10px 14px;
}

.alert-icon {
  font-size: 1.2rem;
}

.alert-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-accent-inverse);
  margin-bottom: 2px;
}

.alert-text {
  font-size: 0.75rem;
  color: var(--semantic-color-text-inverse-muted);
  line-height: 1.4;
}

.reader-form-card,
.chair-decision-card {
  background-color: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
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
  flex-wrap: wrap;
  gap: 8px;
}

.card-subtitle {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.status-pill {
  font-size: var(--font-size-2xs);
  font-weight: 800;
  padding: 3px 8px;
  border-radius: 999px;
}

.status-submitted {
  background-color: var(--semantic-color-emerald-inverse-bg);
  color: var(--semantic-color-emerald-inverse-text);
  border: 1px solid var(--semantic-color-emerald-inverse-border);
}

.status-pending {
  background-color: var(--semantic-badge-queried-bg);
  color: var(--semantic-badge-queried-text);
  border: 1px solid var(--semantic-badge-queried-border);
}

.metrics-preview-row {
  display: grid;
  grid-template-columns: repeat(3, minmax(0, 1fr));
  gap: 10px;
}

.metric-card {
  background-color: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 12px;
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.metric-card-label {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-inverse-muted);
}

.metric-card-val {
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
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
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse-muted);
  padding: 8px 10px;
  text-align: left;
  border-bottom: 1px solid var(--semantic-color-border-inverse);
  font-weight: 600;
}

.reader-lesion-table td {
  padding: 8px 10px;
  border-bottom: 1px solid var(--semantic-color-text-dark);
  color: var(--semantic-color-text-inverse);
}

.empty-row {
  text-align: center;
  color: var(--semantic-color-text-inverse-muted);
  font-style: italic;
  padding: 16px !important;
}

.qualitative-assessment-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
}

.full-width {
  grid-column: 1 / -1;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-inverse-muted);
}

.form-select,
.form-input,
.form-textarea {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 10px;
  font-size: 0.8rem;
  font-family: inherit;
}

.response-highlight-select {
  font-weight: 700;
  color: var(--semantic-color-accent-inverse);
}

.toggle-group {
  display: flex;
  gap: 6px;
}

.toggle-btn {
  flex: 1;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse-muted);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 6px 10px;
  font-size: 0.75rem;
  font-weight: 600;
  cursor: pointer;
}

.toggle-btn.active {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--semantic-color-accent-inverse);
}

.toggle-btn.toggle-danger.active {
  background-color: var(--semantic-color-error-interactive);
  color: var(--semantic-color-text-on-solid);
  border-color: var(--semantic-color-error-interactive);
}

.action-footer {
  display: flex;
  justify-content: flex-end;
  margin-top: 6px;
}

.btn-submit-read {
  background: linear-gradient(
    135deg,
    var(--semantic-color-primary) 0%,
    var(--semantic-color-interactive-primary) 100%
  );
  color: var(--semantic-color-text-on-primary);
  border: none;
  border-radius: 6px;
  padding: 10px 18px;
  font-size: 0.82rem;
  font-weight: 700;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  box-shadow: var(--shadow-md);
}

.btn-retract-read {
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
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
  background-color: var(--semantic-badge-error-bg);
  border: 1px solid var(--semantic-badge-error-border);
}

.banner-concordant {
  background-color: var(--semantic-color-emerald-inverse-bg);
  border: 1px solid var(--semantic-color-emerald-inverse-border);
}

.banner-adjudicated {
  background-color: var(--semantic-badge-candidate-bg);
  border: 1px solid var(--semantic-badge-candidate-border);
}

.banner-icon {
  font-size: 1.4rem;
}

.banner-headline {
  font-size: 0.9rem;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
}

.banner-subtext {
  font-size: 0.75rem;
  color: var(--semantic-color-text-inverse-muted);
}

.threshold-tag {
  color: var(--semantic-color-error);
  font-weight: 700;
}

.side-by-side-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

@media (max-width: 768px) {
  .side-by-side-grid {
    grid-template-columns: minmax(0, 1fr);
  }
}

@media (max-width: 640px) {
  .dual-reader-panel,
  .reader-form-card,
  .chair-decision-card {
    padding: 12px;
  }

  .metrics-preview-row,
  .qualitative-assessment-grid {
    grid-template-columns: minmax(0, 1fr);
  }

  .role-tab-btn {
    min-height: 40px;
  }

  .form-select,
  .form-input,
  .form-textarea {
    font-size: 1rem;
    min-height: 44px;
    width: 100%;
    box-sizing: border-box;
  }

  .post-adjudication-actions,
  .chair-actions-row,
  .discordance-banner {
    flex-wrap: wrap;
  }
}

.reader-compare-card {
  background-color: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  padding: 14px;
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.reader-compare-card.card-highlight {
  border-color: var(--semantic-color-accent-inverse);
  box-shadow: var(--shadow-focus-ring);
}

.compare-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--semantic-color-border-inverse);
  padding-bottom: 8px;
}

.reader-label {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
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
  color: var(--semantic-color-text-inverse-muted);
}

.val {
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.compare-notes {
  margin-top: 4px;
  background-color: var(--semantic-color-surface-inverse);
  border-radius: 4px;
  padding: 6px 8px;
}

.notes-txt {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-inverse-muted);
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
  background: linear-gradient(
    135deg,
    var(--semantic-color-success) 0%,
    var(--semantic-color-primary) 100%
  );
  color: var(--semantic-color-text-on-primary);
  border: none;
  border-radius: 6px;
  padding: 10px 20px;
  font-size: 0.85rem;
  font-weight: 800;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 8px;
  box-shadow: var(--shadow-md);
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
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  border: 1px solid var(--semantic-color-primary);
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
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

/* Modals */
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
  color: var(--semantic-color-text-inverse);
}

.btn-close {
  background: none;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  font-size: 1.1rem;
  cursor: pointer;
}

.modal-warning-box {
  background-color: var(--semantic-badge-queried-bg);
  border: 1px solid var(--semantic-badge-queried-border);
  color: var(--semantic-badge-queried-text);
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
  background-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
  border: none;
  padding: 8px 14px;
  border-radius: 4px;
  cursor: pointer;
}

.btn-confirm-sign {
  background-color: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
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
  background-color: var(--semantic-color-surface-sunken);
  border: 1px solid var(--semantic-color-text-dark);
  border-radius: 6px;
  padding: 12px;
}

.json-code {
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
  color: var(--semantic-color-accent-inverse);
  white-space: pre-wrap;
}

.badge-target {
  background-color: var(--semantic-color-emerald-inverse-bg);
  color: var(--semantic-color-emerald-inverse-text);
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
}

.resp-pr {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  padding: 2px 8px;
  border-radius: 999px;
  font-size: var(--font-size-2xs);
  font-weight: 800;
}

.resp-pd {
  background-color: var(--semantic-color-error-interactive);
  color: var(--semantic-color-text-on-solid);
  padding: 2px 8px;
  border-radius: 999px;
  font-size: var(--font-size-2xs);
  font-weight: 800;
}
</style>
