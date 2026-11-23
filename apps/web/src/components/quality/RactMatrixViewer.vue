<template>
  <div
    class="ract-matrix-viewer"
    role="region"
    aria-label="TransCelerate Risk Assessment & Categorization Tool (RACT) Matrix"
  >
    <!-- Header & Action Ribbon -->
    <header class="ract-header">
      <div class="header-main">
        <div class="title-group">
          <div class="badge-row">
            <span class="badge badge-regulatory">TransCelerate RACT</span>
            <span class="badge badge-gxp">ICH E6(R3) / E8(R1)</span>
            <span class="badge badge-standard">21 CFR Part 11</span>
          </div>
          <h2 class="ract-title">
            TransCelerate RACT Matrix &amp; Automated KRI Linkage
          </h2>
          <p class="ract-subtitle">
            Protocol risk assessment covering 6 clinical categories (Safety,
            Consent, Study Conduct, Data Integrity, IP Management, Technology)
            with Probability, Impact, and Detectability scoring mapped directly
            to automated Key Risk Indicators and Site Risk Index rankings.
          </p>
        </div>

        <div class="header-meta-group">
          <div class="meta-item">
            <label for="ract-study-select" class="meta-label"
              >Active Study:</label
            >
            <select
              id="ract-study-select"
              v-model="selectedStudyId"
              class="ract-select"
              @change="fetchRactData"
            >
              <option v-for="s in availableStudies" :key="s" :value="s">
                {{ s }}
              </option>
            </select>
          </div>
          <div class="meta-item">
            <span class="meta-label">Protocol Risk Posture:</span>
            <span
              class="status-pill"
              :class="
                highRiskCount > 0 ? 'status-pill-alert' : 'status-pill-nominal'
              "
            >
              {{
                highRiskCount > 0
                  ? `${highRiskCount} High Risk Factors`
                  : "Low / Controlled Risk"
              }}
            </span>
          </div>
        </div>
      </div>

      <div class="header-actions">
        <button
          class="ract-btn ract-btn-secondary"
          :disabled="isLoading"
          title="Seed standard TransCelerate default matrix"
          @click="seedDefaults"
        >
          <span class="btn-icon">⚡</span> Seed Defaults
        </button>
        <button
          class="ract-btn ract-btn-secondary"
          :disabled="isLoading"
          title="Compute weighted Site Risk Index across sites"
          @click="computeSiteRiskProfiles"
        >
          <span class="btn-icon">🎯</span> Compute SRI
        </button>
        <button
          class="ract-btn ract-btn-secondary"
          :disabled="isLoading"
          title="Compile Part 11 signed Centralized Monitoring Dossier"
          @click="openDossierModal"
        >
          <span class="btn-icon">📄</span> Inspection Dossier
        </button>
        <button
          class="ract-btn ract-btn-primary"
          :disabled="isLoading"
          title="Define new protocol risk factor"
          @click="openAddModal"
        >
          <span class="btn-icon">➕</span> Add Risk Factor
        </button>
      </div>
    </header>

    <!-- KPI Summary Grid -->
    <div class="kpi-grid">
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div
        class="kpi-card"
        :class="{ 'kpi-card-active': selectedCategory === 'ALL' }"
        @click="selectedCategory = 'ALL'"
      >
        <div class="kpi-icon icon-total">📊</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ totalRiskFactors }}
          </div>
          <div class="kpi-label">Total Protocol Risks</div>
        </div>
      </div>

      <div class="kpi-card kpi-card-danger">
        <div class="kpi-icon icon-high">🚨</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ highRiskCount }}
          </div>
          <div class="kpi-label">High Risk Factors (Score &ge; 15)</div>
        </div>
      </div>

      <div class="kpi-card kpi-card-warning">
        <div class="kpi-icon icon-med">⚠️</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ mediumRiskCount }}
          </div>
          <div class="kpi-label">Medium Risk Factors (7 - 14)</div>
        </div>
      </div>

      <div class="kpi-card kpi-card-success">
        <div class="kpi-icon icon-low">✅</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ lowRiskCount }}
          </div>
          <div class="kpi-label">Low Risk Factors (&le; 6)</div>
        </div>
      </div>

      <div class="kpi-card">
        <div class="kpi-icon icon-kri">🔗</div>
        <div class="kpi-content">
          <div class="kpi-value">{{ linkedKriCoverage }}%</div>
          <div class="kpi-label">Automated KRI Coverage</div>
        </div>
      </div>
    </div>

    <!-- Category Tabs Navigation -->
    <div class="category-tabs-bar">
      <button
        class="category-tab-btn"
        :class="{ active: selectedCategory === 'ALL' }"
        @click="selectedCategory = 'ALL'"
      >
        <span class="tab-icon">🌐</span> All Categories ({{ totalRiskFactors }})
      </button>
      <button
        v-for="cat in categoryList"
        :key="cat.key"
        class="category-tab-btn"
        :class="{ active: selectedCategory === cat.key }"
        @click="selectedCategory = cat.key"
      >
        <span class="tab-icon">{{ cat.icon }}</span> {{ cat.name }} ({{
          getCategoryCount(cat.key)
        }})
      </button>
    </div>

    <!-- Main View Content (Matrix vs. Site Risk Index) -->
    <div class="view-switch-ribbon">
      <div class="sub-nav-group">
        <button
          class="sub-tab-btn"
          :class="{ active: activeViewMode === 'matrix' }"
          @click="activeViewMode = 'matrix'"
        >
          📋 Interactive RACT Risk Matrix
        </button>
        <button
          class="sub-tab-btn"
          :class="{ active: activeViewMode === 'sri' }"
          @click="activeViewMode = 'sri'"
        >
          🎯 Site Risk Index (SRI) &amp; Percentiles ({{ siteProfiles.length }}
          Sites)
        </button>
        <button
          class="sub-tab-btn"
          :class="{ active: activeViewMode === 'kri' }"
          @click="activeViewMode = 'kri'"
        >
          📈 KRI Threshold Definitions
        </button>
      </div>

      <div class="view-filter-group">
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          v-model="searchQuery"
          type="text"
          class="ract-search-input"
          placeholder="Filter risk factors, topics, or KRIs..."
        />
      </div>
    </div>

    <!-- MODE 1: Interactive RACT Risk Matrix Grid -->
    <section v-if="activeViewMode === 'matrix'" class="matrix-section">
      <div v-if="filteredFactors.length === 0" class="empty-state-card">
        <div class="empty-icon">📋</div>
        <h3>No Protocol Risk Factors Found</h3>
        <p>
          No risk factors match the current filter or category. Seed standard
          TransCelerate defaults to initialize the matrix.
        </p>
        <button class="ract-btn ract-btn-primary" @click="seedDefaults">
          ⚡ Seed Standard TransCelerate RACT Defaults
        </button>
      </div>

      <div v-else class="table-container">
        <table class="ract-table">
          <thead>
            <tr>
              <th>Category</th>
              <th>Risk Topic &amp; Description</th>
              <th
                class="text-center"
                title="Likelihood of risk occurring (1=Low, 2=Medium, 3=High)"
              >
                Prob (P)
              </th>
              <th
                class="text-center"
                title="Severity of impact on safety or data (1=Low, 2=Medium, 3=High)"
              >
                Impact (I)
              </th>
              <th
                class="text-center"
                title="Ease of detection (1=High/Easy, 2=Medium, 3=Low/Difficult)"
              >
                Det (D)
              </th>
              <th
                class="text-center"
                title="Composite Risk Score = P x I x D (1 - 27)"
              >
                Total Score
              </th>
              <th class="text-center">Risk Tier</th>
              <th>Mitigation Controls</th>
              <th>Linked KRI</th>
              <th class="text-right">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="factor in filteredFactors"
              :key="factor.id"
              :class="`row-tier-${factor.risk_tier.toLowerCase()}`"
            >
              <td>
                <span
                  :class="`category-badge cat-${factor.category.toLowerCase()}`"
                >
                  {{ formatCategory(factor.category) }}
                </span>
              </td>
              <td>
                <div class="topic-title">
                  {{ factor.risk_topic }}
                </div>
                <div class="topic-desc">
                  {{ factor.risk_description }}
                </div>
              </td>
              <td class="text-center">
                <span class="score-badge">{{ factor.probability_score }}</span>
              </td>
              <td class="text-center">
                <span class="score-badge">{{ factor.impact_score }}</span>
              </td>
              <td class="text-center">
                <span class="score-badge">{{
                  factor.detectability_score
                }}</span>
              </td>
              <td class="text-center">
                <div class="total-score-badge">
                  {{ factor.total_risk_score }}
                </div>
              </td>
              <td class="text-center">
                <span
                  :class="`tier-pill tier-${factor.risk_tier.toLowerCase()}`"
                >
                  {{ factor.risk_tier }}
                </span>
              </td>
              <td>
                <div class="mitigation-text">
                  {{ factor.mitigation_strategy }}
                </div>
              </td>
              <td>
                <div v-if="factor.linked_kri_code" class="kri-link-pill">
                  <span class="kri-code">{{ factor.linked_kri_code }}</span>
                </div>
                <span v-else class="text-muted">— Unlinked —</span>
              </td>
              <td class="text-right table-actions">
                <ClinicalIconButton
                  class="action-btn"
                  title="Edit risk factor"
                  :label="`Edit risk factor ${factor.risk_topic || factor.category || factor.id}`"
                  @click="openEditModal(factor)"
                >
                  ✏️
                </ClinicalIconButton>
                <ClinicalIconButton
                  class="action-btn action-btn-danger"
                  title="Delete risk factor"
                  :label="`Delete risk factor ${factor.risk_topic || factor.category || factor.id}`"
                  @click="deleteFactor(factor.id)"
                >
                  🗑️
                </ClinicalIconButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </section>

    <!-- MODE 2: Site Risk Index (SRI) & Percentile Ranking League Table -->
    <section v-if="activeViewMode === 'sri'" class="sri-section">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">
              Composite Site Risk Index (SRI) &amp; Percentile Rankings
            </h3>
            <p class="panel-subtitle">
              Weighted composite surveillance scores synthesizing multi-metric
              KRI deviations, outlier Z-scores, and active protocol
              non-compliances.
            </p>
          </div>
          <button
            class="ract-btn ract-btn-primary ract-btn-sm"
            @click="computeSiteRiskProfiles"
          >
            ⚡ Recalculate SRI Rankings
          </button>
        </div>

        <div v-if="siteProfiles.length === 0" class="empty-state-card">
          <div class="empty-icon">🎯</div>
          <h3>No Site Risk Profiles Computed</h3>
          <p>
            Execute batch SRI calculation to rank clinical investigational sites
            by composite risk.
          </p>
          <button
            class="ract-btn ract-btn-primary"
            @click="computeSiteRiskProfiles"
          >
            🎯 Compute Site Risk Profiles
          </button>
        </div>

        <div v-else class="table-container">
          <table class="ract-table">
            <thead>
              <tr>
                <th class="text-center">Rank</th>
                <th>Investigational Site</th>
                <th class="text-center">Composite SRI Score</th>
                <th>Cohort Percentile Rank</th>
                <th class="text-center">Risk Classification</th>
                <th class="text-center">High-Risk KRI Breaches</th>
                <th class="text-center">Active Deviations</th>
                <th>Last Evaluated</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="site in siteProfiles"
                :key="site.id"
                :class="`row-tier-${(site.risk_tier || 'LOW').toLowerCase()}`"
              >
                <td class="text-center">
                  <span class="rank-badge">#{{ site.risk_rank }}</span>
                </td>
                <td>
                  <div class="site-name">
                    {{ site.site_id }}
                  </div>
                </td>
                <td class="text-center">
                  <div class="sri-score-val">
                    {{ site.composite_risk_score.toFixed(2) }}
                  </div>
                </td>
                <td>
                  <div class="percentile-bar-wrapper">
                    <div class="percentile-label">
                      {{ site.percentile_rank.toFixed(1) }}th Percentile
                    </div>
                    <div class="progress-track">
                      <div
                        class="progress-fill"
                        :class="`progress-fill-${(site.risk_tier || 'LOW').toLowerCase()}`"
                        :style="{
                          width: `${Math.min(100, Math.max(5, site.percentile_rank))}%`,
                        }"
                      />
                    </div>
                  </div>
                </td>
                <td class="text-center">
                  <span
                    :class="`tier-pill tier-${(site.risk_tier || 'LOW').toLowerCase()}`"
                  >
                    {{ site.risk_tier || "LOW" }}
                  </span>
                </td>
                <td class="text-center">
                  <span
                    :class="
                      site.high_risk_kri_count > 0
                        ? 'badge-alert-count'
                        : 'badge-nominal-count'
                    "
                  >
                    {{ site.high_risk_kri_count }}
                  </span>
                </td>
                <td class="text-center">
                  <span
                    :class="
                      site.active_deviations_count > 0
                        ? 'badge-alert-count'
                        : 'badge-nominal-count'
                    "
                  >
                    {{ site.active_deviations_count }}
                  </span>
                </td>
                <td>
                  <span class="timestamp-val">{{
                    formatDateTime(site.evaluation_date)
                  }}</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </section>

    <!-- MODE 3: KRI Definitions & Formulas -->
    <section v-if="activeViewMode === 'kri'" class="kri-section">
      <div class="panel-card">
        <div class="panel-header">
          <div>
            <h3 class="panel-title">Automated Key Risk Indicators (KRIs)</h3>
            <p class="panel-subtitle">
              Pre-defined dynamic surveillance thresholds across TransCelerate
              protocol risk categories.
            </p>
          </div>
          <span class="badge badge-gxp"
            >{{ kriDefinitions.length }} Defined KRIs</span
          >
        </div>

        <div class="kri-grid-cards">
          <div
            v-for="kri in kriDefinitions"
            :key="kri.code"
            class="kri-def-card"
          >
            <div class="kri-def-header">
              <span class="kri-code-tag">{{ kri.code }}</span>
              <span
                :class="`category-badge cat-${(kri.category || '').toLowerCase()}`"
              >
                {{ formatCategory(kri.category) }}
              </span>
            </div>
            <h4 class="kri-def-name">
              {{ kri.name }}
            </h4>
            <p class="kri-def-desc">
              {{ kri.description }}
            </p>
            <div class="kri-def-formula">
              <code>{{ kri.calculation_formula }}</code>
            </div>
            <div class="kri-threshold-ribbon">
              <div class="threshold-pill pill-green">
                <span class="th-label">Green:</span> &lt;
                {{ kri.green_threshold }}
              </div>
              <div class="threshold-pill pill-amber">
                <span class="th-label">Amber:</span> {{ kri.green_threshold }} -
                {{ kri.red_threshold }}
              </div>
              <div class="threshold-pill pill-red">
                <span class="th-label">Red:</span> &ge; {{ kri.red_threshold }}
              </div>
              <div class="weight-tag">Weight: {{ kri.weight }}x</div>
            </div>
          </div>
        </div>
      </div>
    </section>

    <!-- MODAL 1: Add / Edit Risk Factor Modal -->
    <ClinicalModal
      v-model="isRiskModalOpen"
      :teleport="false"
      :dismissible="!isLoading"
      size="lg"
      dialog-class="modal-card modal-lg"
      :title="
        isEditing
          ? 'Edit Protocol Risk Factor'
          : 'Define TransCelerate Protocol Risk Factor'
      "
    >
      <div class="form-row">
        <div class="form-group flex-1">
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label class="form-label">Protocol Risk Category *</label>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <select
            v-model="riskForm.category"
            class="form-control"
            :disabled="isEditing"
          >
            <option value="SAFETY">Safety (Patient Safety / Toxicity)</option>
            <option value="CONSENT">
              Consent (Informed Consent / Vulnerable Cohorts)
            </option>
            <option value="STUDY_CONDUCT">
              Study Conduct (Protocol Procedures / Windows)
            </option>
            <option value="DATA_INTEGRITY">
              Data Integrity (Queries / SDV / Completeness)
            </option>
            <option value="IP_MANAGEMENT">
              IP Management (Investigational Product / Cold Chain)
            </option>
            <option value="TECHNOLOGY">
              Technology (eCRF / Sensor Streams / ePRO)
            </option>
          </select>
        </div>

        <div class="form-group flex-2">
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label class="form-label">Risk Topic / Title *</label>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <input
            v-model="riskForm.risk_topic"
            type="text"
            class="form-control"
            placeholder="e.g. Ultra-Cold Chain Temperature Excursions"
          />
        </div>
      </div>

      <div class="form-group">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label class="form-label">Detailed Risk Description *</label>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <textarea
          v-model="riskForm.risk_description"
          class="form-control textarea"
          rows="3"
          placeholder="Explain the potential risk, clinical operational context, and hazard to data integrity or patient safety..."
        />
      </div>

      <!-- Scoring Matrix Section -->
      <div class="scoring-box">
        <h4 class="scoring-title">
          TransCelerate 3-Factor Risk Scoring (Probability x Impact x
          Detectability)
        </h4>
        <div class="scoring-row">
          <div class="score-input-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="score-label">Probability (1-3) *</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select
              v-model.number="riskForm.probability_score"
              class="form-control"
            >
              <option :value="1">1 - Low / Rare</option>
              <option :value="2">2 - Medium / Possible</option>
              <option :value="3">3 - High / Likely</option>
            </select>
          </div>

          <div class="score-input-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="score-label">Impact (1-3) *</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select v-model.number="riskForm.impact_score" class="form-control">
              <option :value="1">1 - Low / Minor</option>
              <option :value="2">2 - Medium / Moderate</option>
              <option :value="3">3 - High / Critical</option>
            </select>
          </div>

          <div class="score-input-group">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="score-label">Detectability (1-3) *</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <select
              v-model.number="riskForm.detectability_score"
              class="form-control"
            >
              <option :value="1">1 - High / Readily Detectable</option>
              <option :value="2">2 - Moderate / Detectable with Effort</option>
              <option :value="3">3 - Low / Difficult to Detect</option>
            </select>
          </div>

          <div class="score-result-group">
            <span class="preview-label">Calculated Score:</span>
            <div class="preview-score-val">{{ computedFormScore }} / 27</div>
            <span :class="`tier-pill tier-${computedFormTier.toLowerCase()}`">
              {{ computedFormTier }} TIER
            </span>
          </div>
        </div>
      </div>

      <div class="form-group">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label class="form-label"
          >Mitigation Controls &amp; Operational Strategy *</label
        >
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <textarea
          v-model="riskForm.mitigation_strategy"
          class="form-control textarea"
          rows="2"
          placeholder="Defined operational controls, monitoring plan rules, or automated sentinels to mitigate this risk..."
        />
      </div>

      <div class="form-group">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label class="form-label">Link Directly to Automated KRI</label>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <select v-model="riskForm.linked_kri_code" class="form-control">
          <option :value="null">— None / Manual Monitoring —</option>
          <option
            v-for="kri in kriDefinitions"
            :key="kri.code"
            :value="kri.code"
          >
            {{ kri.code }} — {{ kri.name }} (Category:
            {{ formatCategory(kri.category) }})
          </option>
        </select>
      </div>

      <div class="form-group">
        <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
        <label class="form-label">21 CFR Part 11 Reason for Change *</label>
        <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
        <input
          v-model="riskForm.reason_for_change"
          type="text"
          class="form-control"
          placeholder="e.g. Protocol amendment 3.0 risk calibration"
        />
      </div>

      <template #footer>
        <button
          type="button"
          class="ract-btn ract-btn-secondary"
          :disabled="isLoading"
          @click="isRiskModalOpen = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="ract-btn ract-btn-primary"
          :disabled="!isFormValid || isLoading"
          @click="saveRiskFactor"
        >
          {{ isEditing ? "Update Risk Factor" : "Save Risk Factor" }}
        </button>
      </template>
    </ClinicalModal>

    <!-- MODAL 2: 1-Click Centralized Monitoring Inspection Dossier (21 CFR Part 11) -->
    <ClinicalModal
      v-model="isDossierModalOpen"
      :teleport="false"
      :dismissible="!isLoading"
      size="xl"
      dialog-class="modal-card modal-xl"
      title="Centralized Monitoring Inspection Dossier Compilation"
    >
      <template #header="{ titleId }">
        <div>
          <h3 :id="titleId" class="modal-title">
            Centralized Monitoring Inspection Dossier Compilation
          </h3>
          <span class="badge badge-gxp"
            >21 CFR Part 11 Cryptographically Sealed Dossier</span
          >
        </div>
      </template>

      <div v-if="!compiledDossier" class="dossier-sign-form">
        <div class="dossier-intro-box">
          <h4>Inspection Snapshot Generator</h4>
          <p>
            Compiles on-demand Centralized Monitoring Dossier synthesizing all
            TransCelerate protocol risk scores, KRI metrics, Site Risk Index
            rankings, and forensics anomalies into an immutable,
            cryptographically sealed regulatory audit package.
          </p>
        </div>

        <div class="form-row">
          <div class="form-group flex-1">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="form-label">Signer Full Legal Name *</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="dossierSigner.name"
              type="text"
              class="form-control"
              placeholder="e.g. Dr. Eleanor Vance, MD"
            />
          </div>
          <div class="form-group flex-1">
            <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
            <label class="form-label">Signer Regulatory Role *</label>
            <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
            <input
              v-model="dossierSigner.role"
              type="text"
              class="form-control"
              placeholder="e.g. Head of Quality Assurance / Lead Monitor"
            />
          </div>
        </div>

        <div class="form-group">
          <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
          <label class="form-label">Custom Dossier Name (Optional)</label>
          <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
          <input
            v-model="dossierSigner.customName"
            type="text"
            class="form-control"
            placeholder="e.g. CSM-Inspection-Dossier-Q3-2026"
          />
        </div>

        <div class="signature-intent-box">
          <div class="intent-title">
            21 CFR Part 11 Electronic Signature Attestation
          </div>
          <div class="intent-text">
            "I hereby review and formally certify this Centralized Monitoring
            Inspection Dossier as an accurate, complete, and immutable
            reflection of trial surveillance data, protocol risk
            classifications, and automated KRI evaluations in compliance with
            GCP ICH E6(R3) and 21 CFR Part 11."
          </div>
        </div>

        <button
          type="button"
          class="ract-btn ract-btn-primary ract-btn-lg"
          :disabled="!dossierSigner.name || !dossierSigner.role || isLoading"
          @click="compileDossier"
        >
          🔒 Compile &amp; Cryptographically Sign Dossier
        </button>
      </div>

      <!-- Compiled Dossier Preview -->
      <div v-else class="dossier-view">
        <div class="dossier-seal-banner">
          <div class="seal-icon">🛡️</div>
          <div class="seal-details">
            <div class="seal-title">
              {{ compiledDossier.dossier_name }}
            </div>
            <div class="seal-meta">
              Status: <strong>{{ compiledDossier.status }}</strong> | Merkle
              Root: <code>{{ compiledDossier.merkle_seal }}</code>
            </div>
          </div>
        </div>

        <div class="dossier-sections-grid">
          <div class="dossier-section-card">
            <h5>TransCelerate RACT Executive Summary</h5>
            <p>
              Total Risk Factors:
              <strong>{{
                compiledDossier.ract_summary?.total_risk_factors
              }}</strong>
              ({{ compiledDossier.ract_summary?.high_risk_count }} High /
              {{ compiledDossier.ract_summary?.medium_risk_count }} Medium /
              {{ compiledDossier.ract_summary?.low_risk_count }} Low)
            </p>
            <p>
              KRI Linkage Coverage:
              <strong
                >{{
                  compiledDossier.ract_summary?.linked_kri_coverage_percentage
                }}%</strong
              >
            </p>
          </div>

          <div class="dossier-section-card">
            <h5>KRI Surveillance Summary</h5>
            <p>
              Active KRIs:
              <strong>{{
                compiledDossier.kri_surveillance_summary?.total_kris_defined
              }}</strong>
            </p>
            <p>
              High / Critical Breach Count:
              <strong>{{
                compiledDossier.kri_surveillance_summary
                  ?.high_or_critical_evaluations_count
              }}</strong>
            </p>
          </div>
        </div>

        <!-- Electronic Signature Certificate -->
        <div class="signature-cert-box">
          <h5>21 CFR Part 11 Electronic Signature Certificate</h5>
          <div class="cert-grid">
            <div>
              Signer:
              <strong>{{
                compiledDossier.electronic_signature?.signer_name
              }}</strong>
            </div>
            <div>
              Role:
              <strong>{{
                compiledDossier.electronic_signature?.signer_role
              }}</strong>
            </div>
            <div>
              Timestamp:
              <strong>{{
                compiledDossier.electronic_signature?.signed_at
              }}</strong>
            </div>
            <div>
              Signature Hash:
              <code>{{
                compiledDossier.electronic_signature?.signature_hash
              }}</code>
            </div>
          </div>
        </div>
      </div>

      <template #footer>
        <button
          type="button"
          class="ract-btn ract-btn-secondary"
          :disabled="isLoading"
          @click="isDossierModalOpen = false"
        >
          Close
        </button>
      </template>
    </ClinicalModal>
  </div>
</template>

<script setup lang="ts">
import { encodeChangeReason } from "../../api/changeReason";
import { ref, computed, onMounted } from "vue";
import { ClinicalIconButton, ClinicalModal } from "ui";
import { useConfirmDialog } from "../../composables/useConfirmDialog.js";

interface RACTFactor {
  id: string;
  study_id: string;
  category: string;
  risk_topic: string;
  risk_description: string;
  probability_score: number;
  impact_score: number;
  detectability_score: number;
  total_risk_score: number;
  risk_tier: string;
  mitigation_strategy: string;
  linked_kri_code?: string;
  is_active: boolean;
  created_at: string;
  created_by: string;
  version_index: number;
  reason_for_change: string;
}

interface SiteProfile {
  id: string;
  study_id: string;
  site_id: string;
  evaluation_date: string;
  composite_risk_score: number;
  risk_rank: number;
  percentile_rank: number;
  risk_tier: string;
  high_risk_kri_count: number;
  active_deviations_count: number;
}

interface KRIDefinition {
  id: string;
  code: string;
  name: string;
  category: string;
  description: string;
  calculation_formula: string;
  green_threshold: number;
  amber_threshold: number;
  red_threshold: number;
  weight: number;
  is_active: boolean;
}

// State
const availableStudies = ref<string[]>([
  "STUDY-ONC-2026",
  "STUDY-CARD-2026",
  "STUDY-IMM-2026",
  "STUDY-NEURO-2026",
]);
const selectedStudyId = ref<string>("STUDY-ONC-2026");
const selectedCategory = ref<string>("ALL");
const activeViewMode = ref<"matrix" | "sri" | "kri">("matrix");
const searchQuery = ref<string>("");
const isLoading = ref<boolean>(false);
const { confirmDialog } = useConfirmDialog();

const riskFactors = ref<RACTFactor[]>([]);
const siteProfiles = ref<SiteProfile[]>([]);
const kriDefinitions = ref<KRIDefinition[]>([]);

// Modals
const isRiskModalOpen = ref<boolean>(false);
const isEditing = ref<boolean>(false);
const editingFactorId = ref<string | null>(null);
const isDossierModalOpen = ref<boolean>(false);
const compiledDossier = ref<any>(null);

const riskForm = ref({
  category: "SAFETY",
  risk_topic: "",
  risk_description: "",
  probability_score: 2,
  impact_score: 2,
  detectability_score: 2,
  mitigation_strategy: "",
  linked_kri_code: null as string | null,
  reason_for_change: "Standard RACT risk assessment",
});

const dossierSigner = ref({
  name: "Dr. Eleanor Vance, MD",
  role: "Head of Clinical Quality Assurance",
  customName: "",
});

const categoryList = [
  { key: "SAFETY", name: "Safety", icon: "🛡️" },
  { key: "CONSENT", name: "Consent", icon: "✍️" },
  { key: "STUDY_CONDUCT", name: "Study Conduct", icon: "📋" },
  { key: "DATA_INTEGRITY", name: "Data Integrity", icon: "🔍" },
  { key: "IP_MANAGEMENT", name: "IP Management", icon: "💊" },
  { key: "TECHNOLOGY", name: "Technology", icon: "💻" },
];

// Computed
const totalRiskFactors = computed(() => riskFactors.value.length);
const highRiskCount = computed(
  () => riskFactors.value.filter((f) => f.risk_tier === "HIGH").length
);
const mediumRiskCount = computed(
  () => riskFactors.value.filter((f) => f.risk_tier === "MEDIUM").length
);
const lowRiskCount = computed(
  () => riskFactors.value.filter((f) => f.risk_tier === "LOW").length
);
const linkedKriCoverage = computed(() => {
  if (totalRiskFactors.value === 0) return 0;
  const linked = riskFactors.value.filter((f) => f.linked_kri_code).length;
  return Math.round((linked / totalRiskFactors.value) * 100);
});

const filteredFactors = computed(() => {
  let list = riskFactors.value;
  if (selectedCategory.value !== "ALL") {
    list = list.filter((f) => f.category === selectedCategory.value);
  }
  if (searchQuery.value.trim()) {
    const q = searchQuery.value.toLowerCase();
    list = list.filter(
      (f) =>
        f.risk_topic.toLowerCase().includes(q) ||
        f.risk_description.toLowerCase().includes(q) ||
        (f.linked_kri_code && f.linked_kri_code.toLowerCase().includes(q))
    );
  }
  return list;
});

const computedFormScore = computed(
  () =>
    riskForm.value.probability_score *
    riskForm.value.impact_score *
    riskForm.value.detectability_score
);

const computedFormTier = computed(() => {
  const score = computedFormScore.value;
  if (score >= 15) return "HIGH";
  if (score >= 7) return "MEDIUM";
  return "LOW";
});

const isFormValid = computed(
  () =>
    riskForm.value.risk_topic.trim().length > 0 &&
    riskForm.value.risk_description.trim().length > 0 &&
    riskForm.value.mitigation_strategy.trim().length > 0 &&
    riskForm.value.reason_for_change.trim().length > 0
);

// Helpers
function formatCategory(cat?: string): string {
  if (!cat) return "General";
  return cat.replace("_", " ");
}

function getCategoryCount(catKey: string): number {
  return riskFactors.value.filter((f) => f.category === catKey).length;
}

function formatDateTime(isoStr?: string): string {
  if (!isoStr) return "—";
  try {
    const d = new Date(isoStr);
    return (
      d.toLocaleDateString() +
      " " +
      d.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" })
    );
  } catch {
    return isoStr;
  }
}

// API Actions
async function fetchRactData() {
  isLoading.value = true;
  try {
    // 1. Fetch RACT factors
    const res = await fetch(
      `/api/v1/quality/rbqm/ract/factors?study_id=${selectedStudyId.value}`
    );
    if (res.ok) {
      riskFactors.value = await res.json();
    }

    // 2. Fetch KRI definitions
    const kriRes = await fetch("/api/v1/quality/rbqm/kris");
    if (kriRes.ok) {
      kriDefinitions.value = await kriRes.json();
    }

    // 3. Fetch Site Risk Profiles
    const siteRes = await fetch(
      `/api/v1/quality/rbqm/site-risk-profiles?study_id=${selectedStudyId.value}`
    );
    if (siteRes.ok) {
      siteProfiles.value = await siteRes.json();
    }
  } catch (err) {
    console.error("Failed to load RACT data:", err);
  } finally {
    isLoading.value = false;
  }
}

async function seedDefaults() {
  isLoading.value = true;
  try {
    const res = await fetch(
      `/api/v1/quality/rbqm/ract/seed-default?study_id=${selectedStudyId.value}`,
      {
        method: "POST",
        headers: {
          "X-Change-Reason": "Seed standard TransCelerate defaults",
        },
      }
    );
    if (res.ok) {
      riskFactors.value = await res.json();
    }
  } catch (err) {
    console.error("Failed to seed RACT defaults:", err);
  } finally {
    isLoading.value = false;
  }
}

async function computeSiteRiskProfiles() {
  isLoading.value = true;
  try {
    const res = await fetch(
      `/api/v1/quality/rbqm/site-risk-profiles/compute?study_id=${selectedStudyId.value}`,
      {
        method: "POST",
        headers: {
          "X-Change-Reason": "Batch Site Risk Index calculation",
        },
      }
    );
    if (res.ok) {
      siteProfiles.value = await res.json();
      activeViewMode.value = "sri";
    }
  } catch (err) {
    console.error("Failed to compute site risk profiles:", err);
  } finally {
    isLoading.value = false;
  }
}

function openAddModal() {
  isEditing.value = false;
  editingFactorId.value = null;
  riskForm.value = {
    category:
      selectedCategory.value === "ALL" ? "SAFETY" : selectedCategory.value,
    risk_topic: "",
    risk_description: "",
    probability_score: 2,
    impact_score: 2,
    detectability_score: 2,
    mitigation_strategy: "",
    linked_kri_code: null,
    reason_for_change: "Define new protocol risk factor",
  };
  isRiskModalOpen.value = true;
}

function openEditModal(factor: RACTFactor) {
  isEditing.value = true;
  editingFactorId.value = factor.id;
  riskForm.value = {
    category: factor.category,
    risk_topic: factor.risk_topic,
    risk_description: factor.risk_description,
    probability_score: factor.probability_score,
    impact_score: factor.impact_score,
    detectability_score: factor.detectability_score,
    mitigation_strategy: factor.mitigation_strategy,
    linked_kri_code: factor.linked_kri_code || null,
    reason_for_change: "Update RACT risk scoring",
  };
  isRiskModalOpen.value = true;
}

async function saveRiskFactor() {
  if (!isFormValid.value) return;
  isLoading.value = true;
  try {
    if (isEditing.value && editingFactorId.value) {
      const res = await fetch(
        `/api/v1/quality/rbqm/ract/factors/${editingFactorId.value}`,
        {
          method: "PUT",
          headers: {
            "Content-Type": "application/json",
            "X-Change-Reason": encodeChangeReason(
              riskForm.value.reason_for_change
            ),
          },
          body: JSON.stringify({
            risk_topic: riskForm.value.risk_topic,
            risk_description: riskForm.value.risk_description,
            probability_score: riskForm.value.probability_score,
            impact_score: riskForm.value.impact_score,
            detectability_score: riskForm.value.detectability_score,
            mitigation_strategy: riskForm.value.mitigation_strategy,
            linked_kri_code: riskForm.value.linked_kri_code,
          }),
        }
      );
      if (res.ok) {
        await fetchRactData();
        isRiskModalOpen.value = false;
      }
    } else {
      const res = await fetch("/api/v1/quality/rbqm/ract/factors", {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Change-Reason": encodeChangeReason(
            riskForm.value.reason_for_change
          ),
        },
        body: JSON.stringify({
          study_id: selectedStudyId.value,
          category: riskForm.value.category,
          risk_topic: riskForm.value.risk_topic,
          risk_description: riskForm.value.risk_description,
          probability_score: riskForm.value.probability_score,
          impact_score: riskForm.value.impact_score,
          detectability_score: riskForm.value.detectability_score,
          mitigation_strategy: riskForm.value.mitigation_strategy,
          linked_kri_code: riskForm.value.linked_kri_code,
        }),
      });
      if (res.ok) {
        await fetchRactData();
        isRiskModalOpen.value = false;
      }
    }
  } catch (err) {
    console.error("Failed to save risk factor:", err);
  } finally {
    isLoading.value = false;
  }
}

async function deleteFactor(factorId: string) {
  const confirmed = await confirmDialog({
    title: "Delete Risk Factor",
    message: "Are you sure you want to delete this protocol risk factor?",
    confirmLabel: "Delete",
    cancelLabel: "Cancel",
    variant: "danger",
  });
  if (!confirmed) return;
  isLoading.value = true;
  try {
    const res = await fetch(`/api/v1/quality/rbqm/ract/factors/${factorId}`, {
      method: "DELETE",
      headers: {
        "X-Change-Reason": "Decommission risk factor from matrix",
      },
    });
    if (res.ok) {
      await fetchRactData();
    }
  } catch (err) {
    console.error("Failed to delete factor:", err);
  } finally {
    isLoading.value = false;
  }
}

function openDossierModal() {
  compiledDossier.value = null;
  isDossierModalOpen.value = true;
}

async function compileDossier() {
  isLoading.value = true;
  try {
    const res = await fetch("/api/v1/quality/rbqm/csm/dossiers/compile", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason":
          "Compile 1-Click Centralized Monitoring Inspection Dossier",
      },
      body: JSON.stringify({
        study_id: selectedStudyId.value,
        dossier_name: dossierSigner.value.customName || undefined,
        signer_name: dossierSigner.value.name,
        signer_role: dossierSigner.value.role,
        signature_meaning:
          "I approve this Centralized Monitoring Inspection Dossier in accordance with 21 CFR Part 11 and ICH E6(R3).",
      }),
    });
    if (res.ok) {
      compiledDossier.value = await res.json();
    }
  } catch (err) {
    console.error("Failed to compile dossier:", err);
  } finally {
    isLoading.value = false;
  }
}

onMounted(() => {
  fetchRactData();
});
</script>

<style scoped>
/* TransCelerate RACT Matrix Viewer - 100% Vanilla CSS Tokens */

.ract-matrix-viewer {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-lg);
  font-family: var(--font);
  color: var(--semantic-color-text);
  width: 100%;
}

/* Header Ribbon */
.ract-header {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 12px;
  padding: var(--spacing-lg);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: var(--spacing-md);
  flex-wrap: wrap;
  box-shadow: 0 2px 6px rgba(0, 0, 0, 0.03);
}

.header-main {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
  flex: 1;
  min-width: 320px;
}

.badge-row {
  display: flex;
  gap: var(--spacing-xs);
  flex-wrap: wrap;
}

.badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.badge-regulatory {
  background: var(--semantic-color-ai-bg);
  color: var(--semantic-color-purple-text);
  border: 1px solid var(--semantic-color-ai-border);
}

.badge-gxp {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border-accent);
}

.badge-standard {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.ract-title {
  font-size: var(--font-size-xl);
  font-weight: 800;
  color: var(--semantic-color-text);
  line-height: 1.2;
}

.ract-subtitle {
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-muted);
  max-width: 860px;
  line-height: 1.4;
}

.header-meta-group {
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
  margin-top: var(--spacing-xs);
  flex-wrap: wrap;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: var(--spacing-xs);
  font-size: var(--font-size-sm);
}

.meta-label {
  font-weight: 600;
  color: var(--semantic-color-text-muted);
}

.ract-select {
  padding: 6px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
  font-weight: 600;
  color: var(--semantic-color-text);
  cursor: pointer;
}

.status-pill {
  padding: 4px 10px;
  border-radius: 999px;
  font-size: var(--font-size-xs);
  font-weight: 700;
}

.status-pill-nominal {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.status-pill-alert {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.header-actions {
  display: flex;
  gap: var(--spacing-xs);
  align-items: center;
  flex-wrap: wrap;
}

/* Buttons */
.ract-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 14px;
  border-radius: 6px;
  font-size: var(--font-size-sm);
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  transition:
    background-color 0.15s ease-in-out,
    border-color 0.15s ease-in-out,
    color 0.15s ease-in-out,
    box-shadow 0.15s ease-in-out,
    opacity 0.15s ease-in-out;
  text-decoration: none;
}

.ract-btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.ract-btn-primary {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-primary);
}

.ract-btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-primary);
}

.ract-btn-secondary {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  border-color: var(--semantic-color-border);
}

.ract-btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.ract-btn-sm {
  padding: 5px 10px;
  font-size: var(--font-size-xs);
}

.ract-btn-lg {
  padding: 12px 24px;
  font-size: var(--font-size-base);
  width: 100%;
  justify-content: center;
}

/* KPI Summary Grid */
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: var(--spacing-md);
}

.kpi-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  padding: var(--spacing-md);
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.02);
  cursor: pointer;
  transition:
    transform 0.1s ease,
    border-color 0.1s ease;
}

.kpi-card:hover {
  transform: translateY(-2px);
  border-color: var(--semantic-color-primary);
}

.kpi-card-danger {
  border-left: 4px solid var(--semantic-color-error);
}

.kpi-card-warning {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
}

.kpi-card-success {
  border-left: 4px solid var(--semantic-color-success);
}

.kpi-icon {
  width: 44px;
  height: 44px;
  border-radius: 10px;
  background: var(--semantic-color-surface-header);
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 1.3rem;
  flex-shrink: 0;
}

.kpi-content {
  display: flex;
  flex-direction: column;
}

.kpi-value {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  line-height: 1.1;
}

.kpi-label {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-muted);
  font-weight: 500;
  margin-top: 2px;
}

/* Category Tabs Bar */
.category-tabs-bar {
  display: flex;
  gap: var(--spacing-xs);
  overflow-x: auto;
  padding-bottom: 4px;
  border-bottom: 1px solid var(--semantic-color-border);
}

.category-tab-btn {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  padding: 8px 14px;
  border-radius: 8px 8px 0 0;
  font-size: var(--font-size-xs);
  font-weight: 600;
  color: var(--semantic-color-text-muted);
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  white-space: nowrap;
  transition:
    background-color 0.15s ease,
    border-color 0.15s ease,
    color 0.15s ease,
    box-shadow 0.15s ease,
    opacity 0.15s ease;
}

.category-tab-btn.active {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-primary);
}

/* View Switch Ribbon */
.view-switch-ribbon {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: var(--spacing-md);
  flex-wrap: wrap;
}

.sub-nav-group {
  display: flex;
  gap: var(--spacing-xs);
}

.sub-tab-btn {
  padding: 7px 14px;
  border-radius: 6px;
  font-size: var(--font-size-xs);
  font-weight: 700;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  color: var(--semantic-color-text-muted);
  cursor: pointer;
}

.sub-tab-btn.active {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-color: var(--semantic-color-text);
}

.ract-search-input {
  padding: 7px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  font-size: var(--font-size-sm);
  min-width: 280px;
}

/* Table Container & Matrix */
.table-container {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 10px;
  overflow-x: auto;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.02);
}

.ract-table {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--font-size-sm);
  text-align: left;
}

.ract-table th {
  background: var(--semantic-color-surface-muted);
  padding: 12px 14px;
  font-weight: 700;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-secondary);
  border-bottom: 2px solid var(--semantic-color-border);
  text-transform: uppercase;
  letter-spacing: 0.03em;
}

.ract-table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--semantic-color-border);
  vertical-align: middle;
}

.row-tier-high {
  background: var(--semantic-color-error-bg-soft);
}

.row-tier-medium {
  background: var(--semantic-color-warning-bg-soft);
}

.category-badge {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  text-transform: uppercase;
}

.cat-safety {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.cat-consent {
  background: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.cat-study_conduct {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.cat-data_integrity {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.cat-ip_management {
  background: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
}

.cat-technology {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}

.topic-title {
  font-weight: 700;
  color: var(--semantic-color-text);
}

.topic-desc {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-muted);
  margin-top: 2px;
  max-width: 440px;
}

.score-badge {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  font-weight: 700;
  font-size: var(--font-size-xs);
}

.total-score-badge {
  font-weight: 800;
  font-size: var(--font-size-md);
  color: var(--semantic-color-text);
}

.tier-pill {
  display: inline-block;
  padding: 3px 8px;
  border-radius: 999px;
  font-size: var(--font-size-2xs);
  font-weight: 800;
  text-transform: uppercase;
}

.tier-high {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
  border: 1px solid var(--semantic-color-error);
}

.tier-medium {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
  border: 1px solid var(--semantic-color-warning-border);
}

.tier-low {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
  border: 1px solid var(--semantic-color-success-border);
}

.tier-critical {
  background: var(--semantic-color-error-text-deep);
  color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-error-text-dark);
}

.mitigation-text {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text);
  max-width: 320px;
}

.kri-link-pill {
  display: inline-flex;
  align-items: center;
  padding: 3px 8px;
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border: 1px solid var(--semantic-color-info-border-accent);
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
}

.table-actions {
  white-space: nowrap;
}

.action-btn {
  background: transparent;
  border: none;
  cursor: pointer;
  font-size: 1.1rem;
  padding: 4px;
  border-radius: 4px;
}

.action-btn:hover {
  background: var(--semantic-color-surface-header);
}

/* Site Risk Index Table Specifics */
.rank-badge {
  font-weight: 800;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text);
}

.site-name {
  font-weight: 700;
  color: var(--semantic-color-text);
}

.sri-score-val {
  font-size: var(--font-size-md);
  font-weight: 800;
  color: var(--semantic-color-text);
}

.percentile-bar-wrapper {
  display: flex;
  flex-direction: column;
  gap: 3px;
  min-width: 140px;
}

.percentile-label {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  color: var(--semantic-color-text-muted);
}

.progress-track {
  height: 6px;
  background: var(--semantic-color-surface-header);
  border-radius: 999px;
  overflow: hidden;
}

.progress-fill {
  height: 100%;
  border-radius: 999px;
}

.progress-fill-critical {
  background: var(--semantic-color-error);
}

.progress-fill-high {
  background: var(--semantic-color-warning-action);
}

.progress-fill-medium {
  background: var(--semantic-color-warning-action);
}

.progress-fill-low {
  background: var(--semantic-color-success);
}

.badge-alert-count {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
  font-weight: 700;
  font-size: var(--font-size-xs);
}

.badge-nominal-count {
  display: inline-block;
  padding: 2px 8px;
  border-radius: 999px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
  font-size: var(--font-size-xs);
}

/* KRI Cards Grid */
.kri-grid-cards {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(300px, 1fr));
  gap: var(--spacing-md);
  margin-top: var(--spacing-md);
}

.kri-def-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-xs);
}

.kri-def-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.kri-code-tag {
  font-family: var(--font-mono);
  font-size: var(--font-size-xs);
  font-weight: 700;
  color: var(--semantic-color-primary);
}

.kri-def-name {
  font-size: var(--font-size-sm);
  font-weight: 700;
  color: var(--semantic-color-text);
}

.kri-def-desc {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-muted);
}

.kri-def-formula {
  background: var(--semantic-color-surface);
  padding: 6px 10px;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border);
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-body);
}

.kri-threshold-ribbon {
  display: flex;
  gap: 6px;
  align-items: center;
  flex-wrap: wrap;
  margin-top: var(--spacing-xs);
}

.threshold-pill {
  font-size: var(--font-size-2xs);
  padding: 2px 6px;
  border-radius: 4px;
  font-weight: 600;
}

.pill-green {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.pill-amber {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.pill-red {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.weight-tag {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  font-weight: 700;
  margin-left: auto;
}

/* Empty State Card */
.empty-state-card {
  background: var(--semantic-color-surface);
  border: 1px dashed var(--semantic-color-border);
  border-radius: 12px;
  padding: 48px;
  text-align: center;
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: var(--spacing-sm);
}

.empty-icon {
  font-size: 2.5rem;
}

/* Modal Backdrops & Cards */
.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(15, 23, 42, 0.6);
  backdrop-filter: blur(4px);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: var(--spacing-md);
}

.modal-card {
  background: var(--semantic-color-surface);
  border-radius: 12px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
  width: 100%;
  max-height: 90vh;
  display: flex;
  flex-direction: column;
  overflow: hidden;
}

.modal-lg {
  max-width: 740px;
}

.modal-xl {
  max-width: 900px;
}

.modal-header {
  padding: var(--spacing-md) var(--spacing-lg);
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-title {
  font-size: var(--font-size-lg);
  font-weight: 800;
  color: var(--semantic-color-text);
}

.modal-close-btn {
  background: transparent;
  border: none;
  font-size: 1.2rem;
  cursor: pointer;
  color: var(--semantic-color-text-muted);
}

.modal-body {
  padding: var(--spacing-lg);
  overflow-y: auto;
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.modal-footer {
  padding: var(--spacing-md) var(--spacing-lg);
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: flex-end;
  gap: var(--spacing-xs);
  background: var(--semantic-color-surface-muted);
}

/* Forms */
.form-row {
  display: flex;
  gap: var(--spacing-md);
}

.flex-1 {
  flex: 1;
}

.flex-2 {
  flex: 2;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-label {
  font-size: var(--font-size-xs);
  font-weight: 700;
  color: var(--semantic-color-text);
}

.form-control {
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  font-size: var(--font-size-sm);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.form-control:focus {
  outline: none;
  border-color: var(--semantic-color-primary);
}

.form-control.textarea {
  resize: vertical;
}

/* Scoring Box */
.scoring-box {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: var(--spacing-md);
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.scoring-title {
  font-size: var(--font-size-xs);
  font-weight: 800;
  text-transform: uppercase;
  color: var(--semantic-color-text-body);
}

.scoring-row {
  display: grid;
  grid-template-columns: 1fr 1fr 1fr 1.2fr;
  gap: var(--spacing-sm);
  align-items: flex-end;
}

.score-input-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.score-label {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  color: var(--semantic-color-text-muted);
}

.score-result-group {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 6px 10px;
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
}

.preview-label {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.preview-score-val {
  font-size: var(--font-size-lg);
  font-weight: 800;
  color: var(--semantic-color-text);
  line-height: 1.1;
}

/* Dossier Modal Styling */
.dossier-intro-box {
  background: var(--semantic-color-info-bg-soft);
  border: 1px solid var(--semantic-color-info-border-accent);
  border-radius: 8px;
  padding: var(--spacing-md);
}

.dossier-intro-box h4 {
  font-size: var(--font-size-sm);
  font-weight: 800;
  color: var(--semantic-color-primary);
}

.dossier-intro-box p {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-primary-dark);
  margin-top: 4px;
}

.signature-intent-box {
  background: var(--semantic-color-purple-bg);
  border: 1px solid var(--semantic-color-purple-border);
  border-radius: 8px;
  padding: var(--spacing-md);
  margin-top: var(--spacing-xs);
}

.intent-title {
  font-size: var(--font-size-xs);
  font-weight: 800;
  color: var(--semantic-color-purple-text);
}

.intent-text {
  font-size: var(--font-size-xs);
  font-style: italic;
  color: var(--semantic-color-purple-text);
  margin-top: 4px;
  line-height: 1.4;
}

.dossier-seal-banner {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  padding: var(--spacing-md);
  border-radius: 8px;
  display: flex;
  align-items: center;
  gap: var(--spacing-md);
}

.dossier-sections-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: var(--spacing-md);
}

.dossier-section-card {
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  padding: var(--spacing-md);
}

.signature-cert-box {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: var(--spacing-md);
}

.signature-cert-box h5 {
  font-size: var(--font-size-xs);
  font-weight: 800;
  color: var(--semantic-color-text-body);
  margin-bottom: var(--spacing-xs);
}

.cert-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 8px;
  font-size: var(--font-size-xs);
}

/* Utilities */
.text-center {
  text-align: center;
}

.text-right {
  text-align: right;
}

.text-muted {
  color: var(--semantic-color-text-muted);
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}
</style>
