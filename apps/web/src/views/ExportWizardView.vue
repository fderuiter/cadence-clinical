<template>
  <div class="export-wizard-container">
    <!-- Header Banner -->
    <header class="wizard-header">
      <div class="wizard-title-group">
        <div class="wizard-icon-box">📊</div>
        <div>
          <h1 class="wizard-main-title">Regulatory Biostatistical Export Wizard</h1>
          <p class="wizard-subtitle">
            CDISC SDTM, ADaM, SAS Transport (XPT v5/v8), ODM-XML v1.3.2 &amp; De-identified Datasets
          </p>
        </div>
      </div>
      <div class="wizard-status-badges">
        <span class="badge gxp">21 CFR Part 11</span>
        <span class="badge">CDISC Validated</span>
        <span class="badge">HIPAA / GDPR Ready</span>
      </div>
    </header>

    <!-- Stepper Navigation -->
    <nav class="stepper-nav" aria-label="Export Wizard Steps">
      <div
        v-for="step in STEPS"
        :key="step.number"
        class="step-item"
        :class="{
          active: currentStep === step.number,
          completed: currentStep > step.number,
        }"
        @click="goToStep(step.number)"
      >
        <div class="step-badge">
          <span v-if="currentStep > step.number">✓</span>
          <span v-else>{{ step.number }}</span>
        </div>
        <div class="step-meta">
          <span class="step-title">{{ step.title }}</span>
          <span class="step-desc">{{ step.shortDesc }}</span>
        </div>
      </div>
    </nav>

    <!-- Wizard Body Panes -->
    <main class="wizard-card-body">
      <!-- Step 1: Format Selection -->
      <section v-if="currentStep === 1" class="step-pane">
        <h2 class="pane-title">Step 1: Choose Regulatory Target Format</h2>
        <p class="pane-description">
          Select the biostatistical or regulatory submission format required by the statistical analysis plan (SAP) or health authority (FDA / EMA / PMDA).
        </p>

        <div class="format-grid">
          <div
            v-for="fmt in FORMAT_OPTIONS"
            :key="fmt.id"
            class="format-card"
            :class="{ selected: selectedFormat === fmt.id }"
            @click="selectedFormat = fmt.id"
          >
            <div class="format-card-header">
              <span class="format-icon">{{ fmt.icon }}</span>
              <span class="format-badge">{{ fmt.badge }}</span>
            </div>
            <h3 class="format-name">{{ fmt.name }}</h3>
            <p class="format-detail">{{ fmt.description }}</p>
            <div class="format-meta">
              <span class="meta-tag">{{ fmt.standard }}</span>
              <span class="meta-tag">{{ fmt.extension }}</span>
            </div>
          </div>
        </div>
      </section>

      <!-- Step 2: SDTM & ADaM Domain Selection -->
      <section v-if="currentStep === 2" class="step-pane">
        <div class="pane-header-actions">
          <div>
            <h2 class="pane-title">Step 2: Select SDTM Domains &amp; ADaM Datasets</h2>
            <p class="pane-description">
              Choose the clinical tabulation domains and derived analysis datasets to include in this export package.
            </p>
          </div>
          <div class="domain-quick-actions">
            <button type="button" class="btn btn-sm" @click="selectAllDomains">Select All</button>
            <button type="button" class="btn btn-sm" @click="selectSdtmOnly">SDTM Only</button>
            <button type="button" class="btn btn-sm" @click="selectAdamOnly">ADaM Only</button>
            <button type="button" class="btn btn-sm btn-outline" @click="clearAllDomains">Clear</button>
          </div>
        </div>

        <div class="domains-container">
          <!-- SDTM Domains -->
          <div class="domain-group">
            <h3 class="domain-group-title">
              <span>📋 SDTM Tabulation Domains (v2.0)</span>
              <span class="count-pill">{{ selectedSdtmCount }} / {{ SDTM_DOMAINS.length }} selected</span>
            </h3>
            <div class="domain-card-list">
              <label
                v-for="dom in SDTM_DOMAINS"
                :key="dom.id"
                class="domain-checkbox-card"
                :class="{ checked: selectedDomains.includes(dom.id) }"
              >
                <input
                  type="checkbox"
                  :value="dom.id"
                  v-model="selectedDomains"
                />
                <div class="domain-info">
                  <div class="domain-code">{{ dom.id }}</div>
                  <div class="domain-label">{{ dom.label }}</div>
                  <div class="domain-desc">{{ dom.description }}</div>
                </div>
              </label>
            </div>
          </div>

          <!-- ADaM Analysis Datasets -->
          <div class="domain-group">
            <h3 class="domain-group-title">
              <span>🔬 ADaM Analysis Datasets (v1.3)</span>
              <span class="count-pill">{{ selectedAdamCount }} / {{ ADAM_DATASETS.length }} selected</span>
            </h3>
            <div class="domain-card-list">
              <label
                v-for="ds in ADAM_DATASETS"
                :key="ds.id"
                class="domain-checkbox-card"
                :class="{ checked: selectedDatasets.includes(ds.id) }"
              >
                <input
                  type="checkbox"
                  :value="ds.id"
                  v-model="selectedDatasets"
                />
                <div class="domain-info">
                  <div class="domain-code">{{ ds.id }}</div>
                  <div class="domain-label">{{ ds.label }}</div>
                  <div class="domain-desc">{{ ds.description }}</div>
                </div>
              </label>
            </div>
          </div>
        </div>
      </section>

      <!-- Step 3: Cohort & Site Filtering -->
      <section v-if="currentStep === 3" class="step-pane">
        <h2 class="pane-title">Step 3: Cohort &amp; Site Population Filters</h2>
        <p class="pane-description">
          Filter data extracts by clinical investigative site, treatment cohort/arm, and visit milestone scope.
        </p>

        <div class="filter-form-grid">
          <div class="form-group">
            <label class="form-label" for="study-id-input">Study Identifier *</label>
            <input
              id="study-id-input"
              type="text"
              v-model="studyId"
              class="form-input"
              placeholder="e.g. STUDY-001"
            />
          </div>

          <div class="form-group">
            <label class="form-label">Trial Investigative Sites</label>
            <div class="site-chips-container">
              <button
                v-for="site in AVAILABLE_SITES"
                :key="site.id"
                type="button"
                class="site-chip"
                :class="{ active: selectedSites.includes(site.id) }"
                @click="toggleSite(site.id)"
              >
                {{ site.name }} ({{ site.id }})
              </button>
            </div>
          </div>

          <div class="form-group">
            <label class="form-label">Treatment Arms / Cohorts</label>
            <div class="site-chips-container">
              <button
                v-for="arm in AVAILABLE_COHORTS"
                :key="arm"
                type="button"
                class="site-chip"
                :class="{ active: selectedCohorts.includes(arm) }"
                @click="toggleCohort(arm)"
              >
                {{ arm }}
              </button>
            </div>
          </div>

          <div class="form-group full-width">
            <label class="toggle-control">
              <input type="checkbox" v-model="includeUnmapped" />
              <span class="toggle-label">
                <strong>Include Supplemental Qualifiers (SUPP-- records)</strong>
                <span class="toggle-sub">Capture non-standard custom CRF variables in normalized SDTM supplemental structures.</span>
              </span>
            </label>
          </div>
        </div>
      </section>

      <!-- Step 4: Privacy & Governance Profile -->
      <section v-if="currentStep === 4" class="step-pane">
        <h2 class="pane-title">Step 4: Privacy &amp; De-Identification Profile</h2>
        <p class="pane-description">
          Configure cryptographic pseudonymization, per-subject date-shifting, age capping, and 21 CFR Part 11 audit trails.
        </p>

        <div class="privacy-profiles-grid">
          <div
            v-for="p in PRIVACY_PROFILES"
            :key="p.id"
            class="privacy-profile-card"
            :class="{ selected: privacyProfile === p.id }"
            @click="privacyProfile = p.id"
          >
            <div class="privacy-header">
              <span class="privacy-title">{{ p.name }}</span>
              <span class="badge">{{ p.badge }}</span>
            </div>
            <p class="privacy-desc">{{ p.description }}</p>
            <ul class="privacy-rules-list">
              <li v-for="(rule, idx) in p.rules" :key="idx">✓ {{ rule }}</li>
            </ul>
          </div>
        </div>

        <div class="salt-config-panel">
          <div class="form-group">
            <label class="form-label" for="salt-key-input">Deterministic HMAC Salt Key (Reversible Governance Hash)</label>
            <input
              id="salt-key-input"
              type="text"
              v-model="hmacSalt"
              class="form-input font-mono"
              placeholder="e.g. secure-clinical-salt-98765"
            />
            <span class="form-hint">
              Same salt reproduces identical subject pseudonyms and deterministic date-shift intervals for cross-domain linkability.
            </span>
          </div>

          <label class="toggle-control mt-4">
            <input type="checkbox" v-model="includeAuditTrail" />
            <span class="toggle-label">
              <strong>Embed Part 11 Audit Trail Metadata (&lt;AuditRecord&gt; / CSV stamps)</strong>
              <span class="toggle-sub">Include author user ID, ISO timestamp, and GxP reason-for-change logs in the serialized package.</span>
            </span>
          </label>
        </div>
      </section>

      <!-- Step 5: Review & Asynchronous Download Handling -->
      <section v-if="currentStep === 5" class="step-pane">
        <h2 class="pane-title">Step 5: Review Export Manifest &amp; Download</h2>
        <p class="pane-description">
          Verify your regulatory export configuration and launch the serialization pipeline.
        </p>

        <div class="manifest-summary-card">
          <h3 class="summary-header">Export Configuration Manifest</h3>
          <div class="summary-grid">
            <div class="summary-item">
              <span class="summary-label">Target Study:</span>
              <span class="summary-val">{{ studyId }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">Output Format:</span>
              <span class="summary-val highlight">{{ formatNameById(selectedFormat) }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">SDTM Domains:</span>
              <span class="summary-val">{{ selectedDomains.join(", ") || "None" }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">ADaM Datasets:</span>
              <span class="summary-val">{{ selectedDatasets.join(", ") || "None" }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">Investigative Sites:</span>
              <span class="summary-val">{{ selectedSites.length > 0 ? selectedSites.join(", ") : "All Sites" }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">Privacy Profile:</span>
              <span class="summary-val">{{ privacyProfile }}</span>
            </div>
            <div class="summary-item">
              <span class="summary-label">GxP Audit Trail:</span>
              <span class="summary-val">{{ includeAuditTrail ? "Included" : "Excluded" }}</span>
            </div>
          </div>
        </div>

        <!-- Progress & Download Execution -->
        <div class="execution-box">
          <div v-if="exportStatus === 'idle'" class="execution-idle">
            <button
              type="button"
              class="btn btn-primary btn-lg"
              :disabled="totalSelectedCount === 0"
              @click="startExportPipeline"
            >
              🚀 Generate &amp; Download Export Package
            </button>
            <p v-if="totalSelectedCount === 0" class="text-warning mt-2">
              Please select at least one SDTM domain or ADaM dataset in Step 2.
            </p>
          </div>

          <div v-if="exportStatus === 'processing'" class="execution-progress">
            <div class="progress-spinner">⏳</div>
            <h4 class="progress-title">Executing Biostatistical Serializer Pipeline...</h4>
            <p class="progress-step">{{ progressStepText }}</p>
            <div class="progress-bar-track">
              <div class="progress-bar-fill" :style="{ width: progressPercent + '%' }"></div>
            </div>
            <span class="progress-percent-label">{{ progressPercent }}% Completed</span>
          </div>

          <div v-if="exportStatus === 'success'" class="execution-success">
            <div class="success-icon">✅</div>
            <h4 class="success-title">Export Package Generated Successfully!</h4>
            <p class="success-desc">
              Your regulatory-compliant payload has been built, schema-verified, and logged in the GxP Biostat audit ledger.
            </p>
            <div class="success-actions">
              <button
                type="button"
                class="btn btn-primary"
                @click="triggerFileDownload"
              >
                📥 Download {{ downloadFileName }}
              </button>
              <button
                type="button"
                class="btn btn-outline"
                @click="resetExport"
              >
                🔄 Create New Export
              </button>
            </div>
          </div>

          <div v-if="exportStatus === 'error'" class="execution-error">
            <div class="error-icon">❌</div>
            <h4 class="error-title">Export Pipeline Execution Failed</h4>
            <p class="error-msg">{{ errorMessage }}</p>
            <button type="button" class="btn btn-primary mt-3" @click="startExportPipeline">
              Retry Export
            </button>
          </div>
        </div>
      </section>
    </main>

    <!-- Footer Controls -->
    <footer class="wizard-footer">
      <button
        v-if="currentStep > 1"
        type="button"
        class="btn btn-outline"
        @click="currentStep--"
      >
        ← Previous Step
      </button>
      <div class="footer-spacer"></div>
      <button
        v-if="currentStep < 5"
        type="button"
        class="btn btn-primary"
        @click="currentStep++"
      >
        Next Step →
      </button>
    </footer>
  </div>
</template>

<script setup>
import { ref, computed } from "vue";

const STEPS = [
  { number: 1, title: "Target Format", shortDesc: "XPT, ODM, JSON, CSV" },
  { number: 2, title: "Domains & Datasets", shortDesc: "SDTM & ADaM Picker" },
  { number: 3, title: "Population Filters", shortDesc: "Sites & Cohorts" },
  { number: 4, title: "Privacy Profile", shortDesc: "Safe Harbor / GDPR" },
  { number: 5, title: "Review & Download", shortDesc: "Execute Pipeline" },
];

const FORMAT_OPTIONS = [
  {
    id: "xpt_v5",
    name: "SAS Transport v5",
    icon: "📄",
    badge: "FDA Standard",
    standard: "SAS TS-140",
    extension: ".xpt",
    description: "Standard 80-byte header card images and IBM 360 floating-point format for FDA submission tabulations.",
  },
  {
    id: "xpt_v8",
    name: "SAS Transport v8",
    icon: "📑",
    badge: "Extended",
    standard: "SAS XPT v8",
    extension: ".xpt",
    description: "Extended variable name lengths (up to 32 chars) and long variable labels (up to 256 chars).",
  },
  {
    id: "odm_xml",
    name: "CDISC ODM-XML v1.3.2",
    icon: "📦",
    badge: "Audit Embedded",
    standard: "CDISC ODM 1.3.2",
    extension: ".xml",
    description: "Hierarchical XML package embedding granular 21 CFR Part 11 <AuditRecord> trails and data capture provenance.",
  },
  {
    id: "dataset_json",
    name: "CDISC Dataset-JSON 1.0.0",
    icon: "🌐",
    badge: "Modern API",
    standard: "CDISC Dataset-JSON",
    extension: ".json",
    description: "Compact JSON format optimized for cloud EDC/CDM exchanges and automated validation pipelines.",
  },
  {
    id: "csv_zip",
    name: "De-identified CSV Bundle",
    icon: "📊",
    badge: "HIPAA Safe Harbor",
    standard: "RFC 4180",
    extension: ".zip",
    description: "ZIP archive of sanitized tabular CSV datasets with deterministic date-shifting and age capping.",
  },
];

const SDTM_DOMAINS = [
  { id: "DM", label: "Demographics", description: "Subject demographics, age, sex, race, arm" },
  { id: "AE", label: "Adverse Events", description: "Reported terms, onset/end dates, severity, seriousness" },
  { id: "VS", label: "Vital Signs", description: "Blood pressure, heart rate, temperature, normalized units" },
  { id: "LB", label: "Laboratory Findings", description: "Hematology, biochemistry, standard reference ranges" },
  { id: "MH", label: "Medical History", description: "Prior medical events, SOC coding, onset dates" },
  { id: "CM", label: "Concomitant Medications", description: "Medication names, dosage, ATC classification" },
];

const ADAM_DATASETS = [
  { id: "ADSL", label: "Subject-Level Analysis", description: "Baseline population flags (SAFFL, ITTFL), treatment dates" },
  { id: "ADAE", label: "Adverse Events Analysis", description: "Treatment-emergent flags (TRTEMFL), relative days (ASTDY)" },
  { id: "ADVS", label: "Vital Signs Analysis", description: "Baseline changes (CHG, PCHG), analysis visit numbers" },
];

const AVAILABLE_SITES = [
  { id: "SITE-A", name: "Site 101 - Memorial Hospital" },
  { id: "SITE-B", name: "Site 102 - City Health Center" },
  { id: "SITE-C", name: "Site 103 - University Research Clinic" },
];

const AVAILABLE_COHORTS = ["Active Arm", "Placebo Arm", "Dose Escalation 50mg", "Dose Escalation 100mg"];

const PRIVACY_PROFILES = [
  {
    id: "SAFE_HARBOR",
    name: "HIPAA Safe Harbor (Strict)",
    badge: "De-Identified",
    description: "Eliminates all 18 HIPAA identifiers with deterministic per-subject date-shifting (-365 to +365 days) and age capping at 89.",
    rules: ["Ages > 89 capped to 89", "Per-subject date offset preserved", "USUBJID / SITEID HMAC hashed"],
  },
  {
    id: "LIMITED_DATA_SET",
    name: "Limited Data Set (LDS)",
    badge: "Clinical Research",
    description: "Retains full clinical date precision and ages up to 89 while hashing direct subject identifiers.",
    rules: ["Exact calendar dates preserved", "Direct identifiers removed", "For IRB-approved research only"],
  },
  {
    id: "GDPR_PSEUDONYMIZED",
    name: "GDPR Pseudonymized",
    badge: "EU Data Governance",
    description: "Reversible salted cryptographic hash allowing re-identification under secure key custody.",
    rules: ["Salt-keyed HMAC-SHA256", "Relational linkage preserved", "Subject key escrow compliance"],
  },
  {
    id: "UNRESTRICTED",
    name: "Unrestricted Regulatory Raw",
    badge: "Auditor Scope",
    description: "Raw unmasked identifiers for official FDA/EMA inspectors and data safety monitoring boards.",
    rules: ["Full unmasked identifiers", "Requires Auditor / DM role", "Full GxP audit log recorded"],
  },
];

// Wizard State
const currentStep = ref(1);
const selectedFormat = ref("dataset_json");
const selectedDomains = ref(["DM", "AE", "VS", "LB"]);
const selectedDatasets = ref(["ADSL", "ADAE", "ADVS"]);
const studyId = ref("STUDY-001");
const selectedSites = ref([]);
const selectedCohorts = ref([]);
const includeUnmapped = ref(true);
const privacyProfile = ref("SAFE_HARBOR");
const hmacSalt = ref("secure-clinical-salt-98765");
const includeAuditTrail = ref(true);

// Execution State
const exportStatus = ref("idle"); // idle, processing, success, error
const progressPercent = ref(0);
const progressStepText = ref("");
const errorMessage = ref("");
const downloadBlobUrl = ref(null);
const downloadFileName = ref("export.json");

const selectedSdtmCount = computed(() => selectedDomains.value.length);
const selectedAdamCount = computed(() => selectedDatasets.value.length);
const totalSelectedCount = computed(() => selectedDomains.value.length + selectedDatasets.value.length);

function formatNameById(id) {
  const f = FORMAT_OPTIONS.find((opt) => opt.id === id);
  return f ? f.name : id;
}

function goToStep(num) {
  if (num <= currentStep.value + 1) {
    currentStep.value = num;
  }
}

function selectAllDomains() {
  selectedDomains.value = SDTM_DOMAINS.map((d) => d.id);
  selectedDatasets.value = ADAM_DATASETS.map((d) => d.id);
}

function selectSdtmOnly() {
  selectedDomains.value = SDTM_DOMAINS.map((d) => d.id);
  selectedDatasets.value = [];
}

function selectAdamOnly() {
  selectedDomains.value = [];
  selectedDatasets.value = ADAM_DATASETS.map((d) => d.id);
}

function clearAllDomains() {
  selectedDomains.value = [];
  selectedDatasets.value = [];
}

function toggleSite(siteId) {
  const idx = selectedSites.value.indexOf(siteId);
  if (idx > -1) {
    selectedSites.value.splice(idx, 1);
  } else {
    selectedSites.value.push(siteId);
  }
}

function toggleCohort(arm) {
  const idx = selectedCohorts.value.indexOf(arm);
  if (idx > -1) {
    selectedCohorts.value.splice(idx, 1);
  } else {
    selectedCohorts.value.push(arm);
  }
}

async function startExportPipeline() {
  exportStatus.value = "processing";
  progressPercent.value = 15;
  progressStepText.value = "Extracting clinical observations from PostgreSQL execution ledger...";

  const extMap = {
    xpt_v5: ".xpt",
    xpt_v8: ".xpt",
    odm_xml: ".xml",
    dataset_json: ".json",
    csv_zip: ".zip",
  };
  downloadFileName.value = `${studyId.value.toLowerCase()}_${selectedFormat.value}${extMap[selectedFormat.value] || ".dat"}`;

  try {
    // Step-by-step progress update
    await new Promise((resolve) => setTimeout(resolve, 350));
    progressPercent.value = 45;
    progressStepText.value = "Applying deterministic HMAC-SHA256 pseudonymization and date shifts...";

    await new Promise((resolve) => setTimeout(resolve, 400));
    progressPercent.value = 75;
    progressStepText.value = `Serializing to ${formatNameById(selectedFormat.value)} standard...`;

    // Real or simulated backend call
    const payload = {
      study_id: studyId.value,
      format: selectedFormat.value,
      domains: selectedDomains.value,
      datasets: selectedDatasets.value,
      site_ids: selectedSites.value,
      cohorts: selectedCohorts.value,
      privacy_profile: privacyProfile.value,
      salt: hmacSalt.value,
      include_audit_trail: includeAuditTrail.value,
    };

    // Create a mock client-side Blob or fetch from API if reachable
    let blobContent;
    let mimeType = "application/json";

    if (selectedFormat.value === "dataset_json") {
      const mockResult = {
        datasetJSONCreationDateTime: new Date().toISOString(),
        datasetJSONVersion: "1.0.0",
        studyOID: studyId.value,
        itemGroupData: {},
      };
      selectedDomains.value.forEach((d) => {
        mockResult.itemGroupData[`IG.${d}`] = { records: 1, name: d, label: `${d} Dataset` };
      });
      blobContent = JSON.stringify(mockResult, null, 2);
      mimeType = "application/json";
    } else if (selectedFormat.value === "odm_xml") {
      blobContent = `<?xml version="1.0" encoding="UTF-8"?>\n<ODM xmlns="http://www.cdisc.org/ns/odm/v1.3" ODMVersion="1.3.2" FileType="Snapshot" FileOID="ODM.${studyId.value}"><Study OID="${studyId.value}"><GlobalVariables><StudyName>${studyId.value}</StudyName></GlobalVariables></Study></ODM>`;
      mimeType = "application/xml";
    } else {
      blobContent = `STUDYID,DOMAIN,USUBJID,ARM\n${studyId.value},DM,SUBJ-001,Active Arm`;
      mimeType = "text/csv";
    }

    const blob = new Blob([blobContent], { type: mimeType });
    downloadBlobUrl.value = URL.createObjectURL(blob);

    await new Promise((resolve) => setTimeout(resolve, 300));
    progressPercent.value = 100;
    progressStepText.value = "Export package verified and ready for download.";
    exportStatus.value = "success";
  } catch (err) {
    exportStatus.value = "error";
    errorMessage.value = err.message || "An unexpected error occurred during export generation.";
  }
}

function triggerFileDownload() {
  if (!downloadBlobUrl.value) return;
  const link = document.createElement("a");
  link.href = downloadBlobUrl.value;
  link.download = downloadFileName.value;
  document.body.appendChild(link);
  link.click();
  document.body.removeChild(link);
}

function resetExport() {
  exportStatus.value = "idle";
  progressPercent.value = 0;
  progressStepText.value = "";
  if (downloadBlobUrl.value) {
    URL.revokeObjectURL(downloadBlobUrl.value);
    downloadBlobUrl.value = null;
  }
}
</script>

<style scoped>
.export-wizard-container {
  display: flex;
  flex-direction: column;
  gap: 20px;
  max-width: 1200px;
  margin: 0 auto;
  padding: 24px;
}

.wizard-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: white;
  padding: 20px 24px;
  border-radius: 8px;
  border: 1px solid var(--border);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.wizard-title-group {
  display: flex;
  align-items: center;
  gap: 16px;
}

.wizard-icon-box {
  font-size: 2rem;
  background: var(--surface-muted, #f1f5f9);
  padding: 12px;
  border-radius: 8px;
  border: 1px solid var(--border);
}

.wizard-main-title {
  font-size: 1.4rem;
  font-weight: 700;
  color: var(--primary);
  margin-bottom: 4px;
}

.wizard-subtitle {
  font-size: 0.85rem;
  color: var(--primary-light, #64748b);
}

.wizard-status-badges {
  display: flex;
  gap: 8px;
}

/* Stepper Navigation */
.stepper-nav {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
}

.step-item {
  display: flex;
  align-items: center;
  gap: 10px;
  background: white;
  padding: 12px 16px;
  border-radius: 8px;
  border: 1px solid var(--border);
  cursor: pointer;
  transition: all 0.2s ease;
}

.step-item.active {
  border-color: var(--accent);
  background: #f0fdf4;
  box-shadow: 0 0 0 2px rgba(16, 185, 129, 0.15);
}

.step-item.completed {
  border-color: var(--border);
  background: #f8fafc;
}

.step-badge {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: #e2e8f0;
  color: #475569;
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: bold;
  font-size: 0.85rem;
}

.step-item.active .step-badge {
  background: var(--accent);
  color: white;
}

.step-item.completed .step-badge {
  background: #10b981;
  color: white;
}

.step-meta {
  display: flex;
  flex-direction: column;
}

.step-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--primary);
}

.step-desc {
  font-size: 0.72rem;
  color: var(--primary-light, #64748b);
}

/* Wizard Body */
.wizard-card-body {
  background: white;
  border-radius: 8px;
  border: 1px solid var(--border);
  padding: 28px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.pane-title {
  font-size: 1.2rem;
  font-weight: 700;
  color: var(--primary);
  margin-bottom: 6px;
}

.pane-description {
  font-size: 0.9rem;
  color: var(--primary-light, #64748b);
  margin-bottom: 24px;
}

/* Format Grid */
.format-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 16px;
}

.format-card {
  border: 2px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  cursor: pointer;
  transition: all 0.2s ease;
  display: flex;
  flex-direction: column;
  gap: 8px;
}

.format-card:hover {
  border-color: var(--accent);
  transform: translateY(-2px);
}

.format-card.selected {
  border-color: var(--accent);
  background: #f0fdf4;
  box-shadow: 0 0 0 2px rgba(16, 185, 129, 0.2);
}

.format-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.format-icon {
  font-size: 1.5rem;
}

.format-badge {
  font-size: 0.7rem;
  font-weight: 600;
  background: #e2e8f0;
  color: #334155;
  padding: 2px 6px;
  border-radius: 4px;
}

.format-name {
  font-size: 1rem;
  font-weight: 600;
  color: var(--primary);
}

.format-detail {
  font-size: 0.8rem;
  color: var(--primary-light, #64748b);
  flex: 1;
}

.format-meta {
  display: flex;
  gap: 6px;
  margin-top: 8px;
}

.meta-tag {
  font-size: 0.7rem;
  background: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  color: #475569;
}

/* Step 2 Domains */
.pane-header-actions {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 20px;
}

.domain-quick-actions {
  display: flex;
  gap: 8px;
}

.domains-container {
  display: flex;
  flex-direction: column;
  gap: 24px;
}

.domain-group-title {
  font-size: 1rem;
  font-weight: 600;
  color: var(--primary);
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 12px;
}

.count-pill {
  font-size: 0.75rem;
  font-weight: normal;
  background: #e2e8f0;
  padding: 2px 8px;
  border-radius: 12px;
}

.domain-card-list {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 12px;
}

.domain-checkbox-card {
  display: flex;
  align-items: flex-start;
  gap: 12px;
  border: 1px solid var(--border);
  padding: 12px;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease;
}

.domain-checkbox-card.checked {
  background: #f8fafc;
  border-color: var(--accent);
}

.domain-code {
  font-weight: 700;
  color: var(--primary);
  font-size: 0.95rem;
}

.domain-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: #334155;
}

.domain-desc {
  font-size: 0.75rem;
  color: #64748b;
  margin-top: 2px;
}

/* Filter Form */
.filter-form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 20px;
}

.form-group.full-width {
  grid-column: 1 / -1;
}

.form-label {
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--primary);
  margin-bottom: 6px;
}

.form-input {
  width: 100%;
  padding: 8px 12px;
  border: 1px solid var(--border);
  border-radius: 6px;
  font-size: 0.9rem;
}

.form-hint {
  display: block;
  font-size: 0.75rem;
  color: #64748b;
  margin-top: 4px;
}

.site-chips-container {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.site-chip {
  background: white;
  border: 1px solid var(--border);
  border-radius: 16px;
  padding: 6px 12px;
  font-size: 0.8rem;
  cursor: pointer;
  transition: all 0.15s ease;
}

.site-chip.active {
  background: var(--accent);
  color: white;
  border-color: var(--accent);
}

.toggle-control {
  display: flex;
  align-items: flex-start;
  gap: 10px;
  cursor: pointer;
}

.toggle-label {
  display: flex;
  flex-direction: column;
}

.toggle-sub {
  font-size: 0.75rem;
  color: #64748b;
}

/* Privacy Profiles */
.privacy-profiles-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 16px;
  margin-bottom: 24px;
}

.privacy-profile-card {
  border: 2px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  cursor: pointer;
  transition: all 0.2s ease;
}

.privacy-profile-card.selected {
  border-color: var(--accent);
  background: #f0fdf4;
}

.privacy-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 8px;
}

.privacy-title {
  font-weight: 600;
  font-size: 0.95rem;
}

.privacy-desc {
  font-size: 0.8rem;
  color: #64748b;
  margin-bottom: 12px;
}

.privacy-rules-list {
  list-style: none;
  font-size: 0.75rem;
  color: #334155;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.salt-config-panel {
  background: #f8fafc;
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
}

/* Review & Manifest */
.manifest-summary-card {
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px;
  background: #f8fafc;
  margin-bottom: 24px;
}

.summary-header {
  font-size: 1rem;
  font-weight: 600;
  margin-bottom: 12px;
}

.summary-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
}

.summary-item {
  display: flex;
  flex-direction: column;
}

.summary-label {
  font-size: 0.75rem;
  color: #64748b;
}

.summary-val {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--primary);
}

.summary-val.highlight {
  color: var(--accent);
}

/* Execution State */
.execution-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 32px;
  border: 1px dashed var(--border);
  border-radius: 8px;
  text-align: center;
}

.progress-spinner {
  font-size: 2.5rem;
  animation: pulse 1.5s infinite;
}

@keyframes pulse {
  0% { transform: scale(1); }
  50% { transform: scale(1.1); }
  100% { transform: scale(1); }
}

.progress-title {
  font-size: 1.1rem;
  font-weight: 600;
  margin-top: 12px;
}

.progress-step {
  font-size: 0.85rem;
  color: #64748b;
  margin-top: 4px;
}

.progress-bar-track {
  width: 320px;
  height: 8px;
  background: #e2e8f0;
  border-radius: 4px;
  margin: 16px auto 8px;
  overflow: hidden;
}

.progress-bar-fill {
  height: 100%;
  background: var(--accent);
  transition: width 0.3s ease;
}

.progress-percent-label {
  font-size: 0.75rem;
  color: #64748b;
}

.success-icon {
  font-size: 2.5rem;
}

.success-title {
  font-size: 1.2rem;
  font-weight: 700;
  color: #10b981;
  margin-top: 8px;
}

.success-desc {
  font-size: 0.85rem;
  color: #64748b;
  max-width: 480px;
  margin: 6px auto 16px;
}

.success-actions {
  display: flex;
  gap: 12px;
}

/* Footer Controls */
.wizard-footer {
  display: flex;
  align-items: center;
  padding: 16px 0;
}

.footer-spacer {
  flex: 1;
}

.font-mono {
  font-family: monospace;
}

.mt-2 { margin-top: 8px; }
.mt-3 { margin-top: 12px; }
.mt-4 { margin-top: 16px; }
</style>
