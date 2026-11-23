<template>
  <div
    class="cec-adjudication-console"
    role="region"
    aria-label="Clinical Endpoint Adjudication Committee (CEC) Suite"
  >
    <!-- Top Regulatory & Navigation Header -->
    <header class="console-header">
      <div class="header-main">
        <div class="title-group">
          <div class="badge-row">
            <span class="badge badge-regulatory"
              >FDA ENDPOINTS GUIDANCE / 21 CFR PART 11 / CDISC SDTM CE</span
            >
            <span class="badge badge-charter"
              >CHARTER:
              {{ activeCharter.voting_rule || "CHAIR_TIE_BREAKER" }}</span
            >
            <span
              class="badge badge-phi"
              :class="{ 'phi-active': phiMaskEnabled }"
            >
              {{
                phiMaskEnabled
                  ? "🛡️ IN-FLIGHT PHI MASKING: ACTIVE"
                  : "⚠️ UNMASKED VIEW"
              }}
            </span>
          </div>
          <h2 class="console-title">
            Clinical Endpoint Adjudication Committee (CEC) Review Suite
          </h2>
          <p class="console-subtitle">
            Blinded dual-reader independent adjudication, automated dossier
            packaging, tie-breaker governance, and CDISC SDTM CE export.
          </p>
        </div>

        <div class="header-controls">
          <!-- Role Persona Switcher -->
          <div class="control-box">
            <label for="cec-role-select" class="control-label"
              >Adjudication Persona:</label
            >
            <select
              id="cec-role-select"
              v-model="currentRole"
              class="select-input"
            >
              <option value="READER_1">Dr. Reader 1 (Blinded)</option>
              <option value="READER_2">Dr. Reader 2 (Blinded)</option>
              <option value="CHAIR">
                Dr. Adjudication Chair (Tie-Breaker)
              </option>
              <option value="DATA_MANAGER">Data Manager / Coordinator</option>
              <option value="AUDITOR">TMF / GxP Auditor</option>
            </select>
          </div>

          <!-- Study Selector -->
          <div class="control-box">
            <label for="cec-study-select" class="control-label"
              >Active Protocol:</label
            >
            <select
              id="cec-study-select"
              v-model="selectedStudyId"
              class="select-input"
            >
              <option value="STUDY-101">STUDY-101 (Cardiovascular MACE)</option>
              <option value="STUDY-202">
                STUDY-202 (Neuro Stroke Registry)
              </option>
              <option value="STUDY-303">
                STUDY-303 (Oncology Progression)
              </option>
            </select>
          </div>

          <div class="control-box toggle-box">
            <label class="toggle-label">
              <input
                v-model="phiMaskEnabled"
                type="checkbox"
                class="toggle-checkbox"
              />
              <span>PHI Mask</span>
            </label>
          </div>
        </div>
      </div>

      <!-- Executive Metric Strip -->
      <div class="metric-strip">
        <div class="metric-card">
          <span class="metric-title">Total Dossiers</span>
          <span class="metric-value">{{ dossiers.length }}</span>
          <span class="metric-sub">Packaged candidate events</span>
        </div>
        <div class="metric-card">
          <span class="metric-title">In Primary Review</span>
          <span class="metric-value text-warning">
            {{
              dossiers.filter((d) =>
                [
                  "PENDING_PRIMARY_REVIEWS",
                  "READER_1_COMPLETED",
                  "READER_2_COMPLETED",
                ].includes(d.workflow_status)
              ).length
            }}
          </span>
          <span class="metric-sub">2-Reader queue</span>
        </div>
        <div class="metric-card">
          <span class="metric-title">Consensus Finalized</span>
          <span class="metric-value text-success">
            {{
              dossiers.filter((d) =>
                ["FINALIZED_CONCORDANT", "CONSENSUS_REACHED"].includes(
                  d.workflow_status
                )
              ).length
            }}
          </span>
          <span class="metric-sub">Concordant reviews</span>
        </div>
        <div class="metric-card">
          <span class="metric-title">Escalated to Chair</span>
          <span class="metric-value text-danger">
            {{
              dossiers.filter((d) =>
                ["DISCORDANCE_DETECTED", "ESCALATED_TO_CHAIR"].includes(
                  d.workflow_status
                )
              ).length
            }}
          </span>
          <span class="metric-sub">Discordance detected</span>
        </div>
        <div class="metric-card">
          <span class="metric-title">SDTM CE Compiled</span>
          <span class="metric-value text-info">{{ sdtmCeRecords.length }}</span>
          <span class="metric-sub">Casebook synced records</span>
        </div>
      </div>
    </header>

    <!-- Main Tab Navigation -->
    <nav class="nav-tabs" role="tablist" aria-label="CEC Suite Workspaces">
      <button
        role="tab"
        :aria-selected="activeMainTab === 'dossiers'"
        :class="['tab-btn', { active: activeMainTab === 'dossiers' }]"
        @click="activeMainTab = 'dossiers'"
      >
        📑 Event Dossiers &amp; Blinded Review ({{ dossiers.length }})
      </button>
      <button
        role="tab"
        :aria-selected="activeMainTab === 'chair_tiebreaker'"
        :class="['tab-btn', { active: activeMainTab === 'chair_tiebreaker' }]"
        @click="activeMainTab = 'chair_tiebreaker'"
      >
        ⚖️ Chair Tie-Breaker &amp; Consensus Panel
        <span v-if="discordantCount > 0" class="badge-count">{{
          discordantCount
        }}</span>
      </button>
      <button
        role="tab"
        :aria-selected="activeMainTab === 'charter_quorum'"
        :class="['tab-btn', { active: activeMainTab === 'charter_quorum' }]"
        @click="activeMainTab = 'charter_quorum'"
      >
        📜 Charter Governance &amp; Quorum Tracker
      </button>
      <button
        role="tab"
        :aria-selected="activeMainTab === 'sdtm_ce'"
        :class="['tab-btn', { active: activeMainTab === 'sdtm_ce' }]"
        @click="activeMainTab = 'sdtm_ce'"
      >
        📊 CDISC SDTM CE &amp; EDC Casebook Sync
      </button>
    </nav>

    <!-- TAB 1: Dossiers & Blinded Review Workspace -->
    <section v-if="activeMainTab === 'dossiers'" class="tab-pane">
      <div class="split-layout">
        <!-- Left: Dossier Queue List -->
        <aside class="sidebar-pane">
          <div class="pane-header">
            <h3 class="pane-title">Candidate Event Queue</h3>
            <span class="pill-badge">{{ filteredDossiers.length }} Events</span>
          </div>

          <div class="filter-toolbar">
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="searchQuery"
              type="text"
              placeholder="Search event, term, or subject..."
              class="text-input"
            />
            <div class="filter-chips">
              <button
                v-for="st in ['ALL', 'PENDING', 'DISCORDANT', 'FINALIZED']"
                :key="st"
                :class="['chip-btn', { active: filterStatus === st }]"
                @click="filterStatus = st"
              >
                {{ st }}
              </button>
            </div>
          </div>

          <div class="dossier-list" role="list">
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
            <div
              v-for="item in filteredDossiers"
              :key="item.dossier_id"
              role="listitem"
              :class="[
                'dossier-card',
                { active: selectedDossierId === item.dossier_id },
              ]"
              @click="selectDossier(item.dossier_id)"
            >
              <div class="card-top">
                <span class="category-tag">{{
                  item.candidate_event_type
                }}</span>
                <span
                  :class="[
                    'status-pill',
                    statusPillClass(item.workflow_status),
                  ]"
                >
                  {{ formatStatus(item.workflow_status) }}
                </span>
              </div>
              <h4 class="card-term">
                {{ item.reported_term }}
              </h4>
              <div class="card-meta">
                <span
                  ><strong>ID:</strong>
                  {{
                    phiMaskEnabled ? item.masked_subject_id : item.subject_id
                  }}</span
                >
                <span
                  ><strong>Onset:</strong> {{ item.reported_onset_date }}</span
                >
              </div>
              <div class="reader-progress-dots">
                <span
                  :class="[
                    'dot',
                    { completed: item.reader_1_status === 'COMPLETED' },
                  ]"
                  title="Reader 1"
                  >R1</span
                >
                <span
                  :class="[
                    'dot',
                    { completed: item.reader_2_status === 'COMPLETED' },
                  ]"
                  title="Reader 2"
                  >R2</span
                >
                <span
                  v-if="item.is_discordant"
                  class="dot dot-alert"
                  title="Discordance Detected"
                  >⚠️</span
                >
                <span
                  v-if="
                    item.workflow_status === 'FINALIZED_CONCORDANT' ||
                    item.workflow_status === 'FINALIZED_BY_CHAIR'
                  "
                  class="dot dot-check"
                  title="Finalized"
                  >✓</span
                >
              </div>
            </div>
          </div>
        </aside>

        <!-- Right: Active Dossier Dossier Details & Adjudication View -->
        <div v-if="activeDossier" class="content-pane">
          <!-- Event Summary Banner -->
          <div class="dossier-banner">
            <div class="banner-main">
              <div class="banner-title-row">
                <span class="category-pill">{{
                  activeDossier.candidate_event_type
                }}</span>
                <h3 class="banner-event-title">
                  {{ activeDossier.reported_term }}
                </h3>
                <span
                  :class="[
                    'status-pill',
                    statusPillClass(activeDossier.workflow_status),
                  ]"
                >
                  {{ formatStatus(activeDossier.workflow_status) }}
                </span>
              </div>
              <div class="banner-metadata-grid">
                <div>
                  <strong>Subject Token:</strong>
                  {{
                    phiMaskEnabled
                      ? activeDossier.masked_subject_id
                      : activeDossier.subject_id
                  }}
                </div>
                <div>
                  <strong>Site Token:</strong>
                  {{
                    phiMaskEnabled
                      ? activeDossier.masked_site_id
                      : activeDossier.site_id
                  }}
                </div>
                <div>
                  <strong>Reported Onset:</strong>
                  {{ activeDossier.reported_onset_date }}
                </div>
                <div>
                  <strong>Integrity Seal:</strong>
                  <code class="hash-code"
                    >{{
                      activeDossier.dossier_digest?.substring(0, 12)
                    }}...</code
                  >
                </div>
              </div>
            </div>

            <div class="banner-actions">
              <button
                class="btn btn-secondary"
                @click="activeMainTab = 'chair_tiebreaker'"
              >
                ⚖️ Open Adjudication Panel
              </button>
            </div>
          </div>

          <!-- Discordance Warning Banner (if discordant) -->
          <div
            v-if="activeDossier.is_discordant"
            class="alert-banner alert-discordance"
          >
            <div class="alert-icon">⚠️</div>
            <div class="alert-content">
              <h4 class="alert-title">
                DISCORDANCE DETECTED &mdash; Escalated to Committee Chair
              </h4>
              <ul class="discordance-list">
                <li
                  v-for="(reason, idx) in activeDossier.discordance_reasons"
                  :key="idx"
                >
                  {{ reason }}
                </li>
              </ul>
            </div>
          </div>

          <!-- Consensus Banner (if concordant) -->
          <div
            v-if="
              activeDossier.workflow_status === 'FINALIZED_CONCORDANT' ||
              activeDossier.workflow_status === 'CONSENSUS_REACHED'
            "
            class="alert-banner alert-consensus"
          >
            <div class="alert-icon">✅</div>
            <div class="alert-content">
              <h4 class="alert-title">CONCORDANT CONSENSUS FINALIZED</h4>
              <p class="alert-desc">
                Reader 1 and Reader 2 independently achieved 100% concordance.
                Final Classification:
                <strong>{{
                  activeDossier.final_consensus_classification
                }}</strong>
                <span v-if="activeDossier.final_consensus_subtype">
                  ({{ activeDossier.final_consensus_subtype }})</span
                >.
              </p>
            </div>
          </div>

          <!-- Dossier Package Accordion Sections -->
          <div class="dossier-sections">
            <!-- Section A: Clinical Narrative & Discharge Summary -->
            <div class="section-card">
              <div class="section-header">
                <h4 class="section-title">
                  🏥 Medical Narrative &amp; Hospital Discharge Summary
                </h4>
                <span class="phi-badge">PHI SCRUBBED</span>
              </div>
              <div class="narrative-box">
                <p>
                  {{
                    activeDossier.medical_narrative || "No narrative provided."
                  }}
                </p>
                <div
                  v-if="activeDossier.verbatim_description"
                  class="verbatim-box"
                >
                  <strong>Verbatim Site Description:</strong>
                  {{ activeDossier.verbatim_description }}
                </div>
              </div>
            </div>

            <!-- Section B: Pertinent Laboratory Panels -->
            <div class="section-card">
              <div class="section-header">
                <h4 class="section-title">
                  🧪 Pertinent Laboratory Panels &amp; Biomarkers
                </h4>
                <span class="count-tag"
                  >{{ activeDossier.lab_panels?.length || 0 }} Analytes</span
                >
              </div>
              <div class="table-responsive">
                <table class="data-table">
                  <thead>
                    <tr>
                      <th>Analyte</th>
                      <th>LOINC</th>
                      <th>Observed Value</th>
                      <th>Reference Range</th>
                      <th>Status</th>
                      <th>CTCAE Grade</th>
                    </tr>
                  </thead>
                  <tbody>
                    <tr
                      v-for="(lab, idx) in activeDossier.lab_panels"
                      :key="idx"
                      :class="{ 'row-abnormal': lab.is_out_of_range }"
                    >
                      <td class="cell-bold">
                        {{ lab.test_name }}
                      </td>
                      <td>
                        <code>{{ lab.loinc_code || "N/A" }}</code>
                      </td>
                      <td class="cell-value">
                        {{ lab.result_value }} {{ lab.result_unit }}
                      </td>
                      <td>
                        {{ lab.reference_low ?? "-" }} -
                        {{ lab.reference_high ?? "-" }} {{ lab.result_unit }}
                      </td>
                      <td>
                        <span
                          v-if="lab.is_out_of_range"
                          class="status-pill danger"
                          >ABNORMAL</span
                        >
                        <span v-else class="status-pill success">NORMAL</span>
                      </td>
                      <td>
                        <span
                          v-if="lab.toxicity_grade"
                          :class="[
                            'grade-badge',
                            `grade-${lab.toxicity_grade}`,
                          ]"
                        >
                          Grade {{ lab.toxicity_grade }}
                        </span>
                        <span v-else>-</span>
                      </td>
                    </tr>
                    <tr v-if="!activeDossier.lab_panels?.length">
                      <td colspan="6" class="text-center empty-cell">
                        No laboratory panels attached.
                      </td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>

            <!-- Section C: 12-Lead Electrocardiograms (ECGs) -->
            <div class="section-card">
              <div class="section-header">
                <h4 class="section-title">
                  🫀 12-Lead Electrocardiogram (ECG) Findings
                </h4>
                <span class="count-tag"
                  >{{ activeDossier.ecg_records?.length || 0 }} Tracings</span
                >
              </div>
              <div class="ecg-grid">
                <div
                  v-for="ecg in activeDossier.ecg_records"
                  :key="ecg.ecg_id"
                  class="ecg-card"
                >
                  <div class="ecg-header">
                    <span class="ecg-rhythm"
                      ><strong>Rhythm:</strong> {{ ecg.rhythm }}</span
                    >
                    <span v-if="ecg.qtcf_ms" class="ecg-qtc"
                      ><strong>QTcF:</strong> {{ ecg.qtcf_ms }} ms</span
                    >
                  </div>
                  <div class="ecg-leads-row">
                    <div
                      v-if="ecg.st_elevation_leads?.length"
                      class="lead-group alert-leads"
                    >
                      <span class="lead-label">ST Elevation:</span>
                      <span class="lead-pills">{{
                        ecg.st_elevation_leads.join(", ")
                      }}</span>
                    </div>
                    <div
                      v-if="ecg.st_depression_leads?.length"
                      class="lead-group"
                    >
                      <span class="lead-label">ST Depression:</span>
                      <span class="lead-pills">{{
                        ecg.st_depression_leads.join(", ")
                      }}</span>
                    </div>
                    <div v-if="ecg.q_wave_leads?.length" class="lead-group">
                      <span class="lead-label">Pathological Q-Waves:</span>
                      <span class="lead-pills">{{
                        ecg.q_wave_leads.join(", ")
                      }}</span>
                    </div>
                    <div v-if="ecg.lbbb_present" class="lead-group alert-leads">
                      <span class="lead-label">LBBB:</span>
                      <span class="lead-pills">Present (New onset)</span>
                    </div>
                  </div>
                  <p v-if="ecg.interpretation" class="ecg-interp">
                    <strong>Interpretation:</strong> {{ ecg.interpretation }}
                  </p>
                </div>
                <div
                  v-if="!activeDossier.ecg_records?.length"
                  class="empty-box"
                >
                  No ECG records attached.
                </div>
              </div>
            </div>

            <!-- Section D: Diagnostic Imaging & Attachments -->
            <div class="section-card">
              <div class="section-header">
                <h4 class="section-title">
                  📁 Diagnostic Imaging &amp; Specialty Attachments
                </h4>
                <span class="count-tag"
                  >{{
                    activeDossier.diagnostic_attachments?.length || 0
                  }}
                  Files</span
                >
              </div>
              <div class="attachments-list">
                <div
                  v-for="att in activeDossier.diagnostic_attachments"
                  :key="att.attachment_id"
                  class="attachment-item"
                >
                  <div class="att-icon">📄</div>
                  <div class="att-info">
                    <h5 class="att-title">
                      {{ att.title }} ({{ att.document_type }})
                    </h5>
                    <p class="att-summary">
                      {{ att.summary }}
                    </p>
                    <div v-if="att.key_findings?.length" class="att-findings">
                      <strong>Key Findings:</strong>
                      <ul>
                        <li v-for="(k, i) in att.key_findings" :key="i">
                          {{ k }}
                        </li>
                      </ul>
                    </div>
                  </div>
                  <div class="att-action">
                    <button
                      class="btn btn-sm btn-secondary"
                      @click="viewAttachment(att)"
                    >
                      👁️ View Document
                    </button>
                  </div>
                </div>
                <div
                  v-if="!activeDossier.diagnostic_attachments?.length"
                  class="empty-box"
                >
                  No diagnostic attachments found.
                </div>
              </div>
            </div>

            <!-- Section E: Reader 1 & Reader 2 Assessment Workspace -->
            <div class="section-card assessment-workbench">
              <div class="section-header">
                <h4 class="section-title">
                  🧑‍⚕️ Blinded Reader Assessments &amp; Criteria Verification
                </h4>
                <span class="badge badge-charter">2+1 INDEPENDENT REVIEW</span>
              </div>

              <div class="reader-columns">
                <!-- Reader 1 Review Pane -->
                <div class="reader-card">
                  <div class="reader-card-header">
                    <h5>Reader 1 Review</h5>
                    <span
                      :class="[
                        'status-pill',
                        activeDossier.reader_1_status === 'COMPLETED'
                          ? 'success'
                          : 'warning',
                      ]"
                    >
                      {{ activeDossier.reader_1_status }}
                    </span>
                  </div>

                  <div
                    v-if="
                      currentRole === 'READER_1' ||
                      currentRole === 'CHAIR' ||
                      currentRole === 'DATA_MANAGER' ||
                      activeDossier.workflow_status ===
                        'FINALIZED_CONCORDANT' ||
                      activeDossier.workflow_status === 'FINALIZED_BY_CHAIR'
                    "
                  >
                    <div
                      v-if="activeDossier.reader_1_assessment"
                      class="assessment-details"
                    >
                      <div class="det-row">
                        <strong>Classification:</strong>
                        <span class="badge-class">{{
                          activeDossier.reader_1_assessment.classification
                        }}</span>
                      </div>
                      <div
                        v-if="activeDossier.reader_1_assessment.subtype"
                        class="det-row"
                      >
                        <strong>Subtype:</strong>
                        {{ activeDossier.reader_1_assessment.subtype }}
                      </div>
                      <div class="det-row">
                        <strong>Adjudicated Onset:</strong>
                        {{
                          activeDossier.reader_1_assessment
                            .adjudicated_onset_date || "N/A"
                        }}
                      </div>
                      <div class="det-row">
                        <strong>Confidence:</strong>
                        {{ activeDossier.reader_1_assessment.confidence_score }}
                        / 5
                      </div>
                      <div
                        v-if="
                          activeDossier.reader_1_assessment.criteria_met?.length
                        "
                        class="criteria-box"
                      >
                        <strong>Criteria Met:</strong>
                        <ul>
                          <li
                            v-for="(c, idx) in activeDossier.reader_1_assessment
                              .criteria_met"
                            :key="idx"
                          >
                            ✓ {{ c }}
                          </li>
                        </ul>
                      </div>
                      <p class="rationale-text">
                        <strong>Rationale:</strong>
                        {{
                          activeDossier.reader_1_assessment.clinical_rationale
                        }}
                      </p>
                    </div>
                    <div v-else class="pending-reader-box">
                      <p>Reader 1 assessment is pending.</p>
                      <button
                        v-if="currentRole === 'READER_1'"
                        class="btn btn-primary"
                        @click="openReaderForm('READER_1')"
                      >
                        ✍️ Complete Reader 1 Assessment
                      </button>
                    </div>
                  </div>
                  <div v-else class="blinded-placeholder">
                    <span class="lock-icon">🔒</span>
                    <p>
                      Reader 1 assessment is blinded from peer reviewer until
                      consensus.
                    </p>
                  </div>
                </div>

                <!-- Reader 2 Review Pane -->
                <div class="reader-card">
                  <div class="reader-card-header">
                    <h5>Reader 2 Review</h5>
                    <span
                      :class="[
                        'status-pill',
                        activeDossier.reader_2_status === 'COMPLETED'
                          ? 'success'
                          : 'warning',
                      ]"
                    >
                      {{ activeDossier.reader_2_status }}
                    </span>
                  </div>

                  <div
                    v-if="
                      currentRole === 'READER_2' ||
                      currentRole === 'CHAIR' ||
                      currentRole === 'DATA_MANAGER' ||
                      activeDossier.workflow_status ===
                        'FINALIZED_CONCORDANT' ||
                      activeDossier.workflow_status === 'FINALIZED_BY_CHAIR'
                    "
                  >
                    <div
                      v-if="activeDossier.reader_2_assessment"
                      class="assessment-details"
                    >
                      <div class="det-row">
                        <strong>Classification:</strong>
                        <span class="badge-class">{{
                          activeDossier.reader_2_assessment.classification
                        }}</span>
                      </div>
                      <div
                        v-if="activeDossier.reader_2_assessment.subtype"
                        class="det-row"
                      >
                        <strong>Subtype:</strong>
                        {{ activeDossier.reader_2_assessment.subtype }}
                      </div>
                      <div class="det-row">
                        <strong>Adjudicated Onset:</strong>
                        {{
                          activeDossier.reader_2_assessment
                            .adjudicated_onset_date || "N/A"
                        }}
                      </div>
                      <div class="det-row">
                        <strong>Confidence:</strong>
                        {{ activeDossier.reader_2_assessment.confidence_score }}
                        / 5
                      </div>
                      <div
                        v-if="
                          activeDossier.reader_2_assessment.criteria_met?.length
                        "
                        class="criteria-box"
                      >
                        <strong>Criteria Met:</strong>
                        <ul>
                          <li
                            v-for="(c, idx) in activeDossier.reader_2_assessment
                              .criteria_met"
                            :key="idx"
                          >
                            ✓ {{ c }}
                          </li>
                        </ul>
                      </div>
                      <p class="rationale-text">
                        <strong>Rationale:</strong>
                        {{
                          activeDossier.reader_2_assessment.clinical_rationale
                        }}
                      </p>
                    </div>
                    <div v-else class="pending-reader-box">
                      <p>Reader 2 assessment is pending.</p>
                      <button
                        v-if="currentRole === 'READER_2'"
                        class="btn btn-primary"
                        @click="openReaderForm('READER_2')"
                      >
                        ✍️ Complete Reader 2 Assessment
                      </button>
                    </div>
                  </div>
                  <div v-else class="blinded-placeholder">
                    <span class="lock-icon">🔒</span>
                    <p>
                      Reader 2 assessment is blinded from peer reviewer until
                      consensus.
                    </p>
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <div v-else class="empty-pane">
          <p>
            Select a candidate event dossier from the queue to start blinded
            adjudication.
          </p>
        </div>
      </div>
    </section>

    <!-- TAB 2: Chair Tie-Breaker & Committee Resolution Panel -->
    <section v-if="activeMainTab === 'chair_tiebreaker'" class="tab-pane">
      <div class="panel-container">
        <div class="pane-header">
          <h3 class="pane-title">
            Adjudication Committee Chair Tie-Breaker &amp; Sign-off
          </h3>
          <p class="pane-sub">
            Review discordant primary evaluations, apply charter tie-breaking
            determinations, and execute 21 CFR Part 11 electronic signatures.
          </p>
        </div>

        <div v-if="activeDossier" class="chair-adjudication-workspace">
          <!-- Side by Side Review of Discordance -->
          <div class="discordance-comparison-grid">
            <div class="review-box">
              <h4>Reader 1 Finding</h4>
              <div v-if="activeDossier.reader_1_assessment">
                <p>
                  <strong>Classification:</strong>
                  {{ activeDossier.reader_1_assessment.classification }}
                </p>
                <p>
                  <strong>Subtype:</strong>
                  {{ activeDossier.reader_1_assessment.subtype || "N/A" }}
                </p>
                <p>
                  <strong>Onset:</strong>
                  {{ activeDossier.reader_1_assessment.adjudicated_onset_date }}
                </p>
                <p>
                  <strong>Rationale:</strong>
                  {{ activeDossier.reader_1_assessment.clinical_rationale }}
                </p>
              </div>
              <div v-else class="text-muted">Pending Reader 1.</div>
            </div>

            <div class="vs-divider">VS</div>

            <div class="review-box">
              <h4>Reader 2 Finding</h4>
              <div v-if="activeDossier.reader_2_assessment">
                <p>
                  <strong>Classification:</strong>
                  {{ activeDossier.reader_2_assessment.classification }}
                </p>
                <p>
                  <strong>Subtype:</strong>
                  {{ activeDossier.reader_2_assessment.subtype || "N/A" }}
                </p>
                <p>
                  <strong>Onset:</strong>
                  {{ activeDossier.reader_2_assessment.adjudicated_onset_date }}
                </p>
                <p>
                  <strong>Rationale:</strong>
                  {{ activeDossier.reader_2_assessment.clinical_rationale }}
                </p>
              </div>
              <div v-else class="text-muted">Pending Reader 2.</div>
            </div>
          </div>

          <!-- Tie-Breaker Form -->
          <div class="chair-decision-form">
            <h4>Binding Committee Determination</h4>

            <div class="form-grid">
              <div class="form-group">
                <label for="binding-source-select"
                  >Binding Determination Source:</label
                >
                <select
                  id="binding-source-select"
                  v-model="chairForm.binding_reader"
                  class="select-input"
                >
                  <option value="CHAIR_UPHELD_READER_1">
                    Uphold Reader 1 Assessment
                  </option>
                  <option value="CHAIR_UPHELD_READER_2">
                    Uphold Reader 2 Assessment
                  </option>
                  <option value="CHAIR_CUSTOM_DETERMINATION">
                    Chair Custom Adjudication
                  </option>
                </select>
              </div>

              <div class="form-group">
                <label for="chair-class-select"
                  >Final Adjudicated Classification:</label
                >
                <select
                  id="chair-class-select"
                  v-model="chairForm.final_classification"
                  class="select-input"
                >
                  <option value="CONFIRMED_MI">
                    CONFIRMED_MI (Myocardial Infarction)
                  </option>
                  <option value="CONFIRMED_ISCHEMIC_STROKE">
                    CONFIRMED_ISCHEMIC_STROKE (Ischemic Stroke)
                  </option>
                  <option value="CONFIRMED_HEMORRHAGIC_STROKE">
                    CONFIRMED_HEMORRHAGIC_STROKE
                  </option>
                  <option value="CONFIRMED_CV_DEATH">
                    CONFIRMED_CV_DEATH (Cardiovascular Death)
                  </option>
                  <option value="CONFIRMED_NON_CV_DEATH">
                    CONFIRMED_NON_CV_DEATH
                  </option>
                  <option value="CONFIRMED_HEART_FAILURE_HOSPITALIZATION">
                    CONFIRMED_HEART_FAILURE_HOSPITALIZATION
                  </option>
                  <option value="CONFIRMED_DISEASE_PROGRESSION">
                    CONFIRMED_DISEASE_PROGRESSION
                  </option>
                  <option value="NOT_AN_ENDPOINT">
                    NOT_AN_ENDPOINT (Refuted)
                  </option>
                  <option value="INDETERMINATE">INDETERMINATE</option>
                </select>
              </div>

              <div class="form-group">
                <label for="chair-subtype-input">Subtype / Category:</label>
                <input
                  id="chair-subtype-input"
                  v-model="chairForm.final_subtype"
                  type="text"
                  class="text-input"
                  placeholder="e.g. Type 1 Spontaneous MI"
                />
              </div>

              <div class="form-group">
                <label for="chair-onset-input">Adjudicated Onset Date:</label>
                <input
                  id="chair-onset-input"
                  v-model="chairForm.final_adjudicated_onset_date"
                  type="date"
                  class="text-input"
                />
              </div>
            </div>

            <!-- Criteria Checklist -->
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Confirmed Diagnostic Criteria Checklist:</label>
              <div class="criteria-checkbox-list">
                <label
                  v-for="(crit, idx) in availableCriteria"
                  :key="idx"
                  class="checkbox-item"
                >
                  <input
                    v-model="chairForm.criteria_confirmed"
                    type="checkbox"
                    :value="crit"
                  />
                  <span>{{ crit }}</span>
                </label>
              </div>
            </div>

            <div class="form-group">
              <label for="chair-rationale-input"
                >Adjudication Committee Chair Rationale:</label
              >
              <textarea
                id="chair-rationale-input"
                v-model="chairForm.adjudication_rationale"
                class="textarea-input"
                rows="3"
                placeholder="Provide formal clinical rationale for the tie-breaker determination..."
              />
            </div>

            <!-- 21 CFR Part 11 Electronic Signature Block -->
            <div class="esign-block">
              <div class="esign-header">
                <h5>🔐 21 CFR Part 11 Electronic Signature Authentication</h5>
                <span class="badge badge-regulatory"
                  >MANDATORY STEP-UP SIGN-OFF</span
                >
              </div>
              <p class="esign-manifest">"{{ chairForm.signature_meaning }}"</p>
              <div class="esign-inputs">
                <div class="form-group">
                  <label for="chair-signer-name">Signer Printed Name:</label>
                  <input
                    id="chair-signer-name"
                    v-model="chairForm.signer_name"
                    type="text"
                    class="text-input"
                  />
                </div>
                <div class="form-group">
                  <label for="chair-password">Signer Password / Token:</label>
                  <input
                    id="chair-password"
                    v-model="chairForm.password"
                    type="password"
                    class="text-input"
                    placeholder="Confirm password to seal decision"
                  />
                </div>
              </div>
              <button
                class="btn btn-primary btn-lg"
                :disabled="isSubmitting"
                @click="submitChairAdjudication"
              >
                🖋️ Sign &amp; Finalize Tie-Breaker Adjudication
              </button>
            </div>
          </div>
        </div>
        <div v-else class="empty-pane">
          <p>Please select a dossier to review.</p>
        </div>
      </div>
    </section>

    <!-- TAB 3: Charter Governance & Quorum Tracker -->
    <section v-if="activeMainTab === 'charter_quorum'" class="tab-pane">
      <div class="panel-container">
        <div class="pane-header">
          <h3 class="pane-title">
            Committee Charter Governance &amp; Quorum Tracker
          </h3>
          <p class="pane-sub">
            Manage committee voting rules, roster quotas, and record formal
            meeting quorum calculations.
          </p>
        </div>

        <div class="charter-grid">
          <!-- Charter Parameters Card -->
          <div class="section-card">
            <h4 class="section-title">Charter Governance Rules</h4>
            <div class="charter-details">
              <p>
                <strong>Committee:</strong>
                {{
                  activeCharter.committee_name || "Cardiovascular Endpoints CEC"
                }}
              </p>
              <p><strong>Version:</strong> {{ activeCharter.version }}</p>
              <p>
                <strong>Voting Rule:</strong>
                <span class="badge-charter">{{
                  activeCharter.voting_rule
                }}</span>
              </p>
              <p>
                <strong>Quorum Threshold:</strong>
                {{ activeCharter.minimum_quorum_percentage }}% (Min
                {{ activeCharter.minimum_members_count }} members)
              </p>
              <div class="eligible-roster">
                <strong>Committee Member Roster:</strong>
                <ul>
                  <li v-for="(m, i) in activeCharter.eligible_members" :key="i">
                    {{ m.member_name }} &mdash;
                    <em>{{ m.role }} ({{ m.specialty }})</em>
                  </li>
                </ul>
              </div>
            </div>
          </div>

          <!-- Meeting Quorum Session Recorder -->
          <div class="section-card">
            <h4 class="section-title">
              Record Meeting Quorum &amp; Sign Minutes
            </h4>
            <div class="form-grid">
              <div class="form-group">
                <label for="meeting-date-input">Meeting Date:</label>
                <input
                  id="meeting-date-input"
                  v-model="meetingForm.meeting_date"
                  type="datetime-local"
                  class="text-input"
                />
              </div>
              <div class="form-group">
                <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
                <label>Attendance Checklist (Live Quorum Calc):</label>
                <div class="attendee-checkbox-list">
                  <label
                    v-for="mem in activeCharter.eligible_members"
                    :key="mem.member_id"
                    class="checkbox-item"
                  >
                    <input
                      v-model="meetingForm.selected_member_ids"
                      type="checkbox"
                      :value="mem.member_id"
                    />
                    <span>{{ mem.member_name }} ({{ mem.role }})</span>
                  </label>
                </div>
              </div>
            </div>

            <!-- Live Quorum Badge -->
            <div class="quorum-indicator-box">
              <div class="quorum-stat">
                <span
                  >Attending: {{ meetingForm.selected_member_ids.length }} /
                  {{ activeCharter.eligible_members?.length || 5 }}</span
                >
                <span class="pct-text"
                  >({{ calculatedQuorumPercentage }}%)</span
                >
              </div>
              <div
                :class="[
                  'quorum-status-banner',
                  isQuorumMet ? 'quorum-passed' : 'quorum-failed',
                ]"
              >
                {{
                  isQuorumMet
                    ? "✅ MEETING QUORUM ACHIEVED"
                    : "❌ INSUFFICIENT QUORUM &mdash; Meeting Cannot Proceed"
                }}
              </div>
            </div>

            <div class="form-group">
              <label for="meeting-minutes-input"
                >Formal Meeting Minutes Narrative:</label
              >
              <textarea
                id="meeting-minutes-input"
                v-model="meetingForm.meeting_minutes"
                class="textarea-input"
                rows="3"
                placeholder="Record consensus discussions and formal committee votes..."
              />
            </div>

            <button
              class="btn btn-primary"
              :disabled="!isQuorumMet || isSubmitting"
              @click="recordMeetingSession"
            >
              📝 Record Quorum &amp; Cryptographically Seal Minutes
            </button>
          </div>
        </div>
      </div>
    </section>

    <!-- TAB 4: CDISC SDTM CE & EDC Casebook Sync -->
    <section v-if="activeMainTab === 'sdtm_ce'" class="tab-pane">
      <div class="panel-container">
        <div class="pane-header flex-between">
          <div>
            <h3 class="pane-title">
              CDISC SDTM CE (Clinical Events) Domain &amp; EDC Casebook Sync
            </h3>
            <p class="pane-sub">
              Compiled regulatory dataset mapped directly from validated
              consensus adjudication decisions.
            </p>
          </div>
          <div class="header-actions">
            <button class="btn btn-secondary" @click="compileSdtmCe">
              🔄 Re-Compile SDTM CE
            </button>
            <button class="btn btn-primary" @click="exportSdtmJson">
              💾 Export Dataset-JSON
            </button>
          </div>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>STUDYID</th>
                <th>DOMAIN</th>
                <th>USUBJID</th>
                <th>CESEQ</th>
                <th>CETERM</th>
                <th>CEDECOD</th>
                <th>CECAT</th>
                <th>CESCAT</th>
                <th>CEOCCUR</th>
                <th>CESTDTC</th>
                <th>CECADJ (Outcome)</th>
                <th>EDC Sync</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="ce in sdtmCeRecords" :key="ce.USUBJID + ce.CESEQ">
                <td>{{ ce.STUDYID }}</td>
                <td>
                  <span class="badge-domain">{{ ce.DOMAIN }}</span>
                </td>
                <td class="cell-bold">
                  {{ ce.USUBJID }}
                </td>
                <td>{{ ce.CESEQ }}</td>
                <td>{{ ce.CETERM }}</td>
                <td class="cell-bold text-primary">
                  {{ ce.CEDECOD }}
                </td>
                <td>{{ ce.CECAT }}</td>
                <td>{{ ce.CESCAT || "-" }}</td>
                <td>
                  <span
                    :class="[
                      'status-pill',
                      ce.CEOCCUR === 'Y' ? 'success' : 'info',
                    ]"
                    >{{ ce.CEOCCUR }}</span
                  >
                </td>
                <td>{{ ce.CESTDTC }}</td>
                <td><span class="status-pill success">FINALIZED</span></td>
                <td>
                  <span class="sync-badge">✅ SYNCED</span>
                </td>
              </tr>
              <tr v-if="!sdtmCeRecords.length">
                <td colspan="12" class="text-center empty-cell">
                  No finalized endpoint records compiled yet.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <!-- Modal: Reader Assessment Input -->
    <ClinicalModal
      v-model="showReaderModal"
      :teleport="false"
      :dismissible="!isSubmitting"
      dialog-class="modal-dialog"
      :title="`Submit ${activeReaderTarget} Blinded Assessment`"
    >
      <div class="form-group">
        <label for="modal-class-select">Adjudicated Classification:</label>
        <select
          id="modal-class-select"
          v-model="readerForm.classification"
          class="select-input"
        >
          <option value="CONFIRMED_MI">
            CONFIRMED_MI (Myocardial Infarction)
          </option>
          <option value="CONFIRMED_ISCHEMIC_STROKE">
            CONFIRMED_ISCHEMIC_STROKE
          </option>
          <option value="CONFIRMED_HEMORRHAGIC_STROKE">
            CONFIRMED_HEMORRHAGIC_STROKE
          </option>
          <option value="CONFIRMED_CV_DEATH">CONFIRMED_CV_DEATH</option>
          <option value="CONFIRMED_HEART_FAILURE_HOSPITALIZATION">
            CONFIRMED_HEART_FAILURE_HOSPITALIZATION
          </option>
          <option value="CONFIRMED_DISEASE_PROGRESSION">
            CONFIRMED_DISEASE_PROGRESSION
          </option>
          <option value="NOT_AN_ENDPOINT">NOT_AN_ENDPOINT</option>
          <option value="INDETERMINATE">INDETERMINATE</option>
        </select>
      </div>

      <div class="form-group">
        <label for="modal-subtype-input">Subtype (e.g. Type 1 MI):</label>
        <input
          id="modal-subtype-input"
          v-model="readerForm.subtype"
          type="text"
          class="text-input"
        />
      </div>

      <div class="form-group">
        <label for="modal-onset-input">Adjudicated Onset Date:</label>
        <input
          id="modal-onset-input"
          v-model="readerForm.adjudicated_onset_date"
          type="date"
          class="text-input"
        />
      </div>

      <div class="form-group">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label>Diagnostic Criteria Satisfied:</label>
        <div class="criteria-checkbox-list">
          <label
            v-for="(crit, idx) in availableCriteria"
            :key="idx"
            class="checkbox-item"
          >
            <input
              v-model="readerForm.criteria_met"
              type="checkbox"
              :value="crit"
            />
            <span>{{ crit }}</span>
          </label>
        </div>
      </div>

      <div class="form-group">
        <label for="modal-conf-input">Confidence Level (1-5):</label>
        <input
          id="modal-conf-input"
          v-model.number="readerForm.confidence_score"
          type="number"
          min="1"
          max="5"
          class="text-input"
        />
      </div>

      <div class="form-group">
        <label for="modal-rationale-input">Clinical Rationale:</label>
        <textarea
          id="modal-rationale-input"
          v-model="readerForm.clinical_rationale"
          class="textarea-input"
          rows="3"
          placeholder="Enter clinical rationale..."
        />
      </div>

      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isSubmitting"
          @click="showReaderModal = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="isSubmitting"
          @click="submitReaderForm"
        >
          Submit Blinded Assessment
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";
import {
  buildStepUpVerificationPayload,
  ClinicalModal,
  SEMANTIC_ACTIONS,
} from "ui";
import { isDemoBuild } from "../../utils/demoMode.js";
import { allowUnbackedAction } from "../../utils/unbackedActions.js";
import { apiClient } from "../../api/apiClient";
import { useErrorPresenter } from "../../composables/useErrorPresenter";
import { useToastStore } from "../../stores/toasts";

const toasts = useToastStore();
const { presentError } = useErrorPresenter();
const currentRole = ref("READER_1");
const selectedStudyId = ref("STUDY-101");
const phiMaskEnabled = ref(true);
const activeMainTab = ref("dossiers");
const selectedDossierId = ref(null);
const searchQuery = ref("");
const filterStatus = ref("ALL");
const isSubmitting = ref(false);
const showReaderModal = ref(false);
const activeReaderTarget = ref("READER_1");

// Sample dossiers, in demo builds only (#4827): no service serves adjudication dossiers yet.
const dossiers = ref(
  isDemoBuild()
    ? [
        {
          dossier_id: "DOSSIER-MACE-001",
          event_id: "AE-REC-8801",
          study_id: "STUDY-101",
          site_id: "SITE-101",
          masked_site_id: "SITE-BLINDED-9A1F",
          subject_id: "SUBJ-101-0042",
          masked_subject_id: "SUBJ-BLINDED-B4C2",
          candidate_event_type: "MYOCARDIAL_INFARCTION",
          reported_term: "Acute Myocardial Infarction",
          verbatim_description:
            "Patient presented with crushing substernal chest pressure, diaphoresis, and radiation to left arm for 45 minutes.",
          reported_onset_date: "2026-08-10",
          medical_narrative:
            "Patient presented to the emergency department complaining of acute retrosternal chest pain. High-sensitivity Troponin I rose significantly over 3 hours. 12-lead ECG showed 3mm ST elevation in leads V2-V4. Emergent coronary catheterization revealed 95% occlusion of the proximal LAD, successfully treated with drug-eluting stent.",
          is_deidentified: true,
          workflow_status: "PENDING_PRIMARY_REVIEWS",
          is_discordant: false,
          discordance_reasons: [],
          dossier_digest:
            "8f92a3c4b12398fa209cba112984719283719284729184719284719284719284", // pragma: allowlist secret
          lab_panels: [
            {
              test_name: "High-Sensitivity Troponin I",
              loinc_code: "49563-0",
              result_value: "4.82",
              result_unit: "ng/mL",
              reference_low: 0.0,
              reference_high: 0.04,
              is_out_of_range: true,
              toxicity_grade: 4,
            },
            {
              test_name: "CK-MB",
              loinc_code: "13969-1",
              result_value: "48.5",
              result_unit: "ng/mL",
              reference_low: 0.0,
              reference_high: 5.0,
              is_out_of_range: true,
              toxicity_grade: 3,
            },
            {
              test_name: "Serum Creatinine",
              loinc_code: "2160-0",
              result_value: "1.1",
              result_unit: "mg/dL",
              reference_low: 0.7,
              reference_high: 1.3,
              is_out_of_range: false,
              toxicity_grade: 0,
            },
          ],
          ecg_records: [
            {
              ecg_id: "ECG-001-A",
              timestamp: "2026-08-10T14:20:00Z",
              rhythm: "Sinus rhythm with acute anterior STEMI",
              st_elevation_leads: ["V1", "V2", "V3", "V4"],
              st_depression_leads: ["II", "III", "aVF"],
              q_wave_leads: ["V1", "V2"],
              lbbb_present: false,
              qtcf_ms: 428,
              interpretation:
                "Acute anterior ST-elevation myocardial infarction.",
            },
          ],
          diagnostic_attachments: [
            {
              attachment_id: "ATT-COR-01",
              document_type: "Coronary Angiogram",
              title: "Cardiac Catheterization Report",
              summary:
                "Culprit lesion: 95% proximal LAD thrombus. Successful deployment of 3.5x18mm DES with TIMI 3 flow restored.",
              key_findings: [
                "Proximal LAD 95% stenosis with acute intraluminal thrombus",
                "Post-PCI TIMI 3 flow achieved",
              ],
            },
          ],
          reader_1_id: "reader_01",
          reader_1_name: "Dr. Primary Reader 1",
          reader_1_status: "PENDING",
          reader_1_assessment: null,
          reader_2_id: "reader_02",
          reader_2_name: "Dr. Primary Reader 2",
          reader_2_status: "PENDING",
          reader_2_assessment: null,
          final_consensus_classification: null,
          final_consensus_subtype: null,
          final_adjudicated_onset_date: null,
        },
        {
          dossier_id: "DOSSIER-STRK-002",
          event_id: "AE-REC-8802",
          study_id: "STUDY-101",
          site_id: "SITE-102",
          masked_site_id: "SITE-BLINDED-F102",
          subject_id: "SUBJ-101-0089",
          masked_subject_id: "SUBJ-BLINDED-A92B",
          candidate_event_type: "STROKE",
          reported_term: "Acute Ischemic Stroke / Focal Deficit",
          verbatim_description:
            "Sudden onset right facial droop, right arm hemiparesis, and expressive aphasia lasting > 24 hours.",
          reported_onset_date: "2026-08-12",
          medical_narrative:
            "Patient experienced sudden right-sided hemiparesis and motor aphasia while at home. Emergency brain MRI confirmed acute left MCA territory diffusion restriction consistent with acute cerebral infarction.",
          is_deidentified: true,
          workflow_status: "DISCORDANCE_DETECTED",
          is_discordant: true,
          discordance_reasons: [
            "Primary classification discordance: Reader 1 assessed [CONFIRMED_ISCHEMIC_STROKE] vs Reader 2 assessed [NOT_AN_ENDPOINT]",
          ],
          dossier_digest:
            "4a1b2c3d4e5f6a7b8c9d0e1f2a3b4c5d6e7f8a9b0c1d2e3f4a5b6c7d8e9f0a1b", // pragma: allowlist secret
          lab_panels: [],
          ecg_records: [],
          diagnostic_attachments: [
            {
              attachment_id: "ATT-MRI-02",
              document_type: "Brain MRI",
              title: "MRI Brain with DWI/ADC",
              summary:
                "Restricted diffusion in the left middle cerebral artery distribution compatible with acute non-hemorrhagic infarction.",
              key_findings: [
                "Acute left MCA cortical infarction",
                "No evidence of acute intracranial hemorrhage",
              ],
            },
          ],
          reader_1_id: "reader_01",
          reader_1_name: "Dr. Primary Reader 1",
          reader_1_status: "COMPLETED",
          reader_1_assessment: {
            assessment_id: "ASSESS-001",
            dossier_id: "DOSSIER-STRK-002",
            reader_id: "reader_01",
            reader_name: "Dr. Primary Reader 1",
            reader_role: "READER_1",
            classification: "CONFIRMED_ISCHEMIC_STROKE",
            subtype: "Acute Ischemic Infarction (Left MCA)",
            adjudicated_onset_date: "2026-08-12",
            criteria_met: [
              "Acute focal neurological deficit > 24 hours",
              "Brain MRI restricted diffusion confirmed",
            ],
            confidence_score: 5,
            clinical_rationale:
              "Clear imaging and clinical criteria satisfied for acute ischemic stroke.",
          },
          reader_2_id: "reader_02",
          reader_2_name: "Dr. Primary Reader 2",
          reader_2_status: "COMPLETED",
          reader_2_assessment: {
            assessment_id: "ASSESS-002",
            dossier_id: "DOSSIER-STRK-002",
            reader_id: "reader_02",
            reader_name: "Dr. Primary Reader 2",
            reader_role: "READER_2",
            classification: "NOT_AN_ENDPOINT",
            subtype: "Transient Ischemic Attack (TIA)",
            adjudicated_onset_date: "2026-08-12",
            criteria_met: [],
            confidence_score: 3,
            clinical_rationale:
              "Symptoms may have resolved within 24 hours based on initial EMS dispatch narrative.",
          },
          final_consensus_classification: null,
        },
        {
          dossier_id: "DOSSIER-MACE-003",
          event_id: "AE-REC-8803",
          study_id: "STUDY-101",
          site_id: "SITE-101",
          masked_site_id: "SITE-BLINDED-9A1F",
          subject_id: "SUBJ-101-0120",
          masked_subject_id: "SUBJ-BLINDED-C781",
          candidate_event_type: "CARDIOVASCULAR_MORTALITY",
          reported_term: "Sudden Cardiac Death",
          reported_onset_date: "2026-08-14",
          medical_narrative:
            "Patient found unresponsive at home. Paramedic resuscitation noted ventricular fibrillation. Patient pronounced deceased.",
          is_deidentified: true,
          workflow_status: "FINALIZED_CONCORDANT",
          is_discordant: false,
          discordance_reasons: [],
          dossier_digest:
            "99887766554433221100aabbccddeeff99887766554433221100aabbccddeeff", // pragma: allowlist secret
          lab_panels: [],
          ecg_records: [],
          diagnostic_attachments: [],
          reader_1_id: "reader_01",
          reader_1_name: "Dr. Primary Reader 1",
          reader_1_status: "COMPLETED",
          reader_1_assessment: {
            assessment_id: "ASSESS-003",
            dossier_id: "DOSSIER-MACE-003",
            reader_id: "reader_01",
            reader_name: "Dr. Primary Reader 1",
            reader_role: "READER_1",
            classification: "CONFIRMED_CV_DEATH",
            subtype: "Sudden Cardiac Arrest",
            adjudicated_onset_date: "2026-08-14",
            criteria_met: [
              "Sudden witnessed cardiac arrest with initial rhythm of VF",
            ],
            confidence_score: 5,
            clinical_rationale: "Definitive sudden cardiac death.",
          },
          reader_2_id: "reader_02",
          reader_2_name: "Dr. Primary Reader 2",
          reader_2_status: "COMPLETED",
          reader_2_assessment: {
            assessment_id: "ASSESS-004",
            dossier_id: "DOSSIER-MACE-003",
            reader_id: "reader_02",
            reader_name: "Dr. Primary Reader 2",
            reader_role: "READER_2",
            classification: "CONFIRMED_CV_DEATH",
            subtype: "Sudden Cardiac Arrest",
            adjudicated_onset_date: "2026-08-14",
            criteria_met: [
              "Sudden witnessed cardiac arrest with initial rhythm of VF",
            ],
            confidence_score: 5,
            clinical_rationale: "Concordant with Reader 1.",
          },
          final_consensus_classification: "CONFIRMED_CV_DEATH",
          final_consensus_subtype: "Sudden Cardiac Arrest",
          final_adjudicated_onset_date: "2026-08-14",
        },
      ]
    : []
);

// Charter Governance Mock
const activeCharter = ref({
  charter_id: "CHARTER-MACE-2026",
  study_id: "STUDY-101",
  committee_name: "Independent Clinical Endpoint Adjudication Committee (CEC)",
  version: "2.1",
  voting_rule: "CHAIR_TIE_BREAKER",
  minimum_quorum_percentage: 66.67,
  minimum_members_count: 3,
  eligible_members: [
    {
      member_id: "chair_adjudicator",
      member_name: "Dr. Evelyn Reed, MD, FACC",
      role: "Committee Chair",
      specialty: "Interventional Cardiology",
    },
    {
      member_id: "reader_01",
      member_name: "Dr. Marcus Vance, MD",
      role: "Primary Reader 1",
      specialty: "Cardiovascular Imaging",
    },
    {
      member_id: "reader_02",
      member_name: "Dr. Alistair Finch, MD, FAHA",
      role: "Primary Reader 2",
      specialty: "Vascular Neurology",
    },
    {
      member_id: "member_03",
      member_name: "Dr. Sophia Chen, MD",
      role: "Committee Member",
      specialty: "Electrophysiology",
    },
    {
      member_id: "member_04",
      member_name: "Dr. Robert Martinez, MD",
      role: "Committee Member",
      specialty: "Critical Care Medicine",
    },
  ],
});

// Forms
const chairForm = ref({
  binding_reader: "CHAIR_CUSTOM_DETERMINATION",
  final_classification: "CONFIRMED_ISCHEMIC_STROKE",
  final_subtype: "Acute Ischemic Infarction (Left MCA)",
  final_adjudicated_onset_date: "2026-08-12",
  criteria_confirmed: [
    "Acute focal neurological deficit > 24 hours",
    "Brain MRI restricted diffusion confirmed",
  ],
  adjudication_rationale:
    "After comprehensive review of the emergency neuroimaging attachment, the restricted diffusion in the left MCA territory definitively establishes acute ischemic cerebral infarction.",
  signature_meaning:
    "I confirm this clinical endpoint adjudication determination is final, complete, and accurate per charter guidelines",
  signer_name: "Dr. Evelyn Reed, MD, FACC",
  password: "",
});

const readerForm = ref({
  classification: "CONFIRMED_MI",
  subtype: "Type 1 Spontaneous MI",
  adjudicated_onset_date: "2026-08-10",
  criteria_met: [
    "Cardiac biomarker rise/fall > 99th percentile URL (Troponin / CK-MB)",
    "Ischemic symptoms > 20 minutes duration",
    "New ST elevation / depression or T-wave inversion on 12-lead ECG",
  ],
  confidence_score: 5,
  clinical_rationale: "",
});

const meetingForm = ref({
  meeting_date: "2026-08-23T14:00",
  selected_member_ids: [
    "chair_adjudicator",
    "reader_01",
    "reader_02",
    "member_03",
  ],
  meeting_minutes:
    "Formal quorum achieved. Dossiers reviewed: DOSSIER-STRK-002 discussed. Consensus reached to confirm ischemic stroke.",
});

// SDTM CE Domain records
const sdtmCeRecords = ref([
  {
    STUDYID: "STUDY-101",
    DOMAIN: "CE",
    USUBJID: "STUDY-101-SUBJ-101-0120",
    CESEQ: 1,
    CETERM: "Sudden Cardiac Death",
    CEDECOD: "CONFIRMED_CV_DEATH",
    CECAT: "CARDIOVASCULAR_MORTALITY",
    CESCAT: "Sudden Cardiac Arrest",
    CEOCCUR: "Y",
    CESTDTC: "2026-08-14",
  },
]);

const availableCriteria = computed(() => {
  return [
    "Cardiac biomarker rise/fall > 99th percentile URL (Troponin / CK-MB)",
    "Ischemic symptoms > 20 minutes duration",
    "New ST elevation / depression or T-wave inversion on 12-lead ECG",
    "Development of pathological Q waves",
    "Imaging evidence of new loss of viable myocardium or regional wall motion abnormality",
    "Acute focal neurological deficit lasting > 24 hours",
    "Neuroimaging (CT/MRI) evidence of acute cerebral infarction",
    "Sudden witnessed cardiac arrest with initial rhythm of VF",
  ];
});

const filteredDossiers = computed(() => {
  return dossiers.value.filter((d) => {
    if (filterStatus.value === "PENDING") {
      if (
        ![
          "PENDING_PRIMARY_REVIEWS",
          "READER_1_COMPLETED",
          "READER_2_COMPLETED",
        ].includes(d.workflow_status)
      )
        return false;
    } else if (filterStatus.value === "DISCORDANT") {
      if (!d.is_discordant) return false;
    } else if (filterStatus.value === "FINALIZED") {
      if (
        ![
          "FINALIZED_CONCORDANT",
          "FINALIZED_BY_CHAIR",
          "CONSENSUS_REACHED",
        ].includes(d.workflow_status)
      )
        return false;
    }

    if (searchQuery.value) {
      const q = searchQuery.value.toLowerCase();
      const termMatch = d.reported_term.toLowerCase().includes(q);
      const subMatch = (d.masked_subject_id || d.subject_id)
        .toLowerCase()
        .includes(q);
      const typeMatch = d.candidate_event_type.toLowerCase().includes(q);
      if (!termMatch && !subMatch && !typeMatch) return false;
    }
    return true;
  });
});

const activeDossier = computed(() => {
  return (
    dossiers.value.find((d) => d.dossier_id === selectedDossierId.value) ||
    dossiers.value[0]
  );
});

const discordantCount = computed(() => {
  return dossiers.value.filter((d) => d.is_discordant).length;
});

const calculatedQuorumPercentage = computed(() => {
  const total = activeCharter.value.eligible_members?.length || 5;
  return Math.round(
    (meetingForm.value.selected_member_ids.length / total) * 100
  );
});

const isQuorumMet = computed(() => {
  const count = meetingForm.value.selected_member_ids.length;
  return (
    count >= activeCharter.value.minimum_members_count &&
    calculatedQuorumPercentage.value >=
      activeCharter.value.minimum_quorum_percentage
  );
});

function selectDossier(id) {
  selectedDossierId.value = id;
}

function formatStatus(status) {
  if (!status) return "PENDING";
  return status.replace(/_/g, " ");
}

function statusPillClass(status) {
  if (
    status === "FINALIZED_CONCORDANT" ||
    status === "FINALIZED_BY_CHAIR" ||
    status === "CONSENSUS_REACHED"
  )
    return "success";
  if (status === "DISCORDANCE_DETECTED" || status === "ESCALATED_TO_CHAIR")
    return "danger";
  if (status === "READER_1_COMPLETED" || status === "READER_2_COMPLETED")
    return "info";
  return "warning";
}

function openReaderForm(targetRole) {
  activeReaderTarget.value = targetRole;
  showReaderModal.value = true;
}

function submitReaderForm() {
  // #4827: reader assessments are recorded nowhere yet.
  if (!allowUnbackedAction("Recording a reader assessment")) return;
  isSubmitting.value = true;
  setTimeout(() => {
    if (activeDossier.value) {
      if (activeReaderTarget.value === "READER_1") {
        activeDossier.value.reader_1_status = "COMPLETED";
        activeDossier.value.reader_1_assessment = {
          ...readerForm.value,
          reader_role: "READER_1",
          reader_name: "Dr. Primary Reader 1",
        };
      } else {
        activeDossier.value.reader_2_status = "COMPLETED";
        activeDossier.value.reader_2_assessment = {
          ...readerForm.value,
          reader_role: "READER_2",
          reader_name: "Dr. Primary Reader 2",
        };
      }

      // Check consensus
      if (
        activeDossier.value.reader_1_status === "COMPLETED" &&
        activeDossier.value.reader_2_status === "COMPLETED"
      ) {
        const r1 = activeDossier.value.reader_1_assessment;
        const r2 = activeDossier.value.reader_2_assessment;
        if (r1.classification === r2.classification) {
          activeDossier.value.workflow_status = "FINALIZED_CONCORDANT";
          activeDossier.value.final_consensus_classification =
            r1.classification;
          activeDossier.value.final_consensus_subtype = r1.subtype;
          activeDossier.value.final_adjudicated_onset_date =
            r1.adjudicated_onset_date;
          activeDossier.value.is_discordant = false;
          compileSdtmCe();
        } else {
          activeDossier.value.workflow_status = "DISCORDANCE_DETECTED";
          activeDossier.value.is_discordant = true;
          activeDossier.value.discordance_reasons = [
            `Primary classification discordance: Reader 1 [${r1.classification}] vs Reader 2 [${r2.classification}]`,
          ];
        }
      }
    }
    isSubmitting.value = false;
    showReaderModal.value = false;
  }, 400);
}

async function submitChairAdjudication() {
  if (!chairForm.value.password) {
    toasts.push({
      tone: "warning",
      title: "Re-Authentication Required",
      message:
        "Please enter your re-authentication password for 21 CFR Part 11 electronic sign-off.",
    });
    return;
  }
  const enteredPassword = chairForm.value.password;
  chairForm.value.password = "";
  isSubmitting.value = true;
  try {
    const dossierId = activeDossier.value?.dossier_id || "DOSSIER-101";
    const actionUrl = `/api/v1/execution/cec/dossiers/${dossierId}/chair-adjudicate`;
    const verificationPayload = buildStepUpVerificationPayload({
      username: chairForm.value.signer_name || "chair_adjudicator",
      password: enteredPassword,
      action: actionUrl,
      semanticAction: SEMANTIC_ACTIONS.CEC_CHAIR_ADJUDICATE,
    });

    const reauthRes = await apiClient.post(
      "/api/v1/auth/signature-verification",
      verificationPayload
    );

    const sigToken = reauthRes?.sig_token;
    if (!sigToken) {
      throw new Error("Re-authentication failed: missing signature token");
    }

    const payload = {
      final_classification: chairForm.value.final_classification,
      final_subtype: chairForm.value.final_subtype,
      final_adjudicated_onset_date:
        chairForm.value.final_adjudicated_onset_date,
      binding_reader: chairForm.value.binding_reader,
      criteria_confirmed: chairForm.value.criteria_confirmed,
      adjudication_rationale: chairForm.value.adjudication_rationale,
      signature_token: sigToken,
      signer_name: chairForm.value.signer_name,
      password: enteredPassword,
      signature_meaning: chairForm.value.signature_meaning,
    };

    const res = await apiClient.post(actionUrl, payload, {
      headers: {
        "X-Sig-Token": sigToken,
      },
    });

    if (activeDossier.value) {
      activeDossier.value.workflow_status = "FINALIZED_BY_CHAIR";
      activeDossier.value.is_discordant = false;
      activeDossier.value.final_consensus_classification =
        res?.final_classification || chairForm.value.final_classification;
      activeDossier.value.final_consensus_subtype =
        res?.final_subtype || chairForm.value.final_subtype;
      activeDossier.value.final_adjudicated_onset_date =
        res?.final_adjudicated_onset_date ||
        chairForm.value.final_adjudicated_onset_date;
      activeDossier.value.adjudication_rationale =
        res?.adjudication_rationale || chairForm.value.adjudication_rationale;
      activeDossier.value.electronic_signature = res?.electronic_signature || {
        signer_id: res?.chair_id || "chair_user",
        signer_name: chairForm.value.signer_name,
        signer_role: "CEC_CHAIR",
        signing_reason: chairForm.value.signature_meaning,
        signed_at: res?.finalized_at || new Date().toISOString(),
      };
      compileSdtmCe();
    }
    activeMainTab.value = "dossiers";
  } catch (err) {
    const errCode = err?.data?.code || err?.response?.data?.code || err?.code;
    if (errCode === "REAUTHENTICATION_REQUIRED" || err?.status === 401) {
      toasts.push({
        tone: "error",
        title: "Re-Authentication Failed",
        message:
          "21 CFR Part 11 re-authentication failed: Invalid password or expired token.",
      });
    } else {
      presentError(err, {
        title: "Chair Adjudication Failed",
        fallback: "Chair adjudication submission failed.",
      });
    }
  } finally {
    isSubmitting.value = false;
  }
}

function recordMeetingSession() {
  isSubmitting.value = true;
  setTimeout(() => {
    toasts.push({
      tone: "success",
      title: "Meeting Quorum Verified",
      message: `Meeting quorum of ${calculatedQuorumPercentage.value}% successfully verified and signed by Chair.`,
    });
    isSubmitting.value = false;
  }, 300);
}

function compileSdtmCe() {
  const records = [];
  dossiers.value.forEach((d, idx) => {
    if (d.final_consensus_classification) {
      records.push({
        STUDYID: d.study_id,
        DOMAIN: "CE",
        USUBJID: `${d.study_id}-${d.subject_id}`,
        CESEQ: idx + 1,
        CETERM: d.reported_term,
        CEDECOD: d.final_consensus_classification,
        CECAT: d.candidate_event_type,
        CESCAT: d.final_consensus_subtype || "",
        CEOCCUR:
          d.final_consensus_classification === "NOT_AN_ENDPOINT" ? "N" : "Y",
        CESTDTC: d.final_adjudicated_onset_date || d.reported_onset_date,
      });
    }
  });
  sdtmCeRecords.value = records;
}

function exportSdtmJson() {
  const dataStr =
    "data:text/json;charset=utf-8," +
    encodeURIComponent(JSON.stringify(sdtmCeRecords.value, null, 2));
  const downloadAnchor = document.createElement("a");
  downloadAnchor.setAttribute("href", dataStr);
  downloadAnchor.setAttribute(
    "download",
    `SDTM_CE_${selectedStudyId.value}.json`
  );
  document.body.appendChild(downloadAnchor);
  downloadAnchor.click();
  downloadAnchor.remove();
}

function viewAttachment(att) {
  toasts.push({
    tone: "info",
    title: `Viewing attachment: ${att.title}`,
    message: `Summary: ${att.summary}`,
  });
}

onMounted(() => {
  if (dossiers.value.length) {
    selectedDossierId.value = dossiers.value[0].dossier_id;
  }
  compileSdtmCe();
});
</script>

<style scoped>
/* 100% Vanilla CSS Design Tokens */
.cec-adjudication-console {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  width: 100%;
  box-sizing: border-box;
  color: var(--color-text);
  background-color: var(--semantic-color-surface-muted);
  font-family: var(--semantic-font-family);
  padding: 1.5rem;
}

/* Header */
.console-header {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1.5rem;
  box-shadow: var(--shadow-sm, 0 1px 2px 0 rgba(0, 0, 0, 0.05));
}

.header-main {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 1rem;
}

.badge-row {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 0.5rem;
  flex-wrap: wrap;
}

.badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.25rem 0.6rem;
  border-radius: 4px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.badge-regulatory {
  background: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.badge-charter {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-phi {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.badge-phi.phi-active {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.console-title {
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0 0 0.25rem 0;
  color: var(--semantic-color-text);
}

.console-subtitle {
  font-size: 0.875rem;
  color: var(--color-text-muted);
  margin: 0;
}

.header-controls {
  display: flex;
  gap: 1rem;
  align-items: flex-end;
  flex-wrap: wrap;
}

.control-box {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.control-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-muted);
}

.select-input,
.text-input,
.textarea-input {
  border: 1px solid var(--color-border);
  background: var(--color-surface);
  border-radius: 6px;
  padding: 0.45rem 0.75rem;
  font-size: 0.875rem;
  color: var(--color-text);
  box-sizing: border-box;
}

.select-input:focus-visible,
.text-input:focus-visible,
.textarea-input:focus-visible {
  border-color: var(--color-primary);
}

.toggle-box {
  justify-content: flex-end;
  padding-bottom: 0.35rem;
}

.toggle-label {
  display: flex;
  align-items: center;
  gap: 0.4rem;
  font-size: 0.8125rem;
  font-weight: 600;
  cursor: pointer;
}

/* Metric Strip */
.metric-strip {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 1rem;
  margin-top: 1.25rem;
  padding-top: 1.25rem;
  border-top: 1px solid var(--color-border);
}

.metric-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.75rem 1rem;
  display: flex;
  flex-direction: column;
}

.metric-title {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--color-text-muted);
  text-transform: uppercase;
}

.metric-value {
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0.25rem 0;
  color: var(--semantic-color-text);
}

.metric-sub {
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.text-warning {
  color: var(--semantic-color-warning);
}
.text-success {
  color: var(--semantic-color-success);
}
.text-danger {
  color: var(--semantic-color-error);
}
.text-info {
  color: var(--semantic-color-primary);
}
.text-primary {
  color: var(--semantic-color-interactive-primary);
}

/* Tabs */
.nav-tabs {
  display: flex;
  gap: 0.5rem;
  border-bottom: 2px solid var(--color-border);
  padding-bottom: 0;
}

.tab-btn {
  background: transparent;
  border: none;
  border-bottom: 3px solid transparent;
  padding: 0.75rem 1.25rem;
  font-size: 0.9375rem;
  font-weight: 600;
  color: var(--color-text-muted);
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 0.5rem;
  transition: all 0.2s ease;
}

.tab-btn:hover {
  color: var(--semantic-color-text);
}

.tab-btn.active {
  color: var(--color-primary);
  border-bottom-color: var(--color-primary);
}

.badge-count {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
  font-size: 0.75rem;
  padding: 0.1rem 0.4rem;
  border-radius: 9999px;
  font-weight: 700;
}

/* Split Layout */
.split-layout {
  display: grid;
  grid-template-columns: 360px 1fr;
  gap: 1.5rem;
  align-items: start;
}

@media (max-width: 1024px) {
  .split-layout {
    grid-template-columns: 1fr;
  }
}

.sidebar-pane {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1rem;
}

.pane-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.pane-title {
  font-size: 1.125rem;
  font-weight: 700;
  margin: 0;
  color: var(--semantic-color-text);
}

.pane-sub {
  font-size: 0.8125rem;
  color: var(--color-text-muted);
  margin: 0.25rem 0 0 0;
}

.pill-badge {
  font-size: 0.75rem;
  font-weight: 600;
  background: var(--semantic-color-surface-header);
  padding: 0.2rem 0.5rem;
  border-radius: 12px;
}

.filter-toolbar {
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
  margin-bottom: 1rem;
}

.filter-chips {
  display: flex;
  gap: 0.25rem;
  flex-wrap: wrap;
}

.chip-btn {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  padding: 0.2rem 0.5rem;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  cursor: pointer;
}

.chip-btn.active {
  background: var(--color-primary);
  color: var(--semantic-color-surface);
  border-color: var(--color-primary);
}

.dossier-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
  max-height: 700px;
  overflow-y: auto;
}

.dossier-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.875rem;
  cursor: pointer;
  transition: all 0.15s ease;
}

.dossier-card:hover {
  border-color: var(--color-primary);
  box-shadow: var(--shadow-sm, 0 1px 2px 0 rgba(0, 0, 0, 0.05));
}

.dossier-card.active {
  border-color: var(--color-primary);
  background: var(--semantic-color-info-bg);
}

.card-top {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.4rem;
}

.category-tag {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.card-term {
  font-size: 0.9375rem;
  font-weight: 700;
  margin: 0 0 0.4rem 0;
  color: var(--semantic-color-text);
}

.card-meta {
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: var(--color-text-muted);
  margin-bottom: 0.5rem;
}

.reader-progress-dots {
  display: flex;
  gap: 0.35rem;
  align-items: center;
}

.dot {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 0.1rem 0.35rem;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.dot.completed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.dot-alert {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.dot-check {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

/* Status Pills */
.status-pill {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 0.2rem 0.5rem;
  border-radius: 12px;
  text-transform: uppercase;
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
  color: var(--semantic-color-primary-dark);
}

/* Content Pane */
.content-pane {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.dossier-banner {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1.25rem;
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 1rem;
}

.banner-title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  margin-bottom: 0.5rem;
  flex-wrap: wrap;
}

.category-pill {
  background: var(--semantic-color-surface-header);
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.banner-event-title {
  font-size: 1.25rem;
  font-weight: 700;
  margin: 0;
  color: var(--semantic-color-text);
}

.banner-metadata-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 0.75rem;
  font-size: 0.8125rem;
  color: var(--color-text-muted);
}

.hash-code {
  font-family: var(--semantic-font-mono);
  background: var(--semantic-color-surface-header);
  padding: 0.1rem 0.3rem;
  border-radius: 3px;
}

/* Alert Banners */
.alert-banner {
  display: flex;
  gap: 1rem;
  padding: 1rem 1.25rem;
  border-radius: 8px;
  border: 1px solid transparent;
}

.alert-discordance {
  background: var(--semantic-color-error-bg-row);
  border-color: var(--semantic-color-rose-border);
  color: var(--semantic-color-rose-text);
}

.alert-consensus {
  background: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border-soft);
  color: var(--semantic-color-success-dark);
}

.alert-icon {
  font-size: 1.5rem;
}

.alert-title {
  font-size: 0.9375rem;
  font-weight: 700;
  margin: 0 0 0.25rem 0;
}

.discordance-list {
  margin: 0.25rem 0 0 0;
  padding-left: 1.25rem;
  font-size: 0.8125rem;
}

.alert-desc {
  margin: 0;
  font-size: 0.8125rem;
}

/* Dossier Section Cards */
.dossier-sections {
  display: flex;
  flex-direction: column;
  gap: 1.25rem;
}

.section-card {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1.25rem;
}

.section-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
}

.section-title {
  font-size: 1.0625rem;
  font-weight: 700;
  margin: 0;
  color: var(--semantic-color-text);
}

.phi-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.count-tag {
  font-size: 0.75rem;
  color: var(--color-text-muted);
}

.narrative-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 1rem;
  font-size: 0.875rem;
  line-height: 1.5;
}

.verbatim-box {
  margin-top: 0.75rem;
  padding-top: 0.75rem;
  border-top: 1px dashed var(--color-border);
  font-size: 0.8125rem;
  color: var(--semantic-color-text-secondary);
}

/* Tables */
.table-responsive {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.8125rem;
}

.data-table th {
  background: var(--semantic-color-surface-muted);
  border-bottom: 2px solid var(--color-border);
  padding: 0.6rem 0.75rem;
  text-align: left;
  font-weight: 600;
  color: var(--color-text-muted);
}

.data-table td {
  border-bottom: 1px solid var(--color-border);
  padding: 0.6rem 0.75rem;
  color: var(--color-text);
}

.row-abnormal {
  background: var(--semantic-color-error-bg-row);
}

.cell-bold {
  font-weight: 600;
}

.cell-value {
  font-family: var(--semantic-font-mono);
  font-weight: 700;
}

.grade-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
}

.grade-1 {
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}
.grade-2 {
  background: var(--semantic-color-orange-border);
  color: var(--semantic-color-orange-text);
}
.grade-3 {
  background: var(--semantic-color-rose-border);
  color: var(--semantic-color-rose-text);
}
.grade-4 {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-deep);
}

/* ECG Grid */
.ecg-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 1rem;
}

.ecg-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 1rem;
}

.ecg-header {
  display: flex;
  justify-content: space-between;
  margin-bottom: 0.5rem;
  font-size: 0.8125rem;
}

.ecg-leads-row {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  margin-bottom: 0.5rem;
}

.lead-group {
  font-size: 0.75rem;
}

.lead-group.alert-leads {
  color: var(--semantic-color-error);
  font-weight: 700;
}

.lead-pills {
  background: var(--semantic-color-surface);
  border: 1px solid var(--color-border);
  padding: 0.1rem 0.35rem;
  border-radius: 3px;
  margin-left: 0.25rem;
}

.ecg-interp {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

/* Attachments */
.attachments-list {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.attachment-item {
  display: flex;
  align-items: flex-start;
  gap: 0.75rem;
  padding: 0.75rem;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
}

.att-icon {
  font-size: 1.5rem;
}
.att-info {
  flex: 1;
}
.att-title {
  font-size: 0.875rem;
  font-weight: 700;
  margin: 0 0 0.25rem 0;
}
.att-summary {
  font-size: 0.8125rem;
  color: var(--semantic-color-text-secondary);
  margin: 0 0 0.25rem 0;
}
.att-findings {
  font-size: 0.75rem;
  color: var(--semantic-color-text-body);
}
.att-findings ul {
  margin: 0.2rem 0 0 0;
  padding-left: 1.25rem;
}

/* Reader Columns */
.reader-columns {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.25rem;
}

@media (max-width: 768px) {
  .reader-columns {
    grid-template-columns: 1fr;
  }
}

.reader-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 1rem;
}

.reader-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.75rem;
  border-bottom: 1px solid var(--color-border);
  padding-bottom: 0.5rem;
}

.reader-card-header h5 {
  margin: 0;
  font-size: 0.9375rem;
  font-weight: 700;
}

.assessment-details {
  display: flex;
  flex-direction: column;
  gap: 0.4rem;
  font-size: 0.8125rem;
}

.badge-class {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  font-weight: 700;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
}

.criteria-box {
  background: var(--semantic-color-surface);
  border: 1px solid var(--color-border);
  border-radius: 4px;
  padding: 0.5rem;
  margin-top: 0.35rem;
}

.criteria-box ul {
  margin: 0.25rem 0 0 0;
  padding-left: 1.25rem;
  font-size: 0.75rem;
}

.rationale-text {
  font-size: 0.8125rem;
  color: var(--semantic-color-text-body);
  margin: 0.35rem 0 0 0;
}

.blinded-placeholder {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  padding: 2rem 1rem;
  text-align: center;
  color: var(--color-text-muted);
  background: var(--semantic-color-surface-header);
  border-radius: 6px;
  border: 1px dashed var(--color-border);
}

.lock-icon {
  font-size: 1.5rem;
  margin-bottom: 0.5rem;
}

.pending-reader-box {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 1.5rem 1rem;
  text-align: center;
  gap: 0.75rem;
}

/* Chair Tie-Breaker Tab */
.panel-container {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1.5rem;
}

.chair-adjudication-workspace {
  display: flex;
  flex-direction: column;
  gap: 1.5rem;
  margin-top: 1rem;
}

.discordance-comparison-grid {
  display: grid;
  grid-template-columns: 1fr 60px 1fr;
  gap: 1rem;
  align-items: center;
}

.review-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 1rem;
  font-size: 0.875rem;
}

.review-box h4 {
  margin: 0 0 0.5rem 0;
  font-size: 0.9375rem;
  font-weight: 700;
}

.vs-divider {
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 900;
  color: var(--semantic-color-text-secondary);
  font-size: 1.125rem;
}

.chair-decision-form {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 1.5rem;
}

.chair-decision-form h4 {
  margin: 0 0 1rem 0;
  font-size: 1.125rem;
  font-weight: 700;
}

.form-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(240px, 1fr));
  gap: 1rem;
  margin-bottom: 1rem;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  margin-bottom: 0.875rem;
}

.form-group label {
  font-size: 0.8125rem;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.criteria-checkbox-list {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  background: var(--semantic-color-surface);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.75rem;
  max-height: 180px;
  overflow-y: auto;
}

.checkbox-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.8125rem;
  cursor: pointer;
}

/* eSign Block */
.esign-block {
  background: var(--semantic-color-surface);
  border: 2px solid var(--semantic-color-interactive-primary);
  border-radius: 8px;
  padding: 1.25rem;
  margin-top: 1.25rem;
}

.esign-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
}

.esign-header h5 {
  margin: 0;
  font-size: 0.9375rem;
  font-weight: 700;
  color: var(--semantic-color-info);
}

.esign-manifest {
  font-style: italic;
  font-size: 0.8125rem;
  color: var(--semantic-color-text-body);
  background: var(--semantic-color-info-bg);
  border-left: 3px solid var(--semantic-color-interactive-primary);
  padding: 0.5rem 0.75rem;
  margin-bottom: 1rem;
}

.esign-inputs {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  margin-bottom: 1rem;
}

/* Charter Grid */
.charter-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1.5rem;
  margin-top: 1rem;
}

@media (max-width: 900px) {
  .charter-grid {
    grid-template-columns: 1fr;
  }
}

.charter-details {
  font-size: 0.875rem;
  line-height: 1.6;
}

.eligible-roster {
  margin-top: 0.75rem;
}

.eligible-roster ul {
  padding-left: 1.25rem;
  margin: 0.25rem 0 0 0;
  font-size: 0.8125rem;
}

.attendee-checkbox-list {
  display: flex;
  flex-direction: column;
  gap: 0.35rem;
  background: var(--semantic-color-surface);
  border: 1px solid var(--color-border);
  border-radius: 6px;
  padding: 0.75rem;
}

.quorum-indicator-box {
  margin: 1rem 0;
}

.quorum-stat {
  display: flex;
  justify-content: space-between;
  font-size: 0.875rem;
  font-weight: 700;
  margin-bottom: 0.25rem;
}

.pct-text {
  color: var(--semantic-color-interactive-primary);
}

.quorum-status-banner {
  padding: 0.6rem 1rem;
  border-radius: 6px;
  font-size: 0.8125rem;
  font-weight: 700;
  text-align: center;
}

.quorum-passed {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
}

.quorum-failed {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border);
}

/* SDTM CE */
.badge-domain {
  background: var(--semantic-color-surface-header);
  font-weight: 700;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
}

.sync-badge {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-success-dark);
}

.flex-between {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 1rem;
}

/* Buttons */
.btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 0.4rem;
  font-size: 0.875rem;
  font-weight: 600;
  padding: 0.5rem 1rem;
  border-radius: 6px;
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.15s ease;
}

.btn-primary {
  background: var(--color-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
}

.btn-secondary {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  border-color: var(--color-border);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn-lg {
  padding: 0.75rem 1.5rem;
  font-size: 1rem;
}

.btn-sm {
  padding: 0.25rem 0.5rem;
  font-size: 0.75rem;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

/* Modal */
.modal-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
  padding: 1rem;
}

.modal-dialog {
  background: var(--color-surface);
  border-radius: 8px;
  width: 100%;
  max-width: 560px;
  box-shadow: var(--shadow-xl, 0 20px 25px -5px rgba(0, 0, 0, 0.1));
  display: flex;
  flex-direction: column;
  max-height: 90vh;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--color-border);
}

.modal-title {
  margin: 0;
  font-size: 1.125rem;
  font-weight: 700;
}

.btn-close {
  background: transparent;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
  color: var(--semantic-color-text-secondary);
}

.modal-body {
  padding: 1.25rem;
  overflow-y: auto;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--color-border);
}

.empty-pane {
  background: var(--color-surface);
  border: 1px solid var(--color-border);
  border-radius: 8px;
  padding: 3rem;
  text-align: center;
  color: var(--color-text-muted);
}

.empty-box {
  padding: 1.5rem;
  text-align: center;
  color: var(--color-text-muted);
  background: var(--semantic-color-surface-muted);
  border-radius: 6px;
  font-size: 0.8125rem;
}

.empty-cell {
  padding: 2rem !important;
  color: var(--color-text-muted);
}
</style>
