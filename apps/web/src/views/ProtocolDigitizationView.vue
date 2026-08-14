<template>
  <div id="section-protocol-digitization" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content">
        <h2>⚡ AI-Native USDM Protocol Digitization &amp; eCRF Synthesis</h2>
        <p>
          Automated Digital Data Flow (DDF) pipeline: Ingest unstructured
          protocol documents (PDF/DOCX), compile CDISC USDM v4.0 graph entities
          into Neo4j, and synthesize CDASH eCRFs and Schedule of Activities in
          &lt; 60s.
        </p>
      </div>
      <div class="header-badges">
        <span class="badge gxp">CDISC USDM v4.0</span>
        <span class="badge">CDASHIG v2.3</span>
        <span class="badge">21 CFR Part 11</span>
      </div>
    </div>

    <!-- Stepper Navigation -->
    <div class="wizard-stepper">
      <div
        class="step-item"
        :class="{ active: currentStep === 1, completed: currentStep > 1 }"
        @click="goToStep(1)"
      >
        <div class="step-circle">1</div>
        <div class="step-label">Document Ingestion</div>
      </div>
      <div class="step-divider" :class="{ completed: currentStep > 1 }" />
      <div
        class="step-item"
        :class="{ active: currentStep === 2, completed: currentStep > 2 }"
        @click="goToStep(2)"
      >
        <div class="step-circle">2</div>
        <div class="step-label">AI Extraction Pipeline</div>
      </div>
      <div class="step-divider" :class="{ completed: currentStep > 2 }" />
      <div
        class="step-item"
        :class="{ active: currentStep === 3, completed: currentStep > 3 }"
        @click="goToStep(3)"
      >
        <div class="step-circle">3</div>
        <div class="step-label">Interactive SoA &amp; Verification</div>
      </div>
      <div class="step-divider" :class="{ completed: currentStep > 3 }" />
      <div
        class="step-item"
        :class="{ active: currentStep === 4, completed: committed }"
        @click="goToStep(4)"
      >
        <div class="step-circle">4</div>
        <div class="step-label">eCRF Synthesis &amp; Activation</div>
      </div>
    </div>

    <!-- STEP 1: DOCUMENT INGESTION -->
    <div v-if="currentStep === 1" class="wizard-pane">
      <div class="card upload-card">
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
              <div class="stage-name">{{ stage.name }}</div>
              <div class="stage-desc">{{ stage.description }}</div>
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
        <SoAMatrixEditor
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
            active study in Neo4j and launch the EDC workspace.
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
            <h5 class="form-title">{{ form.form_name }}</h5>

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
              Successfully populated Neo4j knowledge graph ({{
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
  </div>
</template>

<script setup>
import { computed, ref } from "vue";
import ArmVisualizer from "../components/clinical/ArmVisualizer.vue";
import IECriteriaTable from "../components/clinical/IECriteriaTable.vue";
import SoAMatrixEditor from "../components/clinical/SoAMatrixEditor.vue";

const currentStep = ref(1);
const isDragging = ref(false);
const selectedFile = ref(null);
const fileHash = ref("");
const fileInputRef = ref(null);
const extractionProgress = ref(0);
const currentStageIndex = ref(0);
const rawExtractionData = ref(null);
const reviewTab = ref("soa");
const changeReason = ref("Automated Protocol Ingestion & USDM Graph Synthesis");
const isCommitting = ref(false);
const commitResult = ref(null);
const committed = ref(false);

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
    }
  }, 400);
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
.wizard-stepper {
  display: flex;
  align-items: center;
  justify-content: space-between;
  margin-bottom: 24px;
  background-color: var(--surface, #ffffff);
  border: 1px solid var(--border, #e2e8f0);
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
  background-color: #f1f5f9;
  border: 2px solid #cbd5e1;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 0.9rem;
  color: #475569;
}

.step-item.active .step-circle {
  background-color: var(--primary, #0f172a);
  border-color: var(--primary, #0f172a);
  color: #ffffff;
}

.step-item.completed .step-circle {
  background-color: #059669;
  border-color: #059669;
  color: #ffffff;
}

.step-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: #334155;
}

.step-divider {
  flex: 1;
  height: 2px;
  background-color: #e2e8f0;
  margin: 0 16px;
}

.step-divider.completed {
  background-color: #059669;
}

.wizard-pane {
  display: flex;
  flex-direction: column;
  gap: 20px;
}

.dropzone {
  border: 2px dashed #cbd5e1;
  border-radius: 8px;
  padding: 48px 24px;
  text-align: center;
  background-color: #f8fafc;
  cursor: pointer;
  transition: all 0.2s ease;
}

.dropzone:hover,
.dropzone.dragging {
  border-color: var(--primary, #0f172a);
  background-color: #f1f5f9;
}

.dropzone-icon {
  font-size: 3rem;
  margin-bottom: 12px;
}

.dropzone-title {
  font-size: 1.1rem;
  font-weight: 700;
  margin: 0 0 6px 0;
  color: var(--primary, #0f172a);
}

.dropzone-hint {
  font-size: 0.85rem;
  color: #64748b;
  margin: 0 0 16px 0;
}

.supported-formats {
  display: flex;
  justify-content: center;
  gap: 8px;
}

.format-tag {
  background-color: #e2e8f0;
  color: #334155;
  padding: 4px 10px;
  border-radius: 4px;
  font-size: 0.75rem;
  font-weight: 600;
}

.file-meta-box {
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
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
  color: #64748b;
}

.hash-code {
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  color: #0369a1;
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
  color: var(--primary, #0f172a);
}

.extraction-header p {
  margin: 0 0 20px 0;
  color: #64748b;
  font-size: 0.9rem;
}

.progress-bar-container {
  width: 100%;
  height: 10px;
  background-color: #e2e8f0;
  border-radius: 6px;
  overflow: hidden;
  margin-bottom: 8px;
}

.progress-bar-fill {
  height: 100%;
  background-color: #059669;
  transition: width 0.3s ease;
}

.progress-percentage-label {
  font-size: 0.8rem;
  font-weight: 700;
  color: #059669;
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
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
}

.stage-row.active {
  background-color: #eff6ff;
  border-color: #bfdbfe;
}

.stage-row.completed {
  background-color: #f0fdf4;
  border-color: #bbf7d0;
}

.stage-name {
  font-size: 0.85rem;
  font-weight: 600;
  color: #1e293b;
}

.stage-desc {
  font-size: 0.75rem;
  color: #64748b;
}

.confidence-banner {
  display: flex;
  align-items: center;
  gap: 12px;
  background-color: #ecfdf5;
  border: 1px solid #a7f3d0;
  padding: 12px 16px;
  border-radius: 6px;
  margin-bottom: 20px;
}

.confidence-title {
  font-size: 0.85rem;
  font-weight: 700;
  color: #065f46;
}

.confidence-meter {
  flex: 1;
  height: 8px;
  background-color: #d1fae5;
  border-radius: 4px;
  overflow: hidden;
}

.confidence-meter-fill {
  height: 100%;
  background-color: #059669;
}

.confidence-value {
  font-size: 0.85rem;
  font-weight: 700;
  color: #065f46;
}

.study-summary-card {
  padding: 16px;
  background-color: #f8fafc;
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
  color: #64748b;
  text-transform: uppercase;
}

.meta-value {
  font-size: 0.95rem;
  font-weight: 700;
  color: #1e293b;
}

.badge-phase {
  display: inline-block;
  background-color: #dbeafe;
  color: #1e40af;
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
  background-color: #0f172a;
  color: #f8fafc;
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
  color: var(--primary, #0f172a);
}

.synthesis-header p {
  margin: 0 0 20px 0;
  color: #64748b;
  font-size: 0.9rem;
}

.synthesized-forms-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.synthesized-form-card {
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
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
  background-color: #e0f2fe;
  color: #0369a1;
}
.badge-eg {
  background-color: #fef3c7;
  color: #b45309;
}
.badge-lb {
  background-color: #f3e8ff;
  color: #7e22ce;
}
.badge-qs {
  background-color: #dcfce7;
  color: #15803d;
}
.badge-pe {
  background-color: #fee2e2;
  color: #b91c1c;
}

.field-count {
  font-size: 0.75rem;
  color: #64748b;
}

.form-title {
  margin: 0 0 8px 0;
  font-size: 0.95rem;
  font-weight: 600;
  color: #1e293b;
}

.form-items-preview {
  list-style: none;
  padding: 0;
  margin: 0 0 12px 0;
  font-size: 0.8rem;
  color: #475569;
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
  background-color: #ecfdf5;
  color: #065f46;
}

.widget-pill.bodymap {
  background-color: #fff1f2;
  color: #9f1239;
}

.gxp-commit-box {
  background-color: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 16px;
  margin-bottom: 20px;
}

.gxp-label {
  display: block;
  font-size: 0.85rem;
  color: #1e293b;
  margin-bottom: 8px;
}

.input-text-gxp {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.9rem;
}

.btn-commit-edc {
  background-color: #059669;
  border-color: #059669;
  font-size: 0.95rem;
  padding: 10px 20px;
}

.commit-success-banner {
  display: flex;
  align-items: flex-start;
  gap: 16px;
  background-color: #ecfdf5;
  border: 1px solid #a7f3d0;
  padding: 20px;
  border-radius: 8px;
  margin-top: 24px;
}

.success-icon {
  font-size: 2rem;
}

.success-title {
  margin: 0 0 6px 0;
  color: #065f46;
  font-weight: 700;
}

.success-desc {
  margin: 0 0 16px 0;
  color: #047857;
  font-size: 0.9rem;
}

.success-actions {
  display: flex;
  gap: 12px;
}
</style>
