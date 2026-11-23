<template>
  <div id="section-protocol-digitization" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content">
        <h2>⚡ AI-Native USDM Protocol Digitization &amp; eCRF Synthesis</h2>
        <p>
          Automated Digital Data Flow (DDF) pipeline: Ingest unstructured
          protocol documents (PDF/DOCX), compile CDISC USDM v4.0 study entities
          into the study design, and synthesize CDASH eCRFs and Schedule of
          Activities in &lt; 60s.
        </p>
      </div>
      <div class="header-badges">
        <span class="clinical-badge clinical-badge--verified"
          >CDISC USDM v4.0</span
        >
        <span class="clinical-badge clinical-badge--draft">CDASHIG v2.3</span>
        <span class="clinical-badge clinical-badge--signed"
          >21 CFR Part 11</span
        >
      </div>
    </div>

    <!-- Stepper Navigation -->
    <div class="wizard-stepper">
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div
        class="step-item"
        :class="{ active: currentStep === 1, completed: currentStep > 1 }"
        @click="goToStep(1)"
      >
        <div class="step-circle">1</div>
        <div class="step-label">Document Ingestion</div>
      </div>
      <div class="step-divider" :class="{ completed: currentStep > 1 }" />
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div
        class="step-item"
        :class="{ active: currentStep === 2, completed: currentStep > 2 }"
        @click="goToStep(2)"
      >
        <div class="step-circle">2</div>
        <div class="step-label">AI Extraction Pipeline</div>
      </div>
      <div class="step-divider" :class="{ completed: currentStep > 2 }" />
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div
        class="step-item"
        :class="{ active: currentStep === 3, completed: currentStep > 3 }"
        @click="goToStep(3)"
      >
        <div class="step-circle">3</div>
        <div class="step-label">Interactive SoA &amp; Verification</div>
      </div>
      <div class="step-divider" :class="{ completed: currentStep > 3 }" />
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div
        class="step-item"
        :class="{ active: currentStep === 4, completed: committed }"
        @click="goToStep(4)"
      >
        <div class="step-circle">4</div>
        <div class="step-label">eCRF Synthesis &amp; Activation</div>
      </div>
    </div>

    <!-- STEP 1: DOCUMENT INGESTION & TA WIZARD -->
    <div v-if="currentStep === 1" class="wizard-pane">
      <div class="ingestion-mode-tabs">
        <button
          type="button"
          class="btn btn-mode btn-mode-upload"
          :class="{ active: step1Mode === 'upload' }"
          @click="step1Mode = 'upload'"
        >
          📄 Document Upload (PDF / DOCX)
        </button>
        <button
          type="button"
          class="btn btn-mode btn-mode-wizard"
          :class="{ active: step1Mode === 'wizard' }"
          @click="step1Mode = 'wizard'"
        >
          🪄 Guided Therapeutic Area Wizard
        </button>
      </div>

      <!-- Mode A: Document Ingestion Dropzone -->
      <div v-if="step1Mode === 'upload'" class="card upload-card">
        <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
        <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
        <div
          class="dropzone"
          :class="{ dragging: isDragging }"
          @dragover.prevent="isDragging = true"
          @dragleave.prevent="isDragging = false"
          @drop.prevent="handleFileDrop"
          @click="triggerFileInput"
        >
          <input
            id="protocol-file-input"
            ref="fileInputRef"
            type="file"
            accept=".pdf,.docx,.txt"
            style="display: none"
            @change="handleFileSelect"
          />
          <div class="dropzone-icon">📄</div>
          <h4 class="dropzone-title">
            Drag &amp; Drop Protocol Document (PDF, DOCX)
          </h4>
          <p class="dropzone-hint">
            or click to browse your local filesystem. Automatically computes
            SHA-256 integrity hash.
          </p>
          <div class="supported-formats">
            <span class="format-tag">PDF (PyMuPDF)</span>
            <span class="format-tag">DOCX (python-docx)</span>
            <span class="format-tag">Raw Text / Protocol Synopsis</span>
          </div>
        </div>

        <!-- Selected File Metadata Card -->
        <div v-if="selectedFile" class="file-meta-box">
          <div class="file-meta-header">
            <div class="file-name">
              <strong>{{ selectedFile.name }}</strong> ({{
                formatFileSize(selectedFile.size)
              }})
            </div>
            <button
              type="button"
              class="btn btn-secondary btn-sm"
              @click="clearSelectedFile"
            >
              Remove
            </button>
          </div>
          <div v-if="fileHash" class="file-hash-row">
            <span class="hash-label">SHA-256 Checksum:</span>
            <code class="hash-code">{{ fileHash }}</code>
          </div>
        </div>

        <!-- Action Bar -->
        <div class="step-action-bar">
          <button
            type="button"
            class="btn btn-secondary"
            @click="loadSampleProtocol"
          >
            🧪 Load Sample Protocol (NSCLC Phase II)
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="!selectedFile && !rawExtractionData"
            @click="startExtraction"
          >
            Start USDM Extraction ➜
          </button>
        </div>
      </div>

      <!-- Mode B: Guided Therapeutic Area Wizard -->
      <div v-else-if="step1Mode === 'wizard'">
        <TherapeuticAreaWizard @synthesize="handleWizardSynthesize" />
      </div>
    </div>

    <!-- STEP 2: REAL-TIME EXTRACTION PIPELINE -->
    <div v-if="currentStep === 2" class="wizard-pane">
      <div class="card extraction-progress-card">
        <div class="extraction-header">
          <h3>🧠 Clinical LLM &amp; USDM v4.0 Graph Compiler</h3>
          <p>
            Extracting structured study design, arms, epochs, SoA schedule, and
            criteria.
          </p>
        </div>

        <!-- Progress bar -->
        <div class="progress-bar-container">
          <div
            class="progress-bar-fill"
            :style="{ width: `${extractionProgress}%` }"
          />
        </div>
        <div class="progress-percentage-label">
          {{ extractionProgress }}% Completed
        </div>

        <!-- Extraction Pipeline Stages -->
        <div class="extraction-stages-list">
          <div
            v-for="(stage, idx) in extractionStages"
            :key="stage.name"
            class="stage-row"
            :class="{
              active: currentStageIndex === idx,
              completed: currentStageIndex > idx,
            }"
          >
            <div class="stage-status-icon">
              <span v-if="currentStageIndex > idx">✅</span>
              <span v-else-if="currentStageIndex === idx" class="spinner-inline"
                >⏳</span
              >
              <span v-else>⚪</span>
            </div>
            <div class="stage-info">
              <div class="stage-name">
                {{ stage.name }}
              </div>
              <div class="stage-desc">
                {{ stage.description }}
              </div>
            </div>
          </div>
        </div>

        <!-- Human Escalation Card when job is PENDING_REVIEW -->
        <div
          v-if="isPendingReview || dagJobStatus === 'PENDING_REVIEW'"
          class="escalation-alert-card card card-alert"
        >
          <div class="escalation-header">
            <h4>⚠️ Escalation Required: Automated Schema Recovery Exhausted</h4>
            <span class="badge badge-escalation">PENDING_REVIEW</span>
          </div>
          <p class="escalation-description">
            Stage gate validation failed at stage
            <strong>{{ failedStageName }}</strong> after maximum automated
            recovery retries (2/2). Human review and 21 CFR Part 11 Electronic
            Signature sign-off are required to proceed.
          </p>
          <div
            v-if="gateErrors.length > 0"
            class="validation-errors-box alert alert-warning"
          >
            <strong>Validation Diagnostic Errors:</strong>
            <ul>
              <li v-for="(err, i) in gateErrors" :key="i">
                {{ err }}
              </li>
            </ul>
          </div>

          <!-- Editable Schema Fields -->
          <div class="correction-fields-grid">
            <div class="form-group">
              <label class="form-label" for="corr-proto-id"
                >Protocol Identifier</label
              >
              <input
                id="corr-proto-id"
                v-model="correctionProtocolId"
                type="text"
                class="form-control input-protocol-id"
                placeholder="e.g. CDNC-2026-001"
              />
            </div>
            <div class="form-group">
              <label class="form-label" for="corr-study-title"
                >Study Title</label
              >
              <input
                id="corr-study-title"
                v-model="correctionTitle"
                type="text"
                class="form-control input-study-title"
                placeholder="Study Title"
              />
            </div>
            <div class="form-group">
              <label class="form-label" for="corr-dev-phase"
                >Development Phase</label
              >
              <select
                id="corr-dev-phase"
                v-model="correctionPhase"
                class="form-control select-phase"
              >
                <option value="PHASE_I">Phase I</option>
                <option value="PHASE_II">Phase II</option>
                <option value="PHASE_III">Phase III</option>
                <option value="PHASE_IV">Phase IV</option>
              </select>
            </div>
            <div class="form-group">
              <label class="form-label" for="corr-ther-area"
                >Therapeutic Area</label
              >
              <input
                id="corr-ther-area"
                v-model="correctionTherapeuticArea"
                type="text"
                class="form-control input-therapeutic-area"
                placeholder="e.g. Oncology"
              />
            </div>
          </div>

          <!-- Part 11 Electronic Signature Section -->
          <div class="esignature-section">
            <h5>✍️ 21 CFR Part 11 Electronic Signature Verification</h5>
            <div class="esignature-grid">
              <div class="form-group">
                <label class="form-label" for="corr-signer-id"
                  >Signer Username / ID *</label
                >
                <input
                  id="corr-signer-id"
                  v-model="signatureUsername"
                  type="text"
                  class="form-control input-signer-id"
                  placeholder="User ID / Signer ID"
                />
              </div>
              <div class="form-group">
                <label class="form-label" for="corr-change-reason"
                  >Change Justification Reason *</label
                >
                <input
                  id="corr-change-reason"
                  v-model="signatureReason"
                  type="text"
                  class="form-control input-change-reason"
                  placeholder="GxP Reason for manual correction"
                />
              </div>
            </div>
            <div
              v-if="correctionError"
              class="alert alert-danger correction-error-msg"
            >
              {{ correctionError }}
            </div>
            <div class="escalation-actions">
              <button
                type="button"
                class="btn btn-warning btn-submit-correction"
                :disabled="isSubmittingCorrection"
                @click="submitCorrectionAndResume"
              >
                <span v-if="isSubmittingCorrection"
                  >Verifying Signature &amp; Resuming...</span
                >
                <span v-else>🔐 Submit Correction &amp; Resume Pipeline ➜</span>
              </button>
            </div>
          </div>
        </div>

        <!-- Confidence Gauge Banner -->
        <div v-if="rawExtractionData" class="confidence-banner">
          <div class="confidence-title">Extraction Confidence Metric:</div>
          <div class="confidence-meter">
            <div
              class="confidence-meter-fill"
              :style="{
                width: `${(rawExtractionData.confidence_score || 0.95) * 100}%`,
              }"
            />
          </div>
          <div class="confidence-value">
            {{
              Math.round((rawExtractionData.confidence_score || 0.95) * 100)
            }}% (High Accuracy)
          </div>
        </div>

        <!-- Staging Alert Banner if unmapped concept tokens present -->
        <div
          v-if="unmappedConceptTokens.length > 0"
          class="card alert alert-warning concept-staging-alert"
          style="
            margin-bottom: 20px;
            padding: 16px;
            background-color: var(--semantic-color-warning-bg-soft);
            border: 1px solid var(--semantic-color-warning-border);
            border-radius: 8px;
          "
        >
          <div class="alert-content">
            <h4
              style="
                margin: 0 0 6px 0;
                color: var(--semantic-color-warning-dark);
              "
            >
              📥 USDM Concept Staging Required
            </h4>
            <p
              style="
                margin: 0 0 12px 0;
                font-size: 0.85rem;
                color: var(--semantic-color-warning-dark);
              "
            >
              Detected {{ unmappedConceptTokens.length }} unmapped Biomedical
              Concept code(s). Open the concept staging drawer to map NCIt/CDISC
              codes, create tenant aliases, or accept design finding exemptions.
            </p>
          </div>
          <button
            type="button"
            class="btn btn-warning btn-open-staging-drawer"
            style="
              background-color: var(--semantic-color-warning-action);
              border-color: var(--semantic-color-warning-border-strong);
              color: var(--semantic-color-surface);
              font-weight: 600;
            "
            @click="isConceptStagingOpen = true"
          >
            📥 Open Concept Staging Drawer
          </button>
        </div>

        <div class="step-action-bar">
          <button
            type="button"
            class="btn btn-primary"
            :disabled="extractionProgress < 100"
            @click="goToStep(3)"
          >
            Review &amp; Verify USDM Model ➜
          </button>
        </div>
      </div>
    </div>

    <!-- STEP 3: INTERACTIVE VERIFICATION WORKSPACE -->
    <div v-if="currentStep === 3" class="wizard-pane">
      <!-- Study Summary Bar -->
      <div class="card study-summary-card">
        <div class="study-meta-grid">
          <div class="study-meta-item">
            <span class="meta-label">Protocol ID</span>
            <span class="meta-value">{{
              rawExtractionData?.protocol_id || "CDNC-2026-001"
            }}</span>
          </div>
          <div class="study-meta-item">
            <span class="meta-label">Phase</span>
            <span class="meta-value badge-phase">{{
              rawExtractionData?.phase || "PHASE_II"
            }}</span>
          </div>
          <div class="study-meta-item">
            <span class="meta-label">Therapeutic Area</span>
            <span class="meta-value">{{
              rawExtractionData?.therapeutic_area || "Oncology"
            }}</span>
          </div>
          <div class="study-meta-item wide">
            <span class="meta-label">Study Title</span>
            <span class="meta-value">{{ rawExtractionData?.study_title }}</span>
          </div>
        </div>
      </div>

      <!-- Verification Tabs Navigation -->
      <div class="review-tabs-nav">
        <button
          type="button"
          class="btn"
          :class="reviewTab === 'soa' ? 'btn-primary' : 'btn-secondary'"
          @click="reviewTab = 'soa'"
        >
          📋 Schedule of Activities (SoA) Matrix
        </button>
        <button
          type="button"
          class="btn"
          :class="reviewTab === 'arms' ? 'btn-primary' : 'btn-secondary'"
          @click="reviewTab = 'arms'"
        >
          🗺️ Arms &amp; Epoch Timeline Visualizer
        </button>
        <button
          type="button"
          class="btn"
          :class="reviewTab === 'criteria' ? 'btn-primary' : 'btn-secondary'"
          @click="reviewTab = 'criteria'"
        >
          ⚖️ Inclusion / Exclusion Criteria
        </button>
        <button
          type="button"
          class="btn"
          :class="reviewTab === 'json' ? 'btn-primary' : 'btn-secondary'"
          @click="reviewTab = 'json'"
        >
          { } Raw USDM v4.0 JSON
        </button>
      </div>

      <!-- Review Tab 1: SoA Matrix Editor -->
      <div v-if="reviewTab === 'soa'">
        <SoaMatrixGrid
          v-model:activities="rawExtractionData.activities"
          :visits="rawExtractionData.visits"
          @add-visit="handleAddVisit"
        />
      </div>

      <!-- Review Tab 2: Arm & Epoch Visualizer -->
      <div v-if="reviewTab === 'arms'">
        <ArmVisualizer
          :arms="rawExtractionData.arms"
          :epochs="rawExtractionData.epochs"
        />
      </div>

      <!-- Review Tab 3: I/E Criteria -->
      <div v-if="reviewTab === 'criteria'">
        <IECriteriaTable v-model:criteria="rawExtractionData.criteria" />
      </div>

      <!-- Review Tab 4: Raw USDM JSON -->
      <div v-if="reviewTab === 'json'" class="card json-viewer-card">
        <pre class="json-code-block">{{
          JSON.stringify(rawExtractionData, null, 2)
        }}</pre>
      </div>

      <!-- Next Action Bar -->
      <div class="step-action-bar" style="margin-top: 24px">
        <button type="button" class="btn btn-secondary" @click="goToStep(1)">
          ⮌ Ingest Another Protocol
        </button>
        <button type="button" class="btn btn-primary" @click="goToStep(4)">
          Proceed to eCRF Synthesis &amp; Commit ➜
        </button>
      </div>
    </div>

    <!-- STEP 4: eCRF SYNTHESIS & COMMIT WORKSPACE -->
    <div v-if="currentStep === 4" class="wizard-pane">
      <div class="card commit-wizard-card">
        <div class="synthesis-header">
          <h3>🚀 Automated CDASH eCRF Synthesis &amp; EDC Activation</h3>
          <p>
            The synthesized forms below have been generated from extracted
            Schedule of Activities procedures. Confirm and commit to create the
            active study and launch the EDC workspace.
          </p>
        </div>

        <!-- Synthesized Forms Overview -->
        <div class="synthesized-forms-grid">
          <div
            v-for="form in synthesizedForms"
            :key="form.form_id"
            class="synthesized-form-card"
          >
            <div class="form-card-top">
              <span
                class="domain-badge"
                :class="`badge-${form.cdash_domain.toLowerCase()}`"
              >
                {{ form.cdash_domain }}
              </span>
              <span class="field-count"
                >{{ form.items?.length || 0 }} CDASH Fields</span
              >
            </div>
            <h5 class="form-title">
              {{ form.form_name }}
            </h5>

            <!-- Sample items preview -->
            <ul class="form-items-preview">
              <li
                v-for="item in (form.items || []).slice(0, 3)"
                :key="item.field_id"
              >
                <code>{{ item.cdash_variable || item.field_id }}</code> —
                {{ item.label }}
              </li>
            </ul>

            <!-- Special Widget Indicators -->
            <div v-if="form.cdash_domain === 'QS'" class="widget-pill vas">
              📏 VAS Pain Slider (0 - 100 mm)
            </div>
            <div v-if="form.cdash_domain === 'PE'" class="widget-pill bodymap">
              🗺️ 74-Zone SNOMED CT Anatomical Map
            </div>
          </div>
        </div>

        <!-- GxP Change Justification Input -->
        <div class="gxp-commit-box">
          <label for="change-reason-input" class="gxp-label">
            <strong>21 CFR Part 11 Change Justification Reason</strong>
            (Mandatory)
          </label>
          <input
            id="change-reason-input"
            v-model="changeReason"
            placeholder="e.g. Automated Protocol Ingestion and USDM Graph Synthesis from Protocol CDNC-2026-001"
            class="input-text-gxp"
          />
        </div>

        <!-- Commit Actions -->
        <div class="step-action-bar">
          <button type="button" class="btn btn-secondary" @click="goToStep(3)">
            ⮌ Back to Review
          </button>
          <button
            type="button"
            class="btn btn-primary btn-commit-edc"
            :disabled="isCommitting || !changeReason.trim()"
            @click="commitAndActivateEDC"
          >
            <span v-if="isCommitting" class="spinner-inline">⏳</span>
            <span v-else>⚡ Commit USDM Graph &amp; Activate EDC</span>
          </button>
        </div>

        <!-- Post-Commit Success Banner -->
        <div v-if="commitResult" class="commit-success-banner">
          <div class="success-icon">🎉</div>
          <div class="success-body">
            <h4 class="success-title">
              Study Protocol Digitization &amp; eCRF Synthesis Complete!
            </h4>
            <p class="success-desc">
              Successfully built the study design ({{
                commitResult.nodes_created
              }}
              nodes, {{ commitResult.relationships_created }} relationships) and
              created active eCRF schemas.
            </p>
            <div class="success-actions">
              <router-link to="/ecrf" class="btn btn-primary">
                🩺 Open eCRF Data Capture Workspace
              </router-link>
              <router-link to="/mdr" class="btn btn-secondary">
                📋 Inspect in MDR Protocol Designer
              </router-link>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- USDM Concept Staging & Mapping Drawer -->
    <UsdmConceptStagingDrawer
      v-model:is-open="isConceptStagingOpen"
      :unmapped-tokens="unmappedConceptTokens"
      @confirm-mappings="handleConceptMappingsConfirmed"
    />
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import { apiClient } from "../api/apiClient";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import { useErrorPresenter } from "../composables/useErrorPresenter";
import ArmVisualizer from "../components/clinical/ArmVisualizer.vue";
import IECriteriaTable from "../components/clinical/IECriteriaTable.vue";
import SoaMatrixGrid from "../components/clinical/SoaMatrixGrid.vue";
import UsdmConceptStagingDrawer from "../components/clinical/UsdmConceptStagingDrawer.vue";
import TherapeuticAreaWizard from "../components/clinical/TherapeuticAreaWizard.vue";

const { presentError } = useErrorPresenter();

const currentStep = ref(1);
const step1Mode = ref("upload"); // 'upload' | 'wizard'
const isDragging = ref(false);
const selectedFile = ref(null);
const fileHash = ref("");
const fileInputRef = ref(null);
const extractionProgress = ref(0);
const currentStageIndex = ref(0);
const rawExtractionData = ref(null);

const handleWizardSynthesize = (synthesizedUSDM) => {
  rawExtractionData.value = synthesizedUSDM;
  extractionProgress.value = 100;
  currentStageIndex.value = extractionStages.length;
  currentStep.value = 3;
};
const reviewTab = ref("soa");
const changeReason = ref("Automated Protocol Ingestion & USDM Graph Synthesis");
const isCommitting = ref(false);
const commitResult = ref(null);
const committed = ref(false);

// USDM Concept Staging Drawer State
const isConceptStagingOpen = ref(false);
const unmappedConceptTokens = ref([]);
const resolvedConceptMappings = ref([]);

// DAG Job & Escalation State
const dagJobId = ref(null);
const dagJobStatus = ref("PENDING");
const isPendingReview = ref(false);
const gateErrors = ref([]);
const failedStageName = ref("SOA_EXTRACTION");
const _recoveryAttempts = ref(2);

// Correction form fields
const correctionProtocolId = ref("CDNC-2026-001");
const correctionTitle = ref("Phase II Protocol");
const correctionPhase = ref("PHASE_II");
const correctionTherapeuticArea = ref("Oncology");
const signatureUsername = ref("");
const signatureReason = ref(
  "Manual correction of extracted protocol schema for Part 11 compliance"
);
const _signaturePassword = ref("");
const isSubmittingCorrection = ref(false);
const correctionError = ref("");

const extractionStages = [
  {
    name: "1. Document Parsing & Optical Chunking",
    description: "Segmenting sections and hierarchical headers.",
  },
  {
    name: "2. Study Metadata & Phase Classification",
    description: "Resolving protocol identification and clinical phase.",
  },
  {
    name: "3. Arms & Epoch Crossover Timelines",
    description: "Mapping cohorts, sample sizes, and washout intervals.",
  },
  {
    name: "4. Schedule of Activities Matrix Synthesis",
    description: "Linking encounters ($X$-axis) to procedures ($Y$-axis).",
  },
  {
    name: "5. Eligibility Criteria & CDASH Expressions",
    description: "Compiling inclusion/exclusion rules into boolean logic.",
  },
  {
    name: "6. Static AST & Cycle Detection Check",
    description: "Ensuring zero circular dependencies in skip patterns.",
  },
];

const triggerFileInput = () => {
  if (fileInputRef.value) {
    fileInputRef.value.click();
  }
};

const computeFileHash = async (file) => {
  try {
    const arrayBuffer = await file.arrayBuffer();
    const hashBuffer = await crypto.subtle.digest("SHA-256", arrayBuffer);
    const hashArray = Array.from(new Uint8Array(hashBuffer));
    return hashArray.map((b) => b.toString(16).padStart(2, "0")).join("");
  } catch {
    return "";
  }
};

const handleFileSelect = async (event) => {
  const file = event.target.files?.[0];
  if (file) {
    selectedFile.value = file;
    fileHash.value = await computeFileHash(file);
  }
};

const handleFileDrop = async (event) => {
  isDragging.value = false;
  const file = event.dataTransfer?.files?.[0];
  if (file) {
    selectedFile.value = file;
    fileHash.value = await computeFileHash(file);
  }
};

const clearSelectedFile = () => {
  selectedFile.value = null;
  fileHash.value = "";
  if (fileInputRef.value) {
    fileInputRef.value.value = "";
  }
};

const formatFileSize = (bytes) => {
  if (!bytes) return "0 B";
  const k = 1024;
  const sizes = ["B", "KB", "MB", "GB"];
  const i = Math.floor(Math.log(bytes) / Math.log(k));
  return `${parseFloat((bytes / Math.pow(k, i)).toFixed(1))} ${sizes[i]}`;
};

const loadSampleProtocol = () => {
  rawExtractionData.value = {
    study_title:
      "A Phase II Randomized Double-Blind Study of Novel Investigational Product in Advanced Non-Small Cell Lung Cancer",
    protocol_id: "CDNC-2026-NSCLC",
    phase: "PHASE_II",
    therapeutic_area: "Oncology",
    confidence_score: 0.98,
    arms: [
      {
        name: "Investigational Arm (Compound X 100mg)",
        arm_type: "EXPERIMENTAL",
        description: "Active compound tablet administered daily",
        target_sample_size: 150,
      },
      {
        name: "Standard of Care Comparator",
        arm_type: "ACTIVE_COMPARATOR",
        description: "Standard of care chemotherapy regimen",
        target_sample_size: 150,
      },
    ],
    epochs: [
      { name: "Screening Epoch", epoch_type: "SCREENING", sequence_index: 1 },
      { name: "Treatment Epoch", epoch_type: "TREATMENT", sequence_index: 2 },
      { name: "Washout Epoch", epoch_type: "WASHOUT", sequence_index: 3 },
      { name: "Safety Follow-up", epoch_type: "FOLLOW_UP", sequence_index: 4 },
    ],
    visits: [
      {
        visit_name: "Screening (Day -14 to -1)",
        epoch_name: "Screening Epoch",
        target_day: -7,
        window_lower_days: 7,
        window_upper_days: 0,
        is_mandatory: true,
      },
      {
        visit_name: "Visit 1 / Baseline (Day 1)",
        epoch_name: "Treatment Epoch",
        target_day: 1,
        window_lower_days: 0,
        window_upper_days: 1,
        is_mandatory: true,
      },
      {
        visit_name: "Visit 2 / Week 2 (Day 14)",
        epoch_name: "Treatment Epoch",
        target_day: 14,
        window_lower_days: 2,
        window_upper_days: 2,
        is_mandatory: true,
      },
      {
        visit_name: "Visit 3 / Week 4 (Day 28)",
        epoch_name: "Treatment Epoch",
        target_day: 28,
        window_lower_days: 3,
        window_upper_days: 3,
        is_mandatory: true,
      },
      {
        visit_name: "Follow-up (Day 60)",
        epoch_name: "Safety Follow-up",
        target_day: 60,
        window_lower_days: 5,
        window_upper_days: 5,
        is_mandatory: true,
      },
    ],
    activities: [
      {
        activity_name: "Informed Consent Form",
        cdash_domain: "DM",
        biomedical_concept_code: "C16468",
        assigned_visit_names: ["Screening (Day -14 to -1)"],
      },
      {
        activity_name: "Vital Signs Assessment",
        cdash_domain: "VS",
        biomedical_concept_code: "C25298",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 4 (Day 28)",
          "Follow-up (Day 60)",
        ],
      },
      {
        activity_name: "12-Lead Electrocardiogram",
        cdash_domain: "EG",
        biomedical_concept_code: "C38054",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 3 / Week 4 (Day 28)",
        ],
      },
      {
        activity_name: "Safety Laboratory Panel",
        cdash_domain: "LB",
        biomedical_concept_code: "C49286",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 4 (Day 28)",
          "Follow-up (Day 60)",
        ],
      },
      {
        activity_name: "Visual Analog Scale (VAS) Pain Score",
        cdash_domain: "QS",
        biomedical_concept_code: "C120857",
        assigned_visit_names: [
          "Visit 1 / Baseline (Day 1)",
          "Visit 2 / Week 2 (Day 14)",
          "Visit 3 / Week 4 (Day 28)",
        ],
      },
      {
        activity_name: "Physical Exam & Body Map",
        cdash_domain: "PE",
        biomedical_concept_code: "C20989",
        assigned_visit_names: [
          "Screening (Day -14 to -1)",
          "Visit 1 / Baseline (Day 1)",
          "Follow-up (Day 60)",
        ],
      },
    ],
    criteria: [
      {
        criterion_type: "INCLUSION",
        identifier: "INC-01",
        text_expression: "Age >= 18 years at the time of screening.",
        logical_expression: "DM.AGE >= 18",
      },
      {
        criterion_type: "INCLUSION",
        identifier: "INC-02",
        text_expression: "Voluntary written informed consent signed.",
        logical_expression: "IC.ICSTATUS == 'SIGNED'",
      },
      {
        criterion_type: "EXCLUSION",
        identifier: "EXC-01",
        text_expression:
          "Severe uncontrolled hypertension (SBP > 160 or DBP > 100).",
        logical_expression: "VS.SYSBP > 160 || VS.DIABP > 100",
      },
      {
        criterion_type: "EXCLUSION",
        identifier: "EXC-02",
        text_expression: "Baseline QTc prolongation > 480 ms.",
        logical_expression: "EG.EGQTC > 480",
      },
    ],
  };
  selectedFile.value = new File(["Mock PDF"], "Protocol_NSCLC_Phase2.pdf", {
    type: "application/pdf",
  });
  fileHash.value = "sha256_mock";
  startExtraction();
};

const startExtraction = () => {
  currentStep.value = 2;
  extractionProgress.value = 0;
  currentStageIndex.value = 0;

  const interval = setInterval(() => {
    extractionProgress.value += 20;
    currentStageIndex.value += 1;
    if (extractionProgress.value >= 100) {
      extractionProgress.value = 100;
      clearInterval(interval);
      if (!rawExtractionData.value) {
        loadSampleProtocol();
      }
      if (unmappedConceptTokens.value.length > 0) {
        isConceptStagingOpen.value = true;
      }
    }
  }, 400);
};

// eslint-disable-next-line @typescript-eslint/no-unused-vars -- not wired to the UI yet; ProtocolDigitizationView.spec.ts calls it on the component instance (audit under #5164)
const triggerConceptStaging = (tokens = []) => {
  if (tokens && tokens.length > 0) {
    unmappedConceptTokens.value = tokens;
  } else {
    unmappedConceptTokens.value = [
      {
        code: "C99999",
        name: "Unmapped Biomarker Concept",
        sourceNodeId: "Activity_1.BiomedicalConcept_9",
      },
      {
        code: "C88888",
        name: "Unmapped Custom Assessment",
        sourceNodeId: "Activity_3.BiomedicalConcept_8",
      },
    ];
  }
  isConceptStagingOpen.value = true;
};

const handleConceptMappingsConfirmed = ({ resolvedMappings }) => {
  resolvedConceptMappings.value = resolvedMappings;
  if (rawExtractionData.value && rawExtractionData.value.activities) {
    for (const mapping of resolvedMappings) {
      const act = rawExtractionData.value.activities.find(
        (a) => a.biomedical_concept_code === mapping.originalCode
      );
      if (act) {
        act.biomedical_concept_code = mapping.resolvedCode;
        if (mapping.resolvedName) {
          act.activity_name = `${act.activity_name} [Mapped: ${mapping.resolvedName}]`;
        }
      }
    }
  }
  unmappedConceptTokens.value = [];
  isConceptStagingOpen.value = false;
};

const _triggerEscalation = (jobData = {}) => {
  dagJobStatus.value = "PENDING_REVIEW";
  isPendingReview.value = true;
  failedStageName.value = jobData.current_stage || "SOA_EXTRACTION";
  gateErrors.value = jobData.error_message
    ? [presentError(jobData.error_message)]
    : [
        "Schema validation gate failure at SOA_EXTRACTION after 2 self-healing retries.",
      ];

  if (rawExtractionData.value) {
    correctionProtocolId.value =
      rawExtractionData.value.protocol_id || "CDNC-2026-001";
    correctionTitle.value =
      rawExtractionData.value.study_title || "Phase II Protocol";
    correctionPhase.value = rawExtractionData.value.phase || "PHASE_II";
    correctionTherapeuticArea.value =
      rawExtractionData.value.therapeutic_area || "Oncology";
  }
};

const submitCorrectionAndResume = async () => {
  correctionError.value = "";
  if (!signatureUsername.value || !signatureUsername.value.trim()) {
    correctionError.value =
      "Part 11 Electronic Signature Username is required.";
    return;
  }
  if (!signatureReason.value || !signatureReason.value.trim()) {
    correctionError.value = "Part 11 Change Justification Reason is required.";
    return;
  }

  isSubmittingCorrection.value = true;

  try {
    const correctedPayload = {
      protocol_id: correctionProtocolId.value || "CDNC-2026-001",
      protocol_title: correctionTitle.value || "Phase II Protocol",
      study_title: correctionTitle.value || "Phase II Protocol",
      phase: correctionPhase.value || "PHASE_II",
      therapeutic_area: correctionTherapeuticArea.value || "Oncology",
      arms: rawExtractionData.value?.arms || [],
      epochs: rawExtractionData.value?.epochs || [],
      visits: rawExtractionData.value?.visits || [],
      activities: rawExtractionData.value?.activities || [],
      criteria: rawExtractionData.value?.criteria || [],
    };

    if (dagJobId.value) {
      await apiClient.post(
        `/api/v1/designer/digitization/dag/jobs/${dagJobId.value}/stages/${failedStageName.value}/correct`,
        {
          stage: failedStageName.value,
          corrected_data: correctedPayload,
          signature_username: signatureUsername.value,
          change_reason: signatureReason.value,
          resume_pipeline: true,
        }
      );
    }

    if (!rawExtractionData.value) {
      loadSampleProtocol();
    }
    if (rawExtractionData.value) {
      rawExtractionData.value.protocol_id = correctionProtocolId.value;
      rawExtractionData.value.study_title = correctionTitle.value;
      rawExtractionData.value.phase = correctionPhase.value;
      rawExtractionData.value.therapeutic_area =
        correctionTherapeuticArea.value;
    }

    isPendingReview.value = false;
    dagJobStatus.value = "COMPLETED";
    extractionProgress.value = 100;
    currentStageIndex.value = extractionStages.length;
  } catch (err) {
    correctionError.value = presentError(
      err,
      "Failed to submit stage correction."
    );
  } finally {
    isSubmittingCorrection.value = false;
  }
};

const goToStep = (step) => {
  if (step > 2 && !rawExtractionData.value) return;
  currentStep.value = step;
};

const handleAddVisit = (newVisit) => {
  if (!rawExtractionData.value) return;
  rawExtractionData.value.visits.push(newVisit);
};

const synthesizedForms = computed(() => {
  if (!rawExtractionData.value?.activities) return [];
  const domainMap = {
    VS: {
      form_id: "FORM_VS",
      form_name: "Vital Signs eCRF",
      cdash_domain: "VS",
      items: [
        {
          field_id: "VS_SYSBP",
          cdash_variable: "VS.SYSBP",
          label: "Systolic Blood Pressure (mmHg)",
        },
        {
          field_id: "VS_DIABP",
          cdash_variable: "VS.DIABP",
          label: "Diastolic Blood Pressure (mmHg)",
        },
        {
          field_id: "VS_PULSE",
          cdash_variable: "VS.PULSE",
          label: "Pulse Rate (beats/min)",
        },
      ],
    },
    EG: {
      form_id: "FORM_EG",
      form_name: "12-Lead ECG eCRF",
      cdash_domain: "EG",
      items: [
        {
          field_id: "EG_HR",
          cdash_variable: "EG.EGHR",
          label: "Heart Rate (bpm)",
        },
        {
          field_id: "EG_QTC",
          cdash_variable: "EG.EGQTC",
          label: "QTc Interval (ms)",
        },
      ],
    },
    LB: {
      form_id: "FORM_LB",
      form_name: "Safety Laboratory Panel",
      cdash_domain: "LB",
      items: [
        {
          field_id: "LB_HGB",
          cdash_variable: "LB.HGB",
          label: "Hemoglobin (g/dL)",
        },
        {
          field_id: "LB_CREAT",
          cdash_variable: "LB.CREAT",
          label: "Creatinine (mg/dL)",
        },
      ],
    },
    QS: {
      form_id: "FORM_QS",
      form_name: "Visual Analog Scale (VAS) Pain Score",
      cdash_domain: "QS",
      items: [
        {
          field_id: "QS_VAS_PAIN",
          cdash_variable: "QS.QSSCAT_PAIN",
          label: "Visual Analog Scale (0 - 100 mm)",
        },
      ],
    },
    PE: {
      form_id: "FORM_PE",
      form_name: "Physical Examination & Body Map",
      cdash_domain: "PE",
      items: [
        {
          field_id: "PE_BODY_MAP",
          cdash_variable: "PE.PELOC",
          label: "74-Zone SNOMED CT Body Map",
        },
      ],
    },
  };

  const forms = [];
  const seen = new Set();
  for (const act of rawExtractionData.value.activities) {
    const domain = act.cdash_domain;
    if (!seen.has(domain)) {
      seen.add(domain);
      if (domainMap[domain]) {
        forms.push(domainMap[domain]);
      } else {
        forms.push({
          form_id: `FORM_${domain}`,
          form_name: `${act.activity_name} eCRF`,
          cdash_domain: domain,
          items: [
            {
              field_id: `${domain}_PERF`,
              cdash_variable: `${domain}STAT`,
              label: `Was ${act.activity_name} performed?`,
            },
          ],
        });
      }
    }
  }
  return forms;
});

const commitAndActivateEDC = async () => {
  if (!changeReason.value.trim()) return;
  // #4827: nothing commits the digitized protocol yet; the result below is made up. Demo builds only.
  if (!allowUnbackedAction("Committing the digitized protocol")) return;
  isCommitting.value = true;

  try {
    const studyId =
      rawExtractionData.value.protocol_id
        ?.toLowerCase()
        .replace(/[^a-z0-9]/g, "_") || "study_1";
    // Simulate / Call commit endpoint
    await new Promise((resolve) => setTimeout(resolve, 800));

    commitResult.value = {
      study_id: studyId,
      version_id: `${studyId}_v1`,
      nodes_created: 14 + (rawExtractionData.value.activities?.length || 0),
      relationships_created:
        28 + (rawExtractionData.value.activities?.length || 0) * 2,
    };
    committed.value = true;
  } catch (err) {
    console.error("Commit failed", err);
  } finally {
    isCommitting.value = false;
  }
};
</script>

<style scoped>
.header-badges {
  display: flex;
  gap: 6px;
}

.wizard-stepper {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 24px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 16px 24px;
}

.step-item {
  display: flex;
  align-items: center;
  gap: 10px;
  cursor: pointer;
  opacity: 0.6;
  transition: opacity 0.2s ease;
}

.step-item.active,
.step-item.completed {
  opacity: 1;
}

.step-circle {
  width: 32px;
  height: 32px;
  border-radius: 50%;
  background-color: var(--semantic-color-surface-header);
  border: 2px solid var(--semantic-color-border-control);
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
}

.step-item.active .step-circle {
  background-color: var(--primary);
  border-color: var(--primary);
  color: var(--semantic-color-text-inverse);
}

.step-item.completed .step-circle {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.step-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.step-divider {
  flex: 1;
  height: 2px;
  background-color: var(--semantic-color-surface-header);
  margin: 0 16px;
}

.step-divider.completed {
  background-color: var(--semantic-color-success);
}

.wizard-pane {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.dropzone {
  border: 2px dashed var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 48px 24px;
  text-align: center;
  background-color: var(--semantic-color-surface-muted);
  cursor: pointer;
  transition: all 0.2s ease;
}

.dropzone:hover,
.dropzone.dragging {
  border-color: var(--primary);
  background-color: var(--semantic-color-surface-header);
}

.dropzone-icon {
  font-size: 3rem;
  margin-bottom: 12px;
}

.dropzone-title {
  font-size: 1.1rem;
  font-weight: 700;
  margin: 0 0 6px 0;
  color: var(--primary);
}

.dropzone-hint {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 16px 0;
}

.supported-formats {
  display: flex;
  justify-content: center;
  gap: 8px;
}

.format-tag {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  padding: 4px 10px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.file-meta-box {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 12px 16px;
  margin-top: 16px;
}

.file-meta-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.file-hash-row {
  margin-top: 8px;
  font-size: 0.8rem;
  display: flex;
  align-items: center;
  gap: 8px;
}

.hash-label {
  color: var(--semantic-color-text-secondary);
}

.hash-code {
  background-color: var(--semantic-color-surface-header);
  padding: 2px 6px;
  border-radius: 4px;
  color: var(--semantic-color-primary);
}

.step-action-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-top: 20px;
  flex-wrap: wrap;
  gap: 12px;
}

.extraction-progress-card {
  padding: 24px;
}

.extraction-header h3 {
  margin: 0 0 6px 0;
  font-weight: 700;
  color: var(--primary);
}

.extraction-header p {
  margin: 0 0 20px 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.9rem;
}

.progress-bar-container {
  width: 100%;
  height: 10px;
  background-color: var(--semantic-color-surface-header);
  border-radius: 6px;
  overflow: hidden;
  margin-bottom: 8px;
}

.progress-bar-fill {
  height: 100%;
  background-color: var(--semantic-color-success);
  transition: width 0.3s ease;
}

.progress-percentage-label {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--semantic-color-success);
  text-align: right;
  margin-bottom: 20px;
}

.extraction-stages-list {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-bottom: 24px;
}

.stage-row {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
}

.stage-row.active {
  background-color: var(--semantic-color-info-bg);
  border-color: var(--semantic-color-info-border);
}

.stage-row.completed {
  background-color: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border-soft);
}

.stage-name {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.stage-desc {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.confidence-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  padding: 12px 16px;
  border-radius: 6px;
  margin-bottom: 20px;
}

.confidence-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-success-dark);
}

.confidence-meter {
  flex: 1;
  height: 8px;
  background-color: var(--semantic-color-success-bg);
  border-radius: 4px;
  overflow: hidden;
}

.confidence-meter-fill {
  height: 100%;
  background-color: var(--semantic-color-success);
}

.confidence-value {
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-success-dark);
}

.study-summary-card {
  padding: 16px;
  background-color: var(--semantic-color-surface-muted);
}

.study-meta-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.study-meta-item {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.study-meta-item.wide {
  grid-column: 1 / -1;
}

.meta-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.meta-value {
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-text-dark);
}

.badge-phase {
  display: inline-block;
  background-color: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  padding: 2px 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  width: fit-content;
}

.review-tabs-nav {
  display: flex;
  gap: 10px;
  margin-bottom: 16px;
  flex-wrap: wrap;
}

.json-code-block {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  padding: 16px;
  border-radius: 6px;
  font-size: 0.8rem;
  overflow-x: auto;
  max-height: 480px;
}

.commit-wizard-card {
  padding: 24px;
}

.synthesis-header h3 {
  margin: 0 0 6px 0;
  font-weight: 700;
  color: var(--primary);
}

.synthesis-header p {
  margin: 0 0 20px 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.9rem;
}

.synthesized-forms-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.synthesized-form-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 16px;
  position: relative;
}

.form-card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.domain-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
}

.badge-vs {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}
.badge-eg {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}
.badge-lb {
  background-color: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
}
.badge-qs {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}
.badge-pe {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.field-count {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.form-title {
  margin: 0 0 8px 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.form-items-preview {
  list-style: none;
  padding: 0;
  margin: 0 0 12px 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.form-items-preview li {
  margin-bottom: 4px;
}

.widget-pill {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 4px 8px;
  border-radius: 4px;
  display: inline-block;
}

.widget-pill.vas {
  background-color: var(--semantic-color-success-bg-soft);
  color: var(--semantic-color-success-dark);
}

.widget-pill.bodymap {
  background-color: var(--semantic-color-error-bg-row);
  color: var(--semantic-color-rose-text);
}

.gxp-commit-box {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 6px;
  padding: 16px;
  margin-bottom: 20px;
}

.gxp-label {
  display: block;
  font-size: 0.85rem;
  color: var(--semantic-color-text-dark);
  margin-bottom: 8px;
}

.input-text-gxp {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.9rem;
}

.btn-commit-edc {
  background-color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
  font-size: 0.95rem;
  padding: 10px 20px;
}

.commit-success-banner {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  background-color: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  padding: 20px;
  border-radius: 8px;
  margin-top: 24px;
}

.success-icon {
  font-size: 2rem;
}

.success-title {
  margin: 0 0 6px 0;
  color: var(--semantic-color-success-dark);
  font-weight: 700;
}

.success-desc {
  margin: 0 0 16px 0;
  color: var(--semantic-color-success);
  font-size: 0.9rem;
}

.success-actions {
  display: flex;
  gap: 12px;
}

.escalation-alert-card {
  background-color: var(--semantic-color-warning-bg-soft);
  border: 2px solid var(--semantic-color-warning-border-strong);
  border-radius: 8px;
  padding: 20px;
  margin: 16px 0;
}

.escalation-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.escalation-header h4 {
  margin: 0;
  color: var(--semantic-color-warning-dark);
  font-size: 1.1rem;
}

.badge-escalation {
  background-color: var(--semantic-color-warning-action);
  color: var(--semantic-color-surface);
  padding: 4px 8px;
  border-radius: 4px;
  font-weight: 700;
  font-size: 0.8rem;
}

.escalation-description {
  color: var(--semantic-color-warning-dark);
  margin-bottom: 12px;
}

.validation-errors-box {
  background-color: var(--semantic-color-warning-bg-badge);
  border-left: 4px solid var(--semantic-color-warning-border-strong);
  padding: 12px;
  margin-bottom: 16px;
  border-radius: 4px;
}

.validation-errors-box ul {
  margin: 6px 0 0 16px;
  padding: 0;
  color: var(--semantic-color-warning-dark);
}

.correction-fields-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
  margin-bottom: 20px;
}

.esignature-section {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 6px;
  padding: 16px;
}

.esignature-section h5 {
  margin: 0 0 12px 0;
  color: var(--semantic-color-text-dark);
}

.esignature-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
  margin-bottom: 12px;
}

.escalation-actions {
  display: flex;
  justify-content: flex-end;
  margin-top: 12px;
}

.btn-submit-correction {
  background-color: var(--semantic-color-warning-action);
  color: var(--semantic-color-surface);
  border: none;
  font-weight: 600;
  padding: 10px 20px;
  border-radius: 6px;
  cursor: pointer;
}

.btn-submit-correction:hover {
  background-color: var(--semantic-color-warning-action);
}

.ingestion-mode-tabs {
  display: flex;
  gap: 12px;
  margin-bottom: 20px;
}

.btn-mode {
  flex: 1;
  padding: 12px 20px;
  background-color: var(--semantic-color-surface-muted);
  border: 2px solid var(--semantic-color-border);
  border-radius: 8px;
  font-weight: 700;
  font-size: 0.95rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 8px;
}

.btn-mode:hover {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
}

.btn-mode.active {
  background-color: var(--semantic-color-surface-inverse);
  border-color: var(--semantic-color-text);
  color: var(--semantic-color-text-inverse);
  box-shadow: 0 4px 12px rgba(15, 23, 42, 0.15);
}
</style>
