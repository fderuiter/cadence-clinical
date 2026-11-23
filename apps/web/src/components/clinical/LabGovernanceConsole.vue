<template>
  <div
    class="lab-governance-console"
    role="region"
    aria-label="Laboratory Normal Range Governance & Toxicity Grading Console"
  >
    <!-- Header Bar -->
    <header class="console-header">
      <div class="header-main">
        <div class="title-group">
          <span class="badge badge-regulatory"
            >CDISC LAB v1.0.1 / NCI-CTCAE v5.0 / DAIDS / CLIA</span
          >
          <h2 class="console-title">
            Lab Reference Range Governance & Toxicity Grading Console
          </h2>
        </div>
        <div class="header-metadata">
          <span class="meta-item"
            ><strong>Study:</strong> {{ currentStudyId || "STUDY-101" }}</span
          >
          <span class="meta-item"
            ><strong>Active Ranges:</strong>
            <span class="status-pill info">{{ ranges.length }}</span></span
          >
          <span class="meta-item"
            ><strong>Staged AE Triggers:</strong>
            <span class="status-pill danger">{{
              stagedReviews.length
            }}</span></span
          >
          <span class="meta-item"
            ><strong>Local Certifications:</strong>
            <span class="status-pill success">{{
              certifications.length
            }}</span></span
          >
        </div>
      </div>

      <div class="header-actions">
        <button class="btn btn-outline" @click="openBulkImportModal">
          <span class="icon">📊</span> Bulk Import Reference Ranges (CSV/XLSX)
        </button>
        <button class="btn btn-secondary" @click="openIngestModal">
          <span class="icon">📥</span> Ingest Lab Feed (HL7 / CDISC / CSV)
        </button>
        <button class="btn btn-primary" @click="openCreateRangeModal">
          <span class="icon">➕</span> New Reference Range
        </button>
      </div>
    </header>

    <!-- Navigation Tabs -->
    <nav class="nav-tabs" role="tablist" aria-label="Lab Console Views">
      <button
        role="tab"
        :aria-selected="activeTab === 'ranges'"
        :class="['tab-button', { active: activeTab === 'ranges' }]"
        @click="activeTab = 'ranges'"
      >
        🧬 Normal Reference Ranges ({{ ranges.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'toxicity_queue'"
        :class="['tab-button', { active: activeTab === 'toxicity_queue' }]"
        @click="activeTab = 'toxicity_queue'"
      >
        🚨 Investigator AE Review Queue ({{ stagedReviews.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'certifications'"
        :class="['tab-button', { active: activeTab === 'certifications' }]"
        @click="activeTab = 'certifications'"
      >
        🏥 Local Lab Certifications (CLIA/CAP)
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'loinc_converter'"
        :class="['tab-button', { active: activeTab === 'loinc_converter' }]"
        @click="activeTab = 'loinc_converter'"
      >
        📐 LOINC & Unit Conversion Engine
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'batches'"
        :class="['tab-button', { active: activeTab === 'batches' }]"
        @click="activeTab = 'batches'"
      >
        📊 Feed Ingestion Monitor
      </button>
      <button
        role="tab"
        :aria-selected="activeTab === 'trends'"
        :class="['tab-button', { active: activeTab === 'trends' }]"
        @click="activeTab = 'trends'"
      >
        📈 Longitudinal Trends & Delta Checks
      </button>
    </nav>

    <!-- Main Content Panes -->
    <main class="main-content">
      <!-- TAB 1: Normal Reference Ranges Governance -->
      <section
        v-if="activeTab === 'ranges'"
        class="tab-pane"
        aria-labelledby="ranges-tab-heading"
      >
        <div class="pane-toolbar">
          <div class="filter-group">
            <label for="range-test-filter">Analyte Filter:</label>
            <input
              id="range-test-filter"
              v-model="rangeSearch"
              type="text"
              placeholder="Search by code (e.g. ALT, CREAT) or name..."
              class="text-input"
            />
          </div>
          <div class="filter-group">
            <label for="range-source-filter">Lab Source:</label>
            <select
              id="range-source-filter"
              v-model="rangeSourceFilter"
              class="select-input"
            >
              <option value="ALL">All Sources (Central & Local)</option>
              <option value="CENTRAL">Central Lab Only</option>
              <option value="LOCAL">Local Lab Only</option>
            </select>
          </div>
          <div class="filter-group">
            <label for="range-sex-filter">Sex Applicability:</label>
            <select
              id="range-sex-filter"
              v-model="rangeSexFilter"
              class="select-input"
            >
              <option value="ALL">All Demographics</option>
              <option value="M">Male (M)</option>
              <option value="F">Female (F)</option>
            </select>
          </div>
          <button class="btn btn-primary" @click="openBulkImportModal">
            <span class="icon">📥</span> Bulk Import Lab Ranges
          </button>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Test Code</th>
                <th>Test Name</th>
                <th>Source</th>
                <th>Site ID</th>
                <th>Demographics (Sex / Age / Ethnicity)</th>
                <th>Unit</th>
                <th>Normal Limits [LLN - ULN]</th>
                <th>Critical Bounds</th>
                <th>Version</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="r in filteredRanges" :key="r.id">
                <td>
                  <strong class="code-tag">{{ r.test_code }}</strong>
                </td>
                <td>{{ r.test_name }}</td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      r.source === 'CENTRAL' ? 'primary' : 'secondary',
                    ]"
                  >
                    {{ r.source }}
                  </span>
                </td>
                <td>{{ r.site_id || "All Sites (Global)" }}</td>
                <td>
                  <div class="demographic-tags">
                    <span class="mini-tag"
                      >Sex: {{ r.sex_applicability || "ALL" }}</span
                    >
                    <span class="mini-tag"
                      >Age: {{ formatAgeRange(r.age_low, r.age_high) }}</span
                    >
                    <span v-if="r.ethnicity_applicability" class="mini-tag"
                      >Eth: {{ r.ethnicity_applicability }}</span
                    >
                  </div>
                </td>
                <td>
                  <code>{{ r.normalized_unit || r.unit }}</code>
                </td>
                <td>
                  <span class="range-span"
                    >{{ r.low_bound ?? "—" }} – {{ r.high_bound ?? "—" }}</span
                  >
                </td>
                <td>
                  <span
                    v-if="r.critical_low !== null || r.critical_high !== null"
                    class="critical-span"
                  >
                    &lt;{{ r.critical_low ?? "—" }} | &gt;{{
                      r.critical_high ?? "—"
                    }}
                  </span>
                  <span v-else class="text-muted">None</span>
                </td>
                <td>v{{ r.version || 1 }}</td>
                <td>
                  <button class="btn btn-sm btn-outline" @click="editRange(r)">
                    Edit
                  </button>
                  <button
                    class="btn btn-sm btn-outline-danger"
                    @click="deactivateRange(r)"
                  >
                    Deactivate
                  </button>
                </td>
              </tr>
              <tr v-if="filteredRanges.length === 0">
                <td colspan="10" class="empty-state">
                  No laboratory reference ranges match the active filter
                  criteria.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 2: Investigator Grade 3/4 Adverse Event Review Queue -->
      <section
        v-if="activeTab === 'toxicity_queue'"
        class="tab-pane"
        aria-labelledby="toxicity-queue-heading"
      >
        <div class="pane-toolbar alert-toolbar">
          <div class="alert-info-box">
            <span class="icon-warning">⚠️</span>
            <div>
              <strong>Investigator Safety Trigger Workflow:</strong>
              <p>
                Automated CTCAE v5.0 / DAIDS Grade 3/4 toxicities, Hy's Law DILI
                alerts, and acute &ge;50% delta checks auto-draft an AE eCRF
                staged for clinical review with 21 CFR Part 11 electronic
                signature audit trails.
              </p>
            </div>
          </div>
          <div class="filter-group">
            <label for="review-status-filter">Status Filter:</label>
            <select
              id="review-status-filter"
              v-model="reviewStatusFilter"
              class="select-input"
              @change="fetchStagedReviews"
            >
              <option value="STAGED_FOR_INVESTIGATOR_REVIEW">
                Staged for Review (Action Required)
              </option>
              <option value="CONFIRMED_AS_AE">Confirmed as AE</option>
              <option value="DISMISSED_NOT_CLINICALLY_SIGNIFICANT">
                Dismissed (Not Significant)
              </option>
              <option value="ESCALATED_SAE">Escalated to SAE</option>
              <option value="ALL">All Items</option>
            </select>
          </div>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Subject ID</th>
                <th>Visit</th>
                <th>Analyte</th>
                <th>Observed Value</th>
                <th>Toxicity Grade</th>
                <th>Safety Flags</th>
                <th>Clinical Description</th>
                <th>Review Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="review in stagedReviews"
                :key="review.id"
                :class="{
                  'row-critical':
                    review.ctcae_grade >= 4 || review.is_hys_law_warning,
                }"
              >
                <td>
                  <strong>{{ review.subject_id }}</strong>
                </td>
                <td>{{ review.visit_id || "Encounter" }}</td>
                <td>
                  <strong>{{ review.test_code }}</strong>
                  <div class="sub-text">
                    {{ review.test_name }}
                  </div>
                </td>
                <td>
                  <span class="value-highlight"
                    >{{ review.lab_value }} {{ review.unit }}</span
                  >
                </td>
                <td>
                  <span :class="['grade-badge', 'grade-' + review.ctcae_grade]">
                    Grade {{ review.ctcae_grade }}
                  </span>
                </td>
                <td>
                  <div class="flags-stack">
                    <span
                      v-if="review.is_hys_law_warning"
                      class="safety-tag hys-law"
                      >🚨 Hy's Law DILI</span
                    >
                    <span
                      v-if="review.is_delta_check_alert"
                      class="safety-tag delta"
                      >⚡ Delta Alert ({{ review.delta_percentage }}%)</span
                    >
                    <span
                      v-if="
                        !review.is_hys_law_warning &&
                        !review.is_delta_check_alert
                      "
                      class="text-muted"
                      >Standard</span
                    >
                  </div>
                </td>
                <td class="clinical-desc-cell">
                  {{ review.clinical_description }}
                </td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      getReviewStatusPillClass(review.review_status),
                    ]"
                  >
                    {{ formatReviewStatus(review.review_status) }}
                  </span>
                </td>
                <td>
                  <button
                    v-if="
                      review.review_status === 'STAGED_FOR_INVESTIGATOR_REVIEW'
                    "
                    class="btn btn-sm btn-primary"
                    @click="openActionModal(review)"
                  >
                    Investigator Sign-off
                  </button>
                  <span v-else class="text-muted signed-tag">
                    Reviewed by {{ review.reviewed_by || "Investigator" }}
                  </span>
                </td>
              </tr>
              <tr v-if="stagedReviews.length === 0">
                <td colspan="9" class="empty-state">
                  No staged adverse event laboratory triggers pending
                  investigator review.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 3: Local Lab Certifications & Accreditation Governance -->
      <section
        v-if="activeTab === 'certifications'"
        class="tab-pane"
        aria-labelledby="certifications-tab-heading"
      >
        <div class="pane-toolbar">
          <div class="title-sub">
            <h3>
              Local Laboratory Accreditations & CLIA/CAP Certification Tracking
            </h3>
            <p>
              Monitors laboratory accreditation expiration and enforces
              uncalibrated range safeguards.
            </p>
          </div>
          <button class="btn btn-primary" @click="openCreateCertModal">
            <span class="icon">➕</span> Register Local Lab Certification
          </button>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Site ID</th>
                <th>Lab Name</th>
                <th>Accreditation Agency</th>
                <th>Certificate Number</th>
                <th>Effective Date</th>
                <th>Expiration Date</th>
                <th>Status</th>
                <th>Safeguard Status</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="cert in certifications" :key="cert.id">
                <td>
                  <strong>{{ cert.site_id }}</strong>
                </td>
                <td>{{ cert.lab_name }}</td>
                <td>
                  <span class="accred-tag">{{ cert.accreditation_body }}</span>
                </td>
                <td>
                  <code>{{ cert.certification_number }}</code>
                </td>
                <td>{{ formatDate(cert.effective_date) }}</td>
                <td>{{ formatDate(cert.expiration_date) }}</td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      cert.status === 'ACTIVE' ? 'success' : 'danger',
                    ]"
                  >
                    {{ cert.status }}
                  </span>
                </td>
                <td>
                  <span
                    v-if="isCertExpired(cert.expiration_date)"
                    class="safeguard-badge active"
                  >
                    ⚠️ Uncalibrated Range Safeguard Active
                  </span>
                  <span v-else class="safeguard-badge normal">
                    ✅ Calibrated & Verified
                  </span>
                </td>
                <td>
                  <button
                    class="btn btn-sm btn-outline"
                    @click="verifyCert(cert)"
                  >
                    Verify Site
                  </button>
                </td>
              </tr>
              <tr v-if="certifications.length === 0">
                <td colspan="9" class="empty-state">
                  No local laboratory certifications recorded. Register CLIA/CAP
                  credentials to enable local lab ingestion.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 4: LOINC Standard Mapping & Mathematical Unit Conversion Engine -->
      <section
        v-if="activeTab === 'loinc_converter'"
        class="tab-pane"
        aria-labelledby="loinc-tab-heading"
      >
        <div class="converter-grid">
          <!-- Live Interactive Unit Converter -->
          <div class="converter-card">
            <h4>🔄 Real-Time LOINC Mathematical Unit Converter</h4>
            <div class="form-group">
              <label for="conv-test">Analyte / Test Code:</label>
              <select
                id="conv-test"
                v-model="conversionModel.test_code"
                class="select-input"
                @change="onConversionTestChange"
              >
                <option value="ALT">
                  ALT (Alanine Aminotransferase) [LOINC: 1742-6]
                </option>
                <option value="AST">
                  AST (Aspartate Aminotransferase) [LOINC: 1920-8]
                </option>
                <option value="BILI">Total Bilirubin [LOINC: 1975-2]</option>
                <option value="CREAT">Serum Creatinine [LOINC: 2160-0]</option>
                <option value="GLUCOSE">Fasting Glucose [LOINC: 2345-7]</option>
                <option value="HEMOGLOBIN">Hemoglobin [LOINC: 718-7]</option>
                <option value="PLATELETS">Platelets [LOINC: 777-3]</option>
                <option value="ANC">
                  Absolute Neutrophil Count [LOINC: 26499-4]
                </option>
                <option value="POTASSIUM">Potassium [LOINC: 2823-3]</option>
                <option value="SODIUM">Sodium [LOINC: 2951-2]</option>
                <option value="CALCIUM">Calcium [LOINC: 17861-6]</option>
                <option value="CHOLESTEROL">
                  Total Cholesterol [LOINC: 2093-3]
                </option>
                <option value="TRIGLYCERIDES">
                  Triglycerides [LOINC: 2571-8]
                </option>
              </select>
            </div>

            <div class="converter-row">
              <div class="form-group">
                <label for="conv-val">Input Value:</label>
                <input
                  id="conv-val"
                  v-model.number="conversionModel.value"
                  type="number"
                  step="any"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <label for="conv-from">From Unit:</label>
                <input
                  id="conv-from"
                  v-model="conversionModel.from_unit"
                  type="text"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <label for="conv-to">To Unit (Standard SI):</label>
                <input
                  id="conv-to"
                  v-model="conversionModel.to_unit"
                  type="text"
                  class="text-input"
                />
              </div>
            </div>

            <button
              class="btn btn-primary"
              :disabled="isConverting"
              @click="runUnitConversion"
            >
              {{
                isConverting
                  ? "Converting..."
                  : "Execute Mathematical Conversion"
              }}
            </button>

            <div v-if="conversionResult" class="conversion-output">
              <div class="output-row">
                <span>Converted Normalized Result:</span>
                <strong
                  >{{ conversionResult.converted_value }}
                  {{ conversionResult.to_unit }}</strong
                >
              </div>
              <div class="output-row">
                <span>Multiplication Factor:</span>
                <code>{{ conversionResult.factor }}</code>
              </div>
              <div class="output-row">
                <span>Mapped LOINC Code:</span>
                <code>{{
                  conversionResult.loinc_code || "Standard UCUM"
                }}</code>
              </div>
            </div>
          </div>

          <!-- Real-Time CTCAE / DAIDS Toxicity Evaluation Simulator -->
          <div class="converter-card">
            <h4>🧪 Toxicity Evaluation & Hy's Law Simulator</h4>
            <div class="form-group">
              <label for="sim-scale">Grading System Standard:</label>
              <select
                id="sim-scale"
                v-model="simModel.grading_scale"
                class="select-input"
              >
                <option value="CTCAE_V5">NCI-CTCAE v5.0</option>
                <option value="DAIDS_V2_1">DAIDS v2.1 Severity Table</option>
              </select>
            </div>

            <div class="converter-row">
              <div class="form-group">
                <label for="sim-test">Test Code:</label>
                <input
                  id="sim-test"
                  v-model="simModel.test_code"
                  type="text"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <label for="sim-val">Observed Value:</label>
                <input
                  id="sim-val"
                  v-model.number="simModel.value"
                  type="number"
                  step="any"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <label for="sim-unit">Unit:</label>
                <input
                  id="sim-unit"
                  v-model="simModel.unit"
                  type="text"
                  class="text-input"
                />
              </div>
            </div>

            <div class="converter-row">
              <div class="form-group">
                <label for="sim-uln">Demographic ULN:</label>
                <input
                  id="sim-uln"
                  v-model.number="simModel.ref_high"
                  type="number"
                  step="any"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <label for="sim-lln">Demographic LLN:</label>
                <input
                  id="sim-lln"
                  v-model.number="simModel.ref_low"
                  type="number"
                  step="any"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <label for="sim-base">Subject Baseline:</label>
                <input
                  id="sim-base"
                  v-model.number="simModel.baseline_value"
                  type="number"
                  step="any"
                  class="text-input"
                />
              </div>
            </div>

            <!-- Co-analytes for Hy's Law simulation -->
            <div
              v-if="
                simModel.test_code === 'ALT' || simModel.test_code === 'AST'
              "
              class="hys-law-inputs"
            >
              <h5>Co-analytes for Hy's Law DILI Check:</h5>
              <div class="converter-row">
                <div class="form-group">
                  <label for="sim-bili">Total Bilirubin (mg/dL):</label>
                  <input
                    id="sim-bili"
                    v-model.number="simModel.bili_value"
                    type="number"
                    step="any"
                    class="text-input"
                  />
                </div>
                <div class="form-group">
                  <label for="sim-alkp">ALKP (U/L):</label>
                  <input
                    id="sim-alkp"
                    v-model.number="simModel.alkp_value"
                    type="number"
                    step="any"
                    class="text-input"
                  />
                </div>
              </div>
            </div>

            <button class="btn btn-secondary" @click="runToxicitySimulation">
              Simulate Toxicity Grading
            </button>

            <div v-if="simResult" class="conversion-output">
              <div class="output-row">
                <span>Calculated Toxicity Grade:</span>
                <span :class="['grade-badge', 'grade-' + simResult.ctcae_grade]"
                  >Grade {{ simResult.ctcae_grade }}</span
                >
              </div>
              <div class="output-row">
                <span>Clinical Severity:</span>
                <strong>{{ simResult.toxicity_description }}</strong>
              </div>
              <div class="output-row">
                <span>Grade 3/4 AE Trigger Fired:</span>
                <strong>{{
                  simResult.is_critical_ae_trigger
                    ? "YES (Auto-Drafts eCRF)"
                    : "NO"
                }}</strong>
              </div>
              <div
                v-if="simResult.is_hys_law_warning"
                class="output-row alert-row"
              >
                <span>Hy's Law DILI Alert:</span>
                <strong class="text-danger"
                  >CRITICAL DRUG-INDUCED LIVER INJURY WARNING</strong
                >
              </div>
            </div>
          </div>
        </div>

        <!-- LOINC Standard Master Dictionary Table -->
        <div class="loinc-catalog-section">
          <h4>Standardized LOINC Domain Mapping Dictionary</h4>
          <div class="table-responsive">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Analyte Code</th>
                  <th>Standard Name</th>
                  <th>LOINC Code</th>
                  <th>Standard SI Unit</th>
                  <th>Category</th>
                  <th>Molecular Weight (g/mol)</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="item in loincCatalog" :key="item.loinc_code">
                  <td>
                    <strong>{{ item.test_code }}</strong>
                  </td>
                  <td>{{ item.test_name }}</td>
                  <td>
                    <code class="loinc-badge">{{ item.loinc_code }}</code>
                  </td>
                  <td>
                    <code>{{ item.standard_unit }}</code>
                  </td>
                  <td>
                    <span class="category-tag">{{ item.category }}</span>
                  </td>
                  <td>{{ item.molecular_weight ?? "—" }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- TAB 5: Laboratory Feed Ingestion Monitor -->
      <section
        v-if="activeTab === 'batches'"
        class="tab-pane"
        aria-labelledby="batches-tab-heading"
      >
        <div class="pane-toolbar">
          <div class="title-sub">
            <h3>Central & Local Laboratory Feed Ingestion Log</h3>
            <p>
              Real-time transaction tracking across HL7 v2.x, CDISC LAB v1.0.1,
              CSV/sFTP, and Local Lab batch entry.
            </p>
          </div>
          <button class="btn btn-secondary" @click="fetchBatchStatuses">
            <span class="icon">🔄</span> Refresh Feeds
          </button>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Batch ID</th>
                <th>Format</th>
                <th>Status</th>
                <th>Total Processed</th>
                <th>Ingested</th>
                <th>Out of Range</th>
                <th>Grade 3/4 AEs Drafted</th>
                <th>Uncalibrated Safeguards</th>
                <th>Queries Raised</th>
                <th>Created At</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="b in batches" :key="b.batch_id">
                <td>
                  <code>{{ b.batch_id.slice(0, 8) }}...</code>
                </td>
                <td>
                  <span class="format-badge">{{ b.format.toUpperCase() }}</span>
                </td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      b.status === 'COMPLETED'
                        ? 'success'
                        : b.status === 'COMPLETED_WITH_ERRORS'
                          ? 'warning'
                          : 'danger',
                    ]"
                  >
                    {{ b.status }}
                  </span>
                </td>
                <td>{{ b.total_processed }}</td>
                <td>{{ b.ingested_count }}</td>
                <td>{{ b.out_of_range_count }}</td>
                <td>
                  <span class="count-badge danger">{{
                    b.staged_aes_count
                  }}</span>
                </td>
                <td>
                  <span class="count-badge warning">{{
                    b.uncalibrated_safeguards
                  }}</span>
                </td>
                <td>{{ b.queries_raised }}</td>
                <td>{{ formatDate(b.created_at) }}</td>
              </tr>
              <tr v-if="batches.length === 0">
                <td colspan="10" class="empty-state">
                  No laboratory feed batches processed yet.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 6: Longitudinal Trends & Acute Delta Checks -->
      <section
        v-if="activeTab === 'trends'"
        class="tab-pane"
        aria-labelledby="trends-tab-heading"
      >
        <div class="pane-toolbar">
          <div class="filter-group">
            <label for="trend-subject">Subject ID:</label>
            <select
              id="trend-subject"
              v-model="trendSubject"
              class="select-input"
            >
              <option value="SUBJ-101">
                SUBJ-101 (Cohort A - Hepatic Safety)
              </option>
              <option value="SUBJ-202">
                SUBJ-202 (Cohort A - Renal Function)
              </option>
              <option value="SUBJ-303">SUBJ-303 (Cohort B - Hematology)</option>
              <option value="SUBJ-TOX-99">
                SUBJ-TOX-99 (Severe Toxicities)
              </option>
            </select>
          </div>
          <div class="filter-group">
            <label for="trend-test">Analyte:</label>
            <select id="trend-test" v-model="trendTest" class="select-input">
              <option value="ALT">ALT - Alanine Aminotransferase (U/L)</option>
              <option value="AST">
                AST - Aspartate Aminotransferase (U/L)
              </option>
              <option value="CREAT">CREAT - Serum Creatinine (mg/dL)</option>
              <option value="POTASSIUM">
                POTASSIUM - Serum Potassium (mmol/L)
              </option>
              <option value="HGB">HGB - Hemoglobin (g/dL)</option>
              <option value="PLT">PLT - Platelet Count (10^9/L)</option>
            </select>
          </div>
          <div class="filter-group">
            <label for="trend-scale">Grading Scale:</label>
            <span class="badge badge-regulatory">NCI-CTCAE v5.0 Standard</span>
          </div>
        </div>

        <!-- KPI Metric Summary Cards -->
        <div class="trend-kpis">
          <div class="kpi-card">
            <span class="kpi-label">Baseline Value</span>
            <span class="kpi-value"
              >{{ trendSummary.baseline }} {{ trendSummary.unit }}</span
            >
            <span class="kpi-sub"
              >Ref: [{{ trendSummary.lln }} - {{ trendSummary.uln }}]</span
            >
          </div>
          <div class="kpi-card">
            <span class="kpi-label">Peak Value (Cmax)</span>
            <span class="kpi-value text-danger"
              >{{ trendSummary.peakValue }} {{ trendSummary.unit }}</span
            >
            <span class="kpi-sub">Visit: {{ trendSummary.peakVisit }}</span>
          </div>
          <div class="kpi-card">
            <span class="kpi-label">Peak Toxicity Grade</span>
            <span :class="['grade-badge', 'grade-' + trendSummary.peakGrade]">
              Grade {{ trendSummary.peakGrade }}
            </span>
            <span class="kpi-sub">{{ trendSummary.peakDescription }}</span>
          </div>
          <div class="kpi-card">
            <span class="kpi-label">Acute Delta Shift</span>
            <span
              :class="[
                'kpi-value',
                trendSummary.maxDeltaPct >= 50 ? 'text-danger' : 'text-primary',
              ]"
            >
              {{ trendSummary.maxDeltaPct > 0 ? "+" : ""
              }}{{ trendSummary.maxDeltaPct }}%
            </span>
            <span v-if="trendSummary.maxDeltaPct >= 50" class="safety-tag delta"
              >⚡ Acute Shift Alert</span
            >
            <span v-else class="kpi-sub">Within Normal Variance</span>
          </div>
        </div>

        <!-- Interactive SVG Longitudinal Trend Chart -->
        <div class="trend-chart-card">
          <div class="chart-header">
            <h4>Longitudinal Lab Trajectory & Reference Band Shading</h4>
            <div class="chart-legend">
              <span class="legend-item"
                ><span class="legend-box normal-band" /> Normal Limits [LLN -
                ULN]</span
              >
              <span class="legend-item"
                ><span class="legend-line uln-line" /> Upper Limit of Normal
                (ULN: {{ trendSummary.uln }})</span
              >
              <span class="legend-item"
                ><span class="legend-line delta-line" /> Acute Shift Trigger
                (+50%)</span
              >
            </div>
          </div>

          <div class="svg-container">
            <svg
              viewBox="0 0 800 280"
              class="trend-svg"
              role="img"
              aria-label="Longitudinal laboratory trend graph"
            >
              <!-- Background Grid & Reference Shading -->
              <rect
                x="60"
                y="20"
                width="710"
                height="210"
                fill="var(--semantic-color-text-inverse)"
                rx="4"
              />
              <!-- Normal Range Band -->
              <rect
                :x="60"
                :y="getYCoord(trendSummary.uln)"
                width="710"
                :height="
                  Math.max(
                    4,
                    getYCoord(trendSummary.lln) - getYCoord(trendSummary.uln)
                  )
                "
                fill="var(--semantic-color-success-bg-soft)"
                opacity="0.75"
              />

              <!-- ULN Boundary Line -->
              <line
                x1="60"
                :y1="getYCoord(trendSummary.uln)"
                x2="770"
                :y2="getYCoord(trendSummary.uln)"
                stroke="var(--semantic-color-success)"
                stroke-dasharray="4,4"
                stroke-width="1.5"
              />
              <text
                x="775"
                :y="getYCoord(trendSummary.uln) + 4"
                font-size="10"
                fill="var(--semantic-color-success)"
                font-weight="bold"
              >
                ULN
              </text>

              <!-- LLN Boundary Line (if > 0) -->
              <line
                v-if="trendSummary.lln > 0"
                x1="60"
                :y1="getYCoord(trendSummary.lln)"
                x2="770"
                :y2="getYCoord(trendSummary.lln)"
                stroke="var(--semantic-color-success)"
                stroke-dasharray="4,4"
                stroke-width="1.5"
              />
              <text
                v-if="trendSummary.lln > 0"
                x="775"
                :y="getYCoord(trendSummary.lln) + 4"
                font-size="10"
                fill="var(--semantic-color-success)"
                font-weight="bold"
              >
                LLN
              </text>

              <!-- Trend Polyline Path -->
              <polyline
                :points="trendPointsString"
                fill="none"
                stroke="var(--semantic-color-interactive-primary)"
                stroke-width="2.5"
              />

              <!-- Plotted Points & Tooltip Labels -->
              <g v-for="(pt, idx) in trendPlotPoints" :key="idx">
                <circle
                  :cx="pt.x"
                  :cy="pt.y"
                  :r="pt.grade >= 3 ? 7 : 5"
                  :fill="getPointColor(pt.grade)"
                  stroke="var(--semantic-color-surface)"
                  stroke-width="2"
                />
                <!-- Value label above point -->
                <text
                  :x="pt.x"
                  :y="pt.y - 10"
                  text-anchor="middle"
                  font-size="11"
                  font-weight="bold"
                  :fill="getPointColor(pt.grade)"
                >
                  {{ pt.value }}
                </text>
                <!-- Visit label on X-axis -->
                <text
                  :x="pt.x"
                  y="250"
                  text-anchor="middle"
                  font-size="10"
                  fill="var(--semantic-color-text-secondary)"
                  font-weight="600"
                >
                  {{ pt.visit }}
                </text>
                <text
                  :x="pt.x"
                  y="265"
                  text-anchor="middle"
                  font-size="9"
                  fill="var(--semantic-color-text-secondary)"
                >
                  {{ pt.date }}
                </text>
              </g>
            </svg>
          </div>
        </div>

        <!-- Longitudinal Lab Observations Table -->
        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Visit Event</th>
                <th>Collection Date</th>
                <th>Lab Source</th>
                <th>Observed Value</th>
                <th>Normalized SI Value</th>
                <th>Indicator</th>
                <th>CTCAE v5.0 Toxicity</th>
                <th>Baseline Delta</th>
                <th>Investigator Status</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="obs in activeTrendData"
                :key="obs.visit"
                :class="{ 'row-critical': obs.grade >= 3 }"
              >
                <td>
                  <strong>{{ obs.visit }}</strong>
                </td>
                <td>{{ obs.date }}</td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      obs.source === 'CENTRAL' ? 'primary' : 'secondary',
                    ]"
                  >
                    {{ obs.source }}
                  </span>
                </td>
                <td>
                  <strong class="value-highlight"
                    >{{ obs.value }} {{ trendSummary.unit }}</strong
                  >
                </td>
                <td>
                  <code
                    >{{ obs.normalizedValue }}
                    {{ trendSummary.normalizedUnit }}</code
                  >
                </td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      obs.indicator === 'NORMAL'
                        ? 'success'
                        : obs.indicator.includes('HIGH')
                          ? 'danger'
                          : 'warning',
                    ]"
                  >
                    {{ obs.indicator }}
                  </span>
                </td>
                <td>
                  <span :class="['grade-badge', 'grade-' + obs.grade]">
                    Grade {{ obs.grade }}
                  </span>
                  <span v-if="obs.grade >= 3" class="sub-text text-danger"
                    >Severe AE</span
                  >
                </td>
                <td>
                  <span
                    v-if="obs.deltaPct !== null"
                    :class="['delta-tag', obs.deltaPct >= 50 ? 'alert' : '']"
                  >
                    {{ obs.deltaPct > 0 ? "+" : "" }}{{ obs.deltaPct }}%
                  </span>
                  <span v-else class="text-muted">Baseline</span>
                </td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      getReviewStatusPillClass(obs.reviewStatus),
                    ]"
                  >
                    {{ formatReviewStatus(obs.reviewStatus) }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </main>

    <!-- MODAL: Ingest Laboratory Batch -->
    <ClinicalModal
      :is-open="showIngestModal"
      :teleport="false"
      size="lg"
      backdrop-class="modal-backdrop"
      dialog-class="modal-box modal-lg"
      header-class="modal-header"
      body-class="modal-body"
      footer-class="modal-footer"
      title="Ingest Laboratory Batch Feed"
      close-label="Close ingest laboratory batch dialog"
      :dismissible="!isIngesting"
      @close="showIngestModal = false"
    >
      <div class="form-group">
        <label for="ingest-format">Feed Ingestion Format:</label>
        <select
          id="ingest-format"
          v-model="ingestForm.format"
          class="select-input"
        >
          <option value="cdisc_lab">
            CDISC LAB Standard v1.0.1 (XML ODM / JSON)
          </option>
          <option value="csv">Delimited CSV / TSV / sFTP</option>
          <option value="hl7">HL7 v2.x (ORU^R01)</option>
          <option value="fhir">HL7 FHIR Observation JSON</option>
          <option value="local_manual">Manual Local Lab Batch Entry</option>
        </select>
      </div>

      <div class="form-row">
        <div class="form-group">
          <label for="ingest-study">Study Identifier:</label>
          <input
            id="ingest-study"
            v-model="ingestForm.study_id"
            type="text"
            class="text-input"
            placeholder="e.g. STUDY-101"
          />
        </div>
        <div class="form-group">
          <label for="ingest-site">Site Identifier (for Local Lab):</label>
          <input
            id="ingest-site"
            v-model="ingestForm.site_id"
            type="text"
            class="text-input"
            placeholder="e.g. SITE-001"
          />
        </div>
        <div class="form-group">
          <label for="ingest-source">Laboratory Source:</label>
          <select
            id="ingest-source"
            v-model="ingestForm.lab_source"
            class="select-input"
          >
            <option value="CENTRAL">CENTRAL</option>
            <option value="LOCAL">LOCAL</option>
          </select>
        </div>
      </div>

      <div class="form-group">
        <label for="ingest-payload"
          >Raw Feed Content / XML / JSON / Delimited Payload:</label
        >
        <textarea
          id="ingest-payload"
          v-model="ingestForm.payload"
          rows="8"
          class="textarea-input"
          placeholder="Paste raw HL7 v2.x, CDISC LAB XML ODM, FHIR Observation, or CSV text here..."
        />
      </div>

      <div class="form-group">
        <label for="ingest-reason">GxP Reason for Ingestion:</label>
        <input
          id="ingest-reason"
          v-model="ingestForm.reason_for_change"
          type="text"
          class="text-input"
        />
      </div>
      <template #footer>
        <button class="btn btn-secondary" @click="showIngestModal = false">
          Cancel
        </button>
        <button
          class="btn btn-primary"
          :disabled="isIngesting"
          @click="executeBatchIngest"
        >
          {{
            isIngesting
              ? "Processing Feed..."
              : "Execute Ingestion & Grade Toxicities"
          }}
        </button>
      </template>
    </ClinicalModal>

    <!-- MODAL: Investigator Sign-Off Action -->
    <ClinicalModal
      :is-open="Boolean(showActionModal && selectedReview)"
      :teleport="false"
      backdrop-class="modal-backdrop"
      dialog-class="modal-box"
      header-class="modal-header"
      body-class="modal-body"
      footer-class="modal-footer"
      :title="`Investigator Sign-off: Grade ${selectedReview?.ctcae_grade ?? ''} Toxicity`"
      close-label="Close investigator sign-off dialog"
      :dismissible="!isActioning"
      @close="showActionModal = false"
    >
      <template v-if="selectedReview">
        <div class="review-summary-card">
          <p>
            <strong>Subject:</strong> {{ selectedReview.subject_id }} |
            <strong>Analyte:</strong> {{ selectedReview.test_name }} ({{
              selectedReview.test_code
            }})
          </p>
          <p>
            <strong>Observed Measurement:</strong>
            {{ selectedReview.lab_value }} {{ selectedReview.unit }}
          </p>
          <p>
            <strong>Severity Description:</strong>
            {{ selectedReview.clinical_description }}
          </p>
        </div>

        <div class="form-group">
          <label for="action-select"
            >Investigator Clinical Determination:</label
          >
          <select
            id="action-select"
            v-model="actionForm.action"
            class="select-input"
          >
            <option value="CONFIRM">
              Confirm as Adverse Event (Promote to Formal AE eCRF)
            </option>
            <option value="DISMISS">
              Dismiss (Deemed Not Clinically Significant)
            </option>
            <option value="ESCALATE_SAE">
              Escalate to Serious Adverse Event (SAE)
            </option>
          </select>
        </div>

        <div class="form-group">
          <label for="action-notes">Clinical Commentary & Rationale:</label>
          <textarea
            id="action-notes"
            v-model="actionForm.investigator_notes"
            rows="3"
            class="textarea-input"
            placeholder="Provide medical commentary on laboratory abnormality..."
          />
        </div>

        <div class="form-group">
          <label for="action-reason"
            >21 CFR Part 11 Reason for Determination:</label
          >
          <input
            id="action-reason"
            v-model="actionForm.reason_for_change"
            type="text"
            class="text-input"
            placeholder="Mandatory Part 11 audit justification"
          />
        </div>
      </template>
      <template #footer>
        <button class="btn btn-secondary" @click="showActionModal = false">
          Cancel
        </button>
        <button
          class="btn btn-primary"
          :disabled="isActioning"
          @click="executeReviewAction"
        >
          {{
            isActioning
              ? "Submitting Signature..."
              : "Sign & Submit Determination"
          }}
        </button>
      </template>
    </ClinicalModal>

    <!-- MODAL: Bulk Laboratory Reference Range Import -->
    <ClinicalModal
      :is-open="showBulkImportModal"
      :teleport="false"
      size="lg"
      backdrop-class="modal-backdrop"
      dialog-class="modal-box modal-lg"
      header-class="modal-header"
      body-class="modal-body"
      footer-class="modal-footer"
      title="Bulk Laboratory Reference Range Import (CSV / XLSX)"
      close-label="Close bulk reference range import dialog"
      :dismissible="!isCommitting"
      @close="closeBulkImportModal"
    >
      <div class="form-row">
        <div class="form-group">
          <label for="bulk-study">Study Identifier:</label>
          <input
            id="bulk-study"
            v-model="bulkImportForm.study_id"
            type="text"
            class="text-input"
            placeholder="e.g. STUDY-101"
          />
        </div>
        <div class="form-group">
          <label for="bulk-site"
            >Site Identifier (optional for local lab):</label
          >
          <input
            id="bulk-site"
            v-model="bulkImportForm.site_id"
            type="text"
            class="text-input"
            placeholder="e.g. SITE-001"
          />
        </div>
        <div class="form-group">
          <label for="bulk-source">Laboratory Source:</label>
          <select
            id="bulk-source"
            v-model="bulkImportForm.lab_source"
            class="select-input"
          >
            <option value="CENTRAL">CENTRAL</option>
            <option value="LOCAL">LOCAL</option>
          </select>
        </div>
      </div>

      <div class="form-group">
        <label for="bulk-file">Reference Range Data File (CSV / XLSX):</label>
        <input
          id="bulk-file"
          type="file"
          accept=".csv,.xlsx,.tsv"
          class="file-input"
          @change="handleFileUpload"
        />
      </div>

      <div class="form-group">
        <label for="bulk-raw">Or Paste CSV/TSV Reference Range Content:</label>
        <textarea
          id="bulk-raw"
          v-model="bulkImportForm.raw_content"
          rows="5"
          class="textarea-input"
          placeholder="TestCode,TestName,Unit,LowBound,HighBound,CriticalLow,CriticalHigh,Sex,AgeLow,AgeHigh,Ethnicity,LoincCode&#10;ALT,Alanine Aminotransferase,U/L,7,56,,,ALL,,,ALL,1742-6&#10;CREAT,Serum Creatinine,mg/dL,0.6,1.2,0.3,3.0,M,18,65,ALL,2160-0"
        />
      </div>

      <div class="form-group">
        <label for="bulk-reason">GxP Reason for Change (21 CFR Part 11):</label>
        <input
          id="bulk-reason"
          v-model="bulkImportForm.reason_for_change"
          type="text"
          class="text-input"
          placeholder="Mandatory GxP audit justification..."
        />
      </div>

      <!-- Pre-flight Dry-Run Results Section -->
      <div v-if="dryRunResult" class="dry-run-results">
        <h4>Pre-Flight Validation Preview</h4>
        <div class="status-summary">
          <span
            :class="[
              'status-pill',
              dryRunResult.is_valid ? 'success' : 'danger',
            ]"
          >
            {{
              dryRunResult.is_valid
                ? "VALIDATED CLEANLY"
                : "VALIDATION ERRORS DETECTED"
            }}
          </span>
          <span>Total Rows: {{ dryRunResult.total_rows_parsed }}</span>
          <span class="text-success"
            >Valid Rows: {{ dryRunResult.valid_rows_count }}</span
          >
          <span class="text-danger"
            >Invalid Rows: {{ dryRunResult.invalid_rows_count }}</span
          >
        </div>

        <!-- Preview Rows Table -->
        <div
          v-if="dryRunResult.rows && dryRunResult.rows.length > 0"
          class="table-responsive preview-table"
        >
          <table class="data-table">
            <thead>
              <tr>
                <th>Row</th>
                <th>Code</th>
                <th>Name</th>
                <th>Unit</th>
                <th>Range [LLN - ULN]</th>
                <th>Sex</th>
                <th>LOINC Code</th>
                <th>Status</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="row in dryRunResult.rows"
                :key="row.row_number"
                :class="{ 'row-critical': !row.is_valid }"
              >
                <td>#{{ row.row_number }}</td>
                <td>
                  <strong>{{ row.test_code }}</strong>
                </td>
                <td>{{ row.test_name }}</td>
                <td>
                  <code>{{ row.unit }}</code>
                </td>
                <td>
                  {{ row.low_bound ?? "—" }} – {{ row.high_bound ?? "—" }}
                </td>
                <td>{{ row.sex_applicability || "ALL" }}</td>
                <td>
                  <code>{{ row.loinc_code || "Auto-mapped" }}</code>
                </td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      row.is_valid ? 'success' : 'danger',
                    ]"
                  >
                    {{ row.is_valid ? "Valid" : "Invalid" }}
                  </span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <!-- Validation Errors List -->
        <div
          v-if="
            dryRunResult.validation_errors &&
            dryRunResult.validation_errors.length > 0
          "
          class="error-summary"
        >
          <h5>Validation Error Details</h5>
          <ul>
            <li
              v-for="(err, idx) in dryRunResult.validation_errors"
              :key="idx"
              class="error-item"
            >
              <strong
                >Row {{ err.row_number }} (Column
                {{ err.column_name }}):</strong
              >
              {{ err.message }}
            </li>
          </ul>
        </div>
      </div>

      <template #footer>
        <button class="btn btn-secondary" @click="closeBulkImportModal">
          Cancel
        </button>
        <button
          class="btn btn-secondary"
          :disabled="isDryRunning"
          @click="executeDryRun"
        >
          {{ isDryRunning ? "Validating..." : "Pre-Flight Dry Run" }}
        </button>
        <button
          class="btn btn-primary"
          :disabled="isCommitting || !dryRunResult"
          @click="executeCommitImport"
        >
          {{ isCommitting ? "Importing Ranges..." : "Commit Bulk Import" }}
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { ClinicalModal } from "ui";
import { useConfirmDialog } from "../../composables/useConfirmDialog.js";
import { useErrorPresenter } from "../../composables/useErrorPresenter";
import { useToastStore } from "../../stores/toasts.js";

const toastStore = useToastStore();
const { confirmDialog, promptDialog } = useConfirmDialog();
const { presentError, sanitizeError } = useErrorPresenter();

// Active Tab State
const activeTab = ref("ranges");
const currentStudyId = ref("STUDY-101");

// Reference Ranges State
const ranges = ref([]);
const rangeSearch = ref("");
const rangeSourceFilter = ref("ALL");
const rangeSexFilter = ref("ALL");

// Staged Reviews State
const stagedReviews = ref([]);
const reviewStatusFilter = ref("STAGED_FOR_INVESTIGATOR_REVIEW");
const selectedReview = ref(null);
const showActionModal = ref(false);
const isActioning = ref(false);
const actionForm = ref({
  action: "CONFIRM",
  investigator_notes: "",
  reason_for_change: "Investigator review of laboratory toxicity trigger",
});

// Local Lab Certifications State
const certifications = ref([]);

// Ingestion State
const batches = ref([]);
const showIngestModal = ref(false);
const isIngesting = ref(false);
const ingestForm = ref({
  format: "cdisc_lab",
  study_id: "STUDY-101",
  site_id: "SITE-001",
  lab_source: "CENTRAL",
  payload: "",
  reason_for_change: "Batch laboratory data ingestion",
});

// Bulk Reference Range Import State
const showBulkImportModal = ref(false);
const isDryRunning = ref(false);
const isCommitting = ref(false);
const dryRunResult = ref(null);
const uploadedFile = ref(null);
const bulkImportForm = ref({
  study_id: "STUDY-101",
  site_id: "",
  lab_source: "CENTRAL",
  raw_content: "",
  reason_for_change: "Bulk laboratory reference range import",
});

// LOINC & Unit Conversion State
const loincCatalog = ref([]);
const isConverting = ref(false);
const conversionModel = ref({
  test_code: "CREAT",
  value: 2.5,
  from_unit: "mg/dL",
  to_unit: "umol/L",
});
const conversionResult = ref(null);

// Toxicity Simulation State
const simModel = ref({
  grading_scale: "CTCAE_V5",
  test_code: "ALT",
  value: 210,
  unit: "U/L",
  ref_high: 35,
  ref_low: 5,
  baseline_value: 30,
  bili_value: 2.8,
  alkp_value: 95,
});
const simResult = ref(null);

// Longitudinal Trends State & Sample Clinical Progression Datasets
const trendSubject = ref("SUBJ-101");
const trendTest = ref("ALT");

const trendDatabase = {
  "SUBJ-101": {
    ALT: {
      baseline: 28.0,
      lln: 0.0,
      uln: 35.0,
      unit: "U/L",
      normalizedUnit: "U/L",
      data: [
        {
          visit: "Screening",
          date: "2026-06-01",
          source: "CENTRAL",
          value: 26.0,
          normalizedValue: 26.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: null,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Baseline (D1)",
          date: "2026-06-15",
          source: "CENTRAL",
          value: 28.0,
          normalizedValue: 28.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 0.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 1 (D15)",
          date: "2026-06-30",
          source: "CENTRAL",
          value: 65.0,
          normalizedValue: 65.0,
          indicator: "HIGH",
          grade: 1,
          deltaPct: 132.1,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 2 (D29)",
          date: "2026-07-15",
          source: "CENTRAL",
          value: 145.0,
          normalizedValue: 145.0,
          indicator: "HIGH",
          grade: 2,
          deltaPct: 417.8,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 3 (D43)",
          date: "2026-07-29",
          source: "CENTRAL",
          value: 280.0,
          normalizedValue: 280.0,
          indicator: "HIGH HIGH",
          grade: 3,
          deltaPct: 900.0,
          reviewStatus: "STAGED_FOR_INVESTIGATOR_REVIEW",
        },
        {
          visit: "Cycle 4 (D57)",
          date: "2026-08-12",
          source: "CENTRAL",
          value: 110.0,
          normalizedValue: 110.0,
          indicator: "HIGH",
          grade: 1,
          deltaPct: 292.8,
          reviewStatus: "CONFIRMED_AS_AE",
        },
      ],
    },
    AST: {
      baseline: 24.0,
      lln: 0.0,
      uln: 35.0,
      unit: "U/L",
      normalizedUnit: "U/L",
      data: [
        {
          visit: "Screening",
          date: "2026-06-01",
          source: "CENTRAL",
          value: 22.0,
          normalizedValue: 22.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: null,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Baseline (D1)",
          date: "2026-06-15",
          source: "CENTRAL",
          value: 24.0,
          normalizedValue: 24.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 0.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 1 (D15)",
          date: "2026-06-30",
          source: "CENTRAL",
          value: 48.0,
          normalizedValue: 48.0,
          indicator: "HIGH",
          grade: 1,
          deltaPct: 100.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 2 (D29)",
          date: "2026-07-15",
          source: "CENTRAL",
          value: 115.0,
          normalizedValue: 115.0,
          indicator: "HIGH",
          grade: 2,
          deltaPct: 379.2,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 3 (D43)",
          date: "2026-07-29",
          source: "CENTRAL",
          value: 195.0,
          normalizedValue: 195.0,
          indicator: "HIGH HIGH",
          grade: 3,
          deltaPct: 712.5,
          reviewStatus: "STAGED_FOR_INVESTIGATOR_REVIEW",
        },
        {
          visit: "Cycle 4 (D57)",
          date: "2026-08-12",
          source: "CENTRAL",
          value: 85.0,
          normalizedValue: 85.0,
          indicator: "HIGH",
          grade: 1,
          deltaPct: 254.2,
          reviewStatus: "CONFIRMED_AS_AE",
        },
      ],
    },
    CREAT: {
      baseline: 0.9,
      lln: 0.6,
      uln: 1.2,
      unit: "mg/dL",
      normalizedUnit: "umol/L",
      data: [
        {
          visit: "Screening",
          date: "2026-06-01",
          source: "CENTRAL",
          value: 0.85,
          normalizedValue: 75.1,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: null,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Baseline (D1)",
          date: "2026-06-15",
          source: "CENTRAL",
          value: 0.9,
          normalizedValue: 79.6,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 0.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 1 (D15)",
          date: "2026-06-30",
          source: "CENTRAL",
          value: 1.1,
          normalizedValue: 97.2,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 22.2,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 2 (D29)",
          date: "2026-07-15",
          source: "LOCAL",
          value: 1.4,
          normalizedValue: 123.8,
          indicator: "HIGH",
          grade: 1,
          deltaPct: 55.6,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 3 (D43)",
          date: "2026-07-29",
          source: "CENTRAL",
          value: 1.9,
          normalizedValue: 168.0,
          indicator: "HIGH",
          grade: 2,
          deltaPct: 111.1,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 4 (D57)",
          date: "2026-08-12",
          source: "CENTRAL",
          value: 1.0,
          normalizedValue: 88.4,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 11.1,
          reviewStatus: "NOT_APPLICABLE",
        },
      ],
    },
    POTASSIUM: {
      baseline: 4.2,
      lln: 3.5,
      uln: 5.0,
      unit: "mmol/L",
      normalizedUnit: "mmol/L",
      data: [
        {
          visit: "Screening",
          date: "2026-06-01",
          source: "CENTRAL",
          value: 4.1,
          normalizedValue: 4.1,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: null,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Baseline (D1)",
          date: "2026-06-15",
          source: "CENTRAL",
          value: 4.2,
          normalizedValue: 4.2,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 0.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 1 (D15)",
          date: "2026-06-30",
          source: "CENTRAL",
          value: 4.5,
          normalizedValue: 4.5,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 7.1,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 2 (D29)",
          date: "2026-07-15",
          source: "CENTRAL",
          value: 5.4,
          normalizedValue: 5.4,
          indicator: "HIGH",
          grade: 1,
          deltaPct: 28.6,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 3 (D43)",
          date: "2026-07-29",
          source: "LOCAL",
          value: 6.2,
          normalizedValue: 6.2,
          indicator: "HIGH HIGH",
          grade: 3,
          deltaPct: 47.6,
          reviewStatus: "STAGED_FOR_INVESTIGATOR_REVIEW",
        },
        {
          visit: "Cycle 4 (D57)",
          date: "2026-08-12",
          source: "CENTRAL",
          value: 4.4,
          normalizedValue: 4.4,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 4.8,
          reviewStatus: "CONFIRMED_AS_AE",
        },
      ],
    },
    HGB: {
      baseline: 14.2,
      lln: 12.0,
      uln: 16.0,
      unit: "g/dL",
      normalizedUnit: "g/L",
      data: [
        {
          visit: "Screening",
          date: "2026-06-01",
          source: "CENTRAL",
          value: 14.5,
          normalizedValue: 145.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: null,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Baseline (D1)",
          date: "2026-06-15",
          source: "CENTRAL",
          value: 14.2,
          normalizedValue: 142.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 0.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 1 (D15)",
          date: "2026-06-30",
          source: "CENTRAL",
          value: 11.2,
          normalizedValue: 112.0,
          indicator: "LOW",
          grade: 1,
          deltaPct: -21.1,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 2 (D29)",
          date: "2026-07-15",
          source: "CENTRAL",
          value: 9.4,
          normalizedValue: 94.0,
          indicator: "LOW",
          grade: 2,
          deltaPct: -33.8,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 3 (D43)",
          date: "2026-07-29",
          source: "CENTRAL",
          value: 7.6,
          normalizedValue: 76.0,
          indicator: "LOW LOW",
          grade: 3,
          deltaPct: -46.5,
          reviewStatus: "STAGED_FOR_INVESTIGATOR_REVIEW",
        },
        {
          visit: "Cycle 4 (D57)",
          date: "2026-08-12",
          source: "CENTRAL",
          value: 11.5,
          normalizedValue: 115.0,
          indicator: "LOW",
          grade: 1,
          deltaPct: -19.0,
          reviewStatus: "CONFIRMED_AS_AE",
        },
      ],
    },
    PLT: {
      baseline: 240.0,
      lln: 150.0,
      uln: 450.0,
      unit: "10^9/L",
      normalizedUnit: "10^9/L",
      data: [
        {
          visit: "Screening",
          date: "2026-06-01",
          source: "CENTRAL",
          value: 250.0,
          normalizedValue: 250.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: null,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Baseline (D1)",
          date: "2026-06-15",
          source: "CENTRAL",
          value: 240.0,
          normalizedValue: 240.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: 0.0,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 1 (D15)",
          date: "2026-06-30",
          source: "CENTRAL",
          value: 130.0,
          normalizedValue: 130.0,
          indicator: "LOW",
          grade: 1,
          deltaPct: -45.8,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 2 (D29)",
          date: "2026-07-15",
          source: "CENTRAL",
          value: 85.0,
          normalizedValue: 85.0,
          indicator: "LOW",
          grade: 2,
          deltaPct: -64.6,
          reviewStatus: "NOT_APPLICABLE",
        },
        {
          visit: "Cycle 3 (D43)",
          date: "2026-07-29",
          source: "CENTRAL",
          value: 42.0,
          normalizedValue: 42.0,
          indicator: "LOW LOW",
          grade: 3,
          deltaPct: -82.5,
          reviewStatus: "STAGED_FOR_INVESTIGATOR_REVIEW",
        },
        {
          visit: "Cycle 4 (D57)",
          date: "2026-08-12",
          source: "CENTRAL",
          value: 165.0,
          normalizedValue: 165.0,
          indicator: "NORMAL",
          grade: 0,
          deltaPct: -31.3,
          reviewStatus: "CONFIRMED_AS_AE",
        },
      ],
    },
  },
};

const activeTrendData = computed(() => {
  const subj = trendSubject.value;
  const tst = trendTest.value;
  if (trendDatabase[subj] && trendDatabase[subj][tst]) {
    return trendDatabase[subj][tst].data;
  }
  return trendDatabase["SUBJ-101"]["ALT"].data;
});

const trendSummary = computed(() => {
  const subj = trendSubject.value;
  const tst = trendTest.value;
  const dataset =
    trendDatabase[subj] && trendDatabase[subj][tst]
      ? trendDatabase[subj][tst]
      : trendDatabase["SUBJ-101"]["ALT"];
  const data = dataset.data;
  const baseline = dataset.baseline;
  const uln = dataset.uln;
  const lln = dataset.lln;
  const unit = dataset.unit;
  const normalizedUnit = dataset.normalizedUnit;

  let peakVal = data[0].value;
  let peakVisit = data[0].visit;
  let peakGrade = data[0].grade;
  let maxDeltaPct = 0;

  for (const obs of data) {
    if (obs.value > peakVal) {
      peakVal = obs.value;
      peakVisit = obs.visit;
    }
    if (obs.grade > peakGrade) {
      peakGrade = obs.grade;
    }
    if (obs.deltaPct && Math.abs(obs.deltaPct) > Math.abs(maxDeltaPct)) {
      maxDeltaPct = obs.deltaPct;
    }
  }

  const descriptions = {
    0: "Within Normal Limits",
    1: "Mild Abnormality (> ULN - 3.0x ULN)",
    2: "Moderate Toxicity (> 3.0 - 5.0x ULN)",
    3: "Severe Adverse Event Trigger (> 5.0 - 20.0x ULN)",
    4: "Life-Threatening (> 20.0x ULN)",
  };

  return {
    baseline,
    lln,
    uln,
    unit,
    normalizedUnit,
    peakValue: peakVal,
    peakVisit,
    peakGrade,
    peakDescription: descriptions[peakGrade] || "CTCAE Evaluation",
    maxDeltaPct,
  };
});

function getYCoord(val) {
  const data = activeTrendData.value;
  const maxVal = Math.max(
    ...data.map((d) => d.value),
    trendSummary.value.uln * 1.25,
    1
  );
  const minVal = 0;
  const chartHeight = 180;
  const topMargin = 30;
  const normalized = (val - minVal) / (maxVal - minVal);
  return topMargin + chartHeight * (1 - normalized);
}

const trendPlotPoints = computed(() => {
  const data = activeTrendData.value;
  const width = 680;
  const leftMargin = 80;
  const step = data.length > 1 ? width / (data.length - 1) : width;
  return data.map((d, i) => ({
    x: leftMargin + i * step,
    y: getYCoord(d.value),
    value: d.value,
    visit: d.visit,
    date: d.date,
    grade: d.grade,
  }));
});

const trendPointsString = computed(() => {
  return trendPlotPoints.value.map((p) => `${p.x},${p.y}`).join(" ");
});

function getPointColor(grade) {
  if (grade >= 4) return "var(--semantic-color-error-text-dark)";
  if (grade === 3) return "var(--semantic-color-error)";
  if (grade === 2) return "var(--semantic-color-warning-action)";
  if (grade === 1) return "var(--semantic-color-primary)";
  return "var(--semantic-color-success)";
}

// Computed Filters
const filteredRanges = computed(() => {
  return ranges.value.filter((r) => {
    const matchesSearch =
      !rangeSearch.value ||
      r.test_code.toLowerCase().includes(rangeSearch.value.toLowerCase()) ||
      (r.test_name &&
        r.test_name.toLowerCase().includes(rangeSearch.value.toLowerCase()));

    const matchesSource =
      rangeSourceFilter.value === "ALL" || r.source === rangeSourceFilter.value;

    const matchesSex =
      rangeSexFilter.value === "ALL" ||
      r.sex_applicability === rangeSexFilter.value ||
      r.sex_applicability === "ALL";

    return matchesSearch && matchesSource && matchesSex;
  });
});

// Data Fetching Methods
async function fetchRanges() {
  try {
    const res = await fetch("/api/v1/execution/lab-ranges");
    if (res.ok) {
      ranges.value = await res.json();
    }
  } catch (err) {
    console.error("Failed to load lab reference ranges", err);
  }
}

async function fetchStagedReviews() {
  try {
    const url = `/api/v1/execution/labs/toxicity/review-queue?review_status=${reviewStatusFilter.value}`;
    const res = await fetch(url);
    if (res.ok) {
      stagedReviews.value = await res.json();
    }
  } catch (err) {
    console.error("Failed to load staged reviews", err);
  }
}

async function fetchCertifications() {
  try {
    const res = await fetch("/api/v1/execution/labs/certifications");
    if (res.ok) {
      certifications.value = await res.json();
    }
  } catch (err) {
    console.error("Failed to load local lab certifications", err);
  }
}

async function fetchBatchStatuses() {
  try {
    const res = await fetch("/api/v1/execution/labs/batch-status");
    if (res.ok) {
      const data = await res.json();
      batches.value = Array.isArray(data) ? data : [data];
    }
  } catch (err) {
    console.error("Failed to load batch statuses", err);
  }
}

async function fetchLoincCatalog() {
  try {
    const res = await fetch("/api/v1/execution/labs/loinc-catalog");
    if (res.ok) {
      const data = await res.json();
      loincCatalog.value = data.items || [];
    }
  } catch (err) {
    console.error("Failed to load LOINC catalog", err);
  }
}

// Actions
function openBulkImportModal() {
  dryRunResult.value = null;
  uploadedFile.value = null;
  bulkImportForm.value.study_id = currentStudyId.value || "STUDY-101";
  showBulkImportModal.value = true;
}

function closeBulkImportModal() {
  showBulkImportModal.value = false;
  dryRunResult.value = null;
  uploadedFile.value = null;
}

function handleFileUpload(event) {
  const file = event.target.files[0];
  if (file) {
    uploadedFile.value = file;
  }
}

async function executeDryRun() {
  isDryRunning.value = true;
  try {
    let res;
    if (uploadedFile.value) {
      const formData = new FormData();
      formData.append("file", uploadedFile.value);
      formData.append("study_id", bulkImportForm.value.study_id);
      if (bulkImportForm.value.site_id) {
        formData.append("site_id", bulkImportForm.value.site_id);
      }
      formData.append("lab_source", bulkImportForm.value.lab_source);

      res = await fetch("/api/v1/execution/labs/import/dry-run", {
        method: "POST",
        body: formData,
      });
    } else {
      let rawContent = bulkImportForm.value.raw_content;
      if (!rawContent) {
        toastStore.push({
          tone: "warning",
          title: "Missing Import Input",
          message: "Please select a file or paste CSV/TSV content to dry run.",
        });
        isDryRunning.value = false;
        return;
      }

      res = await fetch("/api/v1/execution/labs/import/dry-run", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({
          study_id: bulkImportForm.value.study_id,
          site_id: bulkImportForm.value.site_id || null,
          lab_source: bulkImportForm.value.lab_source,
          file_name: "import.csv",
          raw_file_content: rawContent,
        }),
      });
    }

    if (res.ok) {
      dryRunResult.value = await res.json();
    } else {
      const err = await res.json();
      toastStore.push({
        tone: "error",
        title: "Dry Run Validation Error",
        message: sanitizeError(err?.detail || "", "Validation failed"),
      });
    }
  } catch (err) {
    toastStore.push({
      tone: "error",
      title: "Dry Run Request Failed",
      message: presentError(err, "Dry run request failed."),
    });
  } finally {
    isDryRunning.value = false;
  }
}

async function executeCommitImport() {
  if (!bulkImportForm.value.reason_for_change) {
    toastStore.push({
      tone: "warning",
      title: "Reason for Change Required",
      message: "GxP Reason for Change is required for Part 11 compliance.",
    });
    return;
  }

  isCommitting.value = true;
  try {
    let res;
    const changeReason = bulkImportForm.value.reason_for_change;

    if (uploadedFile.value) {
      const formData = new FormData();
      formData.append("file", uploadedFile.value);
      formData.append("study_id", bulkImportForm.value.study_id);
      if (bulkImportForm.value.site_id) {
        formData.append("site_id", bulkImportForm.value.site_id);
      }
      formData.append("lab_source", bulkImportForm.value.lab_source);
      formData.append("reason_for_change", changeReason);
      if (dryRunResult.value && dryRunResult.value.dry_run_id) {
        formData.append("dry_run_id", dryRunResult.value.dry_run_id);
      }

      res = await fetch("/api/v1/execution/labs/import/commit", {
        method: "POST",
        headers: {
          "X-Change-Reason": changeReason,
        },
        body: formData,
      });
    } else {
      res = await fetch("/api/v1/execution/labs/import/commit", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Change-Reason": changeReason,
        },
        body: JSON.stringify({
          dry_run_id: dryRunResult.value?.dry_run_id || null,
          study_id: bulkImportForm.value.study_id,
          site_id: bulkImportForm.value.site_id || null,
          lab_source: bulkImportForm.value.lab_source,
          file_name: "import.csv",
          raw_file_content: bulkImportForm.value.raw_content,
          reason_for_change: changeReason,
        }),
      });
    }

    if (res.ok) {
      const result = await res.json();
      toastStore.push({
        tone: "success",
        title: "Bulk Reference Range Import Committed",
        message: `Imported: ${result.imported_count}, Total Rows: ${result.total_rows}, Status: ${result.status}`,
      });
      closeBulkImportModal();
      await fetchRanges();
      await fetchBatchStatuses();
    } else {
      const err = await res.json();
      toastStore.push({
        tone: "error",
        title: "Commit Import Error",
        message: sanitizeError(err?.detail || "", "Import failed"),
      });
    }
  } catch (err) {
    toastStore.push({
      tone: "error",
      title: "Commit Import Request Failed",
      message: presentError(err, "Commit import request failed."),
    });
  } finally {
    isCommitting.value = false;
  }
}

async function deactivateRange(range) {
  const confirmed = await confirmDialog({
    title: "Deactivate Reference Range",
    message: `Are you sure you want to deactivate reference range for ${range.test_code}?`,
    confirmLabel: "Continue",
    cancelLabel: "Cancel",
    variant: "danger",
  });
  if (!confirmed) return;

  const reason = await promptDialog({
    title: "21 CFR Part 11 Deactivation Reason",
    message: `Provide a 21 CFR Part 11 reason for deactivating ${range.test_code}.`,
    inputLabel: "Enter 21 CFR Part 11 Reason for Deactivation",
    placeholder: "Enter justification for reference range retirement",
    confirmLabel: "Deactivate Range",
    cancelLabel: "Cancel",
    variant: "danger",
  });
  if (!reason) return;

  try {
    const res = await fetch(`/api/v1/execution/lab-ranges/${range.id}`, {
      method: "DELETE",
      headers: {
        "X-Change-Reason": reason,
      },
    });
    if (res.ok) {
      toastStore.push({
        tone: "success",
        title: "Reference Range Deactivated",
        message: `Reference range for ${range.test_code} deactivated.`,
      });
      await fetchRanges();
    } else {
      const err = await res.json();
      toastStore.push({
        tone: "error",
        title: "Deactivation Failed",
        message: sanitizeError(err?.detail || "", "Error"),
      });
    }
  } catch (err) {
    toastStore.push({
      tone: "error",
      title: "Deactivation Request Failed",
      message: presentError(err, "Deactivation request failed."),
    });
  }
}

function editRange(range) {
  toastStore.push({
    tone: "info",
    title: `Edit Range ${range.test_code}`,
    message: `Editing range ${range.test_code}. Use Bulk Import or New Reference Range for updates.`,
  });
}

function openIngestModal() {
  showIngestModal.value = true;
}

async function openCreateRangeModal() {
  const testCode = await promptDialog({
    title: "Register Reference Range",
    message: "Specify the laboratory test code to configure reference bounds.",
    inputLabel: "Enter Test Code (e.g. ALT, CREAT, WBC)",
    placeholder: "ALT",
    confirmLabel: "Continue",
    cancelLabel: "Cancel",
    variant: "info",
  });
  if (testCode) {
    toastStore.push({
      tone: "info",
      title: `Register Range: ${testCode}`,
      message: `To register new range for ${testCode}, use the Reference Range Authoring Dialog.`,
    });
  }
}

async function openCreateCertModal() {
  const siteId = await promptDialog({
    title: "Register Local Laboratory",
    message:
      "Specify the clinical site identifier for local laboratory certification.",
    inputLabel: "Enter Site ID (e.g. SITE-001)",
    placeholder: "SITE-001",
    confirmLabel: "Register Lab",
    cancelLabel: "Cancel",
    variant: "info",
  });
  if (siteId) {
    toastStore.push({
      tone: "info",
      title: "Local Laboratory Registration",
      message: `Registering Local Lab for Site ${siteId}.`,
    });
  }
}

function openActionModal(review) {
  selectedReview.value = review;
  actionForm.value.investigator_notes = "";
  showActionModal.value = true;
}

async function executeBatchIngest() {
  isIngesting.value = true;
  try {
    const res = await fetch("/api/v1/execution/labs/ingest", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(ingestForm.value),
    });
    if (res.ok) {
      const result = await res.json();
      toastStore.push({
        tone: "success",
        title: "Batch Ingest Completed",
        message: `Ingested: ${result.ingested_count}, Staged AEs: ${result.staged_aes_count}, Queries: ${result.queries_raised}`,
      });
      showIngestModal.value = false;
      await fetchStagedReviews();
      await fetchBatchStatuses();
    } else {
      const err = await res.json();
      toastStore.push({
        tone: "error",
        title: "Batch Ingest Error",
        message: sanitizeError(err?.detail || "", "Ingestion failed"),
      });
    }
  } catch (err) {
    toastStore.push({
      tone: "error",
      title: "Ingest Request Failed",
      message: presentError(err, "Ingest request failed."),
    });
  } finally {
    isIngesting.value = false;
  }
}

async function executeReviewAction() {
  if (!selectedReview.value) return;
  isActioning.value = true;
  try {
    const url = `/api/v1/execution/labs/toxicity/review/${selectedReview.value.id}/action`;
    const res = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(actionForm.value),
    });
    if (res.ok) {
      toastStore.push({
        tone: "success",
        title: "Investigator Action Recorded",
        message:
          "Investigator action successfully recorded with 21 CFR Part 11 audit trail.",
      });
      showActionModal.value = false;
      await fetchStagedReviews();
    } else {
      const err = await res.json();
      toastStore.push({
        tone: "error",
        title: "Action Error",
        message: sanitizeError(
          err?.detail || "",
          "Failed to submit determination"
        ),
      });
    }
  } catch (err) {
    toastStore.push({
      tone: "error",
      title: "Action Request Failed",
      message: presentError(err, "Action request failed."),
    });
  } finally {
    isActioning.value = false;
  }
}

async function runUnitConversion() {
  isConverting.value = true;
  try {
    const res = await fetch("/api/v1/execution/labs/convert-unit", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(conversionModel.value),
    });
    if (res.ok) {
      conversionResult.value = await res.json();
    }
  } catch (err) {
    console.error("Unit conversion failed", err);
  } finally {
    isConverting.value = false;
  }
}

function onConversionTestChange() {
  const code = conversionModel.value.test_code;
  if (code === "CREAT") {
    conversionModel.value.from_unit = "mg/dL";
    conversionModel.value.to_unit = "umol/L";
  } else if (code === "BILI") {
    conversionModel.value.from_unit = "mg/dL";
    conversionModel.value.to_unit = "umol/L";
  } else if (code === "GLUCOSE") {
    conversionModel.value.from_unit = "mg/dL";
    conversionModel.value.to_unit = "mmol/L";
  } else if (code === "HEMOGLOBIN") {
    conversionModel.value.from_unit = "g/dL";
    conversionModel.value.to_unit = "g/L";
  } else if (code === "PLATELETS" || code === "ANC") {
    conversionModel.value.from_unit = "10^3/uL";
    conversionModel.value.to_unit = "10^9/L";
  }
}

async function runToxicitySimulation() {
  try {
    const res = await fetch("/api/v1/execution/labs/toxicity/evaluate", {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify(simModel.value),
    });
    if (res.ok) {
      simResult.value = await res.json();
    }
  } catch (err) {
    console.error("Toxicity simulation failed", err);
  }
}

// Helpers
function formatAgeRange(low, high) {
  if (low !== null && high !== null) return `${low} - ${high} yrs`;
  if (low !== null) return `&ge; ${low} yrs`;
  if (high !== null) return `&le; ${high} yrs`;
  return "All Ages";
}

function formatDate(dtStr) {
  if (!dtStr) return "—";
  return new Date(dtStr).toLocaleDateString();
}

function isCertExpired(expStr) {
  if (!expStr) return true;
  return new Date(expStr) < new Date();
}

function formatReviewStatus(status) {
  if (status === "STAGED_FOR_INVESTIGATOR_REVIEW") return "Staged for Review";
  if (status === "CONFIRMED_AS_AE") return "Confirmed AE";
  if (status === "DISMISSED_NOT_CLINICALLY_SIGNIFICANT") return "Dismissed";
  if (status === "ESCALATED_SAE") return "Escalated SAE";
  return status;
}

function getReviewStatusPillClass(status) {
  if (status === "STAGED_FOR_INVESTIGATOR_REVIEW") return "danger";
  if (status === "CONFIRMED_AS_AE") return "success";
  if (status === "DISMISSED_NOT_CLINICALLY_SIGNIFICANT") return "secondary";
  if (status === "ESCALATED_SAE") return "primary";
  return "info";
}

onMounted(() => {
  fetchRanges();
  fetchStagedReviews();
  fetchCertifications();
  fetchBatchStatuses();
  fetchLoincCatalog();
  runUnitConversion();
  runToxicitySimulation();
});
</script>

<style scoped>
.lab-governance-console {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1.5rem;
  background-color: var(--semantic-color-surface-muted);
  min-height: 100vh;
  min-height: 100dvh;
  font-family:
    -apple-system, BlinkMacSystemFont, "Segoe UI", Roboto, Helvetica, Arial,
    sans-serif;
  color: var(--semantic-color-text);
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--semantic-color-surface);
  padding: 1.25rem 1.5rem;
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.title-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.badge-regulatory {
  font-size: 0.7rem;
  text-transform: uppercase;
  font-weight: 700;
  color: var(--semantic-color-primary);
  background-color: var(--semantic-color-primary-light);
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
  width: fit-content;
}

.console-title {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.header-metadata {
  display: flex;
  gap: 1rem;
  margin-top: 0.5rem;
  font-size: 0.85rem;
}

.header-actions {
  display: flex;
  gap: 0.75rem;
}

.nav-tabs {
  display: flex;
  gap: 0.5rem;
  border-bottom: 2px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
  padding: 0.5rem 1rem 0;
  border-radius: 8px 8px 0 0;
}

.tab-button {
  padding: 0.75rem 1.25rem;
  border: none;
  background: transparent;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
}

.tab-button.active {
  color: var(--semantic-color-interactive-primary);
  border-bottom-color: var(--semantic-color-interactive-primary);
}

.tab-pane {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 0 0 8px 8px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.pane-toolbar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
  flex-wrap: wrap;
}

.alert-toolbar {
  flex-direction: column;
  align-items: stretch;
}

.alert-info-box {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  padding: 0.75rem 1rem;
  border-radius: 6px;
  color: var(--semantic-color-error-text-dark);
  font-size: 0.85rem;
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
}

.text-input,
.select-input,
.textarea-input {
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 0.85rem;
  background-color: var(--semantic-color-surface);
}

.textarea-input {
  font-family: monospace;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  text-align: left;
}

.data-table th {
  background-color: var(--semantic-color-surface-header);
  padding: 0.75rem;
  font-weight: 600;
  border-bottom: 2px solid var(--semantic-color-border-control);
}

.data-table td {
  padding: 0.75rem;
  border-bottom: 1px solid var(--semantic-color-border);
  vertical-align: middle;
}

.row-critical {
  background-color: var(--semantic-color-error-bg-row);
}

.code-tag {
  background: var(--semantic-color-surface-header);
  padding: 0.2rem 0.4rem;
  border-radius: 4px;
}

.demographic-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.mini-tag {
  background: var(--semantic-color-surface-header);
  font-size: 0.75rem;
  padding: 0.15rem 0.35rem;
  border-radius: 3px;
}

.status-pill {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.25rem 0.5rem;
  border-radius: 9999px;
}

.status-pill.primary {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}
.status-pill.secondary {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}
.status-pill.success {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}
.status-pill.warning {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}
.status-pill.danger {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}
.status-pill.info {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.grade-badge {
  padding: 0.25rem 0.5rem;
  font-weight: 700;
  border-radius: 4px;
  font-size: 0.75rem;
}

.grade-badge.grade-0 {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}
.grade-badge.grade-1 {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}
.grade-badge.grade-2 {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}
.grade-badge.grade-3 {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  font-weight: 800;
}
.grade-badge.grade-4 {
  background: var(--semantic-color-error-text-dark);
  color: var(--semantic-color-surface);
  font-weight: 900;
}

.safety-tag {
  font-size: 0.75rem;
  padding: 0.2rem 0.4rem;
  border-radius: 4px;
  font-weight: 700;
}

.safety-tag.hys-law {
  background: var(--semantic-color-error-text-deep);
  color: var(--semantic-color-error-bg-soft);
}
.safety-tag.delta {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.btn {
  padding: 0.5rem 0.875rem;
  font-weight: 600;
  font-size: 0.85rem;
  border-radius: 4px;
  cursor: pointer;
  border: 1px solid transparent;
}

.btn-sm {
  padding: 0.25rem 0.5rem;
  font-size: 0.75rem;
}
.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}
.btn-secondary {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-dark);
}
.btn-outline {
  background: transparent;
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.converter-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
}

.converter-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.converter-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(100px, 1fr));
  gap: 0.75rem;
}

.conversion-output {
  margin-top: 0.75rem;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 0.75rem;
}

.output-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 0.25rem;
  font-size: 0.85rem;
}

.modal-box {
  background: var(--semantic-color-surface);
  border-radius: 8px;
  width: 540px;
  max-width: 90vw;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
}

.modal-lg {
  width: 750px;
}

@media (max-width: 480px) {
  .modal-box,
  .modal-lg {
    width: 100%;
    max-width: 100%;
  }
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  border-bottom: 1px solid var(--semantic-color-border);
}

.modal-body {
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.5rem;
  padding: 1rem;
  border-top: 1px solid var(--semantic-color-border);
}

.empty-state {
  text-align: center;
  color: var(--semantic-color-text-secondary);
  padding: 2rem;
}

/* Longitudinal Trends & Delta Checks Styles */
.trend-kpis {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
}

.kpi-card {
  background: var(--neutral-light);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
}

.kpi-label {
  font-size: 0.75rem;
  text-transform: uppercase;
  font-weight: 700;
  color: var(--semantic-color-text-muted);
  letter-spacing: 0.05em;
}

.kpi-value {
  font-size: 1.35rem;
  font-weight: 800;
  color: var(--primary);
}

.kpi-sub {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.trend-chart-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 1.25rem;
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.chart-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 0.5rem;
}

.chart-header h4 {
  margin: 0;
  font-size: 1rem;
  font-weight: 700;
  color: var(--primary);
}

.chart-legend {
  display: flex;
  gap: 1rem;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  flex-wrap: wrap;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.legend-box.normal-band {
  width: 14px;
  height: 14px;
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success);
  border-radius: 2px;
}

.legend-line.uln-line {
  width: 16px;
  height: 2px;
  background: var(--semantic-color-success);
  border-top: 2px dashed var(--semantic-color-success);
}

.legend-line.delta-line {
  width: 16px;
  height: 2px;
  background: var(--semantic-color-warning-action);
}

.svg-container {
  width: 100%;
  overflow-x: auto;
}

.trend-svg {
  width: 100%;
  min-width: 650px;
  height: auto;
}

.delta-tag {
  font-size: 0.75rem;
  padding: 0.2rem 0.45rem;
  border-radius: 4px;
  font-weight: 700;
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.delta-tag.alert {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
  border: 1px solid var(--semantic-color-warning-border);
}

.text-danger {
  color: var(--semantic-color-error) !important;
}

.text-primary {
  color: var(--semantic-color-interactive-primary) !important;
}

.dry-run-results {
  margin-top: 1rem;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.status-summary {
  display: flex;
  align-items: center;
  gap: 1rem;
  font-size: 0.85rem;
  font-weight: 600;
}

.preview-table {
  max-height: 250px;
  overflow-y: auto;
}

.error-summary {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 6px;
  padding: 0.75rem 1rem;
  color: var(--semantic-color-error-text-dark);
  font-size: 0.85rem;
}

.error-summary h5 {
  margin: 0 0 0.5rem 0;
  font-size: 0.9rem;
  font-weight: 700;
}

.error-summary ul {
  margin: 0;
  padding-left: 1.25rem;
}

.error-item {
  margin-bottom: 0.25rem;
}

.btn-outline-danger {
  background: transparent;
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error);
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-size: 0.75rem;
  cursor: pointer;
}

.btn-outline-danger:hover {
  background: var(--semantic-color-error-bg-soft);
}

.file-input {
  display: block;
  width: 100%;
  padding: 0.4rem;
  font-size: 0.85rem;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
}
</style>
