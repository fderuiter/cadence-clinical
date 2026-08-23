<template>
  <div class="lab-governance-console" role="region" aria-label="Laboratory Normal Range Governance & Toxicity Grading Console">
    <!-- Header Bar -->
    <header class="console-header">
      <div class="header-main">
        <div class="title-group">
          <span class="badge badge-regulatory">CDISC LAB v1.0.1 / NCI-CTCAE v5.0 / DAIDS / CLIA</span>
          <h2 class="console-title">Lab Reference Range Governance & Toxicity Grading Console</h2>
        </div>
        <div class="header-metadata">
          <span class="meta-item"><strong>Study:</strong> {{ currentStudyId || 'STUDY-101' }}</span>
          <span class="meta-item"><strong>Active Ranges:</strong> <span class="status-pill info">{{ ranges.length }}</span></span>
          <span class="meta-item"><strong>Staged AE Triggers:</strong> <span class="status-pill danger">{{ stagedReviews.length }}</span></span>
          <span class="meta-item"><strong>Local Certifications:</strong> <span class="status-pill success">{{ certifications.length }}</span></span>
        </div>
      </div>

      <div class="header-actions">
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
    </nav>

    <!-- Main Content Panes -->
    <main class="main-content">
      <!-- TAB 1: Normal Reference Ranges Governance -->
      <section v-if="activeTab === 'ranges'" class="tab-pane" aria-labelledby="ranges-tab-heading">
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
            <select id="range-source-filter" v-model="rangeSourceFilter" class="select-input">
              <option value="ALL">All Sources (Central & Local)</option>
              <option value="CENTRAL">Central Lab Only</option>
              <option value="LOCAL">Local Lab Only</option>
            </select>
          </div>
          <div class="filter-group">
            <label for="range-sex-filter">Sex Applicability:</label>
            <select id="range-sex-filter" v-model="rangeSexFilter" class="select-input">
              <option value="ALL">All Demographics</option>
              <option value="M">Male (M)</option>
              <option value="F">Female (F)</option>
            </select>
          </div>
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
                <td><strong class="code-tag">{{ r.test_code }}</strong></td>
                <td>{{ r.test_name }}</td>
                <td>
                  <span :class="['status-pill', r.source === 'CENTRAL' ? 'primary' : 'secondary']">
                    {{ r.source }}
                  </span>
                </td>
                <td>{{ r.site_id || 'All Sites (Global)' }}</td>
                <td>
                  <div class="demographic-tags">
                    <span class="mini-tag">Sex: {{ r.sex_applicability || 'ALL' }}</span>
                    <span class="mini-tag">Age: {{ formatAgeRange(r.age_low, r.age_high) }}</span>
                    <span v-if="r.ethnicity_applicability" class="mini-tag">Eth: {{ r.ethnicity_applicability }}</span>
                  </div>
                </td>
                <td><code>{{ r.normalized_unit || r.unit }}</code></td>
                <td>
                  <span class="range-span">{{ r.low_bound ?? '—' }} – {{ r.high_bound ?? '—' }}</span>
                </td>
                <td>
                  <span v-if="r.critical_low !== null || r.critical_high !== null" class="critical-span">
                    &lt;{{ r.critical_low ?? '—' }} | &gt;{{ r.critical_high ?? '—' }}
                  </span>
                  <span v-else class="text-muted">None</span>
                </td>
                <td>v{{ r.version || 1 }}</td>
                <td>
                  <button class="btn btn-sm btn-outline" @click="editRange(r)">Edit</button>
                </td>
              </tr>
              <tr v-if="filteredRanges.length === 0">
                <td colspan="10" class="empty-state">No laboratory reference ranges match the active filter criteria.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 2: Investigator Grade 3/4 Adverse Event Review Queue -->
      <section v-if="activeTab === 'toxicity_queue'" class="tab-pane" aria-labelledby="toxicity-queue-heading">
        <div class="pane-toolbar alert-toolbar">
          <div class="alert-info-box">
            <span class="icon-warning">⚠️</span>
            <div>
              <strong>Investigator Safety Trigger Workflow:</strong>
              <p>Automated CTCAE v5.0 / DAIDS Grade 3/4 toxicities, Hy's Law DILI alerts, and acute &ge;50% delta checks auto-draft an AE eCRF staged for clinical review with 21 CFR Part 11 electronic signature audit trails.</p>
            </div>
          </div>
          <div class="filter-group">
            <label for="review-status-filter">Status Filter:</label>
            <select id="review-status-filter" v-model="reviewStatusFilter" class="select-input" @change="fetchStagedReviews">
              <option value="STAGED_FOR_INVESTIGATOR_REVIEW">Staged for Review (Action Required)</option>
              <option value="CONFIRMED_AS_AE">Confirmed as AE</option>
              <option value="DISMISSED_NOT_CLINICALLY_SIGNIFICANT">Dismissed (Not Significant)</option>
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
              <tr v-for="review in stagedReviews" :key="review.id" :class="{ 'row-critical': review.ctcae_grade >= 4 || review.is_hys_law_warning }">
                <td><strong>{{ review.subject_id }}</strong></td>
                <td>{{ review.visit_id || 'Encounter' }}</td>
                <td>
                  <strong>{{ review.test_code }}</strong>
                  <div class="sub-text">{{ review.test_name }}</div>
                </td>
                <td>
                  <span class="value-highlight">{{ review.lab_value }} {{ review.unit }}</span>
                </td>
                <td>
                  <span :class="['grade-badge', 'grade-' + review.ctcae_grade]">
                    Grade {{ review.ctcae_grade }}
                  </span>
                </td>
                <td>
                  <div class="flags-stack">
                    <span v-if="review.is_hys_law_warning" class="safety-tag hys-law">🚨 Hy's Law DILI</span>
                    <span v-if="review.is_delta_check_alert" class="safety-tag delta">⚡ Delta Alert ({{ review.delta_percentage }}%)</span>
                    <span v-if="!review.is_hys_law_warning && !review.is_delta_check_alert" class="text-muted">Standard</span>
                  </div>
                </td>
                <td class="clinical-desc-cell">{{ review.clinical_description }}</td>
                <td>
                  <span :class="['status-pill', getReviewStatusPillClass(review.review_status)]">
                    {{ formatReviewStatus(review.review_status) }}
                  </span>
                </td>
                <td>
                  <button
                    v-if="review.review_status === 'STAGED_FOR_INVESTIGATOR_REVIEW'"
                    class="btn btn-sm btn-primary"
                    @click="openActionModal(review)"
                  >
                    Investigator Sign-off
                  </button>
                  <span v-else class="text-muted signed-tag">
                    Reviewed by {{ review.reviewed_by || 'Investigator' }}
                  </span>
                </td>
              </tr>
              <tr v-if="stagedReviews.length === 0">
                <td colspan="9" class="empty-state">No staged adverse event laboratory triggers pending investigator review.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 3: Local Lab Certifications & Accreditation Governance -->
      <section v-if="activeTab === 'certifications'" class="tab-pane" aria-labelledby="certifications-tab-heading">
        <div class="pane-toolbar">
          <div class="title-sub">
            <h3>Local Laboratory Accreditations & CLIA/CAP Certification Tracking</h3>
            <p>Monitors laboratory accreditation expiration and enforces uncalibrated range safeguards.</p>
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
                <td><strong>{{ cert.site_id }}</strong></td>
                <td>{{ cert.lab_name }}</td>
                <td><span class="accred-tag">{{ cert.accreditation_body }}</span></td>
                <td><code>{{ cert.certification_number }}</code></td>
                <td>{{ formatDate(cert.effective_date) }}</td>
                <td>{{ formatDate(cert.expiration_date) }}</td>
                <td>
                  <span :class="['status-pill', cert.status === 'ACTIVE' ? 'success' : 'danger']">
                    {{ cert.status }}
                  </span>
                </td>
                <td>
                  <span v-if="isCertExpired(cert.expiration_date)" class="safeguard-badge active">
                    ⚠️ Uncalibrated Range Safeguard Active
                  </span>
                  <span v-else class="safeguard-badge normal">
                    ✅ Calibrated & Verified
                  </span>
                </td>
                <td>
                  <button class="btn btn-sm btn-outline" @click="verifyCert(cert)">Verify Site</button>
                </td>
              </tr>
              <tr v-if="certifications.length === 0">
                <td colspan="9" class="empty-state">No local laboratory certifications recorded. Register CLIA/CAP credentials to enable local lab ingestion.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 4: LOINC Standard Mapping & Mathematical Unit Conversion Engine -->
      <section v-if="activeTab === 'loinc_converter'" class="tab-pane" aria-labelledby="loinc-tab-heading">
        <div class="converter-grid">
          <!-- Live Interactive Unit Converter -->
          <div class="converter-card">
            <h4>🔄 Real-Time LOINC Mathematical Unit Converter</h4>
            <div class="form-group">
              <label for="conv-test">Analyte / Test Code:</label>
              <select id="conv-test" v-model="conversionModel.test_code" class="select-input" @change="onConversionTestChange">
                <option value="ALT">ALT (Alanine Aminotransferase) [LOINC: 1742-6]</option>
                <option value="AST">AST (Aspartate Aminotransferase) [LOINC: 1920-8]</option>
                <option value="BILI">Total Bilirubin [LOINC: 1975-2]</option>
                <option value="CREAT">Serum Creatinine [LOINC: 2160-0]</option>
                <option value="GLUCOSE">Fasting Glucose [LOINC: 2345-7]</option>
                <option value="HEMOGLOBIN">Hemoglobin [LOINC: 718-7]</option>
                <option value="PLATELETS">Platelets [LOINC: 777-3]</option>
                <option value="ANC">Absolute Neutrophil Count [LOINC: 26499-4]</option>
                <option value="POTASSIUM">Potassium [LOINC: 2823-3]</option>
                <option value="SODIUM">Sodium [LOINC: 2951-2]</option>
                <option value="CALCIUM">Calcium [LOINC: 17861-6]</option>
                <option value="CHOLESTEROL">Total Cholesterol [LOINC: 2093-3]</option>
                <option value="TRIGLYCERIDES">Triglycerides [LOINC: 2571-8]</option>
              </select>
            </div>

            <div class="converter-row">
              <div class="form-group">
                <label for="conv-val">Input Value:</label>
                <input id="conv-val" v-model.number="conversionModel.value" type="number" step="any" class="text-input" />
              </div>
              <div class="form-group">
                <label for="conv-from">From Unit:</label>
                <input id="conv-from" v-model="conversionModel.from_unit" type="text" class="text-input" />
              </div>
              <div class="form-group">
                <label for="conv-to">To Unit (Standard SI):</label>
                <input id="conv-to" v-model="conversionModel.to_unit" type="text" class="text-input" />
              </div>
            </div>

            <button class="btn btn-primary" @click="runUnitConversion" :disabled="isConverting">
              {{ isConverting ? 'Converting...' : 'Execute Mathematical Conversion' }}
            </button>

            <div v-if="conversionResult" class="conversion-output">
              <div class="output-row">
                <span>Converted Normalized Result:</span>
                <strong>{{ conversionResult.converted_value }} {{ conversionResult.to_unit }}</strong>
              </div>
              <div class="output-row">
                <span>Multiplication Factor:</span>
                <code>{{ conversionResult.factor }}</code>
              </div>
              <div class="output-row">
                <span>Mapped LOINC Code:</span>
                <code>{{ conversionResult.loinc_code || 'Standard UCUM' }}</code>
              </div>
            </div>
          </div>

          <!-- Real-Time CTCAE / DAIDS Toxicity Evaluation Simulator -->
          <div class="converter-card">
            <h4>🧪 Toxicity Evaluation & Hy's Law Simulator</h4>
            <div class="form-group">
              <label for="sim-scale">Grading System Standard:</label>
              <select id="sim-scale" v-model="simModel.grading_scale" class="select-input">
                <option value="CTCAE_V5">NCI-CTCAE v5.0</option>
                <option value="DAIDS_V2_1">DAIDS v2.1 Severity Table</option>
              </select>
            </div>

            <div class="converter-row">
              <div class="form-group">
                <label for="sim-test">Test Code:</label>
                <input id="sim-test" v-model="simModel.test_code" type="text" class="text-input" />
              </div>
              <div class="form-group">
                <label for="sim-val">Observed Value:</label>
                <input id="sim-val" v-model.number="simModel.value" type="number" step="any" class="text-input" />
              </div>
              <div class="form-group">
                <label for="sim-unit">Unit:</label>
                <input id="sim-unit" v-model="simModel.unit" type="text" class="text-input" />
              </div>
            </div>

            <div class="converter-row">
              <div class="form-group">
                <label for="sim-uln">Demographic ULN:</label>
                <input id="sim-uln" v-model.number="simModel.ref_high" type="number" step="any" class="text-input" />
              </div>
              <div class="form-group">
                <label for="sim-lln">Demographic LLN:</label>
                <input id="sim-lln" v-model.number="simModel.ref_low" type="number" step="any" class="text-input" />
              </div>
              <div class="form-group">
                <label for="sim-base">Subject Baseline:</label>
                <input id="sim-base" v-model.number="simModel.baseline_value" type="number" step="any" class="text-input" />
              </div>
            </div>

            <!-- Co-analytes for Hy's Law simulation -->
            <div v-if="simModel.test_code === 'ALT' || simModel.test_code === 'AST'" class="hys-law-inputs">
              <h5>Co-analytes for Hy's Law DILI Check:</h5>
              <div class="converter-row">
                <div class="form-group">
                  <label for="sim-bili">Total Bilirubin (mg/dL):</label>
                  <input id="sim-bili" v-model.number="simModel.bili_value" type="number" step="any" class="text-input" />
                </div>
                <div class="form-group">
                  <label for="sim-alkp">ALKP (U/L):</label>
                  <input id="sim-alkp" v-model.number="simModel.alkp_value" type="number" step="any" class="text-input" />
                </div>
              </div>
            </div>

            <button class="btn btn-secondary" @click="runToxicitySimulation">
              Simulate Toxicity Grading
            </button>

            <div v-if="simResult" class="conversion-output">
              <div class="output-row">
                <span>Calculated Toxicity Grade:</span>
                <span :class="['grade-badge', 'grade-' + simResult.ctcae_grade]">Grade {{ simResult.ctcae_grade }}</span>
              </div>
              <div class="output-row">
                <span>Clinical Severity:</span>
                <strong>{{ simResult.toxicity_description }}</strong>
              </div>
              <div class="output-row">
                <span>Grade 3/4 AE Trigger Fired:</span>
                <strong>{{ simResult.is_critical_ae_trigger ? 'YES (Auto-Drafts eCRF)' : 'NO' }}</strong>
              </div>
              <div v-if="simResult.is_hys_law_warning" class="output-row alert-row">
                <span>Hy's Law DILI Alert:</span>
                <strong class="text-danger">CRITICAL DRUG-INDUCED LIVER INJURY WARNING</strong>
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
                  <td><strong>{{ item.test_code }}</strong></td>
                  <td>{{ item.test_name }}</td>
                  <td><code class="loinc-badge">{{ item.loinc_code }}</code></td>
                  <td><code>{{ item.standard_unit }}</code></td>
                  <td><span class="category-tag">{{ item.category }}</span></td>
                  <td>{{ item.molecular_weight ?? '—' }}</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- TAB 5: Laboratory Feed Ingestion Monitor -->
      <section v-if="activeTab === 'batches'" class="tab-pane" aria-labelledby="batches-tab-heading">
        <div class="pane-toolbar">
          <div class="title-sub">
            <h3>Central & Local Laboratory Feed Ingestion Log</h3>
            <p>Real-time transaction tracking across HL7 v2.x, CDISC LAB v1.0.1, CSV/sFTP, and Local Lab batch entry.</p>
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
                <td><code>{{ b.batch_id.slice(0, 8) }}...</code></td>
                <td><span class="format-badge">{{ b.format.toUpperCase() }}</span></td>
                <td>
                  <span :class="['status-pill', b.status === 'COMPLETED' ? 'success' : b.status === 'COMPLETED_WITH_ERRORS' ? 'warning' : 'danger']">
                    {{ b.status }}
                  </span>
                </td>
                <td>{{ b.total_processed }}</td>
                <td>{{ b.ingested_count }}</td>
                <td>{{ b.out_of_range_count }}</td>
                <td><span class="count-badge danger">{{ b.staged_aes_count }}</span></td>
                <td><span class="count-badge warning">{{ b.uncalibrated_safeguards }}</span></td>
                <td>{{ b.queries_raised }}</td>
                <td>{{ formatDate(b.created_at) }}</td>
              </tr>
              <tr v-if="batches.length === 0">
                <td colspan="10" class="empty-state">No laboratory feed batches processed yet.</td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>
    </main>

    <!-- MODAL: Ingest Laboratory Batch -->
    <div v-if="showIngestModal" class="modal-backdrop" role="dialog" aria-modal="true">
      <div class="modal-box modal-lg">
        <div class="modal-header">
          <h3>Ingest Laboratory Batch Feed</h3>
          <button class="close-btn" @click="showIngestModal = false">&times;</button>
        </div>
        <div class="modal-body">
          <div class="form-group">
            <label for="ingest-format">Feed Ingestion Format:</label>
            <select id="ingest-format" v-model="ingestForm.format" class="select-input">
              <option value="cdisc_lab">CDISC LAB Standard v1.0.1 (XML ODM / JSON)</option>
              <option value="csv">Delimited CSV / TSV / sFTP</option>
              <option value="hl7">HL7 v2.x (ORU^R01)</option>
              <option value="fhir">HL7 FHIR Observation JSON</option>
              <option value="local_manual">Manual Local Lab Batch Entry</option>
            </select>
          </div>

          <div class="form-row">
            <div class="form-group">
              <label for="ingest-study">Study Identifier:</label>
              <input id="ingest-study" v-model="ingestForm.study_id" type="text" class="text-input" placeholder="e.g. STUDY-101" />
            </div>
            <div class="form-group">
              <label for="ingest-site">Site Identifier (for Local Lab):</label>
              <input id="ingest-site" v-model="ingestForm.site_id" type="text" class="text-input" placeholder="e.g. SITE-001" />
            </div>
            <div class="form-group">
              <label for="ingest-source">Laboratory Source:</label>
              <select id="ingest-source" v-model="ingestForm.lab_source" class="select-input">
                <option value="CENTRAL">CENTRAL</option>
                <option value="LOCAL">LOCAL</option>
              </select>
            </div>
          </div>

          <div class="form-group">
            <label for="ingest-payload">Raw Feed Content / XML / JSON / Delimited Payload:</label>
            <textarea
              id="ingest-payload"
              v-model="ingestForm.payload"
              rows="8"
              class="textarea-input"
              placeholder="Paste raw HL7 v2.x, CDISC LAB XML ODM, FHIR Observation, or CSV text here..."
            ></textarea>
          </div>

          <div class="form-group">
            <label for="ingest-reason">GxP Reason for Ingestion:</label>
            <input id="ingest-reason" v-model="ingestForm.reason_for_change" type="text" class="text-input" />
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showIngestModal = false">Cancel</button>
          <button class="btn btn-primary" @click="executeBatchIngest" :disabled="isIngesting">
            {{ isIngesting ? 'Processing Feed...' : 'Execute Ingestion & Grade Toxicities' }}
          </button>
        </div>
      </div>
    </div>

    <!-- MODAL: Investigator Sign-Off Action -->
    <div v-if="showActionModal && selectedReview" class="modal-backdrop" role="dialog" aria-modal="true">
      <div class="modal-box">
        <div class="modal-header">
          <h3>Investigator Sign-off: Grade {{ selectedReview.ctcae_grade }} Toxicity</h3>
          <button class="close-btn" @click="showActionModal = false">&times;</button>
        </div>
        <div class="modal-body">
          <div class="review-summary-card">
            <p><strong>Subject:</strong> {{ selectedReview.subject_id }} | <strong>Analyte:</strong> {{ selectedReview.test_name }} ({{ selectedReview.test_code }})</p>
            <p><strong>Observed Measurement:</strong> {{ selectedReview.lab_value }} {{ selectedReview.unit }}</p>
            <p><strong>Severity Description:</strong> {{ selectedReview.clinical_description }}</p>
          </div>

          <div class="form-group">
            <label for="action-select">Investigator Clinical Determination:</label>
            <select id="action-select" v-model="actionForm.action" class="select-input">
              <option value="CONFIRM">Confirm as Adverse Event (Promote to Formal AE eCRF)</option>
              <option value="DISMISS">Dismiss (Deemed Not Clinically Significant)</option>
              <option value="ESCALATE_SAE">Escalate to Serious Adverse Event (SAE)</option>
            </select>
          </div>

          <div class="form-group">
            <label for="action-notes">Clinical Commentary & Rationale:</label>
            <textarea id="action-notes" v-model="actionForm.investigator_notes" rows="3" class="textarea-input" placeholder="Provide medical commentary on laboratory abnormality..."></textarea>
          </div>

          <div class="form-group">
            <label for="action-reason">21 CFR Part 11 Reason for Determination:</label>
            <input id="action-reason" v-model="actionForm.reason_for_change" type="text" class="text-input" placeholder="Mandatory Part 11 audit justification" />
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showActionModal = false">Cancel</button>
          <button class="btn btn-primary" @click="executeReviewAction" :disabled="isActioning">
            {{ isActioning ? 'Submitting Signature...' : 'Sign & Submit Determination' }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from 'vue'

// Active Tab State
const activeTab = ref('ranges')
const currentStudyId = ref('STUDY-101')

// Reference Ranges State
const ranges = ref([])
const rangeSearch = ref('')
const rangeSourceFilter = ref('ALL')
const rangeSexFilter = ref('ALL')

// Staged Reviews State
const stagedReviews = ref([])
const reviewStatusFilter = ref('STAGED_FOR_INVESTIGATOR_REVIEW')
const selectedReview = ref(null)
const showActionModal = ref(false)
const isActioning = ref(false)
const actionForm = ref({
  action: 'CONFIRM',
  investigator_notes: '',
  reason_for_change: 'Investigator review of laboratory toxicity trigger',
})

// Local Lab Certifications State
const certifications = ref([])

// Ingestion State
const batches = ref([])
const showIngestModal = ref(false)
const isIngesting = ref(false)
const ingestForm = ref({
  format: 'cdisc_lab',
  study_id: 'STUDY-101',
  site_id: 'SITE-001',
  lab_source: 'CENTRAL',
  payload: '',
  reason_for_change: 'Batch laboratory data ingestion',
})

// LOINC & Unit Conversion State
const loincCatalog = ref([])
const isConverting = ref(false)
const conversionModel = ref({
  test_code: 'CREAT',
  value: 2.5,
  from_unit: 'mg/dL',
  to_unit: 'umol/L',
})
const conversionResult = ref(null)

// Toxicity Simulation State
const simModel = ref({
  grading_scale: 'CTCAE_V5',
  test_code: 'ALT',
  value: 210,
  unit: 'U/L',
  ref_high: 35,
  ref_low: 5,
  baseline_value: 30,
  bili_value: 2.8,
  alkp_value: 95,
})
const simResult = ref(null)

// Computed Filters
const filteredRanges = computed(() => {
  return ranges.value.filter((r) => {
    const matchesSearch =
      !rangeSearch.value ||
      r.test_code.toLowerCase().includes(rangeSearch.value.toLowerCase()) ||
      (r.test_name && r.test_name.toLowerCase().includes(rangeSearch.value.toLowerCase()))

    const matchesSource =
      rangeSourceFilter.value === 'ALL' || r.source === rangeSourceFilter.value

    const matchesSex =
      rangeSexFilter.value === 'ALL' ||
      r.sex_applicability === rangeSexFilter.value ||
      r.sex_applicability === 'ALL'

    return matchesSearch && matchesSource && matchesSex
  })
})

// Data Fetching Methods
async function fetchRanges() {
  try {
    const res = await fetch('/api/v1/execution/lab-ranges')
    if (res.ok) {
      ranges.value = await res.json()
    }
  } catch (err) {
    console.error('Failed to load lab reference ranges', err)
  }
}

async function fetchStagedReviews() {
  try {
    const url = `/api/v1/execution/labs/toxicity/review-queue?review_status=${reviewStatusFilter.value}`
    const res = await fetch(url)
    if (res.ok) {
      stagedReviews.value = await res.json()
    }
  } catch (err) {
    console.error('Failed to load staged reviews', err)
  }
}

async function fetchCertifications() {
  try {
    const res = await fetch('/api/v1/execution/labs/certifications')
    if (res.ok) {
      certifications.value = await res.json()
    }
  } catch (err) {
    console.error('Failed to load local lab certifications', err)
  }
}

async function fetchBatchStatuses() {
  try {
    const res = await fetch('/api/v1/execution/labs/batch-status')
    if (res.ok) {
      const data = await res.json()
      batches.value = Array.isArray(data) ? data : [data]
    }
  } catch (err) {
    console.error('Failed to load batch statuses', err)
  }
}

async function fetchLoincCatalog() {
  try {
    const res = await fetch('/api/v1/execution/labs/loinc-catalog')
    if (res.ok) {
      const data = await res.json()
      loincCatalog.value = data.items || []
    }
  } catch (err) {
    console.error('Failed to load LOINC catalog', err)
  }
}

// Actions
function openIngestModal() {
  showIngestModal.value = true
}

function openCreateRangeModal() {
  // Placeholder or prompt for range creation
  const testCode = prompt('Enter Test Code (e.g. ALT, CREAT, WBC):')
  if (testCode) {
    alert(`To register new range for ${testCode}, use the Reference Range Authoring Dialog.`)
  }
}

function openCreateCertModal() {
  const siteId = prompt('Enter Site ID (e.g. SITE-001):')
  if (siteId) {
    alert(`Registering Local Lab for Site ${siteId}.`)
  }
}

function openActionModal(review) {
  selectedReview.value = review
  actionForm.value.investigator_notes = ''
  showActionModal.value = true
}

async function executeBatchIngest() {
  isIngesting.value = true
  try {
    const res = await fetch('/api/v1/execution/labs/ingest', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(ingestForm.value),
    })
    if (res.ok) {
      const result = await res.json()
      alert(`Batch Ingest Completed! Ingested: ${result.ingested_count}, Staged AEs: ${result.staged_aes_count}, Queries: ${result.queries_raised}`)
      showIngestModal.value = false
      await fetchStagedReviews()
      await fetchBatchStatuses()
    } else {
      const err = await res.json()
      alert(`Batch Ingest Error: ${err.detail || 'Ingestion failed'}`)
    }
  } catch (err) {
    alert(`Ingest Request Failed: ${err}`)
  } finally {
    isIngesting.value = false
  }
}

async function executeReviewAction() {
  if (!selectedReview.value) return
  isActioning.value = true
  try {
    const url = `/api/v1/execution/labs/toxicity/review/${selectedReview.value.id}/action`
    const res = await fetch(url, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(actionForm.value),
    })
    if (res.ok) {
      alert('Investigator action successfully recorded with 21 CFR Part 11 audit trail.')
      showActionModal.value = false
      await fetchStagedReviews()
    } else {
      const err = await res.json()
      alert(`Action error: ${err.detail || 'Failed to submit determination'}`)
    }
  } catch (err) {
    alert(`Action Request Failed: ${err}`)
  } finally {
    isActioning.value = false
  }
}

async function runUnitConversion() {
  isConverting.value = true
  try {
    const res = await fetch('/api/v1/execution/labs/convert-unit', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(conversionModel.value),
    })
    if (res.ok) {
      conversionResult.value = await res.json()
    }
  } catch (err) {
    console.error('Unit conversion failed', err)
  } finally {
    isConverting.value = false
  }
}

function onConversionTestChange() {
  const code = conversionModel.value.test_code
  if (code === 'CREAT') {
    conversionModel.value.from_unit = 'mg/dL'
    conversionModel.value.to_unit = 'umol/L'
  } else if (code === 'BILI') {
    conversionModel.value.from_unit = 'mg/dL'
    conversionModel.value.to_unit = 'umol/L'
  } else if (code === 'GLUCOSE') {
    conversionModel.value.from_unit = 'mg/dL'
    conversionModel.value.to_unit = 'mmol/L'
  } else if (code === 'HEMOGLOBIN') {
    conversionModel.value.from_unit = 'g/dL'
    conversionModel.value.to_unit = 'g/L'
  } else if (code === 'PLATELETS' || code === 'ANC') {
    conversionModel.value.from_unit = '10^3/uL'
    conversionModel.value.to_unit = '10^9/L'
  }
}

async function runToxicitySimulation() {
  try {
    const res = await fetch('/api/v1/execution/labs/toxicity/evaluate', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(simModel.value),
    })
    if (res.ok) {
      simResult.value = await res.json()
    }
  } catch (err) {
    console.error('Toxicity simulation failed', err)
  }
}

// Helpers
function formatAgeRange(low, high) {
  if (low !== null && high !== null) return `${low} - ${high} yrs`
  if (low !== null) return `&ge; ${low} yrs`
  if (high !== null) return `&le; ${high} yrs`
  return 'All Ages'
}

function formatDate(dtStr) {
  if (!dtStr) return '—'
  return new Date(dtStr).toLocaleDateString()
}

function isCertExpired(expStr) {
  if (!expStr) return true
  return new Date(expStr) < new Date()
}

function formatReviewStatus(status) {
  if (status === 'STAGED_FOR_INVESTIGATOR_REVIEW') return 'Staged for Review'
  if (status === 'CONFIRMED_AS_AE') return 'Confirmed AE'
  if (status === 'DISMISSED_NOT_CLINICALLY_SIGNIFICANT') return 'Dismissed'
  if (status === 'ESCALATED_SAE') return 'Escalated SAE'
  return status
}

function getReviewStatusPillClass(status) {
  if (status === 'STAGED_FOR_INVESTIGATOR_REVIEW') return 'danger'
  if (status === 'CONFIRMED_AS_AE') return 'success'
  if (status === 'DISMISSED_NOT_CLINICALLY_SIGNIFICANT') return 'secondary'
  if (status === 'ESCALATED_SAE') return 'primary'
  return 'info'
}

onMounted(() => {
  fetchRanges()
  fetchStagedReviews()
  fetchCertifications()
  fetchBatchStatuses()
  fetchLoincCatalog()
  runUnitConversion()
  runToxicitySimulation()
})
</script>

<style scoped>
.lab-governance-console {
  display: flex;
  flex-direction: column;
  gap: 1rem;
  padding: 1.5rem;
  background-color: #f8fafc;
  min-height: 100vh;
  font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;
  color: #0f172a;
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: #ffffff;
  padding: 1.25rem 1.5rem;
  border-radius: 8px;
  border: 1px solid #e2e8f0;
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
  color: #0369a1;
  background-color: #e0f2fe;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
  width: fit-content;
}

.console-title {
  margin: 0;
  font-size: 1.25rem;
  font-weight: 700;
  color: #0f172a;
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
  border-bottom: 2px solid #e2e8f0;
  background: #ffffff;
  padding: 0.5rem 1rem 0;
  border-radius: 8px 8px 0 0;
}

.tab-button {
  padding: 0.75rem 1.25rem;
  border: none;
  background: transparent;
  font-size: 0.875rem;
  font-weight: 600;
  color: #64748b;
  cursor: pointer;
  border-bottom: 2px solid transparent;
  margin-bottom: -2px;
}

.tab-button.active {
  color: #2563eb;
  border-bottom-color: #2563eb;
}

.tab-pane {
  background: #ffffff;
  border: 1px solid #e2e8f0;
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
  background: #fef2f2;
  border: 1px solid #fecaca;
  padding: 0.75rem 1rem;
  border-radius: 6px;
  color: #991b1b;
  font-size: 0.85rem;
}

.filter-group {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
}

.text-input, .select-input, .textarea-input {
  padding: 0.5rem 0.75rem;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  font-size: 0.85rem;
  background-color: #ffffff;
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
  background-color: #f1f5f9;
  padding: 0.75rem;
  font-weight: 600;
  border-bottom: 2px solid #cbd5e1;
}

.data-table td {
  padding: 0.75rem;
  border-bottom: 1px solid #e2e8f0;
  vertical-align: middle;
}

.row-critical {
  background-color: #fff1f2;
}

.code-tag {
  background: #e2e8f0;
  padding: 0.2rem 0.4rem;
  border-radius: 4px;
}

.demographic-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 0.25rem;
}

.mini-tag {
  background: #f1f5f9;
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

.status-pill.primary { background: #dbeafe; color: #1e40af; }
.status-pill.secondary { background: #f1f5f9; color: #475569; }
.status-pill.success { background: #dcfce7; color: #166534; }
.status-pill.warning { background: #fef3c7; color: #92400e; }
.status-pill.danger { background: #fee2e2; color: #991b1b; }
.status-pill.info { background: #e0f2fe; color: #0369a1; }

.grade-badge {
  padding: 0.25rem 0.5rem;
  font-weight: 700;
  border-radius: 4px;
  font-size: 0.75rem;
}

.grade-badge.grade-0 { background: #f1f5f9; color: #475569; }
.grade-badge.grade-1 { background: #e0f2fe; color: #0369a1; }
.grade-badge.grade-2 { background: #fef3c7; color: #92400e; }
.grade-badge.grade-3 { background: #fee2e2; color: #991b1b; font-weight: 800; }
.grade-badge.grade-4 { background: #991b1b; color: #ffffff; font-weight: 900; }

.safety-tag {
  font-size: 0.75rem;
  padding: 0.2rem 0.4rem;
  border-radius: 4px;
  font-weight: 700;
}

.safety-tag.hys-law { background: #7f1d1d; color: #fef2f2; }
.safety-tag.delta { background: #fef08a; color: #854d0e; }

.btn {
  padding: 0.5rem 0.875rem;
  font-weight: 600;
  font-size: 0.85rem;
  border-radius: 4px;
  cursor: pointer;
  border: 1px solid transparent;
}

.btn-sm { padding: 0.25rem 0.5rem; font-size: 0.75rem; }
.btn-primary { background: #2563eb; color: #ffffff; }
.btn-secondary { background: #e2e8f0; color: #1e293b; }
.btn-outline { background: transparent; border-color: #94a3b8; color: #334155; }

.converter-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
}

.converter-card {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
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
  background: #ffffff;
  border: 1px solid #cbd5e1;
  border-radius: 6px;
  padding: 0.75rem;
}

.output-row {
  display: flex;
  justify-content: space-between;
  margin-bottom: 0.25rem;
  font-size: 0.85rem;
}

.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 999;
}

.modal-box {
  background: #ffffff;
  border-radius: 8px;
  width: 540px;
  max-width: 90vw;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.15);
  display: flex;
  flex-direction: column;
}

.modal-lg { width: 750px; }

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem;
  border-bottom: 1px solid #e2e8f0;
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
  border-top: 1px solid #e2e8f0;
}

.empty-state {
  text-align: center;
  color: #64748b;
  padding: 2rem;
}
</style>
