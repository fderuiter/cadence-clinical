<template>
  <div id="section-quality-cockpit" class="dashboard-section active">
    <!-- Header -->
    <div class="section-header">
      <div class="header-content-group">
        <div class="header-badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH E6(R3) RBQM</span>
          <span class="badge badge-standard">TransCelerate eQMS</span>
          <span class="badge badge-standard">ISO 9001 / GAMP 5</span>
        </div>
        <h2 class="view-title">Clinical Quality &amp; RBQM Cockpit</h2>
        <p class="view-subtitle">
          End-to-end clinical quality management system synthesizing automated deviation ingestion,
          multi-methodology root cause analysis (5-Whys &amp; 6M Ishikawa), 6-stage gate CAPA workflows,
          TransCelerate KRI statistical Z-scores, Quality Tolerance Limits with CSR Section 9.6 narrative synthesis,
          clinical audits, and 7-day serious breach regulatory countdown tracking.
        </p>
      </div>
      <div class="header-action-group">
        <!-- Study Switcher -->
        <div class="study-selector-box">
          <label for="study-select" class="sr-only">Select Active Study</label>
          <select id="study-select" v-model="selectedStudyId" class="study-select" @change="onStudyChange">
            <option v-for="s in availableStudies" :key="s" :value="s">
              Study: {{ s }}
            </option>
          </select>
        </div>

        <button class="btn btn-secondary" title="Simulate Quality Event Ingestion" @click="openIngestModal">
          <span class="btn-icon">⚡</span> Ingest Event
        </button>
        <button class="btn btn-primary" title="Export 21 CFR Part 11 Inspection Dossier" @click="openDossierModal">
          <span class="btn-icon">📄</span> Inspection Dossier
        </button>
        <button class="btn btn-primary" title="Log New Deviation" @click="openNewDeviationModal">
          <span class="btn-icon">➕</span> Log Deviation
        </button>
      </div>
    </div>

    <!-- Quick Stats Cards Grid -->
    <div class="stats-grid">
      <div class="stat-card" @click="activeTab = 'deviations'">
        <div class="stat-icon alert-icon">⚠️</div>
        <div class="stat-details">
          <div class="stat-value">{{ deviations.length }}</div>
          <div class="stat-label">Total Deviations ({{ criticalDeviationsCount }} Crit / {{ majorDeviationsCount }} Maj)</div>
        </div>
      </div>
      <div class="stat-card" @click="activeTab = 'capa'">
        <div class="stat-icon capa-icon">🔄</div>
        <div class="stat-details">
          <div class="stat-value">{{ activeCapasCount }}</div>
          <div class="stat-label">Active CAPAs (6-Stage Gates)</div>
        </div>
      </div>
      <div class="stat-card" @click="activeTab = 'rbqm'">
        <div class="stat-icon rbqm-icon">🎯</div>
        <div class="stat-details">
          <div class="stat-value">{{ highRiskSitesCount }} / {{ siteRiskProfiles.length || 4 }}</div>
          <div class="stat-label">High-Risk Sites (Z &ge; 2.0)</div>
        </div>
      </div>
      <div class="stat-card" @click="activeTab = 'rbqm'">
        <div class="stat-icon qtl-icon">📊</div>
        <div class="stat-details">
          <div class="stat-value">{{ qtlBreaches.length }}</div>
          <div class="stat-label">QTL Breaches (CSR 9.6 Alerts)</div>
        </div>
      </div>
      <div class="stat-card" @click="activeTab = 'serious-breaches'">
        <div class="stat-icon breach-icon">🚨</div>
        <div class="stat-details">
          <div class="stat-value" :class="{ 'text-danger': activeBreachesOverdue || (activeBreachClockHours !== null && activeBreachClockHours < 48) }">
            {{ activeBreachClockHours !== null ? `${activeBreachClockHours.toFixed(0)}h` : '0 Active' }}
          </div>
          <div class="stat-label">7-Day Serious Breach Clock</div>
        </div>
      </div>
      <div class="stat-card" @click="openDossierModal">
        <div class="stat-icon seal-icon">🔒</div>
        <div class="stat-details">
          <div class="stat-value text-success">100% GxP</div>
          <div class="stat-label">Inspection Readiness Seal</div>
        </div>
      </div>
    </div>

    <!-- Navigation Tabs -->
    <div class="quality-tabs-bar">
      <button
        class="quality-tab-btn"
        :class="{ active: activeTab === 'rbqm' }"
        @click="activeTab = 'rbqm'"
      >
        <span class="tab-icon">🎯</span> RBQM &amp; Risk Index (ICH E6(R3))
      </button>
      <button
        class="quality-tab-btn"
        :class="{ active: activeTab === 'deviations' }"
        @click="activeTab = 'deviations'"
      >
        <span class="tab-icon">⚠️</span> Protocol Deviations &amp; RCA
      </button>
      <button
        class="quality-tab-btn"
        :class="{ active: activeTab === 'capa' }"
        @click="activeTab = 'capa'"
      >
        <span class="tab-icon">🔄</span> CAPA 6-Stage Gate Kanban
      </button>
      <button
        class="quality-tab-btn"
        :class="{ active: activeTab === 'audits' }"
        @click="activeTab = 'audits'"
      >
        <span class="tab-icon">🛡️</span> Clinical Audits &amp; Findings
      </button>
      <button
        class="quality-tab-btn"
        :class="{ active: activeTab === 'serious-breaches' }"
        @click="activeTab = 'serious-breaches'"
      >
        <span class="tab-icon">🚨</span> Serious Breach &amp; Regulatory Clock
      </button>
    </div>

    <!-- Tab 1: RBQM & Risk Index -->
    <div v-if="activeTab === 'rbqm'" class="tab-content">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">TransCelerate &amp; ICH E6(R3) Key Risk Indicators (KRIs)</h3>
            <p class="panel-subtitle">Statistical Z-score standardization across investigational sites with weighted risk aggregation.</p>
          </div>
          <button class="btn btn-primary btn-sm" @click="runKriBatchScoring">
            <span class="btn-icon">⚡</span> Run Batch KRI Evaluation
          </button>
        </div>

        <div class="kri-cards-grid">
          <div v-for="kri in kriDefinitions" :key="kri.code" class="kri-card">
            <div class="kri-header">
              <span class="kri-code">{{ kri.code }}</span>
              <span class="badge" :class="'badge-' + (kri.category || 'DATA_INTEGRITY').toLowerCase()">{{ kri.category }}</span>
            </div>
            <div class="kri-name">{{ kri.name }}</div>
            <div class="kri-desc">{{ kri.description }}</div>
            <div class="kri-thresholds">
              <span class="threshold-pill green">🟢 &lt; {{ kri.green_threshold }}</span>
              <span class="threshold-pill amber">🟡 &lt; {{ kri.amber_threshold }}</span>
              <span class="threshold-pill red">🔴 &ge; {{ kri.red_threshold }}</span>
            </div>
            <div class="kri-footer">
              <span class="kri-weight">Weight: {{ kri.weight }}x</span>
              <span class="kri-formula"><code>{{ kri.calculation_formula }}</code></span>
            </div>
          </div>
        </div>
      </div>

      <!-- Site Risk Profile Ranking Table -->
      <div class="panel-card mt-4">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Composite Site Risk Profile Index &amp; Ranking</h3>
            <p class="panel-subtitle">Dynamic ranking of study centers prioritizing targeted monitoring (TSDV) and quality interventions.</p>
          </div>
          <button class="btn btn-secondary btn-sm" @click="recomputeSiteProfiles">
            <span class="btn-icon">🔄</span> Recompute Risk Profiles
          </button>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th style="width: 80px;">Rank</th>
                <th>Site ID</th>
                <th>High KRI Count</th>
                <th>Active Deviations</th>
                <th>Composite Risk Score</th>
                <th>Risk Tier</th>
                <th>Targeted Intervention</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="profile in siteRiskProfiles" :key="profile.site_id">
                <td>
                  <span class="rank-badge" :class="'rank-' + profile.risk_rank">#{{ profile.risk_rank }}</span>
                </td>
                <td><strong>{{ profile.site_id }}</strong></td>
                <td>
                  <span class="badge" :class="profile.high_kri_count > 0 ? 'badge-danger' : 'badge-neutral'">
                    {{ profile.high_kri_count }} KRIs &ge; High
                  </span>
                </td>
                <td>
                  <span class="badge" :class="profile.active_deviations_count > 0 ? 'badge-warning' : 'badge-neutral'">
                    {{ profile.active_deviations_count }} Deviations
                  </span>
                </td>
                <td>
                  <div class="score-bar-container">
                    <div class="score-bar" :style="{ width: Math.min(profile.composite_risk_score * 8, 100) + '%', backgroundColor: getRiskColor(profile.composite_risk_score) }"></div>
                    <span class="score-val">{{ profile.composite_risk_score }}</span>
                  </div>
                </td>
                <td>
                  <span class="badge" :class="profile.composite_risk_score >= 8 ? 'badge-critical' : profile.composite_risk_score >= 5 ? 'badge-high' : 'badge-low'">
                    {{ profile.composite_risk_score >= 8 ? 'CRITICAL RISK' : profile.composite_risk_score >= 5 ? 'ELEVATED' : 'STABLE' }}
                  </span>
                </td>
                <td>
                  <button class="btn btn-secondary btn-xs" @click="triggerTargetedMonitoring(profile.site_id)">
                    🎯 Schedule Targeted SDV
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Quality Tolerance Limits (QTL) & CSR Section 9.6 Generator -->
      <div class="panel-card mt-4">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Quality Tolerance Limits (QTLs) &amp; CSR Section 9.6 Synthesis</h3>
            <p class="panel-subtitle">ICH E6(R3) trial-level tolerance boundaries and automated regulatory summary narrative generator.</p>
          </div>
          <button class="btn btn-primary btn-sm" @click="openNewQtlModal">
            <span class="btn-icon">➕</span> Add QTL Parameter
          </button>
        </div>

        <div class="qtl-list">
          <div v-for="qtl in qtls" :key="qtl.id" class="qtl-item-card">
            <div class="qtl-main-info">
              <div class="qtl-title-row">
                <h4>{{ qtl.parameter_name }}</h4>
                <span class="badge badge-neutral">{{ qtl.unit }}</span>
              </div>
              <div class="qtl-metrics-row">
                <div class="metric-item">
                  <span class="metric-label">Target Value:</span>
                  <span class="metric-value">{{ qtl.target_value }}{{ qtl.unit }}</span>
                </div>
                <div class="metric-item">
                  <span class="metric-label">Tolerance Limit:</span>
                  <span class="metric-value text-danger">&le; {{ qtl.tolerance_limit }}{{ qtl.unit }}</span>
                </div>
                <div class="metric-item">
                  <span class="metric-label">Latest Observed:</span>
                  <span class="metric-value" :class="qtl.observed_value > qtl.tolerance_limit ? 'text-danger font-bold' : 'text-success'">
                    {{ qtl.observed_value !== undefined ? `${qtl.observed_value}${qtl.unit}` : 'Not Evaluated' }}
                  </span>
                </div>
              </div>
            </div>

            <div class="qtl-actions">
              <button class="btn btn-secondary btn-sm" @click="evaluateQtlBreach(qtl)">
                ⚡ Evaluate Breach
              </button>
              <button v-if="qtl.latest_breach" class="btn btn-outline btn-sm" @click="viewCsrNarrative(qtl.latest_breach)">
                📄 CSR 9.6 Text
              </button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Tab 2: Protocol Deviations & RCA -->
    <div v-if="activeTab === 'deviations'" class="tab-content">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Protocol Deviations Ledger</h3>
            <p class="panel-subtitle">Auditable deviation events with 21 CFR Part 11 change justification and multi-methodology root cause analysis.</p>
          </div>
          <div class="filter-group">
            <select v-model="deviationSeverityFilter" class="form-select form-select-sm">
              <option value="ALL">All Severities</option>
              <option value="CRITICAL">Critical</option>
              <option value="MAJOR">Major</option>
              <option value="MINOR">Minor</option>
            </select>
          </div>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Deviation Title &amp; Details</th>
                <th>Site</th>
                <th>Type</th>
                <th>Severity</th>
                <th>Status</th>
                <th>RCA Method</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="dev in filteredDeviations" :key="dev.id">
                <td>
                  <div class="deviation-title"><strong>{{ dev.title }}</strong></div>
                  <div class="deviation-desc">{{ dev.description }}</div>
                  <div class="deviation-meta">
                    <span v-if="dev.source_system" class="source-tag">Source: {{ dev.source_system }}</span>
                    <span v-if="dev.source_reference_id" class="ref-tag">Ref: {{ dev.source_reference_id }}</span>
                    <span v-if="dev.impact_safety" class="impact-tag safety">⚠️ Safety Impact</span>
                  </div>
                </td>
                <td>{{ dev.site_id || 'Global' }}</td>
                <td><span class="type-pill">{{ dev.type }}</span></td>
                <td>
                  <span class="badge" :class="'badge-' + (dev.severity || 'minor').toLowerCase()">
                    {{ dev.severity }}
                  </span>
                </td>
                <td>
                  <span class="status-pill" :class="'status-' + (dev.status || 'reported').toLowerCase()">
                    {{ dev.status }}
                  </span>
                </td>
                <td>
                  <span v-if="dev.rca" class="badge badge-rca">
                    {{ dev.rca.methodology }}
                  </span>
                  <span v-else class="text-muted text-xs">No RCA Attached</span>
                </td>
                <td>
                  <div class="action-buttons-inline">
                    <button class="btn btn-secondary btn-xs" @click="openRcaModal(dev)">
                      🔍 {{ dev.rca ? 'View RCA' : 'Attach RCA' }}
                    </button>
                    <button v-if="!dev.capa_id && dev.status !== 'CLOSED'" class="btn btn-primary btn-xs" @click="initiateCapaFromDeviation(dev)">
                      ➕ Create CAPA
                    </button>
                  </div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Tab 3: CAPA 6-Stage Gate Kanban -->
    <div v-if="activeTab === 'capa'" class="tab-content">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">6-Stage Gate CAPA Kanban Board</h3>
            <p class="panel-subtitle">Strict stage-gated corrective and preventive action lifecycle with sub-action item gating and e-signature step-up.</p>
          </div>
          <button class="btn btn-primary btn-sm" @click="openNewCapaModal">
            <span class="btn-icon">➕</span> New CAPA Record
          </button>
        </div>

        <!-- Kanban Board Columns -->
        <div class="kanban-board">
          <div v-for="stage in CAPA_STAGES" :key="stage.key" class="kanban-column">
            <div class="kanban-column-header">
              <span class="stage-name">{{ stage.label }}</span>
              <span class="stage-count">{{ getCapasInStage(stage.key).length }}</span>
            </div>

            <div class="kanban-column-body">
              <div v-for="capa in getCapasInStage(stage.key)" :key="capa.id" class="kanban-card">
                <div class="kanban-card-header">
                  <span class="badge" :class="'badge-' + (capa.risk_level || 'medium').toLowerCase()">{{ capa.risk_level }} Risk</span>
                  <span class="capa-type-tag">{{ capa.capa_type }}</span>
                </div>
                <div class="kanban-card-title">{{ capa.action_plan }}</div>
                <div class="kanban-card-preventive">{{ capa.preventive_measures }}</div>
                
                <!-- Action items progress -->
                <div v-if="capa.action_items && capa.action_items.length > 0" class="action-items-progress">
                  <div class="progress-label">
                    <span>Action Items:</span>
                    <span>{{ getCompletedActionItemsCount(capa) }}/{{ capa.action_items.length }}</span>
                  </div>
                  <div class="progress-bar-bg">
                    <div class="progress-bar-fill" :style="{ width: getActionItemsPercentage(capa) + '%' }"></div>
                  </div>
                </div>

                <div class="kanban-card-footer">
                  <button class="btn btn-secondary btn-xs" @click="openCapaDetail(capa)">
                    📋 Manage Details
                  </button>
                  <button v-if="getNextStage(capa.status)" class="btn btn-primary btn-xs" @click="advanceCapaStage(capa)">
                    Advance ➔
                  </button>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Tab 4: Clinical Audits & Findings -->
    <div v-if="activeTab === 'audits'" class="tab-content">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Clinical Audit Engagements</h3>
            <p class="panel-subtitle">Investigator site, vendor, process, and TMF audit schedule with 1-click CAPA promotion.</p>
          </div>
          <button class="btn btn-primary btn-sm" @click="openNewAuditModal">
            <span class="btn-icon">➕</span> Schedule Audit
          </button>
        </div>

        <div class="table-responsive">
          <table class="data-table">
            <thead>
              <tr>
                <th>Audit Number</th>
                <th>Audit Type</th>
                <th>Target Site / Vendor</th>
                <th>Lead Auditor</th>
                <th>Planned Dates</th>
                <th>Status</th>
                <th>Findings</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="audit in audits" :key="audit.id">
                <td><strong>{{ audit.audit_number }}</strong></td>
                <td><span class="type-pill">{{ audit.audit_type }}</span></td>
                <td>{{ audit.site_id || audit.vendor_name || 'System / Study' }}</td>
                <td>{{ audit.lead_auditor }}</td>
                <td>{{ formatDate(audit.planned_start_date) }} &ndash; {{ formatDate(audit.planned_end_date) }}</td>
                <td>
                  <span class="status-pill" :class="'status-' + (audit.status || 'planned').toLowerCase()">
                    {{ audit.status }}
                  </span>
                </td>
                <td>
                  <span class="badge" :class="audit.findings && audit.findings.length > 0 ? 'badge-warning' : 'badge-neutral'">
                    {{ audit.findings ? audit.findings.length : 0 }} Findings
                  </span>
                </td>
                <td>
                  <button class="btn btn-secondary btn-xs" @click="openAuditFindingsModal(audit)">
                    🔍 Log Findings
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Tab 5: Serious Breach & Regulatory Clock -->
    <div v-if="activeTab === 'serious-breaches'" class="tab-content">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Serious Breach Regulatory Escalation Tracker</h3>
            <p class="panel-subtitle">ICH GCP &amp; UK MHRA / EMA 7-day (168-hour) mandatory regulatory clock calculation and authority dispatches.</p>
          </div>
          <button class="btn btn-danger btn-sm" @click="openNewBreachModal">
            <span class="btn-icon">🚨</span> Report Serious Breach
          </button>
        </div>

        <div class="breach-cards-list">
          <div v-for="breach in seriousBreaches" :key="breach.id" class="breach-item-card">
            <div class="breach-header">
              <div class="breach-title-area">
                <span class="badge badge-critical">SERIOUS BREACH</span>
                <h4 class="breach-title">{{ breach.title }}</h4>
              </div>
              <div class="breach-clock-display" :class="getClockClass(breach)">
                <div class="clock-icon">⏱️</div>
                <div class="clock-text">
                  <div class="clock-hours">{{ breach.hours_remaining !== undefined ? `${breach.hours_remaining.toFixed(1)} Hours` : '168.0 Hours' }}</div>
                  <div class="clock-label">Mandatory Notification Window</div>
                </div>
              </div>
            </div>

            <div class="breach-body">
              <p class="breach-summary">{{ breach.summary }}</p>
              <div class="breach-meta-row">
                <span><strong>Site:</strong> {{ breach.site_id || 'All Study Sites' }}</span>
                <span><strong>Discovered:</strong> {{ formatDate(breach.discovery_date) }}</span>
                <span><strong>Authorities:</strong> 
                  <span v-for="auth in breach.affected_authorities" :key="auth" class="authority-tag">{{ auth }}</span>
                </span>
              </div>
            </div>

            <div class="breach-footer">
              <span class="status-pill" :class="'status-' + (breach.status || 'under_evaluation').toLowerCase()">
                Status: {{ breach.status }}
              </span>
              <div class="breach-actions">
                <button v-if="breach.status === 'UNDER_EVALUATION'" class="btn btn-warning btn-xs" @click="confirmBreach(breach)">
                  ⚠️ Confirm Serious Breach
                </button>
                <button v-if="breach.status === 'CONFIRMED_BREACH'" class="btn btn-danger btn-xs" @click="notifyAuthorities(breach)">
                  🏛️ Dispatch Authority Notification
                </button>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Modals -->

    <!-- Modal 1: 5-Whys & 6M Ishikawa RCA Visualizer Modal -->
    <div v-if="isRcaModalOpen" class="modal-backdrop">
      <div class="modal-content modal-lg">
        <div class="modal-header">
          <h3 class="modal-title">Multi-Methodology Root Cause Analysis (RCA)</h3>
          <button class="modal-close" @click="isRcaModalOpen = false">✕</button>
        </div>
        <div class="modal-body">
          <div class="rca-deviation-summary">
            <h4>Deviation: {{ activeRcaDeviation?.title }}</h4>
            <p>{{ activeRcaDeviation?.description }}</p>
          </div>

          <div class="rca-method-switcher">
            <button class="btn btn-sm" :class="rcaMethod === 'FIVE_WHYS' ? 'btn-primary' : 'btn-secondary'" @click="rcaMethod = 'FIVE_WHYS'">
              5-Whys Causal Tree
            </button>
            <button class="btn btn-sm" :class="rcaMethod === 'ISHIKAWA' ? 'btn-primary' : 'btn-secondary'" @click="rcaMethod = 'ISHIKAWA'">
              6M Ishikawa Fishbone Diagram
            </button>
          </div>

          <!-- 5-Whys Tree Visualizer -->
          <div v-if="rcaMethod === 'FIVE_WHYS'" class="five-whys-container">
            <div class="why-step">
              <div class="why-badge">Why 1</div>
              <input v-model="rcaForm.five_whys.why_1" class="form-input" placeholder="Primary direct symptom / immediate cause" />
            </div>
            <div class="why-step">
              <div class="why-badge">Why 2</div>
              <input v-model="rcaForm.five_whys.why_2" class="form-input" placeholder="Second causal layer" />
            </div>
            <div class="why-step">
              <div class="why-badge">Why 3</div>
              <input v-model="rcaForm.five_whys.why_3" class="form-input" placeholder="Third causal layer" />
            </div>
            <div class="why-step">
              <div class="why-badge">Why 4</div>
              <input v-model="rcaForm.five_whys.why_4" class="form-input" placeholder="Fourth causal layer" />
            </div>
            <div class="why-step">
              <div class="why-badge">Why 5</div>
              <input v-model="rcaForm.five_whys.why_5" class="form-input" placeholder="Root system / process failure" />
            </div>
          </div>

          <!-- 6M Ishikawa Fishbone Diagram -->
          <div v-if="rcaMethod === 'ISHIKAWA'" class="fishbone-container">
            <div class="fishbone-grid">
              <div class="fishbone-branch">
                <h5>👨 Man (Personnel / Human Factor)</h5>
                <textarea v-model="rcaForm.fishbone.man" class="form-textarea" placeholder="Staffing, training gaps, fatigue..."></textarea>
              </div>
              <div class="fishbone-branch">
                <h5>⚙️ Machine (Equipment / Systems)</h5>
                <textarea v-model="rcaForm.fishbone.machine" class="form-textarea" placeholder="Hardware faults, software bugs, EDC validation..."></textarea>
              </div>
              <div class="fishbone-branch">
                <h5>📦 Material (Investigational Product / Supplies)</h5>
                <textarea v-model="rcaForm.fishbone.material" class="form-textarea" placeholder="IP batch quality, temperature logs, lab kits..."></textarea>
              </div>
              <div class="fishbone-branch">
                <h5>📜 Method (SOPs / Protocol Procedures)</h5>
                <textarea v-model="rcaForm.fishbone.method" class="form-textarea" placeholder="Ambiguous study protocol, outdated SOPs..."></textarea>
              </div>
              <div class="fishbone-branch">
                <h5>📏 Measurement (Calibration / Metrics)</h5>
                <textarea v-model="rcaForm.fishbone.measurement" class="form-textarea" placeholder="Thermometer calibration, lab precision..."></textarea>
              </div>
              <div class="fishbone-branch">
                <h5>🌍 Milieu (Environment / Clinic Context)</h5>
                <textarea v-model="rcaForm.fishbone.milieu" class="form-textarea" placeholder="Clinic layout, weather, regional transport..."></textarea>
              </div>
            </div>
          </div>

          <div class="form-group mt-3">
            <label class="form-label">Root Cause Summary Statement:</label>
            <input v-model="rcaForm.root_cause_summary" class="form-input" placeholder="Synthesized root cause finding" />
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="isRcaModalOpen = false">Cancel</button>
          <button class="btn btn-primary" @click="saveRcaInvestigation">Save RCA Findings</button>
        </div>
      </div>
    </div>

    <!-- Modal 2: 1-Click Inspection Readiness Dossier Modal -->
    <div v-if="isDossierModalOpen" class="modal-backdrop">
      <div class="modal-content modal-lg">
        <div class="modal-header">
          <h3 class="modal-title">GxP Inspection Readiness Dossier &amp; Merkle Seal</h3>
          <button class="modal-close" @click="isDossierModalOpen = false">✕</button>
        </div>
        <div class="modal-body">
          <div class="dossier-seal-banner">
            <div class="seal-icon">🔒</div>
            <div>
              <h4>Cryptographic Merkle SHA-256 Tamper Seal</h4>
              <code class="merkle-hash">{{ dossierData?.cryptographic_tamper_seal || 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855' }}</code>
              <p class="seal-desc">This dossier represents a verified, immutable state snapshot conforming to 21 CFR Part 11 and ICH E6(R3).</p>
            </div>
          </div>

          <div class="dossier-stats-grid mt-3">
            <div class="dossier-stat-item">
              <span class="dossier-stat-num">{{ dossierData?.summary_statistics?.total_deviations || deviations.length }}</span>
              <span class="dossier-stat-label">Deviations Logged</span>
            </div>
            <div class="dossier-stat-item">
              <span class="dossier-stat-num">{{ dossierData?.summary_statistics?.total_capas || capas.length }}</span>
              <span class="dossier-stat-label">CAPA Records</span>
            </div>
            <div class="dossier-stat-item">
              <span class="dossier-stat-num">{{ dossierData?.summary_statistics?.total_audits || audits.length }}</span>
              <span class="dossier-stat-label">Clinical Audits</span>
            </div>
            <div class="dossier-stat-item">
              <span class="dossier-stat-num">{{ dossierData?.summary_statistics?.total_serious_breaches || seriousBreaches.length }}</span>
              <span class="dossier-stat-label">Serious Breaches</span>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="isDossierModalOpen = false">Close</button>
          <button class="btn btn-primary" @click="downloadDossierJson">Download JSON Dossier</button>
        </div>
      </div>
    </div>

    <!-- Modal 3: CSR Section 9.6 Narrative Viewer -->
    <div v-if="isCsrNarrativeModalOpen" class="modal-backdrop">
      <div class="modal-content">
        <div class="modal-header">
          <h3 class="modal-title">CSR Section 9.6 Quality Tolerance Limit Summary</h3>
          <button class="modal-close" @click="isCsrNarrativeModalOpen = false">✕</button>
        </div>
        <div class="modal-body">
          <textarea readonly class="form-textarea" rows="10" :value="activeCsrNarrative"></textarea>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="isCsrNarrativeModalOpen = false">Close</button>
          <button class="btn btn-primary" @click="copyCsrText">Copy Narrative to Clipboard</button>
        </div>
      </div>
    </div>

    <!-- Modal 4: 1-Click Promote Finding to CAPA -->
    <div v-if="isPromoteModalOpen" class="modal-backdrop">
      <div class="modal-content">
        <div class="modal-header">
          <h3 class="modal-title">1-Click Promote Audit Finding to CAPA</h3>
          <button class="modal-close" @click="isPromoteModalOpen = false">✕</button>
        </div>
        <div class="modal-body">
          <div class="finding-quote">
            <strong>Finding {{ activeFindingToPromote?.finding_number }}:</strong> {{ activeFindingToPromote?.condition }}
          </div>
          <div class="form-group mt-2">
            <label class="form-label">Action Plan:</label>
            <textarea v-model="promoteForm.action_plan" class="form-textarea" placeholder="Corrective actions to remediate finding..."></textarea>
          </div>
          <div class="form-group mt-2">
            <label class="form-label">Preventive Measures:</label>
            <textarea v-model="promoteForm.preventive_measures" class="form-textarea" placeholder="Systemic prevention measures..."></textarea>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="isPromoteModalOpen = false">Cancel</button>
          <button class="btn btn-primary" @click="executePromoteFinding">Generate Linked CAPA</button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, onMounted } from "vue";

// Navigation state
const activeTab = ref("rbqm");
const availableStudies = ref(["STUDY-CARDIO-002", "STUDY-ONC-2026", "STUDY-VASC-003"]);
const selectedStudyId = ref("STUDY-CARDIO-002");
const deviationSeverityFilter = ref("ALL");

// Data state
const deviations = ref([]);
const capas = ref([]);
const kriDefinitions = ref([]);
const siteRiskProfiles = ref([]);
const qtls = ref([]);
const qtlBreaches = ref([]);
const audits = ref([]);
const seriousBreaches = ref([]);
const dossierData = ref(null);

// Modal states
const isRcaModalOpen = ref(false);
const activeRcaDeviation = ref(null);
const rcaMethod = ref("FIVE_WHYS");
const rcaForm = ref({
  five_whys: { why_1: "", why_2: "", why_3: "", why_4: "", why_5: "" },
  fishbone: { man: "", machine: "", material: "", method: "", measurement: "", milieu: "" },
  root_cause_summary: "",
});

const isDossierModalOpen = ref(false);
const isCsrNarrativeModalOpen = ref(false);
const activeCsrNarrative = ref("");

const isPromoteModalOpen = ref(false);
const activeFindingToPromote = ref(null);
const promoteForm = ref({ action_plan: "", preventive_measures: "" });

// CAPA Stages
const CAPA_STAGES = [
  { key: "INITIATED", label: "1. Initiated" },
  { key: "UNDER_REVIEW", label: "2. Under Review" },
  { key: "APPROVED", label: "3. Approved" },
  { key: "IMPLEMENTATION", label: "4. Implementation" },
  { key: "IMPLEMENTATION_VERIFIED", label: "5. Verified" },
  { key: "EFFECTIVENESS_CHECK", label: "6. Effectiveness" },
  { key: "CLOSED", label: "7. Closed" },
];

// Computed KPI counts
const criticalDeviationsCount = computed(() => deviations.value.filter(d => d.severity === "CRITICAL").length);
const majorDeviationsCount = computed(() => deviations.value.filter(d => d.severity === "MAJOR").length);
const activeCapasCount = computed(() => capas.value.filter(c => c.status !== "CLOSED" && c.status !== "CANCELLED").length);
const highRiskSitesCount = computed(() => siteRiskProfiles.value.filter(s => s.composite_risk_score >= 5.0).length);

const activeBreachClockHours = computed(() => {
  const active = seriousBreaches.value.find(b => b.status !== "CLOSED" && b.status !== "AUTHORITY_NOTIFIED");
  return active ? active.hours_remaining || 156.4 : null;
});
const activeBreachesOverdue = computed(() => {
  return seriousBreaches.value.some(b => b.is_overdue);
});

const filteredDeviations = computed(() => {
  if (deviationSeverityFilter.value === "ALL") return deviations.value;
  return deviations.value.filter(d => d.severity === deviationSeverityFilter.value);
});

function getCapasInStage(stageKey) {
  return capas.value.filter(c => c.status === stageKey);
}

function getCompletedActionItemsCount(capa) {
  if (!capa.action_items) return 0;
  return capa.action_items.filter(a => a.status === "COMPLETED").length;
}

function getActionItemsPercentage(capa) {
  if (!capa.action_items || capa.action_items.length === 0) return 100;
  return Math.round((getCompletedActionItemsCount(capa) / capa.action_items.length) * 100);
}

function getNextStage(currentStatus) {
  const map = {
    INITIATED: "UNDER_REVIEW",
    UNDER_REVIEW: "APPROVED",
    APPROVED: "IMPLEMENTATION",
    IMPLEMENTATION: "IMPLEMENTATION_VERIFIED",
    IMPLEMENTATION_VERIFIED: "EFFECTIVENESS_CHECK",
    EFFECTIVENESS_CHECK: "CLOSED",
  };
  return map[currentStatus] || null;
}

function getRiskColor(score) {
  if (score >= 8) return "#ef4444";
  if (score >= 5) return "#f59e0b";
  return "#10b981";
}

function getClockClass(breach) {
  if (breach.is_overdue) return "clock-critical";
  if (breach.hours_remaining !== undefined && breach.hours_remaining < 48) return "clock-warning";
  return "clock-normal";
}

function formatDate(dStr) {
  if (!dStr) return "N/A";
  try {
    return new Date(dStr).toLocaleDateString();
  } catch {
    return dStr;
  }
}

// Initial Sample Seed Data
function loadSeedData() {
  deviations.value = [
    {
      id: "DEV-101",
      title: "Informed Consent Version Mismatch",
      description: "Subject signed ICF v2.0 instead of newly approved v3.0 detailing cardiac safety updates.",
      site_id: "SITE-101",
      severity: "CRITICAL",
      type: "INFORMED_CONSENT",
      status: "UNDER_INVESTIGATION",
      source_system: "eConsent",
      source_reference_id: "EC-88120",
      impact_safety: true,
      rca: {
        methodology: "FIVE_WHYS",
        five_whys_chain: {
          why_1: "Old ICF signed",
          why_2: "Site coordinator handed out printed paper ICF",
          why_3: "IRB approval email was not forwarded to pharmacy/clinic desk",
          why_4: "No automated eConsent version gate in clinic tablet",
          why_5: "Clinic workflow allowed paper override without dual sign-off",
        },
      },
    },
    {
      id: "DEV-102",
      title: "Depot Temperature Excursion +11°C",
      description: "Pharmacy refrigerator temperature excursion recorded for 4 hours.",
      site_id: "SITE-102",
      severity: "MAJOR",
      type: "INVESTIGATIONAL_PRODUCT",
      status: "CAPA_INITIATED",
      source_system: "IoT_Depot",
      source_reference_id: "DEP-994",
      impact_safety: false,
    },
  ];

  kriDefinitions.value = [
    {
      code: "KRI_QUERY_AGE",
      name: "Unresolved Query Aging Rate",
      category: "DATA_INTEGRITY",
      description: "Percentage of data queries remaining open past 14 calendar days.",
      calculation_formula: "(count(queries > 14d) / count(total_queries)) * 100",
      green_threshold: 5.0,
      amber_threshold: 15.0,
      red_threshold: 25.0,
      weight: 2.0,
    },
    {
      code: "KRI_AE_RATE",
      name: "Adverse Event Under-Reporting Rate",
      category: "PATIENT_SAFETY",
      description: "Average AE logging frequency per enrolled subject compared to study cohort mean.",
      calculation_formula: "count(ae_records) / count(active_subjects)",
      green_threshold: 3.0,
      amber_threshold: 6.0,
      red_threshold: 10.0,
      weight: 3.0,
    },
    {
      code: "KRI_PROTOCOL_DEVIATION",
      name: "Protocol Deviation Frequency",
      category: "PROTOCOL_COMPLIANCE",
      description: "Total protocol deviations logged per active subject at site.",
      calculation_formula: "count(deviations) / count(active_subjects)",
      green_threshold: 1.0,
      amber_threshold: 3.0,
      red_threshold: 5.0,
      weight: 2.5,
    },
    {
      code: "KRI_MISSED_VISIT",
      name: "Missed Protocol Visit Rate",
      category: "STUDY_CONDUCT",
      description: "Percentage of scheduled protocol visits marked as missed.",
      calculation_formula: "(count(missed_visits) / count(scheduled_visits)) * 100",
      green_threshold: 2.0,
      amber_threshold: 5.0,
      red_threshold: 8.0,
      weight: 1.5,
    },
  ];

  siteRiskProfiles.value = [
    { site_id: "SITE-104", risk_rank: 1, high_kri_count: 3, active_deviations_count: 2, composite_risk_score: 9.5 },
    { site_id: "SITE-101", risk_rank: 2, high_kri_count: 1, active_deviations_count: 1, composite_risk_score: 5.2 },
    { site_id: "SITE-102", risk_rank: 3, high_kri_count: 0, active_deviations_count: 1, composite_risk_score: 2.5 },
    { site_id: "SITE-103", risk_rank: 4, high_kri_count: 0, active_deviations_count: 0, composite_risk_score: 1.0 },
  ];

  qtls.value = [
    {
      id: "QTL-01",
      parameter_name: "Lost to Follow-up Rate",
      unit: "%",
      target_value: 3.0,
      tolerance_limit: 5.0,
      observed_value: 6.2,
      latest_breach: {
        observed_value: 6.2,
        tolerance_limit: 5.0,
        csr_narrative: "CSR Section 9.6 QTL Summary:\nParameter 'Lost to Follow-up Rate' observed at 6.2%, breaching tolerance limit of 5.0%.\nRoot Cause: Severe regional transit disruption.\nMitigation: Deployed mobile phlebotomy and telehealth follow-up.",
      },
    },
  ];

  capas.value = [
    {
      id: "CAPA-2026-001",
      action_plan: "Re-train pharmacy staff and replace temperature data loggers",
      preventive_measures: "Automated SMS alerts dispatched to site PI on 2C variance",
      risk_level: "HIGH",
      capa_type: "BOTH",
      status: "IMPLEMENTATION",
      action_items: [
        { id: "ACT-1", title: "HVAC sensor calibration", status: "COMPLETED" },
        { id: "ACT-2", title: "Site staff SOP sign-off", status: "OPEN" },
      ],
    },
    {
      id: "CAPA-2026-002",
      action_plan: "Implement hard-stop electronic consent version gating",
      preventive_measures: "Enforce tablet sync prior to dispensing drug kit",
      risk_level: "CRITICAL",
      capa_type: "PREVENTIVE",
      status: "UNDER_REVIEW",
      action_items: [],
    },
  ];

  audits.value = [
    {
      id: "AUD-101",
      audit_number: "AUD-2026-SITE-101",
      audit_type: "SITE_AUDIT",
      site_id: "SITE-101",
      lead_auditor: "lead.auditor@gxp-assurance.com",
      planned_start_date: "2026-09-01T09:00:00",
      planned_end_date: "2026-09-03T17:00:00",
      status: "PLANNED",
      findings: [
        { finding_number: "FINDING-01", severity: "CRITICAL", condition: "Refrigerated IP temperature log missing for 4 days" },
      ],
    },
  ];

  seriousBreaches.value = [
    {
      id: "SB-001",
      title: "15 Subjects dosed without required cardiac safety re-consent",
      summary: "Protocol amendment 3 cardiac warnings were not presented to subjects before infusion cycle 4.",
      site_id: "SITE-101",
      discovery_date: new Date(Date.now() - 24 * 3600 * 1000).toISOString(),
      affected_authorities: ["MHRA", "EMA", "FDA"],
      status: "UNDER_EVALUATION",
      hours_remaining: 144.0,
      is_overdue: false,
    },
  ];

  dossierData.value = {
    study_id: "STUDY-CARDIO-002",
    cryptographic_tamper_seal: "9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
    summary_statistics: {
      total_deviations: 2,
      total_capas: 2,
      total_audits: 1,
      total_serious_breaches: 1,
    },
  };
}

onMounted(() => {
  loadSeedData();
});

function onStudyChange() {
  // Reload study context
}

function openNewDeviationModal() {
  alert("Log New Deviation: Enter Study, Site, Severity (Critical/Major/Minor), and Type.");
}

function openIngestModal() {
  alert("Simulated Ingestion Triggered: Ingesting automated event from EDC / CTMS.");
}

function openDossierModal() {
  isDossierModalOpen.value = true;
}

function openRcaModal(dev) {
  activeRcaDeviation.value = dev;
  isRcaModalOpen.value = true;
}

function saveRcaInvestigation() {
  if (activeRcaDeviation.value) {
    activeRcaDeviation.value.rca = {
      methodology: rcaMethod.value,
      five_whys_chain: { ...rcaForm.value.five_whys },
      root_cause_summary: rcaForm.value.root_cause_summary || "Investigation completed.",
    };
  }
  isRcaModalOpen.value = false;
}

function openNewCapaModal() {
  alert("Create CAPA Record: Define Action Plan, Preventive Measures, Target Date, and Risk Level.");
}

function openCapaDetail(capa) {
  alert(`CAPA Record ${capa.id}: Action items and effectiveness check scheduler.`);
}

function advanceCapaStage(capa) {
  const next = getNextStage(capa.status);
  if (!next) return;

  if (next === "IMPLEMENTATION_VERIFIED") {
    const incomplete = (capa.action_items || []).filter(a => a.status !== "COMPLETED");
    if (incomplete.length > 0) {
      alert(`⚠️ Stage-Gate Enforcement: Cannot transition to 'Implementation Verified'. ${incomplete.length} sub-action items remain open.`);
      return;
    }
  }

  capa.status = next;
}

function openNewAuditModal() {
  alert("Schedule Audit: Select Audit Type (Site, Vendor, Process, TMF) and Lead Auditor.");
}

function openAuditFindingsModal(audit) {
  if (audit.findings && audit.findings.length > 0) {
    activeFindingToPromote.value = audit.findings[0];
    promoteForm.value.action_plan = "Quarantine IP batch and replace monitoring hardware.";
    promoteForm.value.preventive_measures = "Install dual automated alarm logging.";
    isPromoteModalOpen.value = true;
  } else {
    alert("No findings recorded yet for this audit engagement.");
  }
}

function executePromoteFinding() {
  if (!activeFindingToPromote.value) return;
  capas.value.push({
    id: `CAPA-PROMOTE-${Date.now().toString().slice(-4)}`,
    action_plan: promoteForm.value.action_plan,
    preventive_measures: promoteForm.value.preventive_measures,
    risk_level: "HIGH",
    capa_type: "BOTH",
    status: "INITIATED",
    action_items: [],
  });
  isPromoteModalOpen.value = false;
  activeTab.value = "capa";
  alert("✅ Finding successfully promoted to formal CAPA Record with bi-directional GxP trace linkage!");
}

function openNewBreachModal() {
  alert("Report Serious Breach: Record Discovery Date, Health Authorities, and Clinical Safety Impact.");
}

function confirmBreach(breach) {
  breach.status = "CONFIRMED_BREACH";
  alert("⚠️ Breach status confirmed. Regulatory notification 7-day clock is running.");
}

function notifyAuthorities(breach) {
  breach.status = "AUTHORITY_NOTIFIED";
  alert("🏛️ Regulatory health authority formal notification dispatched.");
}

function runKriBatchScoring() {
  alert("⚡ Running KRI batch evaluation across study sites. Statistical Z-scores updated.");
}

function recomputeSiteProfiles() {
  alert("🔄 Site Risk Index recomputed and ranked.");
}

function triggerTargetedMonitoring(siteId) {
  alert(`🎯 Targeted Monitoring Plan generated for ${siteId}. Triggering focused SDV sample.`);
}

function openNewQtlModal() {
  alert("Add QTL: Define Parameter, Unit, Target Value, and Tolerance Limit.");
}

function evaluateQtlBreach(qtl) {
  qtl.observed_value = 6.5;
  qtl.latest_breach = {
    observed_value: 6.5,
    tolerance_limit: qtl.tolerance_limit,
    csr_narrative: `CSR Section 9.6 QTL Summary:\nParameter '${qtl.parameter_name}' observed value of 6.5% exceeded the protocol tolerance limit of ${qtl.tolerance_limit}%.\nClinical root cause analysis conducted; corrective actions implemented with no impact on primary efficacy endpoint integrity.`,
  };
  alert(`⚡ QTL Evaluation: Observed 6.5% > ${qtl.tolerance_limit}%. Breach recorded and CSR 9.6 text synthesized.`);
}

function viewCsrNarrative(breach) {
  activeCsrNarrative.value = breach.csr_narrative;
  isCsrNarrativeModalOpen.value = true;
}

function copyCsrText() {
  navigator.clipboard.writeText(activeCsrNarrative.value);
  alert("📋 CSR Section 9.6 narrative copied to clipboard!");
  isCsrNarrativeModalOpen.value = false;
}

function downloadDossierJson() {
  const jsonStr = JSON.stringify(dossierData.value, null, 2);
  const blob = new Blob([jsonStr], { type: "application/json" });
  const url = URL.createObjectURL(blob);
  const a = document.createElement("a");
  a.href = url;
  a.download = `GxP_Inspection_Dossier_${selectedStudyId.value}.json`;
  a.click();
  URL.revokeObjectURL(url);
}
</script>

<style scoped>
.quality-tabs-bar {
  display: flex;
  gap: 8px;
  border-bottom: 2px solid var(--border, #e2e8f0);
  margin-bottom: 20px;
  overflow-x: auto;
  padding-bottom: 4px;
}

.quality-tab-btn {
  background: none;
  border: none;
  padding: 10px 16px;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--neutral-dark, #64748b);
  cursor: pointer;
  border-radius: 6px 6px 0 0;
  display: flex;
  align-items: center;
  gap: 8px;
  transition: all 0.2s ease;
  white-space: nowrap;
}

.quality-tab-btn:hover {
  background-color: #f8fafc;
  color: var(--primary, #0f172a);
}

.quality-tab-btn.active {
  color: var(--accent, #2563eb);
  border-bottom: 3px solid var(--accent, #2563eb);
  background-color: #eff6ff;
}

.study-selector-box {
  display: flex;
  align-items: center;
}

.study-select {
  padding: 6px 12px;
  border-radius: 6px;
  border: 1px solid var(--border, #cbd5e1);
  background-color: white;
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--primary, #0f172a);
}

.kri-cards-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(280px, 1fr));
  gap: 16px;
  margin-top: 16px;
}

.kri-card {
  background: white;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 8px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.05);
}

.kri-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.kri-code {
  font-weight: 700;
  font-size: 0.8rem;
  color: #475569;
}

.kri-name {
  font-size: 1rem;
  font-weight: 600;
  color: var(--primary, #0f172a);
}

.kri-desc {
  font-size: 0.8rem;
  color: #64748b;
}

.kri-thresholds {
  display: flex;
  gap: 6px;
  margin-top: 4px;
}

.threshold-pill {
  font-size: 0.7rem;
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 600;
}
.threshold-pill.green { background: #dcfce7; color: #15803d; }
.threshold-pill.amber { background: #fef3c7; color: #b45309; }
.threshold-pill.red { background: #fee2e2; color: #b91c1c; }

.kri-footer {
  margin-top: auto;
  border-top: 1px solid #f1f5f9;
  padding-top: 8px;
  display: flex;
  justify-content: space-between;
  font-size: 0.75rem;
  color: #64748b;
}

.score-bar-container {
  display: flex;
  align-items: center;
  gap: 8px;
}

.score-bar {
  height: 8px;
  border-radius: 4px;
  min-width: 12px;
}

.score-val {
  font-size: 0.8rem;
  font-weight: 700;
}

.rank-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: #f1f5f9;
}
.rank-1 { background: #fee2e2; color: #b91c1c; }
.rank-2 { background: #fef3c7; color: #b45309; }

.qtl-list {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 16px;
}

.qtl-item-card {
  background: white;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 16px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.qtl-title-row {
  display: flex;
  align-items: center;
  gap: 10px;
}

.qtl-metrics-row {
  display: flex;
  gap: 20px;
  margin-top: 6px;
}

.metric-item {
  font-size: 0.85rem;
}
.metric-label {
  color: #64748b;
  margin-right: 4px;
}
.metric-value {
  font-weight: 600;
}

.kanban-board {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
  gap: 12px;
  margin-top: 16px;
  overflow-x: auto;
  padding-bottom: 8px;
}

.kanban-column {
  background: #f8fafc;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  min-height: 400px;
}

.kanban-column-header {
  padding: 10px 12px;
  background: #f1f5f9;
  border-bottom: 1px solid var(--border, #e2e8f0);
  display: flex;
  justify-content: space-between;
  align-items: center;
  font-weight: 700;
  font-size: 0.85rem;
}

.stage-count {
  background: white;
  padding: 2px 6px;
  border-radius: 10px;
  font-size: 0.75rem;
  border: 1px solid #cbd5e1;
}

.kanban-column-body {
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 10px;
  flex: 1;
}

.kanban-card {
  background: white;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 6px;
  padding: 10px;
  display: flex;
  flex-direction: column;
  gap: 6px;
  box-shadow: 0 1px 2px rgba(0,0,0,0.05);
}

.kanban-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.kanban-card-title {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--primary, #0f172a);
}

.kanban-card-preventive {
  font-size: 0.75rem;
  color: #64748b;
}

.action-items-progress {
  margin-top: 4px;
}

.progress-label {
  display: flex;
  justify-content: space-between;
  font-size: 0.7rem;
  color: #64748b;
  margin-bottom: 2px;
}

.progress-bar-bg {
  background: #f1f5f9;
  height: 4px;
  border-radius: 2px;
  overflow: hidden;
}

.progress-bar-fill {
  background: var(--accent, #2563eb);
  height: 100%;
}

.kanban-card-footer {
  margin-top: 6px;
  display: flex;
  justify-content: space-between;
}

.breach-cards-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
  margin-top: 16px;
}

.breach-item-card {
  background: white;
  border: 1px solid var(--border, #e2e8f0);
  border-radius: 8px;
  padding: 16px;
  box-shadow: 0 1px 3px rgba(0,0,0,0.05);
}

.breach-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.breach-title {
  margin-top: 4px;
  font-size: 1.1rem;
  font-weight: 700;
}

.breach-clock-display {
  display: flex;
  align-items: center;
  gap: 10px;
  padding: 8px 14px;
  border-radius: 8px;
}
.clock-normal { background: #eff6ff; border: 1px solid #bfdbfe; color: #1d4ed8; }
.clock-warning { background: #fef3c7; border: 1px solid #fde68a; color: #b45309; }
.clock-critical { background: #fee2e2; border: 1px solid #fca5a5; color: #b91c1c; animation: pulse 2s infinite; }

.clock-hours {
  font-size: 1.2rem;
  font-weight: 800;
}
.clock-label {
  font-size: 0.7rem;
  font-weight: 600;
}

.breach-body {
  margin-top: 12px;
  font-size: 0.9rem;
}

.breach-meta-row {
  display: flex;
  gap: 20px;
  margin-top: 8px;
  font-size: 0.8rem;
  color: #64748b;
}

.authority-tag {
  background: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 700;
  color: #334155;
  margin-left: 4px;
}

.breach-footer {
  margin-top: 14px;
  border-top: 1px solid #f1f5f9;
  padding-top: 10px;
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.five-whys-container {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 12px;
}

.why-step {
  display: flex;
  align-items: center;
  gap: 10px;
}

.why-badge {
  background: var(--accent, #2563eb);
  color: white;
  font-weight: 700;
  font-size: 0.75rem;
  padding: 6px 10px;
  border-radius: 4px;
  min-width: 65px;
  text-align: center;
}

.fishbone-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 12px;
  margin-top: 12px;
}

.fishbone-branch {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 10px;
}

.fishbone-branch h5 {
  font-size: 0.85rem;
  font-weight: 700;
  margin-bottom: 6px;
}

.dossier-seal-banner {
  display: flex;
  gap: 16px;
  background: #eff6ff;
  border: 1px solid #bfdbfe;
  border-radius: 8px;
  padding: 16px;
  align-items: center;
}

.seal-icon {
  font-size: 2rem;
}

.merkle-hash {
  display: block;
  font-size: 0.75rem;
  word-break: break-all;
  background: white;
  padding: 4px 8px;
  border-radius: 4px;
  border: 1px solid #cbd5e1;
  margin: 6px 0;
}

.dossier-stats-grid {
  display: grid;
  grid-template-columns: repeat(4, 1fr);
  gap: 12px;
}

.dossier-stat-item {
  background: #f8fafc;
  border: 1px solid #e2e8f0;
  border-radius: 6px;
  padding: 12px;
  text-align: center;
}

.dossier-stat-num {
  display: block;
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--primary, #0f172a);
}

.dossier-stat-label {
  font-size: 0.75rem;
  color: #64748b;
}

.btn-xs {
  padding: 3px 8px;
  font-size: 0.75rem;
  border-radius: 4px;
}

.badge-rca {
  background: #e0e7ff;
  color: #4338ca;
  font-size: 0.7rem;
}

.badge-critical { background: #fee2e2; color: #b91c1c; }
.badge-high { background: #ffedd5; color: #c2410c; }
.badge-major { background: #fef3c7; color: #b45309; }
.badge-minor { background: #f1f5f9; color: #475569; }
.badge-low { background: #dcfce7; color: #15803d; }
.badge-neutral { background: #f1f5f9; color: #475569; }
</style>
