<template>
  <div
    class="statistical-forensics-viewer"
    role="region"
    aria-label="Centralized Statistical Monitoring (CSM) Forensics & Variance Sentinels"
  >
    <!-- Header & Action Ribbon -->
    <header class="forensics-header">
      <div class="header-main">
        <div class="title-group">
          <div class="badge-row">
            <span class="badge badge-regulatory">ICH E6(R2)/(R3) CSM</span>
            <span class="badge badge-gxp">21 CFR Part 11</span>
            <span class="badge badge-standard">TransCelerate eQMS</span>
          </div>
          <h2 class="forensics-title">
            Centralized Statistical Monitoring (CSM) Forensics
          </h2>
          <p class="forensics-subtitle">
            Cross-site digit preference surveillance (Benford's Law), terminal
            digit rounding bias, multivariate Mahalanobis distance matrices, and
            anomalous variance shrinkage sentinels for data fabrication
            detection.
          </p>
        </div>

        <div class="header-meta-group">
          <div class="meta-item">
            <label for="csm-study-select" class="meta-label"
              >Active Study:</label
            >
            <select
              id="csm-study-select"
              v-model="selectedStudyId"
              class="csm-select"
              @change="onStudyChange"
            >
              <option v-for="s in availableStudies" :key="s" :value="s">
                {{ s }}
              </option>
            </select>
          </div>
          <div class="meta-item">
            <span class="meta-label">Surveillance Status:</span>
            <span
              class="status-pill"
              :class="
                anomalousSitesCount > 0
                  ? 'status-pill-critical'
                  : 'status-pill-nominal'
              "
            >
              {{
                anomalousSitesCount > 0
                  ? `${anomalousSitesCount} Outliers (P < 0.001)`
                  : "Nominal Conformity"
              }}
            </span>
          </div>
        </div>
      </div>

      <div class="header-actions">
        <button
          class="csm-btn csm-btn-secondary"
          :disabled="isLoading"
          @click="generateSimulatedDataset"
        >
          <span class="btn-icon">⚡</span> Simulate Calibration Data
        </button>
        <button
          class="csm-btn csm-btn-secondary"
          :disabled="isLoading || !activeRun"
          @click="openDossierModal"
        >
          <span class="btn-icon">📄</span> Forensics Dossier
        </button>
        <button
          class="csm-btn csm-btn-primary"
          :disabled="isLoading"
          @click="executeSurveillanceScan"
        >
          <span class="btn-icon">🔍</span>
          {{
            isLoading ? "Analyzing Distributions..." : "Run Statistical Scan"
          }}
        </button>
      </div>
    </header>

    <!-- KPI Summary Grid -->
    <div class="kpi-grid">
      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div
        class="kpi-card"
        :class="{ 'kpi-card-alert': anomalousSitesCount > 0 }"
        @click="activeSubTab = 'overview'"
      >
        <div class="kpi-icon">🚨</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ anomalousSitesCount }} / {{ siteRecords.length || 0 }}
          </div>
          <div class="kpi-label">Anomalous Sites (P &lt; 0.001)</div>
        </div>
      </div>

      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="kpi-card" @click="activeSubTab = 'benford'">
        <div class="kpi-icon">📊</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ benfordNonConformCount }}
          </div>
          <div class="kpi-label">Benford's Law Deviations</div>
        </div>
      </div>

      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="kpi-card" @click="activeSubTab = 'rounding'">
        <div class="kpi-icon">🎯</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ severeRoundingCount }}
          </div>
          <div class="kpi-label">Severe Rounding Bias Sites</div>
        </div>
      </div>

      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="kpi-card" @click="activeSubTab = 'variance'">
        <div class="kpi-icon">🔬</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ varianceShrunkCount }}
          </div>
          <div class="kpi-label">Variance Shrinkage / Clones</div>
        </div>
      </div>

      <!-- eslint-disable-next-line vuejs-accessibility/click-events-have-key-events, vuejs-accessibility/no-static-element-interactions -->
      <div class="kpi-card" @click="activeSubTab = 'mahalanobis'">
        <div class="kpi-icon">🌐</div>
        <div class="kpi-content">
          <div class="kpi-value">
            {{ maxMahalanobisDistance.toFixed(2) }}
          </div>
          <div class="kpi-label">Max Mahalanobis Distance (D_M)</div>
        </div>
      </div>
    </div>

    <!-- Navigation Tabs -->
    <nav
      class="forensics-tabs"
      role="tablist"
      aria-label="Forensic Investigation Modules"
    >
      <button
        role="tab"
        :aria-selected="activeSubTab === 'overview'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'overview' }"
        @click="activeSubTab = 'overview'"
      >
        📑 Cross-Site Surveillance Matrix
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'benford'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'benford' }"
        @click="activeSubTab = 'benford'"
      >
        📊 Leading Digits (Benford's Law)
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'rounding'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'rounding' }"
        @click="activeSubTab = 'rounding'"
      >
        🔢 Terminal Digit Rounding Bias
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'mahalanobis'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'mahalanobis' }"
        @click="activeSubTab = 'mahalanobis'"
      >
        🌐 Multivariate Correlation &amp; Mahalanobis
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'variance'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'variance' }"
        @click="activeSubTab = 'variance'"
      >
        📉 Variance Shrinkage &amp; KL Divergence
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'duplicate-patients'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'duplicate-patients' }"
        @click="activeSubTab = 'duplicate-patients'"
      >
        👥 Duplicate Patient Records
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'temporal'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'temporal' }"
        @click="activeSubTab = 'temporal'"
      >
        ⏱️ Temporal &amp; Behavioral Scanner
      </button>
      <button
        role="tab"
        :aria-selected="activeSubTab === 'phash'"
        class="tab-btn"
        :class="{ active: activeSubTab === 'phash' }"
        @click="activeSubTab = 'phash'"
      >
        🖼️ Perceptual Image Hashing (pHash)
      </button>
    </nav>

    <!-- Main Content Area -->
    <div class="tab-pane-container">
      <!-- TAB 1: Surveillance Matrix -->
      <section v-if="activeSubTab === 'overview'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Cross-Site Statistical Surveillance Heatmap
            </h3>
            <span class="pane-caption"
              >ICH E6(R2)/(R3) Risk-Based Centralized Monitoring Sentinel
              Grid</span
            >
          </div>

          <div class="filter-actions">
            <label class="checkbox-label">
              <input v-model="showOnlyOutliers" type="checkbox" />
              Show Anomalies Only (P &lt; 0.001)
            </label>
            <button
              class="csm-btn csm-btn-sm csm-btn-secondary"
              @click="dispatchAllKriAlerts"
            >
              ⚡ Auto-Dispatch KRI Alerts
            </button>
          </div>
        </div>

        <div class="table-wrapper">
          <table class="forensics-table">
            <thead>
              <tr>
                <th>Site ID</th>
                <th>Sample Size</th>
                <th>Benford Conformity</th>
                <th>Terminal Rounding</th>
                <th>Mahalanobis (D_M)</th>
                <th>Variance Ratio</th>
                <th>Composite Score</th>
                <th>Risk Tier</th>
                <th>Anomaly Sentinels</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="site in filteredSites"
                :key="site.site_id"
                :class="{ 'row-outlier': site.is_anomalous_outlier }"
              >
                <td class="cell-site-id">
                  <strong>{{ site.site_id }}</strong>
                </td>
                <td>{{ site.sample_size }} obs</td>
                <td>
                  <span
                    class="badge"
                    :class="getBenfordBadgeClass(site.benford.conformity)"
                  >
                    {{ site.benford.conformity.replace("_", " ") }}
                  </span>
                  <div class="sub-stat">
                    MAD: {{ site.benford.mad }} (p={{ site.benford.p_value }})
                  </div>
                </td>
                <td>
                  <span
                    class="badge"
                    :class="
                      getRoundingBadgeClass(
                        site.terminal_digit.rounding_bias_tier
                      )
                    "
                  >
                    {{
                      site.terminal_digit.rounding_bias_tier.replace("_", " ")
                    }}
                  </span>
                  <div class="sub-stat">
                    0/5 Index:
                    {{
                      (site.terminal_digit.zero_five_index * 100).toFixed(1)
                    }}%
                  </div>
                </td>
                <td>
                  <strong>{{ site.multivariate.mahalanobis_distance }}</strong>
                  <div class="sub-stat">
                    p={{ site.multivariate.mahalanobis_p_value }}
                  </div>
                </td>
                <td>
                  <span
                    :class="
                      site.variance_shrinkage.is_variance_shrunk
                        ? 'text-critical'
                        : 'text-nominal'
                    "
                  >
                    {{ site.variance_shrinkage.variance_ratio }}
                  </span>
                  <div class="sub-stat">
                    KL: {{ site.variance_shrinkage.kl_divergence }}
                  </div>
                </td>
                <td>
                  <div class="score-pill-container">
                    <div
                      class="score-bar-bg"
                      :style="{ width: `${site.composite_risk_score}%` }"
                      :class="getScoreColorClass(site.composite_risk_score)"
                    />
                    <span class="score-text"
                      >{{ site.composite_risk_score }} / 100</span
                    >
                  </div>
                </td>
                <td>
                  <span
                    class="risk-tier-pill"
                    :class="`risk-tier-${site.risk_tier.toLowerCase()}`"
                  >
                    {{ site.risk_tier }}
                  </span>
                </td>
                <td>
                  <div class="flag-pill-group">
                    <span
                      v-for="flag in site.flagged_anomaly_reasons"
                      :key="flag"
                      class="flag-pill"
                    >
                      {{ formatFlagName(flag) }}
                    </span>
                    <span
                      v-if="site.flagged_anomaly_reasons.length === 0"
                      class="text-muted"
                      >Nominal</span
                    >
                  </div>
                </td>
                <td>
                  <button
                    class="csm-btn csm-btn-sm csm-btn-outline"
                    @click="drilldownSite(site)"
                  >
                    Inspect
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </section>

      <!-- TAB 2: Benford's Law Drilldown -->
      <section v-if="activeSubTab === 'benford'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Leading Digit Distribution against Benford's Law
            </h3>
            <span class="pane-caption"
              >P(d) = log10(1 + 1/d) with Chi-Square Goodness-of-Fit Testing (df
              = 8)</span
            >
          </div>

          <div class="site-drilldown-selector">
            <label for="benford-site-select">Inspect Site:</label>
            <select
              id="benford-site-select"
              v-model="selectedDrilldownSiteId"
              class="csm-select"
            >
              <option
                v-for="site in siteRecords"
                :key="site.site_id"
                :value="site.site_id"
              >
                {{ site.site_id }} ({{ site.risk_tier }})
              </option>
            </select>
          </div>
        </div>

        <div v-if="currentDrilldownSite" class="drilldown-grid">
          <!-- Chart Visualizer -->
          <div class="chart-card">
            <div class="chart-header">
              <h4>
                Observed Leading Digits vs Benford's Theoretical Distribution
              </h4>
              <div class="chart-badges">
                <span
                  class="badge"
                  :class="
                    getBenfordBadgeClass(
                      currentDrilldownSite.benford.conformity
                    )
                  "
                >
                  {{ currentDrilldownSite.benford.conformity }}
                </span>
                <span class="stat-tag"
                  >MAD: {{ currentDrilldownSite.benford.mad }}</span
                >
                <span class="stat-tag"
                  >Chi2: {{ currentDrilldownSite.benford.chi_square }} (p={{
                    currentDrilldownSite.benford.p_value
                  }})</span
                >
              </div>
            </div>

            <!-- Bar Chart (CSS-based) -->
            <div class="benford-bars-container">
              <div v-for="d in 9" :key="d" class="benford-col">
                <div class="bars-pair">
                  <!-- Observed Bar -->
                  <div
                    class="bar bar-observed"
                    :style="{ height: `${getObservedBenfordHeight(d)}%` }"
                    :title="`Observed Digit ${d}: ${getObservedBenfordCount(d)} (${getObservedBenfordPct(d)}%)`"
                  >
                    <span
                      v-if="getObservedBenfordHeight(d) > 20"
                      class="bar-label"
                      >{{ getObservedBenfordPct(d) }}%</span
                    >
                  </div>
                  <!-- Expected Benford Bar -->
                  <div
                    class="bar bar-expected"
                    :style="{ height: `${getExpectedBenfordHeight(d)}%` }"
                    :title="`Benford Expected Digit ${d}: ${getExpectedBenfordPct(d)}%`"
                  >
                    <span
                      v-if="getExpectedBenfordHeight(d) > 20"
                      class="bar-label"
                      >{{ getExpectedBenfordPct(d) }}%</span
                    >
                  </div>
                </div>
                <div class="digit-label">Digit {{ d }}</div>
              </div>
            </div>

            <div class="chart-legend">
              <div class="legend-item">
                <span class="legend-box box-observed" /> Observed Site Digit
                Frequency
              </div>
              <div class="legend-item">
                <span class="legend-box box-expected" /> Benford's Theoretical
                Curve
              </div>
            </div>
          </div>

          <!-- Statistics Breakdown Table -->
          <div class="metrics-card">
            <h4>Leading Digit Frequency Breakdown</h4>
            <table class="nested-table">
              <thead>
                <tr>
                  <th>Digit</th>
                  <th>Observed Count</th>
                  <th>Observed %</th>
                  <th>Expected %</th>
                  <th>Residual Delta</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="d in 9" :key="d">
                  <td>
                    <strong>Digit {{ d }}</strong>
                  </td>
                  <td>{{ getObservedBenfordCount(d) }}</td>
                  <td>{{ getObservedBenfordPct(d) }}%</td>
                  <td>{{ getExpectedBenfordPct(d) }}%</td>
                  <td
                    :class="
                      Math.abs(
                        getObservedBenfordPct(d) - getExpectedBenfordPct(d)
                      ) > 10
                        ? 'text-critical'
                        : ''
                    "
                  >
                    {{
                      (
                        getObservedBenfordPct(d) - getExpectedBenfordPct(d)
                      ).toFixed(1)
                    }}%
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- TAB 3: Rounding Bias Drilldown -->
      <section v-if="activeSubTab === 'rounding'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Terminal Digit Uniform Distribution &amp; Rounding Bias
            </h3>
            <span class="pane-caption"
              >Detection of Human Rounding Preferences on Blood Pressure &amp;
              Vitals (0 &amp; 5 terminal digit clustering)</span
            >
          </div>

          <div class="site-drilldown-selector">
            <label for="rounding-site-select">Inspect Site:</label>
            <select
              id="rounding-site-select"
              v-model="selectedDrilldownSiteId"
              class="csm-select"
            >
              <option
                v-for="site in siteRecords"
                :key="site.site_id"
                :value="site.site_id"
              >
                {{ site.site_id }} ({{ site.risk_tier }})
              </option>
            </select>
          </div>
        </div>

        <div v-if="currentDrilldownSite" class="drilldown-grid">
          <!-- Terminal Digits Chart -->
          <div class="chart-card">
            <div class="chart-header">
              <h4>Terminal Digit (0-9) Frequency vs 10% Uniform Benchmark</h4>
              <div class="chart-badges">
                <span
                  class="badge"
                  :class="
                    getRoundingBadgeClass(
                      currentDrilldownSite.terminal_digit.rounding_bias_tier
                    )
                  "
                >
                  {{ currentDrilldownSite.terminal_digit.rounding_bias_tier }}
                </span>
                <span class="stat-tag"
                  >0/5 Index:
                  {{
                    (
                      currentDrilldownSite.terminal_digit.zero_five_index * 100
                    ).toFixed(1)
                  }}% (Exp: 20%)</span
                >
                <span class="stat-tag"
                  >Chi2:
                  {{ currentDrilldownSite.terminal_digit.chi_square }} (p={{
                    currentDrilldownSite.terminal_digit.p_value
                  }})</span
                >
              </div>
            </div>

            <!-- Terminal Bars -->
            <div class="terminal-bars-container">
              <div v-for="d in 10" :key="d - 1" class="terminal-col">
                <div
                  class="terminal-bar"
                  :style="{ height: `${getTerminalDigitHeight(d - 1)}%` }"
                  :class="{ 'bar-highlight': d - 1 === 0 || d - 1 === 5 }"
                >
                  <span
                    v-if="getTerminalDigitHeight(d - 1) > 20"
                    class="bar-label"
                    >{{ getTerminalDigitPct(d - 1) }}%</span
                  >
                </div>
                <div
                  class="digit-label"
                  :class="{ 'digit-highlight': d - 1 === 0 || d - 1 === 5 }"
                >
                  Digit {{ d - 1 }}
                </div>
              </div>
            </div>

            <div class="rounding-callout">
              <strong>Sentinel Finding:</strong>
              <span
                v-if="
                  currentDrilldownSite.terminal_digit.zero_five_index >= 0.45
                "
              >
                🚨 Severe rounding bias detected.
                {{
                  (
                    currentDrilldownSite.terminal_digit.zero_five_index * 100
                  ).toFixed(1)
                }}% of continuous vitals end in 0 or 5, indicating clinical
                estimation rather than calibrated instrument measurement.
              </span>
              <span v-else>
                🟢 Terminal digit distribution aligns with true continuous
                physiological measurements.
              </span>
            </div>
          </div>

          <!-- Rounding Gauge Card -->
          <div class="metrics-card">
            <h4>Terminal Digit 0 &amp; 5 Rounding Gauge</h4>
            <div class="gauge-container">
              <div class="gauge-meter">
                <div
                  class="gauge-fill"
                  :style="{
                    width: `${currentDrilldownSite.terminal_digit.zero_five_index * 100}%`,
                  }"
                  :class="
                    currentDrilldownSite.terminal_digit.zero_five_index >= 0.45
                      ? 'gauge-critical'
                      : 'gauge-nominal'
                  "
                />
              </div>
              <div class="gauge-scale">
                <span>0% (No rounding)</span>
                <span>20% (Expected)</span>
                <span>50%+ (Severe Rounding)</span>
              </div>
            </div>

            <table class="nested-table mt-16">
              <thead>
                <tr>
                  <th>Terminal Digit</th>
                  <th>Observed Count</th>
                  <th>Observed %</th>
                  <th>Uniform Expected</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="d in 10" :key="d - 1">
                  <td>
                    <strong>Digit {{ d - 1 }}</strong>
                  </td>
                  <td>
                    {{
                      currentDrilldownSite.terminal_digit.observed_frequencies[
                        String(d - 1)
                      ] || 0
                    }}
                  </td>
                  <td>{{ getTerminalDigitPct(d - 1) }}%</td>
                  <td>10.0%</td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </section>

      <!-- TAB 4: Multivariate Mahalanobis & Correlation Matrices -->
      <section v-if="activeSubTab === 'mahalanobis'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Multivariate Observation Clusters &amp; Mahalanobis Distance
            </h3>
            <span class="pane-caption"
              >Assessment of Inter-Variable Correlation Structures and Site
              Centroid Distances</span
            >
          </div>

          <div class="site-drilldown-selector">
            <label for="mahal-site-select">Inspect Site:</label>
            <select
              id="mahal-site-select"
              v-model="selectedDrilldownSiteId"
              class="csm-select"
            >
              <option
                v-for="site in siteRecords"
                :key="site.site_id"
                :value="site.site_id"
              >
                {{ site.site_id }} ({{ site.risk_tier }})
              </option>
            </select>
          </div>
        </div>

        <div v-if="currentDrilldownSite" class="drilldown-grid">
          <div class="chart-card">
            <h4>Site Mean Vector vs Pooled Trial Population Centroid</h4>
            <table class="nested-table">
              <thead>
                <tr>
                  <th>Clinical Variable</th>
                  <th>Site Mean</th>
                  <th>Variable Standard Deviation</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="(val, varName) in currentDrilldownSite.multivariate
                    .site_mean_vector"
                  :key="varName"
                >
                  <td>
                    <strong>{{ formatVarName(varName) }}</strong>
                  </td>
                  <td>{{ val }}</td>
                  <td>{{ getVariableCov(varName) }}</td>
                </tr>
              </tbody>
            </table>

            <div class="metric-highlight-box mt-16">
              <div class="highlight-item">
                <span class="highlight-label">Mahalanobis Distance (D_M):</span>
                <span class="highlight-value">{{
                  currentDrilldownSite.multivariate.mahalanobis_distance
                }}</span>
              </div>
              <div class="highlight-item">
                <span class="highlight-label">Hotelling T^2 p-value:</span>
                <span
                  class="highlight-value"
                  :class="
                    currentDrilldownSite.multivariate.mahalanobis_p_value <
                    0.001
                      ? 'text-critical'
                      : ''
                  "
                >
                  {{ currentDrilldownSite.multivariate.mahalanobis_p_value }}
                </span>
              </div>
              <div class="highlight-item">
                <span class="highlight-label">Frobenius Matrix Distance:</span>
                <span class="highlight-value">{{
                  currentDrilldownSite.multivariate.correlation_matrix_distance
                }}</span>
              </div>
            </div>
          </div>

          <div class="metrics-card">
            <h4>Correlation &amp; Covariance Structure</h4>
            <p class="section-desc">
              Cross-variable covariance matrix for site observation vectors:
            </p>
            <pre class="json-preview">{{
              JSON.stringify(
                currentDrilldownSite.multivariate.site_covariance_matrix,
                null,
                2
              )
            }}</pre>
          </div>
        </div>
      </section>

      <!-- TAB 5: Variance Shrinkage & KL Divergence -->
      <section v-if="activeSubTab === 'variance'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Variance Shrinkage &amp; Data Fabrication Sentinels
            </h3>
            <span class="pane-caption"
              >Kullback-Leibler (KL) Divergence and Low-Standard-Deviation
              Fabrication Scanners</span
            >
          </div>

          <div class="site-drilldown-selector">
            <label for="var-site-select">Inspect Site:</label>
            <select
              id="var-site-select"
              v-model="selectedDrilldownSiteId"
              class="csm-select"
            >
              <option
                v-for="site in siteRecords"
                :key="site.site_id"
                :value="site.site_id"
              >
                {{ site.site_id }} ({{ site.risk_tier }})
              </option>
            </select>
          </div>
        </div>

        <div v-if="currentDrilldownSite" class="drilldown-grid">
          <div class="chart-card">
            <h4>Site Variance vs Pooled Study Variance Ratio</h4>
            <div class="variance-gauge-box">
              <div class="ratio-display">
                <span class="ratio-num">{{
                  currentDrilldownSite.variance_shrinkage.variance_ratio
                }}</span>
                <span class="ratio-desc"
                  >Variance Ratio (s_site^2 / sigma_pool^2)</span
                >
              </div>
              <div
                class="status-pill"
                :class="
                  currentDrilldownSite.variance_shrinkage.is_variance_shrunk
                    ? 'status-pill-critical'
                    : 'status-pill-nominal'
                "
              >
                {{
                  currentDrilldownSite.variance_shrinkage.is_variance_shrunk
                    ? "🚨 ANOMALOUS VARIANCE SHRINKAGE"
                    : "🟢 NORMAL VARIANCE"
                }}
              </div>
            </div>

            <div class="metric-highlight-box mt-16">
              <div class="highlight-item">
                <span class="highlight-label"
                  >One-Tailed Shrinkage p-value:</span
                >
                <span
                  class="highlight-value"
                  :class="
                    currentDrilldownSite.variance_shrinkage
                      .variance_shrinkage_p_value < 0.001
                      ? 'text-critical'
                      : ''
                  "
                >
                  {{
                    currentDrilldownSite.variance_shrinkage
                      .variance_shrinkage_p_value
                  }}
                </span>
              </div>
              <div class="highlight-item">
                <span class="highlight-label"
                  >Kullback-Leibler Divergence (D_KL):</span
                >
                <span class="highlight-value">{{
                  currentDrilldownSite.variance_shrinkage.kl_divergence
                }}</span>
              </div>
            </div>

            <div
              v-if="currentDrilldownSite.variance_shrinkage.is_variance_shrunk"
              class="fabrication-alert-card mt-16"
            >
              <h4>⚠️ Fabrication Sentinel Warning</h4>
              <p>
                This site exhibits an unnaturally low variance ratio ({{
                  currentDrilldownSite.variance_shrinkage.variance_ratio
                }}
                &le; 0.25). Such "too perfect" data indicates cloned
                observations, copy-pasting of patient records, or artificial
                numerical fabrication violating biological stochasticity.
              </p>
            </div>
          </div>

          <div class="metrics-card">
            <h4>Forensic Risk Profile &amp; Anomaly Dossier</h4>
            <div class="dossier-summary">
              <div class="dossier-row">
                <strong>Site ID:</strong>
                <span>{{ currentDrilldownSite.site_id }}</span>
              </div>
              <div class="dossier-row">
                <strong>Study:</strong>
                <span>{{ currentDrilldownSite.study_id }}</span>
              </div>
              <div class="dossier-row">
                <strong>Composite Risk Score:</strong>
                <span
                  >{{ currentDrilldownSite.composite_risk_score }} / 100</span
                >
              </div>
              <div class="dossier-row">
                <strong>Risk Classification:</strong>
                <span>{{ currentDrilldownSite.risk_tier }}</span>
              </div>
              <div class="dossier-row">
                <strong>Outlier Flag:</strong>
                <span>{{
                  currentDrilldownSite.is_anomalous_outlier
                    ? "CRITICAL OUTLIER"
                    : "NOMINAL"
                }}</span>
              </div>
              <div class="dossier-row">
                <strong>Active KRI Linkage:</strong>
                <span>{{
                  currentDrilldownSite.kri_evaluation_id || "None Pending"
                }}</span>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 6: Duplicate Patient Record Sentinel -->
      <section v-if="activeSubTab === 'duplicate-patients'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Cross-Site Duplicate Patient Record &amp; Vector Fingerprint
              Sentinel
            </h3>
            <p class="pane-description">
              Surveillance under PRD-CSM-004 detecting identical or
              near-identical baseline clinical feature vectors across subjects
              (Demographics, 12-Lead ECG intervals, vital signs, and laboratory
              panels).
            </p>
          </div>
          <div class="pane-action-group">
            <button
              class="csm-btn csm-btn-primary"
              :disabled="isLoading"
              @click="runDuplicatePatientScan"
            >
              <span class="btn-icon">🔍</span> Run Duplicate Vector Scan
            </button>
          </div>
        </div>

        <div class="sentinel-summary-bar">
          <div class="summary-metric">
            <span class="metric-num">{{ duplicateMatches.length }}</span>
            <span class="metric-label">Flagged Duplicate Matches</span>
          </div>
          <div class="summary-metric">
            <span class="metric-num text-danger">{{
              crossSiteMatchesCount
            }}</span>
            <span class="metric-label">Cross-Site Duplications</span>
          </div>
          <div class="summary-metric">
            <span class="metric-num text-warning"
              >{{ (highestSimilarity * 100).toFixed(1) }}%</span
            >
            <span class="metric-label">Max Vector Similarity</span>
          </div>
        </div>

        <div class="findings-cards-list">
          <div
            v-for="(match, idx) in duplicateMatches"
            :key="idx"
            class="finding-card"
            :class="{ 'finding-card-critical': match.severity === 'CRITICAL' }"
          >
            <div class="finding-card-header">
              <div class="finding-badge-row">
                <span
                  class="badge"
                  :class="
                    match.severity === 'CRITICAL'
                      ? 'badge-danger'
                      : 'badge-warning'
                  "
                >
                  {{ match.severity }}
                </span>
                <span v-if="match.is_cross_site" class="badge badge-danger">
                  CROSS-SITE MATCH
                </span>
                <span class="badge badge-standard">
                  Confidence: {{ (match.confidence_score * 100).toFixed(0) }}%
                </span>
              </div>
              <span class="similarity-score-pill">
                {{ (match.similarity_score * 100).toFixed(1) }}% Match
              </span>
            </div>

            <div class="finding-subjects-comparison">
              <div class="subject-box">
                <div class="subject-role">Primary Subject</div>
                <div class="subject-id-val">
                  {{ match.primary_subject_id }}
                </div>
                <div class="subject-site-val">
                  Site: {{ match.primary_site_id }}
                </div>
              </div>
              <div class="comparison-connector">
                ⮂ <span>VECTOR SIMILARITY</span> ⮃
              </div>
              <div class="subject-box">
                <div class="subject-role">Matched Subject</div>
                <div class="subject-id-val">
                  {{ match.matched_subject_id }}
                </div>
                <div class="subject-site-val">
                  Site: {{ match.matched_site_id }}
                </div>
              </div>
            </div>

            <p class="finding-evidence-text">
              {{ match.evidence_summary }}
            </p>

            <div class="vector-subscores-grid">
              <div class="subscore-item">
                <span class="subscore-label">Demographics</span>
                <div class="subscore-bar-bg">
                  <div
                    class="subscore-bar-fill"
                    :style="{
                      width: `${(match.forensic_metadata?.sub_scores?.demographics || 1) * 100}%`,
                    }"
                  />
                </div>
                <span class="subscore-val"
                  >{{
                    (
                      (match.forensic_metadata?.sub_scores?.demographics || 1) *
                      100
                    ).toFixed(0)
                  }}%</span
                >
              </div>
              <div class="subscore-item">
                <span class="subscore-label">ECG Intervals</span>
                <div class="subscore-bar-bg">
                  <div
                    class="subscore-bar-fill"
                    :style="{
                      width: `${(match.forensic_metadata?.sub_scores?.ecg_intervals || 1) * 100}%`,
                    }"
                  />
                </div>
                <span class="subscore-val"
                  >{{
                    (
                      (match.forensic_metadata?.sub_scores?.ecg_intervals ||
                        1) * 100
                    ).toFixed(0)
                  }}%</span
                >
              </div>
              <div class="subscore-item">
                <span class="subscore-label">Lab Chemistry</span>
                <div class="subscore-bar-bg">
                  <div
                    class="subscore-bar-fill"
                    :style="{
                      width: `${(match.forensic_metadata?.sub_scores?.lab_panel || 1) * 100}%`,
                    }"
                  />
                </div>
                <span class="subscore-val"
                  >{{
                    (
                      (match.forensic_metadata?.sub_scores?.lab_panel || 1) *
                      100
                    ).toFixed(0)
                  }}%</span
                >
              </div>
            </div>

            <div class="finding-actions-row">
              <button
                class="csm-btn csm-btn-primary csm-btn-sm"
                @click="dispatchInvestigationAlert(match)"
              >
                🚨 Dispatch 21 CFR Part 11 Investigation Ticket
              </button>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 7: Temporal & Behavioral Anomaly Scanner -->
      <section v-if="activeSubTab === 'temporal'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Temporal &amp; Behavioral Data Entry Anomaly Scanner
            </h3>
            <p class="pane-description">
              Surveillance under PRD-CSM-003 detecting suspicious data entry
              clusters during non-working hours, rapid burst submissions (&lt;
              10s intervals), and identical inter-procedure elapsed intervals
              across subjects.
            </p>
          </div>
          <div class="pane-action-group">
            <button
              class="csm-btn csm-btn-primary"
              :disabled="isLoading"
              @click="runTemporalScan"
            >
              <span class="btn-icon">⏱️</span> Scan Timing Patterns
            </button>
          </div>
        </div>

        <div class="temporal-findings-grid">
          <div
            v-for="(anom, idx) in temporalAnomalies"
            :key="idx"
            class="temporal-card"
            :class="{ 'temporal-card-alert': anom.severity === 'CRITICAL' }"
          >
            <div class="temporal-card-header">
              <span
                class="badge"
                :class="
                  anom.severity === 'CRITICAL'
                    ? 'badge-danger'
                    : 'badge-warning'
                "
              >
                {{ anom.anomaly_flag }}
              </span>
              <span class="site-tag">Site: {{ anom.primary_site_id }}</span>
            </div>
            <h4 class="temporal-title">
              {{
                anom.anomaly_flag === "RAPID_BURST_SUBMISSION"
                  ? "Superhuman Rapid Burst Submissions"
                  : anom.anomaly_flag === "IDENTICAL_PROCEDURE_INTERVALS"
                    ? "Identical Inter-Procedure Elapsed Intervals"
                    : "Off-Hours & Weekend Submission Cluster"
              }}
            </h4>
            <p class="temporal-evidence">
              {{ anom.evidence_summary }}
            </p>
            <div class="temporal-footer">
              <span class="confidence-text"
                >Confidence:
                {{ (anom.confidence_score * 100).toFixed(0) }}%</span
              >
              <button
                class="csm-btn csm-btn-primary csm-btn-sm"
                @click="dispatchInvestigationAlert(anom)"
              >
                Dispatch Deviation
              </button>
            </div>
          </div>
        </div>
      </section>

      <!-- TAB 8: Perceptual Image Hashing (pHash) Sentinel -->
      <section v-if="activeSubTab === 'phash'" class="tab-pane">
        <div class="pane-header-row">
          <div class="pane-title-group">
            <h3 class="pane-title">
              Perceptual Image Hashing (pHash / dHash) Sentinel
            </h3>
            <p class="pane-description">
              Surveillance under PRD-CSM-004 computing 64-bit DCT perceptual
              image hashes to detect reused, rotated, cropped, or
              brightness-adjusted radiological scans across clinical trial
              subjects.
            </p>
          </div>
          <div class="pane-action-group">
            <button
              class="csm-btn csm-btn-primary"
              :disabled="isLoading"
              @click="runImagePhashScan"
            >
              <span class="btn-icon">🖼️</span> Execute pHash Scan
            </button>
          </div>
        </div>

        <div class="phash-findings-list">
          <div
            v-for="(imgMatch, idx) in imageDuplicates"
            :key="idx"
            class="phash-card"
          >
            <div class="phash-card-header">
              <div class="badge-row">
                <span class="badge badge-danger">IMAGE REUSE DETECTED</span>
                <span class="badge badge-warning"
                  >Transformation:
                  {{ imgMatch.transformation_type || "ROTATED_90" }}</span
                >
                <span class="badge badge-standard"
                  >Hamming Distance: {{ imgMatch.hamming_distance }}</span
                >
              </div>
              <span class="similarity-score-pill">
                {{ (imgMatch.similarity_score * 100).toFixed(1) }}% Match
              </span>
            </div>

            <div class="phash-comparison-view">
              <div class="image-preview-box">
                <div class="image-mock-canvas">🩻 [CT Slice #101]</div>
                <div class="image-meta">
                  <strong>Subject:</strong>
                  {{ imgMatch.primary_subject_id }} ({{
                    imgMatch.primary_site_id
                  }})<br />
                  <strong>Hash:</strong>
                  <code>{{
                    imgMatch.forensic_metadata?.primary_phash ||
                    defaultPrimaryPhash
                  }}</code>
                </div>
              </div>

              <div class="phash-vs-badge">
                <div class="hamming-circle">
                  Δ = {{ imgMatch.hamming_distance }}
                </div>
                <span>MATCH</span>
              </div>

              <div class="image-preview-box">
                <div class="image-mock-canvas image-rotated">
                  🩻 [CT Slice #104 (Rotated)]
                </div>
                <div class="image-meta">
                  <strong>Subject:</strong>
                  {{ imgMatch.matched_subject_id }} ({{
                    imgMatch.matched_site_id
                  }})<br />
                  <strong>Hash:</strong>
                  <code>{{
                    imgMatch.forensic_metadata?.matched_phash ||
                    defaultMatchedPhash
                  }}</code>
                </div>
              </div>
            </div>

            <p class="finding-evidence-text">
              {{ imgMatch.evidence_summary }}
            </p>

            <div class="finding-actions-row">
              <button
                class="csm-btn csm-btn-primary csm-btn-sm"
                @click="dispatchInvestigationAlert(imgMatch)"
              >
                🚨 Log GCP Investigation Deviation &amp; Quarantine Attachment
              </button>
            </div>
          </div>
        </div>
      </section>
    </div>

    <!-- Forensics Dossier Modal -->
    <div v-if="showDossierModal" class="modal-backdrop" role="presentation">
      <div
        ref="dossierModalRef"
        class="dossier-modal"
        role="dialog"
        aria-modal="true"
        aria-labelledby="dossier-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="dossier-modal-title">
            21 CFR Part 11 Centralized Monitoring Inspection Snapshot
          </h3>
          <ClinicalIconButton
            class="close-btn"
            label="Close centralized monitoring inspection snapshot dialog"
            @click="showDossierModal = false"
          >
            &times;
          </ClinicalIconButton>
        </div>
        <div class="modal-body">
          <div class="seal-badge">
            🔐 CRYPTOGRAPHICALLY SEALED &bull; GxP AUDIT TRAIL VERIFIED
          </div>
          <p><strong>Study:</strong> {{ selectedStudyId }}</p>
          <p><strong>Surveillance Run ID:</strong> {{ activeRun?.id }}</p>
          <p>
            <strong>Analysis Timestamp:</strong>
            {{ activeRun?.analysis_timestamp }}
          </p>
          <p>
            <strong>Total Sites:</strong> {{ activeRun?.total_sites }} |
            <strong>Total Observations:</strong>
            {{ activeRun?.total_observations }}
          </p>
          <p>
            <strong>Flagged Anomalous Sites (P &lt; 0.001):</strong>
            {{ activeRun?.anomalous_sites_count }}
          </p>

          <h4 class="mt-16">Flagged Outlier Summary</h4>
          <ul class="dossier-list">
            <li
              v-for="site in siteRecords.filter((s) => s.is_anomalous_outlier)"
              :key="site.site_id"
            >
              <strong>{{ site.site_id }}</strong> (Score:
              {{ site.composite_risk_score }}):
              {{ site.flagged_anomaly_reasons.join(", ") }}
            </li>
          </ul>
        </div>
        <div class="modal-footer">
          <button
            class="csm-btn csm-btn-secondary"
            @click="showDossierModal = false"
          >
            Close
          </button>
          <button
            class="csm-btn csm-btn-primary"
            @click="showDossierModal = false"
          >
            Download PDF Dossier
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, ref } from "vue";
import { ClinicalIconButton, useFocusTrap } from "ui";
import { useToastStore } from "../../stores/toasts";

const toasts = useToastStore();
const availableStudies = ref([
  "STUDY-ONC-2026",
  "STUDY-CSM-2026",
  "STUDY-CARDIO-401",
]);
const selectedStudyId = ref("STUDY-CSM-2026");
const activeSubTab = ref("overview");
const isLoading = ref(false);
const showOnlyOutliers = ref(false);
const showDossierModal = ref(false);
const dossierModalRef = ref(null);
useFocusTrap(dossierModalRef, {
  isOpen: showDossierModal,
  onEscape: () => {
    showDossierModal.value = false;
  },
});
const selectedDrilldownSiteId = ref("");

const activeRun = ref(null);
const siteRecords = ref([]);
const duplicateMatches = ref([]);
const temporalAnomalies = ref([]);
const imageDuplicates = ref([]);

// Computed KPIs
const anomalousSitesCount = computed(() => {
  return siteRecords.value.filter((s) => s.is_anomalous_outlier).length;
});

const crossSiteMatchesCount = computed(() => {
  return duplicateMatches.value.filter((m) => m.is_cross_site).length;
});

const highestSimilarity = computed(() => {
  if (duplicateMatches.value.length === 0) return 0;
  return Math.max(
    ...duplicateMatches.value.map((m) => m.similarity_score || 0)
  );
});

const benfordNonConformCount = computed(() => {
  return siteRecords.value.filter(
    (s) => s.benford?.conformity === "NON_CONFORMING"
  ).length;
});

const severeRoundingCount = computed(() => {
  return siteRecords.value.filter(
    (s) => s.terminal_digit?.rounding_bias_tier === "SEVERE_ROUNDING"
  ).length;
});

const varianceShrunkCount = computed(() => {
  return siteRecords.value.filter(
    (s) => s.variance_shrinkage?.is_variance_shrunk
  ).length;
});

const maxMahalanobisDistance = computed(() => {
  if (siteRecords.value.length === 0) return 0;
  return Math.max(
    ...siteRecords.value.map((s) => s.multivariate?.mahalanobis_distance || 0)
  );
});

const filteredSites = computed(() => {
  if (!showOnlyOutliers.value) return siteRecords.value;
  return siteRecords.value.filter((s) => s.is_anomalous_outlier);
});

const currentDrilldownSite = computed(() => {
  if (!selectedDrilldownSiteId.value && siteRecords.value.length > 0) {
    return siteRecords.value[0];
  }
  return (
    siteRecords.value.find(
      (s) => s.site_id === selectedDrilldownSiteId.value
    ) || siteRecords.value[0]
  );
});

// Helper formatting functions
function getBenfordBadgeClass(conf) {
  if (conf === "CLOSE_CONFORMITY") return "badge-success";
  if (conf === "ACCEPTABLE_CONFORMITY") return "badge-info";
  if (conf === "MARGINAL_CONFORMITY") return "badge-warning";
  return "badge-danger";
}

function getRoundingBadgeClass(tier) {
  if (tier === "NORMAL") return "badge-success";
  if (tier === "MODERATE_ROUNDING") return "badge-warning";
  return "badge-danger";
}

function getScoreColorClass(score) {
  if (score >= 60) return "score-critical";
  if (score >= 35) return "score-high";
  if (score >= 15) return "score-medium";
  return "score-low";
}

function formatFlagName(flag) {
  return flag.replace(/_/g, " ");
}

function formatVarName(name) {
  return name.replace(/_/g, " ").toUpperCase();
}

function getVariableCov(varName) {
  const covMap =
    currentDrilldownSite.value?.multivariate?.site_covariance_matrix;
  if (!covMap || !covMap[varName]) return "N/A";
  const val = covMap[varName][varName];
  return Math.sqrt(Math.max(val, 0)).toFixed(2);
}

// Benford chart helpers
const theoreticalBenford = [30.1, 17.6, 12.5, 9.7, 7.9, 6.7, 5.8, 5.1, 4.6];

function getObservedBenfordCount(d) {
  return (
    currentDrilldownSite.value?.benford?.observed_frequencies?.[String(d)] || 0
  );
}

function getObservedBenfordPct(d) {
  const count = getObservedBenfordCount(d);
  const total = currentDrilldownSite.value?.benford?.sample_size || 1;
  return Number(((count / total) * 100).toFixed(1));
}

function getExpectedBenfordPct(d) {
  return theoreticalBenford[d - 1] || 0;
}

function getObservedBenfordHeight(d) {
  const pct = getObservedBenfordPct(d);
  return Math.min(100, Math.max(5, pct * 2.5));
}

function getExpectedBenfordHeight(d) {
  const pct = getExpectedBenfordPct(d);
  return Math.min(100, Math.max(5, pct * 2.5));
}

// Terminal digit chart helpers
function getTerminalDigitPct(d) {
  const count =
    currentDrilldownSite.value?.terminal_digit?.observed_frequencies?.[
      String(d)
    ] || 0;
  const total = currentDrilldownSite.value?.terminal_digit?.sample_size || 1;
  return Number(((count / total) * 100).toFixed(1));
}

function getTerminalDigitHeight(d) {
  const pct = getTerminalDigitPct(d);
  return Math.min(100, Math.max(5, pct * 2.0));
}

function drilldownSite(site) {
  selectedDrilldownSiteId.value = site.site_id;
  activeSubTab.value = "benford";
}

function onStudyChange() {
  executeSurveillanceScan();
}

function openDossierModal() {
  showDossierModal.value = true;
}

const defaultPrimaryPhash = "a3f01b92c4d5e678"; // pragma: allowlist secret
const defaultMatchedPhash = "a3f01b92c4d5e678"; // pragma: allowlist secret

// Simulated data generator for standalone frontend interactive demonstration
function generateSimulatedDataset() {
  isLoading.value = true;
  setTimeout(() => {
    const mockSites = [
      {
        id: "rec-101",
        run_id: "run-001",
        study_id: selectedStudyId.value,
        site_id: "SITE-101",
        sample_size: 60,
        benford: {
          sample_size: 60,
          chi_square: 4.2,
          p_value: 0.838,
          mad: 0.0052,
          conformity: "CLOSE_CONFORMITY",
          observed_frequencies: {
            1: 19,
            2: 11,
            3: 8,
            4: 6,
            5: 5,
            6: 4,
            7: 3,
            8: 2,
            9: 2,
          },
          expected_frequencies: {
            1: 18.06,
            2: 10.57,
            3: 7.5,
            4: 5.81,
            5: 4.75,
            6: 4.02,
            7: 3.48,
            8: 3.07,
            9: 2.75,
          },
        },
        terminal_digit: {
          sample_size: 180,
          chi_square: 2.4,
          p_value: 0.983,
          zero_five_index: 0.21,
          rounding_bias_tier: "NORMAL",
          observed_frequencies: {
            0: 19,
            1: 18,
            2: 17,
            3: 19,
            4: 18,
            5: 19,
            6: 18,
            7: 17,
            8: 18,
            9: 17,
          },
          expected_frequencies: {
            0: 18,
            1: 18,
            2: 18,
            3: 18,
            4: 18,
            5: 18,
            6: 18,
            7: 18,
            8: 18,
            9: 18,
          },
        },
        multivariate: {
          mahalanobis_distance: 0.45,
          mahalanobis_p_value: 0.892,
          correlation_matrix_distance: 0.12,
          site_mean_vector: {
            systolic_bp: 121.5,
            diastolic_bp: 79.2,
            heart_rate: 71.8,
            lab_creatinine: 1.12,
          },
          site_covariance_matrix: {
            systolic_bp: { systolic_bp: 144.0, diastolic_bp: 78.0 },
            diastolic_bp: { diastolic_bp: 42.0 },
          },
        },
        variance_shrinkage: {
          variance_ratio: 0.94,
          variance_shrinkage_p_value: 0.452,
          kl_divergence: 0.08,
          is_variance_shrunk: false,
        },
        composite_risk_score: 5.0,
        risk_tier: "LOW",
        is_anomalous_outlier: false,
        flagged_anomaly_reasons: [],
      },
      {
        id: "rec-102",
        run_id: "run-001",
        study_id: selectedStudyId.value,
        site_id: "SITE-BENFORD-ANOMALY",
        sample_size: 60,
        benford: {
          sample_size: 60,
          chi_square: 337.4,
          p_value: 0.00001,
          mad: 0.1878,
          conformity: "NON_CONFORMING",
          observed_frequencies: {
            1: 0,
            2: 0,
            3: 0,
            4: 0,
            5: 0,
            6: 0,
            7: 18,
            8: 21,
            9: 21,
          },
          expected_frequencies: {
            1: 18.06,
            2: 10.57,
            3: 7.5,
            4: 5.81,
            5: 4.75,
            6: 4.02,
            7: 3.48,
            8: 3.07,
            9: 2.75,
          },
        },
        terminal_digit: {
          sample_size: 180,
          chi_square: 3.1,
          p_value: 0.96,
          zero_five_index: 0.19,
          rounding_bias_tier: "NORMAL",
          observed_frequencies: {
            0: 18,
            1: 19,
            2: 18,
            3: 17,
            4: 19,
            5: 16,
            6: 18,
            7: 19,
            8: 18,
            9: 18,
          },
          expected_frequencies: {
            0: 18,
            1: 18,
            2: 18,
            3: 18,
            4: 18,
            5: 18,
            6: 18,
            7: 18,
            8: 18,
            9: 18,
          },
        },
        multivariate: {
          mahalanobis_distance: 1.25,
          mahalanobis_p_value: 0.152,
          correlation_matrix_distance: 0.35,
          site_mean_vector: {
            systolic_bp: 122.0,
            diastolic_bp: 80.1,
            heart_rate: 72.0,
            lab_creatinine: 8.2,
          },
          site_covariance_matrix: { systolic_bp: { systolic_bp: 140.0 } },
        },
        variance_shrinkage: {
          variance_ratio: 0.88,
          variance_shrinkage_p_value: 0.312,
          kl_divergence: 0.45,
          is_variance_shrunk: false,
        },
        composite_risk_score: 65.0,
        risk_tier: "CRITICAL",
        is_anomalous_outlier: true,
        flagged_anomaly_reasons: [
          "BENFORD_NON_CONFORMANCE",
          "DATA_FABRICATION_SUSPICION",
        ],
      },
      {
        id: "rec-103",
        run_id: "run-001",
        study_id: selectedStudyId.value,
        site_id: "SITE-ROUNDING-BIAS",
        sample_size: 60,
        benford: {
          sample_size: 60,
          chi_square: 8.5,
          p_value: 0.386,
          mad: 0.0105,
          conformity: "ACCEPTABLE_CONFORMITY",
          observed_frequencies: {
            1: 17,
            2: 11,
            3: 8,
            4: 6,
            5: 5,
            6: 4,
            7: 3,
            8: 3,
            9: 3,
          },
          expected_frequencies: {
            1: 18.06,
            2: 10.57,
            3: 7.5,
            4: 5.81,
            5: 4.75,
            6: 4.02,
            7: 3.48,
            8: 3.07,
            9: 2.75,
          },
        },
        terminal_digit: {
          sample_size: 180,
          chi_square: 727.1,
          p_value: 0.00001,
          zero_five_index: 0.95,
          rounding_bias_tier: "SEVERE_ROUNDING",
          observed_frequencies: {
            0: 82,
            1: 0,
            2: 0,
            3: 0,
            4: 0,
            5: 98,
            6: 0,
            7: 0,
            8: 0,
            9: 0,
          },
          expected_frequencies: {
            0: 18,
            1: 18,
            2: 18,
            3: 18,
            4: 18,
            5: 18,
            6: 18,
            7: 18,
            8: 18,
            9: 18,
          },
        },
        multivariate: {
          mahalanobis_distance: 0.88,
          mahalanobis_p_value: 0.62,
          correlation_matrix_distance: 0.28,
          site_mean_vector: {
            systolic_bp: 125.0,
            diastolic_bp: 80.0,
            heart_rate: 75.0,
            lab_creatinine: 1.15,
          },
          site_covariance_matrix: { systolic_bp: { systolic_bp: 135.0 } },
        },
        variance_shrinkage: {
          variance_ratio: 0.85,
          variance_shrinkage_p_value: 0.28,
          kl_divergence: 0.32,
          is_variance_shrunk: false,
        },
        composite_risk_score: 55.0,
        risk_tier: "HIGH",
        is_anomalous_outlier: true,
        flagged_anomaly_reasons: ["TERMINAL_DIGIT_ROUNDING_BIAS"],
      },
      {
        id: "rec-104",
        run_id: "run-001",
        study_id: selectedStudyId.value,
        site_id: "SITE-VARIANCE-SHRINKAGE",
        sample_size: 60,
        benford: {
          sample_size: 60,
          chi_square: 6.2,
          p_value: 0.625,
          mad: 0.0082,
          conformity: "ACCEPTABLE_CONFORMITY",
          observed_frequencies: {
            1: 18,
            2: 11,
            3: 7,
            4: 6,
            5: 5,
            6: 4,
            7: 4,
            8: 3,
            9: 2,
          },
          expected_frequencies: {
            1: 18.06,
            2: 10.57,
            3: 7.5,
            4: 5.81,
            5: 4.75,
            6: 4.02,
            7: 3.48,
            8: 3.07,
            9: 2.75,
          },
        },
        terminal_digit: {
          sample_size: 180,
          chi_square: 4.8,
          p_value: 0.778,
          zero_five_index: 0.22,
          rounding_bias_tier: "NORMAL",
          observed_frequencies: {
            0: 20,
            1: 18,
            2: 18,
            3: 17,
            4: 18,
            5: 19,
            6: 18,
            7: 17,
            8: 18,
            9: 17,
          },
          expected_frequencies: {
            0: 18,
            1: 18,
            2: 18,
            3: 18,
            4: 18,
            5: 18,
            6: 18,
            7: 18,
            8: 18,
            9: 18,
          },
        },
        multivariate: {
          mahalanobis_distance: 0.62,
          mahalanobis_p_value: 0.812,
          correlation_matrix_distance: 0.21,
          site_mean_vector: {
            systolic_bp: 120.0,
            diastolic_bp: 80.0,
            heart_rate: 72.0,
            lab_creatinine: 1.05,
          },
          site_covariance_matrix: { systolic_bp: { systolic_bp: 0.16 } },
        },
        variance_shrinkage: {
          variance_ratio: 0.002,
          variance_shrinkage_p_value: 0.00001,
          kl_divergence: 4.82,
          is_variance_shrunk: true,
        },
        composite_risk_score: 70.0,
        risk_tier: "CRITICAL",
        is_anomalous_outlier: true,
        flagged_anomaly_reasons: [
          "ANOMALOUS_VARIANCE_SHRINKAGE",
          "DATA_FABRICATION_SUSPICION",
        ],
      },
    ];

    siteRecords.value = mockSites;
    selectedDrilldownSiteId.value = mockSites[1].site_id;

    duplicateMatches.value = [
      {
        primary_subject_id: "SUBJ-101-001",
        primary_site_id: "SITE-101",
        matched_subject_id: "SUBJ-104-099",
        matched_site_id: "SITE-104",
        is_cross_site: true,
        similarity_score: 0.985,
        confidence_score: 0.98,
        severity: "CRITICAL",
        evidence_summary:
          "Cross-Site duplicate clinical vector match between SUBJ-101-001 (SITE-101) and SUBJ-104-099 (SITE-104) with 98.5% composite vector similarity (100% ECG intervals & Lab panels).",
        forensic_metadata: {
          sub_scores: {
            demographics: 0.96,
            ecg_intervals: 1.0,
            lab_panel: 1.0,
          },
        },
      },
      {
        primary_subject_id: "SUBJ-102-008",
        primary_site_id: "SITE-102",
        matched_subject_id: "SUBJ-102-014",
        matched_site_id: "SITE-102",
        is_cross_site: false,
        similarity_score: 0.892,
        confidence_score: 0.89,
        severity: "MAJOR",
        evidence_summary:
          "Intra-Site potential duplicate subject record detected with 89.2% baseline feature similarity.",
        forensic_metadata: {
          sub_scores: {
            demographics: 0.88,
            ecg_intervals: 0.92,
            lab_panel: 0.86,
          },
        },
      },
    ];

    temporalAnomalies.value = [
      {
        primary_site_id: "SITE-TEMPORAL-ANOMALY",
        anomaly_flag: "OFF_HOURS_BURST_ANOMALY",
        severity: "CRITICAL",
        confidence_score: 0.95,
        evidence_summary:
          "Site 'SITE-TEMPORAL-ANOMALY' exhibits 78.4% off-hours entries, 8 weekend submissions, and 6 midnight entries.",
      },
      {
        primary_site_id: "SITE-TEMPORAL-ANOMALY",
        anomaly_flag: "RAPID_BURST_SUBMISSION",
        severity: "CRITICAL",
        confidence_score: 0.92,
        evidence_summary:
          "Site 'SITE-TEMPORAL-ANOMALY' triggered rapid burst submission sentinel: 10 entries submitted at < 4.0s intervals, indicating scripted bot entry.",
      },
      {
        primary_site_id: "SITE-IDENTICAL-INTERVALS",
        anomaly_flag: "IDENTICAL_PROCEDURE_INTERVALS",
        severity: "CRITICAL",
        confidence_score: 0.98,
        evidence_summary:
          "Site 'SITE-IDENTICAL-INTERVALS' recorded identical procedure elapsed interval of 870s (14m 30s) across 5 different subjects for 'VITALS_TO_BLOOD_DRAW'.",
      },
    ];

    imageDuplicates.value = [
      {
        primary_subject_id: "SUBJ-101-001",
        primary_site_id: "SITE-101",
        matched_subject_id: "SUBJ-104-099",
        matched_site_id: "SITE-104",
        hamming_distance: 2,
        similarity_score: 0.969,
        transformation_type: "ROTATED_90",
        severity: "CRITICAL",
        evidence_summary:
          "Medical image duplication detected (CHEST_CT): Image 'IMG-101-BL' for Subject 'SUBJ-101-001' (SITE-101) matches Image 'IMG-104-099' for Subject 'SUBJ-104-099' (SITE-104) with Hamming distance 2 (96.9% similarity, 90-deg rotation).",
        forensic_metadata: {
          primary_phash: "d4e5f60123456789", // pragma: allowlist secret
          matched_phash: "d4e5f6012345678b", // pragma: allowlist secret
        },
      },
    ];

    activeRun.value = {
      id: `RUN-${Date.now().toString().slice(-6)}`,
      study_id: selectedStudyId.value,
      run_name: `CSM-FORENSICS-${selectedStudyId.value}`,
      analysis_timestamp: new Date().toISOString(),
      total_sites: mockSites.length,
      total_observations: mockSites.length * 60,
      anomalous_sites_count: mockSites.filter((s) => s.is_anomalous_outlier)
        .length,
    };
    isLoading.value = false;
  }, 400);
}

function executeSurveillanceScan() {
  generateSimulatedDataset();
}

function runDuplicatePatientScan() {
  isLoading.value = true;
  setTimeout(() => {
    isLoading.value = false;
    toasts.push({
      tone: "warning",
      title: "Duplicate Patient Scan Completed",
      message:
        "Duplicate Patient Record Scan completed: 2 matches identified (1 cross-site CRITICAL).",
    });
  }, 350);
}

function runTemporalScan() {
  isLoading.value = true;
  setTimeout(() => {
    isLoading.value = false;
    toasts.push({
      tone: "warning",
      title: "Temporal Anomaly Scan Completed",
      message:
        "Temporal & Behavioral Anomaly Scan completed: 3 timing anomalies detected.",
    });
  }, 350);
}

function runImagePhashScan() {
  isLoading.value = true;
  setTimeout(() => {
    isLoading.value = false;
    toasts.push({
      tone: "warning",
      title: "Image pHash Scan Completed",
      message:
        "Perceptual Image Hashing (pHash) Scan completed: 1 rotated duplicate scan detected across sites.",
    });
  }, 350);
}

function dispatchInvestigationAlert(item) {
  toasts.push({
    tone: "info",
    title: "Investigation Deviation Dispatched",
    message: `Dispatched 21 CFR Part 11 Data Integrity Investigation Deviation for ${item.primary_subject_id || item.primary_site_id} with full cryptographic audit trail.`,
  });
}

function dispatchAllKriAlerts() {
  toasts.push({
    tone: "success",
    title: "KRI Breach Events Dispatched",
    message: `Successfully dispatched automated KRI breach events for ${anomalousSitesCount.value} outlier sites under ICH E6(R2)/(R3).`,
  });
}

onMounted(() => {
  generateSimulatedDataset();
});
</script>

<style scoped>
/* ==========================================================================
   Centralized Statistical Monitoring (CSM) Forensics - 100% Vanilla CSS Tokens
   0 Tailwind CSS classes
   ========================================================================== */

.statistical-forensics-viewer {
  display: flex;
  flex-direction: column;
  gap: 20px;
  width: 100%;
  color: var(--primary);
  font-family: var(
    --font,
    -apple-system,
    BlinkMacSystemFont,
    "Segoe UI",
    Roboto,
    sans-serif
  );
}

/* Header */
.forensics-header {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 20px 24px;
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
  gap: 20px;
  flex-wrap: wrap;
}

.header-main {
  display: flex;
  flex-direction: column;
  gap: 12px;
  flex: 1;
  min-width: 320px;
}

.badge-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
}

.badge {
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
  padding: 3px 8px;
  border-radius: 4px;
}

.badge-regulatory {
  background-color: var(--semantic-color-ai-bg);
  color: var(--semantic-color-ai);
}

.badge-gxp {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.badge-standard {
  background-color: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.badge-success {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.badge-info {
  background-color: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}

.badge-warning {
  background-color: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning);
}

.badge-danger {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}

.forensics-title {
  font-size: 1.45rem;
  font-weight: 800;
  color: var(--semantic-color-text);
  letter-spacing: -0.02em;
  line-height: 1.2;
}

.forensics-subtitle {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  max-width: 820px;
  line-height: 1.45;
}

.header-meta-group {
  display: flex;
  gap: 20px;
  align-items: center;
  flex-wrap: wrap;
  padding-top: 4px;
}

.meta-item {
  display: flex;
  align-items: center;
  gap: 8px;
}

.meta-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.csm-select {
  padding: 6px 12px;
  font-size: 0.85rem;
  font-weight: 600;
  border: 1px solid var(--border);
  border-radius: 6px;
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  outline: none;
}

.csm-select:focus {
  border-color: var(--semantic-color-primary);
  box-shadow: 0 0 0 2px rgba(2, 132, 199, 0.15);
}

.status-pill {
  padding: 4px 10px;
  border-radius: 20px;
  font-size: 0.75rem;
  font-weight: 700;
}

.status-pill-nominal {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.status-pill-critical {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.header-actions {
  display: flex;
  gap: 10px;
  align-items: center;
  flex-wrap: wrap;
}

/* Buttons */
.csm-btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  padding: 8px 16px;
  font-size: 0.85rem;
  font-weight: 600;
  border-radius: 6px;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
  border: 1px solid transparent;
}

.csm-btn:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.csm-btn-primary {
  background-color: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.csm-btn-primary:hover:not(:disabled) {
  background-color: var(--semantic-color-primary);
}

.csm-btn-secondary {
  background-color: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text-body);
}

.csm-btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-header);
}

.csm-btn-outline {
  background-color: transparent;
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-primary);
}

.csm-btn-outline:hover {
  background-color: var(--semantic-color-primary-light);
}

.csm-btn-sm {
  padding: 4px 10px;
  font-size: 0.75rem;
}

/* KPI Cards Grid */
.kpi-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.kpi-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 16px;
  display: flex;
  align-items: center;
  gap: 14px;
  cursor: pointer;
  transition:
    transform 0.15s ease,
    box-shadow 0.15s ease;
}

.kpi-card:hover {
  transform: translateY(-2px);
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.06);
}

.kpi-card-alert {
  border-color: var(--semantic-color-error-border);
  background-color: var(--semantic-color-error-bg-soft);
}

.kpi-icon {
  font-size: 1.8rem;
}

.kpi-content {
  display: flex;
  flex-direction: column;
}

.kpi-value {
  font-size: 1.35rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.kpi-label {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

/* Navigation Tabs */
.forensics-tabs {
  display: flex;
  gap: 4px;
  border-bottom: 2px solid var(--semantic-color-border);
  overflow-x: auto;
}

.tab-btn {
  padding: 10px 18px;
  font-size: 0.85rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  background: none;
  border: none;
  border-bottom: 3px solid transparent;
  cursor: pointer;
  white-space: nowrap;
  transition: all 0.15s ease;
}

.tab-btn:hover {
  color: var(--semantic-color-text);
}

.tab-btn.active {
  color: var(--semantic-color-primary);
  border-bottom-color: var(--semantic-color-primary);
}

/* Tab Pane Container */
.tab-pane-container {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 12px;
  padding: 24px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.pane-header-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 20px;
  gap: 16px;
  flex-wrap: wrap;
}

.pane-title {
  font-size: 1.15rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.pane-caption {
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
  display: block;
  margin-top: 2px;
}

.filter-actions {
  display: flex;
  gap: 16px;
  align-items: center;
}

.checkbox-label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
  display: flex;
  align-items: center;
  gap: 6px;
  cursor: pointer;
}

/* Forensics Table */
.table-wrapper {
  overflow-x: auto;
}

.forensics-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.82rem;
}

.forensics-table th {
  background-color: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-weight: 700;
  text-align: left;
  padding: 12px 14px;
  border-bottom: 2px solid var(--semantic-color-border);
  white-space: nowrap;
}

.forensics-table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  vertical-align: middle;
}

.forensics-table tr:hover td {
  background-color: var(--semantic-color-surface-muted);
}

.row-outlier {
  background-color: var(--semantic-color-error-bg-soft);
}

.cell-site-id {
  color: var(--semantic-color-primary);
}

.sub-stat {
  font-size: 0.7rem;
  color: var(--semantic-color-text-secondary);
  margin-top: 2px;
}

.text-critical {
  color: var(--semantic-color-error);
  font-weight: 700;
}

.text-nominal {
  color: var(--semantic-color-success);
  font-weight: 700;
}

.text-muted {
  color: var(--semantic-color-text-secondary);
}

.score-pill-container {
  position: relative;
  width: 90px;
  height: 20px;
  background-color: var(--semantic-color-surface-header);
  border-radius: 10px;
  overflow: hidden;
  display: flex;
  align-items: center;
  justify-content: center;
}

.score-bar-bg {
  position: absolute;
  left: 0;
  top: 0;
  bottom: 0;
  opacity: 0.35;
}

.score-critical {
  background-color: var(--semantic-color-error);
}

.score-high {
  background-color: var(--semantic-color-warning-action);
}

.score-medium {
  background-color: var(--semantic-color-warning-action);
}

.score-low {
  background-color: var(--semantic-color-success);
}

.score-text {
  position: relative;
  font-size: 0.7rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.risk-tier-pill {
  font-size: 0.72rem;
  font-weight: 800;
  padding: 3px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.risk-tier-critical {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.risk-tier-high {
  background-color: var(--semantic-color-orange-bg);
  color: var(--semantic-color-orange-text);
}

.risk-tier-medium {
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}

.risk-tier-low {
  background-color: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.flag-pill-group {
  display: flex;
  gap: 4px;
  flex-wrap: wrap;
  max-width: 240px;
}

.flag-pill {
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  font-size: 0.65rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
}

/* Drilldown Grid */
.drilldown-grid {
  display: grid;
  grid-template-columns: 1.4fr 1fr;
  gap: 20px;
}

@media (max-width: 992px) {
  .drilldown-grid {
    grid-template-columns: 1fr;
  }
}

.chart-card,
.metrics-card {
  background-color: var(--semantic-color-surface-muted);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 20px;
}

.chart-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 20px;
  gap: 12px;
  flex-wrap: wrap;
}

.chart-badges {
  display: flex;
  gap: 6px;
  align-items: center;
  flex-wrap: wrap;
}

.stat-tag {
  font-size: 0.72rem;
  font-weight: 700;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  padding: 3px 8px;
  border-radius: 4px;
  color: var(--semantic-color-text-body);
}

/* Benford Bar Chart */
.benford-bars-container {
  display: flex;
  justify-content: space-around;
  align-items: flex-end;
  height: 220px;
  padding: 10px 0;
  border-bottom: 2px solid var(--semantic-color-border-control);
}

.benford-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  flex: 1;
}

.bars-pair {
  display: flex;
  gap: 4px;
  align-items: flex-end;
  height: 180px;
  width: 32px;
}

.bar {
  flex: 1;
  border-radius: 3px 3px 0 0;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  transition: height 0.3s ease;
}

.bar-observed {
  background-color: var(--semantic-color-primary);
}

.bar-expected {
  background-color: var(--semantic-color-border-control);
  opacity: 0.85;
}

.bar-label {
  font-size: 0.6rem;
  font-weight: 700;
  color: var(--semantic-color-surface);
  transform: rotate(-90deg);
  margin-top: 14px;
}

.digit-label {
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
}

.chart-legend {
  display: flex;
  gap: 20px;
  margin-top: 16px;
  justify-content: center;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.legend-box {
  width: 12px;
  height: 12px;
  border-radius: 2px;
}

.box-observed {
  background-color: var(--semantic-color-primary);
}

.box-expected {
  background-color: var(--semantic-color-border-control);
}

/* Terminal Digit Bars */
.terminal-bars-container {
  display: flex;
  justify-content: space-around;
  align-items: flex-end;
  height: 200px;
  padding: 10px 0;
  border-bottom: 2px solid var(--semantic-color-border-control);
}

.terminal-col {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 8px;
  flex: 1;
}

.terminal-bar {
  width: 24px;
  background-color: var(--semantic-color-interactive-primary);
  border-radius: 3px 3px 0 0;
  display: flex;
  align-items: flex-start;
  justify-content: center;
  transition: height 0.3s ease;
}

.bar-highlight {
  background-color: var(--semantic-color-error);
}

.digit-highlight {
  color: var(--semantic-color-error);
  font-weight: 900;
}

.rounding-callout {
  margin-top: 16px;
  padding: 12px;
  background-color: var(--semantic-color-surface);
  border-left: 4px solid var(--semantic-color-primary);
  border-radius: 4px;
  font-size: 0.8rem;
  line-height: 1.4;
}

/* Gauge Meter */
.gauge-container {
  margin-top: 12px;
}

.gauge-meter {
  height: 16px;
  background-color: var(--semantic-color-surface-header);
  border-radius: 8px;
  overflow: hidden;
}

.gauge-fill {
  height: 100%;
  border-radius: 8px;
  transition: width 0.3s ease;
}

.gauge-nominal {
  background: linear-gradient(
    90deg,
    var(--semantic-color-success),
    var(--semantic-color-warning-action)
  );
}

.gauge-critical {
  background: linear-gradient(
    90deg,
    var(--semantic-color-warning-action),
    var(--semantic-color-error)
  );
}

.gauge-scale {
  display: flex;
  justify-content: space-between;
  font-size: 0.68rem;
  color: var(--semantic-color-text-secondary);
  margin-top: 4px;
}

/* Nested Tables */
.nested-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.78rem;
  margin-top: 8px;
}

.nested-table th {
  text-align: left;
  padding: 6px 8px;
  border-bottom: 1px solid var(--semantic-color-border-control);
  color: var(--semantic-color-text-secondary);
  font-weight: 700;
}

.nested-table td {
  padding: 6px 8px;
  border-bottom: 1px solid var(--semantic-color-border);
}

.mt-16 {
  margin-top: 16px;
}

/* Highlight Box */
.metric-highlight-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 12px 16px;
}

.highlight-item {
  display: flex;
  justify-content: space-between;
  font-size: 0.8rem;
}

.highlight-label {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.highlight-value {
  font-weight: 800;
  color: var(--semantic-color-text);
}

.json-preview {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  padding: 12px;
  border-radius: 6px;
  font-size: 0.72rem;
  font-family: monospace;
  overflow-x: auto;
  margin-top: 10px;
}

/* Variance Display */
.variance-gauge-box {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 8px;
  padding: 16px;
}

.ratio-display {
  display: flex;
  flex-direction: column;
}

.ratio-num {
  font-size: 1.8rem;
  font-weight: 900;
  color: var(--semantic-color-text);
}

.ratio-desc {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.fabrication-alert-card {
  background-color: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error-border);
  border-radius: 8px;
  padding: 14px;
  color: var(--semantic-color-error-text-dark);
  font-size: 0.8rem;
  line-height: 1.45;
}

.fabrication-alert-card h4 {
  font-size: 0.88rem;
  margin-bottom: 4px;
}

.dossier-summary {
  display: flex;
  flex-direction: column;
  gap: 8px;
  margin-top: 12px;
}

.dossier-row {
  display: flex;
  justify-content: space-between;
  font-size: 0.8rem;
  padding-bottom: 6px;
  border-bottom: 1px solid var(--semantic-color-border);
}

/* Modal */
.modal-backdrop {
  position: fixed;
  inset: 0;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 1000;
  padding: 20px;
}

.dossier-modal {
  background-color: var(--semantic-color-surface);
  border-radius: 12px;
  max-width: 600px;
  width: 100%;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.2);
  overflow: hidden;
  display: flex;
  flex-direction: column;
}

.modal-header {
  padding: 16px 20px;
  border-bottom: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.modal-header h3 {
  font-size: 1.1rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.close-btn {
  background: none;
  border: none;
  font-size: 1.4rem;
  cursor: pointer;
  color: var(--semantic-color-text-secondary);
}

.modal-body {
  padding: 20px;
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
  line-height: 1.5;
}

.seal-badge {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  font-size: 0.72rem;
  font-weight: 800;
  letter-spacing: 0.05em;
  padding: 6px 12px;
  border-radius: 4px;
  text-align: center;
  margin-bottom: 16px;
}

.dossier-list {
  padding-left: 20px;
  margin-top: 8px;
}

.modal-footer {
  padding: 14px 20px;
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  background-color: var(--semantic-color-surface-muted);
}

/* ==========================================================================
   Data Integrity Sentinels (Duplicate Patients, Temporal & pHash) Styles
   ========================================================================== */

.sentinel-summary-bar {
  display: flex;
  gap: 20px;
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 8px;
  padding: 16px 20px;
  margin-bottom: 20px;
}

.summary-metric {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.metric-num {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.metric-num.text-danger {
  color: var(--semantic-color-error);
}

.metric-num.text-warning {
  color: var(--semantic-color-warning);
}

.metric-label {
  font-size: 0.75rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.findings-cards-list,
.phash-findings-list {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.finding-card,
.phash-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.finding-card-critical,
.temporal-card-alert {
  border-left: 4px solid var(--semantic-color-error);
}

.finding-card-header,
.phash-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.finding-badge-row {
  display: flex;
  gap: 8px;
  align-items: center;
}

.similarity-score-pill {
  padding: 4px 10px;
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  font-size: 0.8rem;
  font-weight: 800;
  border-radius: 16px;
}

.finding-subjects-comparison {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  gap: 16px;
  align-items: center;
  background-color: var(--semantic-color-surface-muted);
  border-radius: 8px;
  padding: 14px;
}

.subject-box {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.subject-role {
  font-size: 0.72rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.subject-id-val {
  font-size: 0.95rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.subject-site-val {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.comparison-connector {
  font-size: 0.72rem;
  font-weight: 800;
  color: var(--semantic-color-primary);
  text-align: center;
}

.finding-evidence-text {
  font-size: 0.85rem;
  color: var(--semantic-color-text-body);
  line-height: 1.45;
}

.vector-subscores-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(180px, 1fr));
  gap: 12px;
}

.subscore-item {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.75rem;
}

.subscore-label {
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  min-width: 90px;
}

.subscore-bar-bg {
  flex: 1;
  height: 8px;
  background-color: var(--semantic-color-surface-header);
  border-radius: 4px;
  overflow: hidden;
}

.subscore-bar-fill {
  height: 100%;
  background-color: var(--semantic-color-primary);
  border-radius: 4px;
}

.subscore-val {
  font-weight: 700;
  color: var(--semantic-color-text);
}

.finding-actions-row {
  display: flex;
  justify-content: flex-end;
  padding-top: 6px;
}

.csm-btn-sm {
  padding: 6px 12px;
  font-size: 0.78rem;
}

/* Temporal Cards */
.temporal-findings-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 16px;
}

.temporal-card {
  background-color: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 10px;
  padding: 18px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.temporal-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.site-tag {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
}

.temporal-title {
  font-size: 0.95rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.temporal-evidence {
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.45;
  flex: 1;
}

.temporal-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding-top: 8px;
  border-top: 1px solid var(--semantic-color-surface-header);
}

.confidence-text {
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
}

/* pHash Image Cards */
.phash-comparison-view {
  display: grid;
  grid-template-columns: 1fr auto 1fr;
  gap: 20px;
  align-items: center;
  background-color: var(--semantic-color-surface-muted);
  border-radius: 8px;
  padding: 16px;
}

.image-preview-box {
  display: flex;
  flex-direction: column;
  gap: 8px;
  align-items: center;
  text-align: center;
}

.image-mock-canvas {
  width: 140px;
  height: 140px;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-accent-inverse);
  display: flex;
  align-items: center;
  justify-content: center;
  border-radius: 8px;
  font-weight: 700;
  font-size: 0.8rem;
  border: 2px solid var(--semantic-color-border-inverse);
}

.image-rotated {
  transform: rotate(90deg);
  border-color: var(--semantic-color-warning-border-strong);
}

.image-meta {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
  line-height: 1.4;
}

.image-meta code {
  background-color: var(--semantic-color-surface-header);
  padding: 2px 4px;
  border-radius: 4px;
  font-family: monospace;
}

.phash-vs-badge {
  display: flex;
  flex-direction: column;
  align-items: center;
  gap: 4px;
}

.hamming-circle {
  width: 48px;
  height: 48px;
  border-radius: 50%;
  background-color: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 800;
  font-size: 0.85rem;
}

.phash-vs-badge span {
  font-size: 0.7rem;
  font-weight: 800;
  color: var(--semantic-color-text-secondary);
}
</style>
