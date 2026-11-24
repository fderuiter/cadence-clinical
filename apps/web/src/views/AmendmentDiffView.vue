<template>
  <div class="amendment-view-container">
    <!-- Notification Live Banner -->
    <div
      v-if="notificationBanner"
      class="notification-banner"
      :class="'banner-' + notificationBanner.type"
      role="status"
      aria-live="polite"
    >
      <span class="banner-icon">
        {{ notificationBanner.type === "success" ? "✅" : "⚠️" }}
      </span>
      <span class="banner-message">{{ notificationBanner.message }}</span>
      <ClinicalIconButton
        class="banner-close"
        label="Dismiss notification"
        @click="notificationBanner = null"
      >
        &times;
      </ClinicalIconButton>
    </div>

    <!-- Header Section -->
    <div class="view-header">
      <div class="header-content">
        <div class="title-row">
          <h2 class="view-title">
            Protocol Amendments &amp; In-Flight Subject Migration
          </h2>
          <span class="badge badge-primary">Zero-Downtime Engine</span>
        </div>
        <p class="view-description">
          Graph-native immutable versioning and dynamic subject schema
          projection. Compare version graphs, inspect field deltas, run guided
          4-step study upversioning, and execute bulk re-consent across clinical
          sites.
        </p>
      </div>

      <div class="header-actions">
        <button
          id="btn-create-amendment"
          class="btn btn-primary"
          @click="openWizard"
        >
          <span class="btn-icon">🧙</span>
          <span>Launch Upversioning Wizard</span>
        </button>
      </div>
    </div>

    <!-- Main Navigation Persona Mode Switcher -->
    <div class="mode-navigation-bar">
      <button
        class="mode-nav-btn"
        :class="{ active: activeMode === 'manager' }"
        @click="activeMode = 'manager'"
      >
        <span class="nav-icon">📐</span>
        <span>Study Manager: Guided Wizard &amp; Semantic Diff</span>
      </button>
      <button
        class="mode-nav-btn"
        :class="{ active: activeMode === 'coordinator' }"
        @click="activeMode = 'coordinator'"
      >
        <span class="nav-icon">📋</span>
        <span>Site Coordinator: Bulk Re-Consent &amp; Migration Workspace</span>
        <span v-if="gatedSubjectCount > 0" class="nav-pill-badge tabular-nums"
          >{{ gatedSubjectCount }} Hold(s)</span
        >
      </button>
    </div>

    <!-- Protocol Version Selection & Diff Controls Panel -->
    <div class="controls-panel">
      <div class="version-selectors">
        <div class="selector-group">
          <label for="base-version-select" class="selector-label"
            >Base Version (Frozen):</label
          >
          <select
            id="base-version-select"
            v-model="selectedBaseVersion"
            class="form-select"
            @change="handleVersionChange"
          >
            <option value="1.0.0">v1.0.0 (Approved / Locked)</option>
            <option value="1.1.0">v1.1.0 (Locked)</option>
          </select>
        </div>

        <div class="diff-arrow">➔</div>

        <div class="selector-group">
          <label for="amended-version-select" class="selector-label"
            >Amended Target Version:</label
          >
          <select
            id="amended-version-select"
            v-model="selectedAmendedVersion"
            class="form-select"
            @change="handleVersionChange"
          >
            <option value="2.0.0">v2.0.0-AMENDMENT (Approved / Active)</option>
            <option value="2.1.0-DRAFT">v2.1.0-DRAFT (Drafting)</option>
          </select>
        </div>
      </div>

      <div class="version-meta-tags">
        <span class="meta-tag">
          <strong>Re-Consent Mandated:</strong>
          <span
            class="tag-status"
            :class="requiresReconsent ? 'status-alert' : 'status-ok'"
          >
            {{ requiresReconsent ? "YES (PRD-SUB-007)" : "NO" }}
          </span>
        </span>
        <span class="meta-tag">
          <strong>Graph Status:</strong>
          <span class="tag-status status-immutable">IMMUTABLE BRANCH</span>
        </span>
      </div>
    </div>

    <!-- MODE 1: Study Manager Guided Wizard & Diff Inspector -->
    <div v-if="activeMode === 'manager'" class="manager-workspace-container">
      <!-- Tabs Bar -->
      <div class="workspace-tabs-bar">
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'dashboard' }"
          @click="activeTab = 'dashboard'"
        >
          📊 Subject Impact Breakdown
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'graph' }"
          @click="activeTab = 'graph'"
        >
          🔀 Graph Diff &amp; Target Rules
        </button>
        <button
          class="tab-btn"
          :class="{ active: activeTab === 'wizard' }"
          @click="openWizard"
        >
          🧙 4-Step Upversioning Wizard
        </button>
      </div>

      <!-- Tab 1: Subject Impact Analyzer Dashboard -->
      <div v-if="activeTab === 'dashboard'" class="dashboard-section">
        <div class="section-header">
          <h3 class="section-title">
            📊 In-Flight Subject Migration &amp; Re-Consent Analyzer
          </h3>
          <div class="header-right-actions">
            <button
              class="btn btn-sm btn-secondary"
              :disabled="isLoadingImpact"
              @click="fetchSubjectImpact"
            >
              {{
                isLoadingImpact ? "Refreshing API..." : "🔄 Refresh Impact Data"
              }}
            </button>
            <span class="subject-total-counter"
              >Total In-Flight Cohort:
              <strong>{{ activeSubjectCount }} Subjects</strong></span
            >
          </div>
        </div>

        <div
          v-if="impactLoadError"
          class="load-error-state"
          role="alert"
          data-testid="impact-load-error"
        >
          Live subject-impact analysis unavailable: {{ impactLoadError }}
        </div>

        <div class="impact-metrics-grid">
          <!-- Migrated & Re-Consented -->
          <div class="metric-card metric-green">
            <div class="card-header">
              <span class="card-badge badge-green"
                >MIGRATED &amp; RE-CONSENTED</span
              >
              <span class="metric-count">{{
                impactStats.migrated.length
              }}</span>
            </div>
            <p class="metric-label">
              Subjects executing on Target Schema v{{ selectedAmendedVersion }}
            </p>
            <div class="progress-bar-container">
              <div
                class="progress-bar bar-green"
                :style="{
                  width: getPercentage(impactStats.migrated.length) + '%',
                }"
              />
            </div>
          </div>

          <!-- Pending Re-Consent -->
          <div class="metric-card metric-yellow">
            <div class="card-header">
              <span class="card-badge badge-yellow">PENDING RE-CONSENT</span>
              <span class="metric-count">{{ impactStats.pending.length }}</span>
            </div>
            <p class="metric-label">
              eCRF Data Entry Gated until ICF Signed (PRD-SUB-007)
            </p>
            <div class="progress-bar-container">
              <div
                class="progress-bar bar-yellow"
                :style="{
                  width: getPercentage(impactStats.pending.length) + '%',
                }"
              />
            </div>
          </div>

          <!-- Completed under Previous Version -->
          <div class="metric-card metric-gray">
            <div class="card-header">
              <span class="card-badge badge-gray"
                >COMPLETED UNDER PREVIOUS</span
              >
              <span class="metric-count">{{
                impactStats.completedPrev.length
              }}</span>
            </div>
            <p class="metric-label">
              Historical Visits Preserved under v{{ selectedBaseVersion }}
              Schema
            </p>
            <div class="progress-bar-container">
              <div
                class="progress-bar bar-gray"
                :style="{
                  width: getPercentage(impactStats.completedPrev.length) + '%',
                }"
              />
            </div>
          </div>
        </div>

        <!-- Subject Table Breakdown -->
        <div class="subject-table-wrapper">
          <table class="data-table">
            <thead>
              <tr>
                <th>Subject ID</th>
                <th>Site</th>
                <th>Current Status</th>
                <th>Active Protocol Tag</th>
                <th>Consent Status</th>
                <th>Data Entry Gating State</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="sub in subjectsList"
                :key="sub.id"
                :class="'row-' + sub.category"
              >
                <td class="cell-id">
                  <strong>{{ sub.id }}</strong>
                </td>
                <td>
                  <span class="site-tag">{{ sub.site_id || "SITE-101" }}</span>
                </td>
                <td>
                  <span class="state-pill">{{ sub.status }}</span>
                </td>
                <td>
                  <span class="version-tag"
                    >v{{ sub.active_protocol_version }}</span
                  >
                </td>
                <td>
                  <span :class="['consent-badge', 'badge-' + sub.consentColor]">
                    {{ sub.consentText }}
                  </span>
                </td>
                <td>
                  <span v-if="sub.isGated" class="gating-pill pill-locked">
                    🔒 Gated (Re-Consent Required)
                  </span>
                  <span v-else class="gating-pill pill-unlocked">
                    ✅ Active &amp; Projected
                  </span>
                </td>
                <td>
                  <button
                    v-if="sub.isGated"
                    class="btn btn-sm btn-action"
                    @click="openReconsentModal(sub)"
                  >
                    Clear Re-Consent Gate
                  </button>
                  <span v-else class="text-muted">Compliant</span>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Tab 2: Visual Graph & Schema Diff Visualizer -->
      <div v-if="activeTab === 'graph'" class="diff-section">
        <div
          v-if="diffLoadError"
          class="load-error-state"
          role="alert"
          data-testid="diff-load-error"
        >
          {{
            presentError(
              "Amendment diff could not be loaded: " + diffLoadError
            )
          }}. No impact summary or SoA diff is shown.
        </div>

        <!-- Amendment Impact Summary Card -->
        <div v-else class="impact-summary-header-card">
          <div class="summary-top-row">
            <div class="summary-title-group">
              <h3 class="section-title">
                📊 Protocol Amendment Impact Summary
              </h3>
              <p class="summary-subtext">
                Quantitative patient burden delta, affected schedule visits, and
                USDM schema revisions comparing v{{ selectedBaseVersion }} to
                v{{ selectedAmendedVersion }}.
              </p>
            </div>
            <div class="summary-status-badge">
              <span
                class="badge"
                :class="
                  amendmentImpact.requires_reconsent
                    ? 'badge-alert-reconsent'
                    : 'badge-ok'
                "
              >
                {{
                  amendmentImpact.requires_reconsent
                    ? "🔒 MANDATORY RE-CONSENT GATED (PRD-SUB-007)"
                    : "✅ ADMINISTRATIVE AMENDMENT"
                }}
              </span>
            </div>
          </div>

          <div class="summary-stat-chips-grid">
            <div class="stat-chip">
              <span class="chip-label">Operational Burden Delta</span>
              <strong class="chip-val chip-burden"
                >{{ amendmentImpact.burden_delta > 0 ? "+" : ""
                }}{{ amendmentImpact.burden_delta }} Index</strong
              >
            </div>
            <div class="stat-chip">
              <span class="chip-label">Affected Visits</span>
              <strong class="chip-val"
                >{{
                  amendmentImpact.affected_visits_count
                }}
                Encounter(s)</strong
              >
            </div>
            <div class="stat-chip">
              <span class="chip-label">Affected Procedures</span>
              <strong class="chip-val"
                >{{
                  amendmentImpact.affected_activities_count
                }}
                Activity(ies)</strong
              >
            </div>
            <div class="stat-chip">
              <span class="chip-label">Schema Revision Scope</span>
              <strong class="chip-val chip-scope">{{
                amendmentImpact.is_substantial
                  ? "Substantial (Clinical/Safety)"
                  : "Minor"
              }}</strong>
            </div>
          </div>

          <!-- Revision breakdown chips -->
          <div class="schema-breakdown-chips">
            <span
              v-if="amendmentImpact.schema_revisions?.encounters?.added"
              class="breakdown-chip"
            >
              ➕ {{ amendmentImpact.schema_revisions.encounters.added }} Added
              Visit(s)
            </span>
            <span
              v-if="amendmentImpact.schema_revisions?.encounters?.modified"
              class="breakdown-chip"
            >
              🔄
              {{ amendmentImpact.schema_revisions.encounters.modified }}
              Modified Visit(s)
            </span>
            <span
              v-if="amendmentImpact.schema_revisions?.activities?.added"
              class="breakdown-chip"
            >
              ➕ {{ amendmentImpact.schema_revisions.activities.added }} Added
              Procedure(s)
            </span>
            <span
              v-if="
                amendmentImpact.schema_revisions?.eligibility_criteria?.added
              "
              class="breakdown-chip"
            >
              📋
              {{ amendmentImpact.schema_revisions.eligibility_criteria.added }}
              Added Criteria
            </span>
            <span
              v-if="amendmentImpact.schema_revisions?.forms?.added"
              class="breakdown-chip"
            >
              📝 {{ amendmentImpact.schema_revisions.forms.added }} Added eCRF
              Form(s)
            </span>
          </div>
        </div>

        <!-- Multi-Layer Diff Layer Selector Tabs -->
        <div class="diff-layer-tabs">
          <button
            class="layer-tab-btn"
            :class="{ active: activeDiffLayer === 'soa' }"
            @click="activeDiffLayer = 'soa'"
          >
            🔀 USDM Graph &amp; SoA Matrix Diff
          </button>
          <button
            class="layer-tab-btn"
            :class="{ active: activeDiffLayer === 'eligibility' }"
            @click="activeDiffLayer = 'eligibility'"
          >
            📋 Eligibility Criteria Diff
          </button>
          <button
            class="layer-tab-btn"
            :class="{ active: activeDiffLayer === 'ecrf' }"
            @click="activeDiffLayer = 'ecrf'"
          >
            📝 eCRF Forms &amp; Data Capture Diff
          </button>
        </div>

        <!-- Layer 1: USDM Graph & SoA Matrix Diff -->
        <div
          v-if="activeDiffLayer === 'soa' && !diffLoadError"
          class="layer-content"
        >
          <div class="layer-header">
            <h4>Side-by-Side Schedule of Activities &amp; Graph Structure</h4>
            <div class="legend-box">
              <span class="legend-item"
                ><span class="color-dot dot-green" /> + Added
                Encounter/Activity</span
              >
              <span class="legend-item"
                ><span class="color-dot dot-yellow" /> ~ Modified
                Schedule/Assay</span
              >
              <span class="legend-item"
                ><span class="color-dot dot-red" /> - Removed Procedure</span
              >
              <span class="legend-item"
                ><span class="color-dot dot-gray" /> = Preserved Baseline</span
              >
            </div>
          </div>

          <div class="graph-diff-grid">
            <!-- Base Version Column -->
            <div class="graph-column">
              <div class="column-header base-header">
                <h4>Base Protocol Version (v{{ selectedBaseVersion }})</h4>
                <span class="badge badge-locked">LOCKED IMMUTABLE</span>
              </div>

              <div class="nodes-container">
                <div
                  v-for="item in graphDiff.baseNodes"
                  :key="item.id"
                  class="graph-node node-base"
                  :class="item.statusClass"
                >
                  <div class="node-title-row">
                    <span class="node-type-badge">{{ item.type }}</span>
                    <span class="node-name">{{ item.name }}</span>
                  </div>
                  <div class="node-details">
                    <span class="node-spec">{{ item.spec }}</span>
                    <span class="node-schedule">{{ item.schedule }}</span>
                  </div>
                </div>
              </div>
            </div>

            <!-- Comparison Separator -->
            <div class="graph-divider">
              <div class="diff-line" />
            </div>

            <!-- Amended Version Column -->
            <div class="graph-column">
              <div class="column-header amended-header">
                <h4>
                  Amended Protocol Version (v{{ selectedAmendedVersion }})
                </h4>
                <span class="badge badge-active">ACTIVE PROJECTION</span>
              </div>

              <div class="nodes-container">
                <div
                  v-for="item in graphDiff.amendedNodes"
                  :key="item.id"
                  class="graph-node"
                  :class="item.diffType"
                >
                  <div class="node-title-row">
                    <span class="node-type-badge">{{ item.type }}</span>
                    <span class="node-name">{{ item.name }}</span>
                    <span
                      class="diff-badge"
                      :class="'diff-badge-' + item.diffType"
                    >
                      {{ item.diffBadgeText }}
                    </span>
                  </div>
                  <div class="node-details">
                    <span class="node-spec">{{ item.spec }}</span>
                    <span class="node-schedule">{{ item.schedule }}</span>
                  </div>
                  <div v-if="item.deltaNote" class="delta-annotation">
                    <span class="delta-icon">ℹ️</span> {{ item.deltaNote }}
                  </div>
                </div>
              </div>
            </div>
          </div>
        </div>

        <!-- Layer 2: Eligibility Criteria Diff -->
        <div v-if="activeDiffLayer === 'eligibility'" class="layer-content">
          <div class="layer-header">
            <h4>
              Eligibility Criteria Modifications &amp; Patient Screening Rules
            </h4>
          </div>
          <div class="criteria-diff-table-wrapper">
            <table class="data-table">
              <thead>
                <tr>
                  <th>Criterion ID</th>
                  <th>Type</th>
                  <th>Baseline Criterion (v{{ selectedBaseVersion }})</th>
                  <th>Amended Criterion (v{{ selectedAmendedVersion }})</th>
                  <th>Diff Classification</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="crit in eligibilityDiffList"
                  :key="crit.id"
                  :class="'row-diff-' + crit.change_type.toLowerCase()"
                >
                  <td>
                    <strong>{{ crit.id }}</strong>
                  </td>
                  <td>
                    <span class="type-pill">{{ crit.type }}</span>
                  </td>
                  <td>
                    {{ crit.baseText || "— None (Added in Amendment) —" }}
                  </td>
                  <td>
                    <strong>{{ crit.amendedText || "— Deprecated —" }}</strong>
                  </td>
                  <td>
                    <span
                      class="diff-badge"
                      :class="
                        'diff-badge-node-' +
                        (crit.change_type === 'ADDED'
                          ? 'added'
                          : crit.change_type === 'MODIFIED'
                            ? 'modified'
                            : crit.change_type === 'REMOVED'
                              ? 'deprecated'
                              : 'unchanged')
                      "
                    >
                      {{ crit.change_type }}
                    </span>
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Layer 3: eCRF Forms Diff -->
        <div v-if="activeDiffLayer === 'ecrf'" class="layer-content">
          <div class="layer-header">
            <h4>
              eCRF Form Definitions &amp; Clinical Data Capture Schema Diff
            </h4>
          </div>
          <div class="forms-diff-grid">
            <div
              v-for="form in ecrfFormsDiffList"
              :key="form.id"
              class="form-diff-card"
              :class="'card-diff-' + form.change_type.toLowerCase()"
            >
              <div class="form-diff-card-header">
                <div class="form-header-title">
                  <span class="form-key-tag">{{ form.form_key }}</span>
                  <h5 class="form-title">
                    {{ form.name }}
                  </h5>
                </div>
                <span
                  class="diff-badge"
                  :class="
                    'diff-badge-node-' +
                    (form.change_type === 'ADDED'
                      ? 'added'
                      : form.change_type === 'MODIFIED'
                        ? 'modified'
                        : 'unchanged')
                  "
                >
                  {{ form.change_type }}
                </span>
              </div>
              <p class="form-desc">
                {{ form.description }}
              </p>
              <div v-if="form.deltaNote" class="delta-annotation">
                <span class="delta-icon">ℹ️</span> {{ form.deltaNote }}
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- MODE 2: Site Coordinator Bulk Re-Consent & Site Migration Workspace -->
    <div
      v-if="activeMode === 'coordinator'"
      class="coordinator-workspace-container"
    >
      <div class="workspace-card">
        <div class="workspace-card-header">
          <div>
            <h3 class="card-title">
              📋 Site Coordinator Bulk Re-Consent Workspace
            </h3>
            <p class="card-subtitle">
              Manage patient protocol re-consent holds, verify eConsent or
              signed paper ICF uploads, and authorize batch 21 CFR Part 11
              electronic signatures.
            </p>
          </div>
          <div class="site-badge-header">
            <span
              >Site Context: <strong>{{ selectedSiteScope }}</strong></span
            >
          </div>
        </div>

        <!-- Filter Controls Bar -->
        <div class="filter-toolbar">
          <div class="filter-item">
            <label for="site-filter-select" class="filter-label"
              >Filter by Trial Site:</label
            >
            <select
              id="site-filter-select"
              v-model="siteFilter"
              class="form-select form-select-sm"
              @change="fetchSubjectImpact"
            >
              <option value="ALL">
                All Assigned Sites ({{
                  siteOptions.map((site) => site.code).join(", ")
                }})
              </option>
              <option
                v-for="site in siteOptions"
                :key="site.code"
                :value="site.code"
              >
                {{ site.code }} ({{ site.name }})
              </option>
            </select>
          </div>

          <div class="filter-item">
            <label for="version-filter-select" class="filter-label"
              >Protocol Version:</label
            >
            <select
              id="version-filter-select"
              v-model="versionFilter"
              class="form-select form-select-sm"
            >
              <option value="ALL">All Protocol Versions</option>
              <option value="1.0.0">v1.0.0 (Legacy Schema)</option>
              <option value="2.0.0">v2.0.0 (Amended Target)</option>
            </select>
          </div>

          <div class="filter-item">
            <label for="gating-filter-select" class="filter-label"
              >Gating Status:</label
            >
            <select
              id="gating-filter-select"
              v-model="gatingFilter"
              class="form-select form-select-sm"
            >
              <option value="ALL">All Gating States</option>
              <option value="GATED">🔒 Gated (Pending Re-Consent)</option>
              <option value="UNLOCKED">✅ Unlocked (Compliant)</option>
            </select>
          </div>

          <div class="filter-item search-filter-item">
            <label for="subject-search-input" class="filter-label"
              >Search Subject ID:</label
            >
            <input
              id="subject-search-input"
              v-model="searchQuery"
              type="text"
              class="form-control form-control-sm"
              placeholder="e.g. SUBJ-101-002"
            />
          </div>
        </div>

        <!-- Cohort Table with Multi-Subject Checkboxes -->
        <div class="table-container">
          <table class="data-table bulk-table">
            <thead>
              <tr>
                <th class="col-checkbox">
                  <input
                    id="select-all-checkbox"
                    type="checkbox"
                    :checked="allGatedSelected"
                    :disabled="gatedInFilteredCount === 0"
                    @change="toggleSelectAllGated"
                  />
                </th>
                <th>Subject ID</th>
                <th>Site ID</th>
                <th>Status</th>
                <th>Active Protocol Tag</th>
                <th>Consent Status</th>
                <th>Data Entry Gating State</th>
                <th>Action</th>
              </tr>
            </thead>
            <tbody>
              <tr
                v-for="sub in filteredSubjects"
                :key="sub.id"
                :class="{
                  'row-selected': selectedSubjectIds.includes(sub.id),
                  'row-gated': sub.isGated,
                }"
              >
                <td class="col-checkbox">
                  <input
                    :id="'checkbox-sub-' + sub.id"
                    v-model="selectedSubjectIds"
                    type="checkbox"
                    :value="sub.id"
                    :disabled="!sub.isGated"
                  />
                </td>
                <td class="cell-id">
                  <strong>{{ sub.id }}</strong>
                </td>
                <td>
                  <span class="site-tag">{{ sub.site_id || "SITE-101" }}</span>
                </td>
                <td>
                  <span class="state-pill">{{ sub.status }}</span>
                </td>
                <td>
                  <span class="version-tag"
                    >v{{ sub.active_protocol_version }}</span
                  >
                </td>
                <td>
                  <span :class="['consent-badge', 'badge-' + sub.consentColor]">
                    {{ sub.consentText }}
                  </span>
                </td>
                <td>
                  <span v-if="sub.isGated" class="gating-pill pill-locked">
                    🔒 Gated (Re-Consent Required)
                  </span>
                  <span v-else class="gating-pill pill-unlocked">
                    ✅ Active &amp; Projected
                  </span>
                </td>
                <td>
                  <button
                    v-if="sub.isGated"
                    class="btn btn-sm btn-action"
                    @click="openReconsentModal(sub)"
                  >
                    Clear Re-Consent Gate
                  </button>
                  <span v-else class="text-muted">Compliant</span>
                </td>
              </tr>

              <tr v-if="filteredSubjects.length === 0">
                <td colspan="8" class="text-center empty-state-cell">
                  No subjects match the selected site or gating filters.
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>
    </div>

    <!-- Sticky Batch Selection Toolbar -->
    <div
      v-if="selectedSubjectIds.length > 0"
      id="sticky-batch-toolbar"
      class="sticky-batch-toolbar"
    >
      <div class="batch-toolbar-content">
        <div class="batch-info">
          <span class="batch-count-badge tabular-nums"
            >⚡ {{ selectedSubjectIds.length }} Subject(s) Selected</span
          >
          <span class="batch-site-scope"
            >Site Scope: <strong>{{ selectedSiteScope }}</strong></span
          >
          <span class="batch-version-target"
            >Target Protocol Version:
            <strong>v{{ selectedAmendedVersion }}</strong></span
          >
        </div>
        <div class="batch-actions">
          <button
            id="btn-clear-selection"
            class="btn btn-sm btn-secondary"
            @click="selectedSubjectIds = []"
          >
            Deselect All
          </button>
          <button
            id="btn-batch-reconsent"
            class="btn btn-primary btn-batch-action"
            @click="openBulkReconsentModal"
          >
            ✍️ Execute Bulk Re-Consent &amp; Sign (21 CFR Part 11)
          </button>
        </div>
      </div>
    </div>

    <!-- 4-Step Guided Upversioning Wizard Modal -->
    <div
      v-if="showCreateModal"
      class="modal-overlay"
      role="presentation"
      @click.self="closeWizard"
    >
      <div
        ref="createWizardModalRef"
        class="modal-card wizard-modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="amendment-wizard-title"
        tabindex="-1"
      >
        <!-- Wizard Header & Stepper -->
        <div class="modal-header wizard-header">
          <div>
            <h3 id="amendment-wizard-title" class="modal-title">
              Protocol Upversioning &amp; Amendment Wizard
            </h3>
            <p class="modal-subtitle">
              4-step guided process for study managers to publish amendments and
              target rules.
            </p>
          </div>
          <button
            type="button"
            class="modal-close"
            aria-label="Close wizard"
            @click="closeWizard"
          >
            &times;
          </button>
        </div>

        <!-- Wizard Stepper Indicators -->
        <div class="wizard-stepper">
          <div
            class="step-item"
            role="button"
            tabindex="0"
            :class="{ active: wizardStep === 1, completed: wizardStep > 1 }"
            @click="wizardStep = 1"
            @keydown.enter.prevent="wizardStep = 1"
            @keydown.space.prevent="wizardStep = 1"
          >
            <div class="step-number">1</div>
            <div class="step-label">Classification</div>
          </div>
          <div class="step-divider" />
          <div
            class="step-item"
            role="button"
            tabindex="0"
            :class="{ active: wizardStep === 2, completed: wizardStep > 2 }"
            @click="wizardStep = 2"
            @keydown.enter.prevent="wizardStep = 2"
            @keydown.space.prevent="wizardStep = 2"
          >
            <div class="step-number">2</div>
            <div class="step-label">Target Version</div>
          </div>
          <div class="step-divider" />
          <div
            class="step-item"
            role="button"
            tabindex="0"
            :class="{ active: wizardStep === 3, completed: wizardStep > 3 }"
            @click="goToWizardStep(3)"
            @keydown.enter.prevent="goToWizardStep(3)"
            @keydown.space.prevent="goToWizardStep(3)"
          >
            <div class="step-number">3</div>
            <div class="step-label">Impact Preview</div>
          </div>
          <div class="step-divider" />
          <div
            class="step-item"
            role="button"
            tabindex="0"
            :class="{ active: wizardStep === 4, completed: wizardStep > 4 }"
            @click="goToWizardStep(4)"
            @keydown.enter.prevent="goToWizardStep(4)"
            @keydown.space.prevent="goToWizardStep(4)"
          >
            <div class="step-number">4</div>
            <div class="step-label">Schema Mapping</div>
          </div>
        </div>

        <!-- Wizard Body Steps -->
        <div class="modal-body wizard-body">
          <!-- Step 1: Amendment Scope & Classification -->
          <div v-if="wizardStep === 1" class="wizard-step-content">
            <h4>Step 1: Amendment Classification &amp; Scope</h4>
            <p class="step-desc">
              Classify protocol change scope and establish GxP change rationale.
            </p>

            <div class="form-group">
              <label for="amendment-classification-select" class="form-label"
                >Amendment Classification Type:</label
              >
              <select
                id="amendment-classification-select"
                v-model="newAmendment.amendment_type"
                class="form-select"
              >
                <option value="major">
                  Major Amendment (Structural / Safety / Visit Schedule Changes)
                </option>
                <option value="minor">
                  Minor Amendment (Administrative / Clarification /
                  Non-substantive)
                </option>
              </select>
            </div>

            <div class="form-group checkbox-group">
              <label class="checkbox-label">
                <input
                  v-model="newAmendment.requires_reconsent"
                  type="checkbox"
                />
                <span
                  ><strong
                    >Mandatory Subject Re-Consent (PRD-SUB-007)</strong
                  ></span
                >
              </label>
              <small class="form-hint">
                When enabled, eCRF data entry for active in-flight participants
                is locked until signed ICF is recorded.
              </small>
            </div>

            <div
              v-if="newAmendment.requires_reconsent"
              class="form-group notification-channels-config"
              role="group"
              aria-labelledby="amendment-channels-label"
              style="
                margin-top: 12px;
                padding: 12px;
                background-color: var(--semantic-color-surface-muted);
                border: 1px solid var(--border);
                border-radius: 6px;
              "
            >
              <div
                id="amendment-channels-label"
                class="form-label"
                style="
                  font-size: 0.85rem;
                  font-weight: 600;
                  color: var(--text-main);
                "
              >
                Automated Re-Consent Notification Channels:
              </div>
              <ReconsentChannelPicker
                v-model="newAmendment.notification_channels"
                style="margin-top: 6px"
                :channels="amendmentChannels"
              />
            </div>

            <div class="form-group">
              <label for="amendment-change-reason" class="form-label"
                >GxP Justification &amp; Change Reason:</label
              >
              <textarea
                id="amendment-change-reason"
                v-model="newAmendment.change_reason"
                class="form-control text-area"
                placeholder="e.g. Protocol Amendment 2.0 introducing optional PK visit and updating dosing cohort safety rules..."
                rows="4"
              />
            </div>
          </div>

          <!-- Step 2: Target Version Selection -->
          <div v-if="wizardStep === 2" class="wizard-step-content">
            <h4>Step 2: Target Version &amp; Study Scope Selection</h4>
            <p class="step-desc">
              Select frozen baseline snapshot and establish target amended
              version tag.
            </p>

            <div class="form-group">
              <label for="amendment-target-study-id" class="form-label"
                >Target Study ID:</label
              >
              <input
                id="amendment-target-study-id"
                v-model="selectedStudyId"
                type="text"
                class="form-control"
                disabled
              />
            </div>

            <div class="form-group">
              <label for="amendment-base-frozen-version" class="form-label"
                >Base Frozen Version:</label
              >
              <input
                id="amendment-base-frozen-version"
                type="text"
                class="form-control"
                value="v1.0.0 (Approved / Locked Snapshot)"
                disabled
              />
            </div>

            <div class="form-group">
              <label for="amendment-new-target-version" class="form-label"
                >New Target Version Tag:</label
              >
              <input
                id="amendment-new-target-version"
                v-model="newAmendment.target_version"
                type="text"
                class="form-control"
                placeholder="e.g. 2.0.0"
              />
            </div>
          </div>

          <!-- Step 3: Predictive Subject Impact Preview -->
          <div v-if="wizardStep === 3" class="wizard-step-content">
            <h4>Step 3: Predictive Site &amp; Subject Impact Analysis</h4>
            <p class="step-desc">
              Calculated in real-time from live execution APIs in under 2
              seconds.
            </p>

            <div v-if="isLoadingImpact" class="loading-state">
              <span>🔄 Calculating predictive subject impact analysis...</span>
            </div>

            <div
              v-else-if="impactLoadError"
              class="load-error-state"
              role="alert"
              data-testid="wizard-impact-load-error"
            >
              Predictive subject impact could not be calculated:
              {{ impactLoadError }}
            </div>

            <div v-else class="impact-summary-box">
              <div class="impact-stat-row">
                <div class="impact-stat-card card-green">
                  <span class="stat-val">{{
                    wizardImpactData?.categories?.migrated_and_reconsented
                      ?.count || impactStats.migrated.length
                  }}</span>
                  <span class="stat-lbl">Migrated / Re-Consented</span>
                </div>
                <div class="impact-stat-card card-yellow">
                  <span class="stat-val">{{
                    wizardImpactData?.categories?.pending_reconsent?.count ||
                    impactStats.pending.length
                  }}</span>
                  <span class="stat-lbl">Pending Re-Consent Holds</span>
                </div>
                <div class="impact-stat-card card-gray">
                  <span class="stat-val">{{
                    wizardImpactData?.categories
                      ?.completed_under_previous_version?.count ||
                    impactStats.completedPrev.length
                  }}</span>
                  <span class="stat-lbl">Completed under v1.0.0</span>
                </div>
              </div>

              <div class="impact-notice">
                <span class="notice-icon">⚡</span>
                <span
                  >Active execution pipeline verified.
                  <strong>{{ impactStats.pending.length }} subject(s)</strong>
                  across trial sites will enter mandatory re-consent holds upon
                  publication.</span
                >
              </div>
            </div>
          </div>

          <!-- Step 4: Schema Mapping & Publish Confirmation -->
          <div v-if="wizardStep === 4" class="wizard-step-content">
            <h4>Step 4: Target Schema Rules &amp; Publication Confirmation</h4>
            <p class="step-desc">
              Verify structural graph projections and confirm zero-downtime
              amendment release.
            </p>

            <div class="schema-mapping-preview">
              <div class="schema-rule-box">
                <h5>
                  📋 Added &amp; Modified Visit Rules (v{{
                    newAmendment.target_version
                  }})
                </h5>
                <ul>
                  <li>
                    <strong>Visit 3.5 Interim PK Assessment:</strong> New
                    mid-cycle pharmacokinetic encounter added.
                  </li>
                  <li>
                    <strong>Standard Safety Chemistry:</strong> Added
                    high-sensitivity troponin biomarker requirement.
                  </li>
                  <li>
                    <strong>Historical Record Rule:</strong> In-flight subject
                    historical visits remain immutable read-only records under
                    v1.0.0 schema.
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>

        <!-- Wizard Footer -->
        <div class="modal-footer wizard-footer">
          <button
            v-if="wizardStep > 1"
            class="btn btn-secondary"
            :disabled="isSubmitting"
            @click="wizardStep--"
          >
            ← Back
          </button>
          <button class="btn btn-secondary" @click="closeWizard">Cancel</button>
          <button
            v-if="wizardStep < 4"
            class="btn btn-primary"
            :disabled="wizardStep === 1 && !newAmendment.change_reason.trim()"
            @click="goToWizardStep(wizardStep + 1)"
          >
            Next Step →
          </button>
          <button
            v-if="wizardStep === 4"
            id="btn-publish-amendment"
            class="btn btn-primary"
            :disabled="isSubmitting"
            @click="submitCreateAmendment"
          >
            {{
              isSubmitting
                ? "Publishing Target Schema..."
                : "🚀 Publish Protocol Amendment & Apply Target Rules"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- 21 CFR Part 11 Bulk Signature Modal -->
    <div
      v-if="showBulkReconsentModal"
      id="bulk-reconsent-modal"
      class="modal-overlay"
      role="presentation"
      @click.self="showBulkReconsentModal = false"
    >
      <div
        ref="bulkReconsentModalRef"
        class="modal-card bulk-reconsent-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="bulk-reconsent-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="bulk-reconsent-modal-title" class="modal-title">
            Authorize Bulk Re-Consent Signatures (21 CFR Part 11)
          </h3>
          <button
            type="button"
            class="modal-close"
            aria-label="Close bulk re-consent modal"
            @click="showBulkReconsentModal = false"
          >
            &times;
          </button>
        </div>

        <div class="modal-body">
          <p class="modal-intro">
            You are authorizing batch protocol re-consent sign-off for
            <strong>{{ selectedSubjectIds.length }} subject(s)</strong>:
            <span class="subject-tags-inline">{{
              selectedSubjectIds.join(", ")
            }}</span>.
          </p>

          <div class="reconsent-options">
            <div
              class="option-card"
              role="button"
              tabindex="0"
              :aria-pressed="bulkReconsentForm.signature_type === 'ECONSENT'"
              :class="{
                selected: bulkReconsentForm.signature_type === 'ECONSENT',
              }"
              @click="bulkReconsentForm.signature_type = 'ECONSENT'"
              @keydown.enter.prevent="
                bulkReconsentForm.signature_type = 'ECONSENT'
              "
              @keydown.space.prevent="
                bulkReconsentForm.signature_type = 'ECONSENT'
              "
            >
              <h4>✍️ Electronic Consent (eConsent)</h4>
              <p>Cryptographic 21 CFR Part 11 electronic signature record.</p>
            </div>
            <div
              class="option-card"
              role="button"
              tabindex="0"
              :aria-pressed="
                bulkReconsentForm.signature_type === 'PAPER_UPLOAD'
              "
              :class="{
                selected: bulkReconsentForm.signature_type === 'PAPER_UPLOAD',
              }"
              @click="bulkReconsentForm.signature_type = 'PAPER_UPLOAD'"
              @keydown.enter.prevent="
                bulkReconsentForm.signature_type = 'PAPER_UPLOAD'
              "
              @keydown.space.prevent="
                bulkReconsentForm.signature_type = 'PAPER_UPLOAD'
              "
            >
              <h4>📄 Verified Paper ICF Upload</h4>
              <p>
                Site PI verified paper ICF signed and filed in investigator
                binder.
              </p>
            </div>
          </div>

          <div class="form-group margin-top-md">
            <label for="bulk-signer-name" class="form-label"
              >Site Staff Signer Name / User ID:</label
            >
            <input
              id="bulk-signer-name"
              v-model="bulkReconsentForm.signer_name"
              type="text"
              class="form-control"
              placeholder="e.g. crc.user"
            />
          </div>

          <div class="form-group">
            <label for="bulk-signer-pin" class="form-label"
              >Re-authentication Password / PIN:</label
            >
            <input
              id="bulk-signer-pin"
              v-model="bulkReconsentForm.signer_pin"
              type="password"
              class="form-control"
              placeholder="••••"
            />
          </div>

          <div class="form-group">
            <label for="bulk-reason-for-change" class="form-label"
              >GxP Audit Justification &amp; Change Reason:</label
            >
            <input
              id="bulk-reason-for-change"
              v-model="bulkReconsentForm.reason_for_change"
              type="text"
              class="form-control"
              placeholder="e.g. Bulk protocol amendment re-consent verification"
            />
          </div>
        </div>

        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showBulkReconsentModal = false"
          >
            Cancel
          </button>
          <button
            id="btn-submit-bulk-signature"
            type="button"
            class="btn btn-primary"
            :disabled="!bulkReconsentForm.signer_name.trim() || isSubmitting"
            @click="executeBulkReconsent"
          >
            {{
              isSubmitting
                ? "Authorizing Batch Signatures..."
                : "Confirm & Authorize Bulk Signatures"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- Single Subject Re-Consent Resolution Modal -->
    <div
      v-if="showReconsentModal"
      class="modal-overlay"
      role="presentation"
      @click.self="showReconsentModal = false"
    >
      <div
        ref="singleReconsentModalRef"
        class="modal-card"
        role="dialog"
        aria-modal="true"
        aria-labelledby="single-reconsent-modal-title"
        tabindex="-1"
      >
        <div class="modal-header">
          <h3 id="single-reconsent-modal-title" class="modal-title">
            Clear Subject Re-Consent Gate (PRD-SUB-007)
          </h3>
          <button
            type="button"
            class="modal-close"
            aria-label="Close re-consent modal"
            @click="showReconsentModal = false"
          >
            &times;
          </button>
        </div>
        <div class="modal-body">
          <p>
            Subject <strong>{{ activeModalSubject?.id }}</strong> is currently
            locked from data entry on upcoming visits under Protocol Amendment
            <strong>v{{ selectedAmendedVersion }}</strong
            >.
          </p>
          <div class="reconsent-options">
            <div
              class="option-card"
              role="button"
              tabindex="0"
              :aria-pressed="reconsentMode === 'ECONSENT'"
              :class="{ selected: reconsentMode === 'ECONSENT' }"
              @click="reconsentMode = 'ECONSENT'"
              @keydown.enter.prevent="reconsentMode = 'ECONSENT'"
              @keydown.space.prevent="reconsentMode = 'ECONSENT'"
            >
              <h4>✍️ Execute eConsent</h4>
              <p>
                Register electronic signature verified with 21 CFR Part 11
                cryptographic seal.
              </p>
            </div>
            <div
              class="option-card"
              role="button"
              tabindex="0"
              :aria-pressed="reconsentMode === 'PAPER'"
              :class="{ selected: reconsentMode === 'PAPER' }"
              @click="reconsentMode = 'PAPER'"
              @keydown.enter.prevent="reconsentMode = 'PAPER'"
              @keydown.space.prevent="reconsentMode = 'PAPER'"
            >
              <h4>📄 Upload Signed Paper ICF</h4>
              <p>
                Record site PI verified paper ICF signed and dated by the
                patient.
              </p>
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button
            type="button"
            class="btn btn-secondary"
            @click="showReconsentModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="isSubmitting"
            @click="submitReconsent"
          >
            {{
              isSubmitting
                ? "Registering &amp; Unlocking..."
                : "Register Signed Consent &amp; Unlock eCRF"
            }}
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import ReconsentChannelPicker from "../components/econsent/ReconsentChannelPicker.vue";
import { ref, computed, onMounted } from "vue";
import { DEMO_SITES } from "demo-fixtures";
import { ClinicalIconButton, useFocusTrap } from "ui";
import { apiClient } from "../api/apiClient";
import { designerService } from "../api/designer";
import { useClinicalStore } from "../stores/clinical";
import { useToastStore } from "../stores/toasts.js";
import { useErrorPresenter } from "../composables/useErrorPresenter";

const amendmentChannels = [
  { value: "SMS", label: "SMS Alerts" },
  { value: "Mobile Push", label: "Mobile Push" },
  { value: "Email", label: "Email" },
];

const store = useClinicalStore();
const clinicalStore = store;
const toasts = useToastStore();
const { presentError } = useErrorPresenter();

// Mode & Tab Navigation State
const activeMode = ref("manager"); // 'manager' | 'coordinator'
const activeTab = ref("dashboard"); // 'dashboard' | 'graph' | 'wizard'
const activeDiffLayer = ref("soa"); // 'soa' | 'eligibility' | 'ecrf'

// Protocol Version & Study State
const selectedStudyId = ref("CADENCE-101");
const selectedBaseVersion = ref("1.0.0");
const selectedAmendedVersion = ref("2.0.0");
const requiresReconsent = ref(true);
const isSubmitting = ref(false);
const isLoadingImpact = ref(false);
const isLoadingDiff = ref(false);
const notificationBanner = ref(null);
// #4523: set when the live subject-impact / amendment-diff calls fail, so seeded figures are never shown as
// the verified server analysis.
const impactLoadError = ref(null);
const diffLoadError = ref(null);

// Wizard State
const showCreateModal = ref(false);
const wizardStep = ref(1);
const wizardImpactData = ref(null);
const newAmendment = ref({
  amendment_type: "major",
  requires_reconsent: true,
  notification_channels: ["SMS", "Mobile Push", "Email"],
  change_reason:
    "Protocol Amendment 2.0 introducing optional PK visit and updating dosing cohort safety rules",
  target_version: "2.0.0",
});

// Bulk Workspace & Filters State
const siteFilter = ref("ALL");
const versionFilter = ref("ALL");
const gatingFilter = ref("ALL");
const searchQuery = ref("");
const selectedSubjectIds = ref([]);
const showBulkReconsentModal = ref(false);
const bulkReconsentForm = ref({
  signature_type: "ECONSENT",
  signer_name: "crc.user",
  signer_pin: "1234",
  reason_for_change: "Bulk protocol amendment re-consent verification",
});

// Single Reconsent Modal
const showReconsentModal = ref(false);
const reconsentMode = ref("ECONSENT");
const activeModalSubject = ref(null);

const createWizardModalRef = ref(null);
const bulkReconsentModalRef = ref(null);
const singleReconsentModalRef = ref(null);

useFocusTrap(createWizardModalRef, {
  isOpen: () => showCreateModal.value,
  onEscape: () => closeWizard(),
});

useFocusTrap(bulkReconsentModalRef, {
  isOpen: () => showBulkReconsentModal.value,
  onEscape: () => {
    showBulkReconsentModal.value = false;
  },
});

useFocusTrap(singleReconsentModalRef, {
  isOpen: () => showReconsentModal.value,
  onEscape: () => {
    showReconsentModal.value = false;
  },
});

const siteOptions = computed(() =>
  DEMO_SITES.map((site) => ({ code: site.code, name: site.name }))
);

// Cohort Subjects Data
const subjectsList = computed(() => store.subjects);

// Computed Metrics
const activeSubjectCount = computed(() => subjectsList.value.length);

const impactStats = computed(() => {
  return {
    migrated: subjectsList.value.filter((s) => s.category === "migrated"),
    pending: subjectsList.value.filter((s) => s.category === "pending"),
    completedPrev: subjectsList.value.filter(
      (s) => s.category === "completedPrev"
    ),
  };
});

const gatedSubjectCount = computed(() => {
  return subjectsList.value.filter((s) => s.isGated).length;
});

const selectedSiteScope = computed(() => {
  if (siteFilter.value === "ALL") return "All Assigned Trial Sites";
  return siteFilter.value;
});

// Filtered Subjects for Bulk Workspace
const filteredSubjects = computed(() => {
  return subjectsList.value.filter((sub) => {
    const subjectSite = sub.siteId || sub.site_id;
    if (siteFilter.value !== "ALL" && subjectSite !== siteFilter.value) {
      return false;
    }
    if (
      versionFilter.value !== "ALL" &&
      sub.active_protocol_version !== versionFilter.value
    ) {
      return false;
    }
    if (gatingFilter.value === "GATED" && !sub.isGated) {
      return false;
    }
    if (gatingFilter.value === "UNLOCKED" && sub.isGated) {
      return false;
    }
    if (
      searchQuery.value.trim() &&
      !sub.id.toLowerCase().includes(searchQuery.value.toLowerCase())
    ) {
      return false;
    }
    return true;
  });
});

const gatedInFilteredCount = computed(() => {
  return filteredSubjects.value.filter((s) => s.isGated).length;
});

const allGatedSelected = computed(() => {
  const gatedIds = filteredSubjects.value
    .filter((s) => s.isGated)
    .map((s) => s.id);
  if (gatedIds.length === 0) return false;
  return gatedIds.every((id) => selectedSubjectIds.value.includes(id));
});

function toggleSelectAllGated() {
  const gatedIds = filteredSubjects.value
    .filter((s) => s.isGated)
    .map((s) => s.id);
  if (allGatedSelected.value) {
    selectedSubjectIds.value = selectedSubjectIds.value.filter(
      (id) => !gatedIds.includes(id)
    );
  } else {
    const combined = new Set([...selectedSubjectIds.value, ...gatedIds]);
    selectedSubjectIds.value = Array.from(combined);
  }
}

function getPercentage(count) {
  if (!activeSubjectCount.value) return 0;
  return Math.round((count / activeSubjectCount.value) * 100);
}

// Amendment Impact Summary Reactive Object
const amendmentImpact = ref({
  base_version: "1.0.0",
  amended_version: "2.0.0",
  burden_delta: 2.0,
  affected_visits_count: 2,
  affected_visits: [
    "Visit 3: Treatment Cycle 1",
    "Visit 3.5: Interim PK Assessment",
  ],
  affected_activities_count: 2,
  affected_activities: ["Standard Safety Chemistry", "PK Blood Draw"],
  schema_revisions: {
    encounters: { added: 1, modified: 1, removed: 0, unchanged: 2 },
    activities: { added: 1, modified: 1, removed: 0, unchanged: 0 },
    eligibility_criteria: { added: 1, modified: 1, removed: 0, unchanged: 1 },
    forms: { added: 1, modified: 0, removed: 0, unchanged: 1 },
  },
  is_substantial: true,
  requires_reconsent: true,
  estimated_cost_usd: 7600.0,
  narrative_summary:
    "Protocol Amendment 2.0 introducing mid-cycle PK visit and troponin biomarker.",
});

// Graph Diff Nodes Data
const graphDiff = ref({
  baseNodes: [
    {
      id: "b-arm1",
      type: "Study Arm",
      name: "Arm A: Active Dose",
      spec: "Cohort: 100mg Daily",
      schedule: "4 Epochs",
      statusClass: "node-unchanged",
    },
    {
      id: "b-v1",
      type: "Visit Encounter",
      name: "Visit 1: Screening",
      spec: "eCRF Forms: Demographics, Eligibility",
      schedule: "Day -7",
      statusClass: "node-unchanged",
    },
    {
      id: "b-v2",
      type: "Visit Encounter",
      name: "Visit 2: Baseline",
      spec: "eCRF Forms: Vitals, ECG, Labs",
      schedule: "Day 1",
      statusClass: "node-unchanged",
    },
    {
      id: "b-v3",
      type: "Visit Encounter",
      name: "Visit 3: Treatment Cycle 1",
      spec: "eCRF Forms: Dosing, Safety Labs",
      schedule: "Day 14",
      statusClass: "node-unchanged",
    },
    {
      id: "b-act1",
      type: "Procedure",
      name: "Standard Safety Chemistry",
      spec: "Assay: CBC + Chem Panel",
      schedule: "Bi-weekly",
      statusClass: "node-unchanged",
    },
  ],
  amendedNodes: [
    {
      id: "a-arm1",
      type: "Study Arm",
      name: "Arm A: Active Dose",
      spec: "Cohort: 100mg Daily",
      schedule: "4 Epochs",
      diffType: "node-unchanged",
      diffBadgeText: "Preserved",
    },
    {
      id: "a-v1",
      type: "Visit Encounter",
      name: "Visit 1: Screening",
      spec: "eCRF Forms: Demographics, Eligibility",
      schedule: "Day -7",
      diffType: "node-unchanged",
      diffBadgeText: "Preserved",
    },
    {
      id: "a-v2",
      type: "Visit Encounter",
      name: "Visit 2: Baseline",
      spec: "eCRF Forms: Vitals, ECG, Labs",
      schedule: "Day 1",
      diffType: "node-unchanged",
      diffBadgeText: "Preserved",
    },
    {
      id: "a-v3",
      type: "Visit Encounter",
      name: "Visit 3: Treatment Cycle 1",
      spec: "eCRF Forms: Dosing, Safety Labs, PK Blood Draw",
      schedule: "Day 14",
      diffType: "node-modified",
      diffBadgeText: "~ Modified",
      deltaNote:
        "Added PK Blood Draw form and expanded safety lab range criteria.",
    },
    {
      id: "a-v4",
      type: "Visit Encounter",
      name: "Visit 3.5: Interim PK Assessment",
      spec: "eCRF Forms: Pharmacokinetics, Biomarkers",
      schedule: "Day 21",
      diffType: "node-added",
      diffBadgeText: "+ Added",
      deltaNote: "New mid-cycle pharmacokinetic visit added in Amendment 2.0.",
    },
    {
      id: "a-act1",
      type: "Procedure",
      name: "Standard Safety Chemistry",
      spec: "Assay: CBC + Chem Panel + Biomarkers",
      schedule: "Bi-weekly",
      diffType: "node-modified",
      diffBadgeText: "~ Modified",
      deltaNote: "Added high-sensitivity troponin biomarker requirement.",
    },
    {
      id: "a-act2",
      type: "Procedure",
      name: "PK Blood Draw",
      spec: "Pharmacokinetics Plasma Assay",
      schedule: "Visit 3, Visit 3.5",
      diffType: "node-added",
      diffBadgeText: "+ Added",
      deltaNote: "New pharmacokinetic blood sampling procedure.",
    },
  ],
});

// Eligibility Criteria Diff Data
const eligibilityDiffList = ref([
  {
    id: "crit_01",
    type: "Inclusion",
    baseText: "Age >= 18",
    amendedText: "Age >= 18 and Age <= 75",
    change_type: "MODIFIED",
  },
  {
    id: "crit_02",
    type: "Inclusion",
    baseText: "Confirmed solid tumor diagnosis",
    amendedText: "Confirmed solid tumor diagnosis",
    change_type: "UNCHANGED",
  },
  {
    id: "crit_03",
    type: "Inclusion",
    baseText: null,
    amendedText: "Signed informed consent (v2.0 with PK biomarker addendum)",
    change_type: "ADDED",
  },
]);

// eCRF Forms Diff Data
const ecrfFormsDiffList = ref([
  {
    id: "f_demo",
    form_key: "DEMO",
    name: "Demographics & Baseline Characteristics",
    description: "Subject demographic info, birth date, race, ethnicity.",
    change_type: "UNCHANGED",
  },
  {
    id: "f_pk",
    form_key: "PK_ASSAY",
    name: "Pharmacokinetics Blood Sampling (eCRF)",
    description:
      "PK plasma blood draw timing, tube barcoding, lab accession numbers.",
    change_type: "ADDED",
    deltaNote:
      "New CRF form mandated for Visit 3 and Visit 3.5 under Amendment 2.0.",
  },
  {
    id: "f_safety",
    form_key: "LAB_SAFETY",
    name: "Safety Laboratory Panel",
    description: "Hematology, Chemistry, and Troponin biomarkers.",
    change_type: "MODIFIED",
    deltaNote: "Added Troponin high-sensitivity quantitative field.",
  },
]);

// Live API Integration Functions
function toastMessage(toastId) {
  return (
    toasts.toasts.find((t) => t.id === toastId)?.message || "Request failed"
  );
}

function reportFailure(title, err) {
  const id = toasts.apiError(title, err);
  notificationBanner.value = {
    type: "error",
    message: `${title}: ${toastMessage(id)}`,
  };
}

async function fetchSubjectImpact() {
  isLoadingImpact.value = true;
  try {
    const siteQuery =
      siteFilter.value !== "ALL" ? `&site_id=${siteFilter.value}` : "";
    const res = await apiClient.get(
      `/api/v1/execution/amendments/${selectedStudyId.value}/subject-impact?target_version=${selectedAmendedVersion.value}${siteQuery}`
    );
    if (res && res.categories) {
      wizardImpactData.value = res;
    }
    impactLoadError.value = null;
  } catch (err) {
    // A superseded duplicate request is aborted by apiClient; that is not a failure.
    if (err?.name === "AbortError") return;
    wizardImpactData.value = null;
    impactLoadError.value = toastMessage(
      toasts.apiError("Subject impact analysis could not be loaded", err)
    );
  } finally {
    isLoadingImpact.value = false;
  }
}

async function fetchAmendmentDiffAndImpact() {
  isLoadingDiff.value = true;
  try {
    const diffRes = await designerService.fetchAmendmentDiff({
      study_id: selectedStudyId.value,
      base_version_tag: selectedBaseVersion.value,
      amended_version_tag: selectedAmendedVersion.value,
    });
    if (diffRes && diffRes.impact_summary) {
      amendmentImpact.value = diffRes.impact_summary;
    }
    if (
      diffRes &&
      diffRes.soa_matrix_diffs &&
      diffRes.soa_matrix_diffs.length > 0
    ) {
      // Map API response to amended nodes
      const amendedFromApi = diffRes.soa_matrix_diffs.map((d) => ({
        id: `a-${d.entity_id}`,
        type: d.entity_type === "Encounter" ? "Visit Encounter" : "Procedure",
        name: d.name,
        spec: d.spec || "Assigned Protocol Scope",
        schedule: d.schedule || "Scheduled",
        diffType:
          d.change_type === "ADDED"
            ? "node-added"
            : d.change_type === "MODIFIED"
              ? "node-modified"
              : d.change_type === "REMOVED"
                ? "node-deprecated"
                : "node-unchanged",
        diffBadgeText:
          d.change_type === "ADDED"
            ? "+ Added"
            : d.change_type === "MODIFIED"
              ? "~ Modified"
              : d.change_type === "REMOVED"
                ? "- Deprecated"
                : "Preserved",
        deltaNote: d.delta_note,
      }));
      if (amendedFromApi.length > 0) {
        graphDiff.value.amendedNodes = amendedFromApi;
      }
    }
    diffLoadError.value = null;
  } catch (err) {
    diffLoadError.value = toastMessage(
      toasts.apiError("Amendment diff could not be loaded", err)
    );
  } finally {
    isLoadingDiff.value = false;
  }
}

function handleVersionChange() {
  fetchSubjectImpact();
  fetchAmendmentDiffAndImpact();
}

// Wizard Controls
function openWizard() {
  activeMode.value = "manager";
  activeTab.value = "wizard";
  showCreateModal.value = true;
  wizardStep.value = 1;
  fetchSubjectImpact();
  fetchAmendmentDiffAndImpact();
}

function closeWizard() {
  showCreateModal.value = false;
  wizardStep.value = 1;
}

function goToWizardStep(step) {
  wizardStep.value = step;
  if (step === 3) {
    fetchSubjectImpact();
    fetchAmendmentDiffAndImpact();
  }
}

async function submitCreateAmendment() {
  isSubmitting.value = true;
  try {
    // 1. Branch in Designer API
    await designerService.createAmendmentBranch({
      study_id: selectedStudyId.value,
      base_version_tag: selectedBaseVersion.value,
      amendment_type: newAmendment.value.amendment_type,
      requires_reconsent: newAmendment.value.requires_reconsent,
      change_reason: newAmendment.value.change_reason,
      branch_name: `amendment-v${newAmendment.value.target_version}-draft`,
    });

    // 2. Publish in Execution API
    const payload = {
      study_id: selectedStudyId.value,
      version_number: newAmendment.value.target_version,
      description: newAmendment.value.change_reason,
      baseline_snapshot: { version: selectedBaseVersion.value },
      amended_snapshot: { version: newAmendment.value.target_version },
      requires_reconsent: newAmendment.value.requires_reconsent,
      channels: newAmendment.value.notification_channels,
    };

    await apiClient.post("/api/v1/execution/amendments/publish", payload);

    selectedAmendedVersion.value = newAmendment.value.target_version;
    requiresReconsent.value = newAmendment.value.requires_reconsent;

    notificationBanner.value = {
      type: "success",
      message: newAmendment.value.requires_reconsent
        ? `Protocol Amendment v${newAmendment.value.target_version} published! Multi-channel (${(newAmendment.value.notification_channels || []).join(", ")}) notifications dispatched for re-consent.`
        : `Protocol Amendment v${newAmendment.value.target_version} published successfully! Target schema rules applied.`,
    };

    closeWizard();
    await fetchSubjectImpact();
    await fetchAmendmentDiffAndImpact();
  } catch (err) {
    // #4523: a failed branch or publish leaves the version, gating flag and wizard untouched.
    reportFailure("Protocol amendment publication failed", err);
  } finally {
    isSubmitting.value = false;
  }
}

// Single Re-Consent Action
function openReconsentModal(subject) {
  activeModalSubject.value = subject;
  showReconsentModal.value = true;
}

async function submitReconsent() {
  if (!activeModalSubject.value) return;
  isSubmitting.value = true;
  try {
    await apiClient.post("/api/v1/execution/amendments/reconsent", {
      subject_id: activeModalSubject.value.id,
      study_id: selectedStudyId.value,
      protocol_version: selectedAmendedVersion.value,
      version_index: 2,
      icf_signed: true,
      signature_type: reconsentMode.value,
    });

    await store.clearReconsentGate(
      activeModalSubject.value.id,
      reconsentMode.value,
      `Subject ${activeModalSubject.value.id} re-consent recorded via ${reconsentMode.value} in Amendment Management. Gating unlocked.`
    );

    notificationBanner.value = {
      type: "success",
      message: `Re-consent cleared for ${activeModalSubject.value.id}. eCRF gating unlocked.`,
    };

    showReconsentModal.value = false;
  } catch (err) {
    // #4523: the re-consent gate stays locked unless the execution service recorded the signature.
    reportFailure(
      `Re-consent for ${activeModalSubject.value.id} was not recorded`,
      err
    );
  } finally {
    isSubmitting.value = false;
  }
}

// Bulk Re-Consent Actions
function openBulkReconsentModal() {
  showBulkReconsentModal.value = true;
}

async function executeBulkReconsent() {
  if (selectedSubjectIds.value.length === 0) return;
  isSubmitting.value = true;
  try {
    const payload = {
      subject_ids: selectedSubjectIds.value,
      study_id: selectedStudyId.value,
      protocol_version: selectedAmendedVersion.value,
      version_index: 2,
      icf_signed: true,
      signature_type: bulkReconsentForm.value.signature_type,
      signer_name: bulkReconsentForm.value.signer_name,
      reason_for_change: bulkReconsentForm.value.reason_for_change,
    };

    await apiClient.post(
      "/api/v1/execution/amendments/bulk-reconsent",
      payload
    );

    // Update local state for all selected subjects
    subjectsList.value.forEach((sub) => {
      if (selectedSubjectIds.value.includes(sub.id)) {
        sub.active_protocol_version = selectedAmendedVersion.value;
        sub.consentText = `Signed ICF v${selectedAmendedVersion.value}`;
        sub.consentColor = "green";
        sub.category = "migrated";
        sub.isGated = false;
      }
    });

    if (clinicalStore && clinicalStore.addLedgerBlock) {
      try {
        await clinicalStore.addLedgerBlock(
          "BULK_RECONSENT_EXECUTED",
          {
            subject_ids: selectedSubjectIds.value,
            protocol_version: selectedAmendedVersion.value,
            signature_type: bulkReconsentForm.value.signature_type,
            signer_name: bulkReconsentForm.value.signer_name,
          },
          `Batch Part 11 signature authorized for ${selectedSubjectIds.value.length} subject(s). Gating unlocked across active sites.`
        );
      } catch (err) {
        console.warn(
          "Ledger block generation error during bulk reconsent:",
          err
        );
      }
    }

    const subjectCount = selectedSubjectIds.value.length;
    selectedSubjectIds.value = [];
    showBulkReconsentModal.value = false;

    notificationBanner.value = {
      type: "success",
      message: `Batch 21 CFR Part 11 signature authorized! Cleared re-consent holds for ${subjectCount} subject(s) across active sites.`,
    };
  } catch (err) {
    // #4523: no subject is marked signed and no ledger block is written when the bulk call fails.
    reportFailure("Bulk re-consent was not recorded", err);
  } finally {
    isSubmitting.value = false;
  }
}

onMounted(() => {
  fetchSubjectImpact();
  fetchAmendmentDiffAndImpact();
});
</script>

<style scoped>
.amendment-view-container {
  padding: 1.5rem 2rem;
  max-width: 100%;
  box-sizing: border-box;
  color: var(--semantic-color-text);
  position: relative;
}

.notification-banner {
  display: flex;
  align-items: center;
  gap: 0.75rem;
  padding: 0.85rem 1.25rem;
  border-radius: var(--radius-md, 8px);
  margin-bottom: 1.25rem;
  font-weight: 600;
  font-size: 0.925rem;
}

.banner-success {
  background: var(--semantic-color-success-bg);
  border: 1px solid var(--semantic-color-success-border);
  color: var(--semantic-color-success-dark);
}

.banner-error {
  background: var(--semantic-color-error-bg);
  border: 1px solid var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
}

.load-error-state {
  margin: 0 0 16px;
  padding: 12px 16px;
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 8px;
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error);
  font-size: 0.875rem;
}

.banner-close {
  margin-left: auto;
  background: none;
  border: none;
  font-size: 1.25rem;
  cursor: pointer;
  color: inherit;
}

.view-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 1.25rem;
  padding-bottom: 1rem;
  border-bottom: 1px solid var(--border);
}

.title-row {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.view-title {
  font-size: 1.5rem;
  font-weight: 700;
  margin: 0;
}

.view-description {
  color: var(--semantic-color-text-secondary);
  margin-top: 0.25rem;
  font-size: 0.925rem;
  max-width: 900px;
}

/* Persona Mode Navigation Switcher */
.mode-navigation-bar {
  display: flex;
  gap: 0.75rem;
  margin-bottom: 1.25rem;
}

.mode-nav-btn {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  padding: 0.65rem 1.25rem;
  border: 1px solid var(--border);
  background: var(--semantic-color-surface);
  border-radius: var(--radius-md, 8px);
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
}

.mode-nav-btn.active {
  background: var(--primary);
  color: var(--semantic-color-surface);
  border-color: var(--primary);
  box-shadow: var(--shadow-sm);
}

.nav-pill-badge {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
  font-size: 0.75rem;
  padding: 0.15rem 0.5rem;
  border-radius: 12px;
  font-weight: 700;
  margin-left: 0.35rem;
}

.controls-panel {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 1rem 1.25rem;
  margin-bottom: 1.5rem;
}

.version-selectors {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.selector-group {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.selector-label {
  font-size: 0.8rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--semantic-color-text-secondary);
}

.diff-arrow {
  font-size: 1.25rem;
  color: var(--text-main);
  margin-top: 1rem;
}

.form-select,
.form-control {
  padding: 0.5rem 0.75rem;
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  background: var(--semantic-color-surface);
  font-size: 0.9rem;
}

.form-select-sm,
.form-control-sm {
  padding: 0.35rem 0.6rem;
  font-size: 0.825rem;
}

.version-meta-tags {
  display: flex;
  gap: 1rem;
}

.meta-tag {
  font-size: 0.85rem;
  padding: 0.4rem 0.75rem;
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
}

.status-alert {
  color: var(--semantic-color-error);
  font-weight: 700;
}

.status-ok {
  color: var(--semantic-color-success);
  font-weight: 700;
}

.status-immutable {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

/* Workspace Tabs Bar */
.workspace-tabs-bar {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1rem;
  border-bottom: 2px solid var(--border);
}

.tab-btn {
  padding: 0.6rem 1.2rem;
  border: none;
  background: none;
  font-size: 0.9rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  border-bottom: 3px solid transparent;
  margin-bottom: -2px;
}

.tab-btn.active {
  color: var(--primary);
  border-bottom-color: var(--primary);
}

/* Dashboard Section */
.dashboard-section,
.diff-section,
.workspace-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 1.25rem;
  margin-bottom: 1.75rem;
}

.section-header,
.workspace-card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  padding-bottom: 0.75rem;
  border-bottom: 1px solid var(--border);
}

.section-title,
.card-title {
  font-size: 1.15rem;
  font-weight: 600;
  margin: 0;
}

.card-subtitle {
  color: var(--semantic-color-text-secondary);
  font-size: 0.875rem;
  margin: 0.25rem 0 0 0;
}

.header-right-actions {
  display: flex;
  align-items: center;
  gap: 1rem;
}

.subject-total-counter {
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
}

.impact-metrics-grid {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 1rem;
  margin-bottom: 1.25rem;
}

.metric-card {
  padding: 1rem;
  border-radius: var(--radius-md, 8px);
  border: 1px solid var(--border);
}

.metric-green {
  background: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border-soft);
}

.metric-yellow {
  background: var(--semantic-color-warning-bg-light);
  border-color: var(--semantic-color-warning-border);
}

.metric-gray {
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
}

.card-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.5rem;
}

.card-badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.badge-green {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}
.badge-yellow {
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}
.badge-gray {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.metric-count {
  font-size: 1.5rem;
  font-weight: 700;
}

.metric-label {
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
  margin-bottom: 0.75rem;
}

.progress-bar-container {
  height: 6px;
  background: var(--semantic-color-surface-header);
  border-radius: 3px;
  overflow: hidden;
}

.progress-bar {
  height: 100%;
  transition: width 0.3s ease;
}
.bar-green {
  background: var(--semantic-color-success);
}
.bar-yellow {
  background: var(--semantic-color-warning-action);
}
.bar-gray {
  background: var(--semantic-color-border-control);
}

/* Filter Toolbar */
.filter-toolbar {
  display: flex;
  gap: 1rem;
  align-items: flex-end;
  background: var(--semantic-color-surface-header);
  padding: 0.85rem 1rem;
  border-radius: var(--radius-sm, 6px);
  border: 1px solid var(--border);
  margin-bottom: 1.25rem;
  flex-wrap: wrap;
}

.filter-item {
  display: flex;
  flex-direction: column;
  gap: 0.25rem;
}

.filter-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.search-filter-item {
  flex-grow: 1;
}

/* Data Table */
.subject-table-wrapper,
.table-container {
  overflow-x: auto;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.9rem;
}

.data-table th,
.data-table td {
  padding: 0.75rem 1rem;
  text-align: left;
  border-bottom: 1px solid var(--border);
}

.data-table th {
  background: var(--semantic-color-surface-header);
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.col-checkbox {
  width: 40px;
  text-align: center;
}

.site-tag {
  font-size: 0.8rem;
  font-weight: 700;
  padding: 0.2rem 0.5rem;
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
  border-radius: 4px;
}

.state-pill {
  font-size: 0.75rem;
  padding: 0.2rem 0.5rem;
  background: var(--semantic-color-surface-header);
  border-radius: 4px;
  font-weight: 600;
}

.version-tag {
  font-weight: 600;
  color: var(--text-main);
}

.consent-badge {
  font-size: 0.8rem;
  font-weight: 600;
  padding: 0.2rem 0.5rem;
  border-radius: 4px;
}

.gating-pill {
  font-size: 0.8rem;
  font-weight: 600;
  padding: 0.25rem 0.6rem;
  border-radius: 4px;
}

.pill-locked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.pill-unlocked {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.row-selected {
  background: var(--semantic-color-info-bg) !important;
}

.row-gated {
  background: var(--semantic-color-warning-bg-soft);
}

.empty-state-cell {
  padding: 2rem;
  color: var(--semantic-color-text-secondary);
}

/* Sticky Batch Toolbar */
.sticky-batch-toolbar {
  position: fixed;
  bottom: 1.5rem;
  left: 50%;
  transform: translateX(-50%);
  width: 80%;
  max-width: 900px;
  background: var(--semantic-color-surface-elevated);
  color: var(--semantic-color-text-inverse);
  border-radius: var(--radius-md, 8px);
  padding: 0.85rem 1.5rem;
  box-shadow: var(--shadow-xl);
  z-index: 900;
}

.batch-toolbar-content {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 1rem;
}

.batch-info {
  display: flex;
  align-items: center;
  gap: 1rem;
  font-size: 0.9rem;
}

.batch-count-badge {
  background: var(--primary);
  color: var(--semantic-color-text-on-primary);
  font-weight: 700;
  padding: 0.3rem 0.75rem;
  border-radius: 6px;
  font-size: 0.85rem;
}

.batch-actions {
  display: flex;
  align-items: center;
  gap: 0.75rem;
}

.btn-batch-action {
  background: var(--semantic-color-success-interactive);
  border-color: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
  font-weight: 700;
}

/* Graph Diff Grid */
.graph-diff-grid {
  display: grid;
  grid-template-columns: 1fr 40px 1fr;
  gap: 1rem;
}

.graph-column {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 1rem;
}

.column-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 1rem;
  padding-bottom: 0.5rem;
  border-bottom: 1px solid var(--border);
}

.column-header h4 {
  margin: 0;
  font-size: 1rem;
  font-weight: 600;
}

.badge-locked {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}
.badge-active {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}

.nodes-container {
  display: flex;
  flex-direction: column;
  gap: 0.75rem;
}

.graph-node {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  padding: 0.75rem 1rem;
  transition: all 0.2s ease;
}

.node-unchanged {
  border-left: 4px solid var(--semantic-color-border-control);
}

.node-modified {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
  background: var(--semantic-color-warning-bg-light);
}

.node-added {
  border-left: 4px solid var(--semantic-color-success);
  background: var(--semantic-color-success-bg-soft);
}

.node-deprecated {
  border-left: 4px solid var(--semantic-color-error);
  background: var(--semantic-color-error-bg-soft);
}

.node-title-row {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  margin-bottom: 0.4rem;
}

.node-type-badge {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  text-transform: uppercase;
  background: var(--semantic-color-surface-header);
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
  color: var(--semantic-color-text-secondary);
}

.node-name {
  font-weight: 600;
  font-size: 0.95rem;
}

.diff-badge {
  margin-left: auto;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 0.15rem 0.4rem;
  border-radius: 4px;
}

.diff-badge-node-added {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}
.diff-badge-node-modified {
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}
.diff-badge-node-unchanged {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.node-details {
  display: flex;
  justify-content: space-between;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.delta-annotation {
  margin-top: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px dashed var(--semantic-color-border-control);
  font-size: 0.825rem;
  color: var(--semantic-color-text-secondary);
}

.legend-box {
  display: flex;
  gap: 1rem;
  font-size: 0.825rem;
}

.legend-item {
  display: flex;
  align-items: center;
  gap: 0.35rem;
}

.color-dot {
  width: 10px;
  height: 10px;
  border-radius: 50%;
}

.dot-green {
  background: var(--semantic-color-success);
}
.dot-yellow {
  background: var(--semantic-color-warning-action);
}
.dot-red {
  background: var(--semantic-color-error);
}

.graph-divider {
  display: flex;
  justify-content: center;
  align-items: center;
}

.diff-line {
  width: 2px;
  height: 100%;
  background: var(--border);
}

/* Modals & Wizard */
.modal-overlay {
  position: fixed;
  inset: 0;
  background: var(--semantic-color-backdrop);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-card {
  background: var(--semantic-color-surface);
  border-radius: var(--radius-md, 8px);
  width: 580px;
  max-width: 90vw;
  box-shadow: var(--shadow-xl);
  overflow: hidden;
}

.wizard-modal-card {
  width: 720px;
}

.bulk-reconsent-card {
  width: 620px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  padding: 1rem 1.25rem;
  border-bottom: 1px solid var(--border);
}

.modal-title {
  margin: 0;
  font-size: 1.15rem;
  font-weight: 600;
}

.modal-subtitle {
  color: var(--semantic-color-text-secondary);
  font-size: 0.825rem;
  margin: 0.2rem 0 0 0;
}

.modal-close {
  background: none;
  border: none;
  font-size: 1.5rem;
  cursor: pointer;
}

/* Wizard Stepper Bar */
.wizard-stepper {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 0.85rem 1.5rem;
  background: var(--semantic-color-surface-header);
  border-bottom: 1px solid var(--border);
}

.step-item {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.step-number {
  width: 28px;
  height: 28px;
  border-radius: 50%;
  background: var(--semantic-badge-draft-bg);
  color: var(--semantic-badge-draft-text);
  border: 1px solid var(--semantic-badge-draft-border);
  display: flex;
  align-items: center;
  justify-content: center;
  font-weight: 700;
  font-size: 0.85rem;
}

.step-item.active .step-number {
  background: var(--primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--primary);
}

.step-item.completed .step-number {
  background: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
  border-color: var(--semantic-color-success-interactive);
}

.step-label {
  font-size: 0.85rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.step-item.active .step-label {
  color: var(--primary);
}

.step-divider {
  flex-grow: 1;
  height: 2px;
  background: var(--semantic-color-surface-header);
  margin: 0 0.5rem;
}

.modal-body {
  padding: 1.25rem;
}

.wizard-step-content h4 {
  margin: 0 0 0.25rem 0;
  font-size: 1.05rem;
  color: var(--semantic-color-text);
}

.step-desc {
  color: var(--semantic-color-text-secondary);
  font-size: 0.85rem;
  margin-bottom: 1.25rem;
}

.form-group {
  margin-bottom: 1rem;
}

.form-label {
  display: block;
  font-size: 0.875rem;
  font-weight: 600;
  margin-bottom: 0.35rem;
}

.checkbox-group {
  margin: 1.25rem 0;
}

.checkbox-label {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  cursor: pointer;
}

.form-hint {
  display: block;
  color: var(--semantic-color-text-secondary);
  margin-top: 0.25rem;
  font-size: 0.8rem;
}

.text-area {
  width: 100%;
  box-sizing: border-box;
  resize: vertical;
}

.impact-summary-box {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  padding: 1rem;
}

.impact-stat-row {
  display: grid;
  grid-template-columns: repeat(3, 1fr);
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.impact-stat-card {
  padding: 0.75rem;
  border-radius: var(--radius-sm, 6px);
  text-align: center;
  border: 1px solid var(--semantic-color-border);
}

.card-green {
  background: var(--semantic-color-success-bg-soft);
  border-color: var(--semantic-color-success-border-soft);
}
.card-yellow {
  background: var(--semantic-color-warning-bg-light);
  border-color: var(--semantic-color-warning-border);
}
.card-gray {
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
}

.stat-val {
  display: block;
  font-size: 1.4rem;
  font-weight: 700;
}

.stat-lbl {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.impact-notice {
  display: flex;
  align-items: center;
  gap: 0.5rem;
  font-size: 0.85rem;
  color: var(--semantic-color-warning);
  background: var(--semantic-color-warning-bg);
  padding: 0.6rem 0.85rem;
  border-radius: 4px;
}

.schema-rule-box {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border-soft);
  border-radius: var(--radius-sm, 6px);
  padding: 1rem;
}

.schema-rule-box h5 {
  margin: 0 0 0.5rem 0;
  font-size: 0.95rem;
  color: var(--semantic-color-success-dark);
}

.schema-rule-box ul {
  margin: 0;
  padding-left: 1.25rem;
  font-size: 0.85rem;
  color: var(--semantic-color-success);
}

.reconsent-options {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 1rem;
  margin-top: 1rem;
}

.option-card {
  border: 2px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  padding: 1rem;
  cursor: pointer;
  transition: all 0.2s ease;
}

.option-card h4 {
  margin: 0 0 0.5rem 0;
  font-size: 0.95rem;
}

.option-card p {
  margin: 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.option-card.selected {
  border-color: var(--primary);
  background: var(--semantic-color-info-bg);
}

.subject-tags-inline {
  color: var(--text-main);
  font-family: var(--semantic-font-mono);
}

.margin-top-md {
  margin-top: 1rem;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 0.75rem;
  padding: 1rem 1.25rem;
  border-top: 1px solid var(--border);
  background: var(--semantic-color-surface-header);
}

.btn {
  padding: 0.5rem 1rem;
  border-radius: var(--radius-sm, 6px);
  font-size: 0.875rem;
  font-weight: 600;
  cursor: pointer;
  border: 1px solid transparent;
  display: inline-flex;
  align-items: center;
  gap: 0.5rem;
}

.btn-primary {
  background: var(--primary);
  color: var(--semantic-color-text-on-primary);
}

.btn-primary:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.btn-secondary {
  background: var(--semantic-color-surface);
  border-color: var(--border);
  color: var(--semantic-color-text);
}

.btn-sm {
  padding: 0.25rem 0.6rem;
  font-size: 0.8rem;
}

.btn-action {
  background: var(--semantic-color-error-bg);
  border-color: var(--semantic-color-error-border);
  color: var(--semantic-color-error-text-dark);
}

.badge {
  font-size: 0.75rem;
  padding: 0.25rem 0.5rem;
  border-radius: 4px;
  font-weight: 700;
}

.badge-primary {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
}

.text-muted {
  color: var(--semantic-color-text-secondary);
  font-size: 0.85rem;
}

.text-center {
  text-align: center;
}

/* Impact Summary Header Card */
.impact-summary-header-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 1.25rem 1.5rem;
  margin-bottom: 1.5rem;
  box-shadow: var(--shadow-sm);
}

.summary-top-row {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  margin-bottom: 1rem;
}

.summary-title-group h3 {
  margin: 0;
  font-size: 1.2rem;
  font-weight: 700;
}

.summary-subtext {
  margin: 0.25rem 0 0 0;
  font-size: 0.875rem;
  color: var(--semantic-color-text-secondary);
}

.badge-alert-reconsent {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border);
  font-weight: 700;
  padding: 0.35rem 0.75rem;
  border-radius: 6px;
}

.badge-ok {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
  font-weight: 700;
  padding: 0.35rem 0.75rem;
  border-radius: 6px;
}

.summary-stat-chips-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 0.75rem;
  margin-bottom: 1rem;
}

.stat-chip {
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  padding: 0.65rem 1rem;
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.chip-label {
  font-size: 0.75rem;
  font-weight: 600;
  text-transform: uppercase;
  color: var(--semantic-color-text-secondary);
}

.chip-val {
  font-size: 1.05rem;
  color: var(--semantic-color-text);
}

.chip-burden {
  color: var(--semantic-color-error);
  font-weight: 700;
}

.chip-scope {
  color: var(--semantic-color-interactive-primary);
  font-weight: 700;
}

.schema-breakdown-chips {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding-top: 0.5rem;
  border-top: 1px dashed var(--border);
}

.breakdown-chip {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
  font-size: 0.8rem;
  font-weight: 600;
  padding: 0.25rem 0.6rem;
  border-radius: 4px;
  border: 1px solid var(--semantic-color-border-control);
}

/* Layer Tabs */
.diff-layer-tabs {
  display: flex;
  gap: 0.5rem;
  margin-bottom: 1rem;
  border-bottom: 1px solid var(--border);
  padding-bottom: 0.5rem;
}

.layer-tab-btn {
  padding: 0.5rem 1rem;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-sm, 6px);
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
}

.layer-tab-btn.active {
  background: var(--primary);
  color: var(--semantic-color-text-on-primary);
  border-color: var(--primary);
}

.layer-content {
  margin-top: 0.75rem;
}

.layer-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 0.75rem;
}

.layer-header h4 {
  margin: 0;
  font-size: 1.05rem;
  font-weight: 700;
}

/* Criteria Diff Table */
.criteria-diff-table-wrapper {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  overflow: hidden;
}

.type-pill {
  font-size: 0.75rem;
  font-weight: 600;
  padding: 0.15rem 0.45rem;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
}

.row-diff-added {
  background: var(--semantic-color-success-bg-soft) !important;
}

.row-diff-modified {
  background: var(--semantic-color-warning-bg-light) !important;
}

.row-diff-deprecated {
  background: var(--semantic-color-error-bg-soft) !important;
}

/* eCRF Forms Grid */
.forms-diff-grid {
  display: grid;
  grid-template-columns: repeat(auto-fill, minmax(320px, 1fr));
  gap: 1rem;
}

.form-diff-card {
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: var(--radius-md, 8px);
  padding: 1.15rem;
  display: flex;
  flex-direction: column;
  gap: 0.5rem;
}

.card-diff-added {
  border-left: 4px solid var(--semantic-color-success);
  background: var(--semantic-color-success-bg-soft);
}

.card-diff-modified {
  border-left: 4px solid var(--semantic-color-warning-border-strong);
  background: var(--semantic-color-warning-bg-light);
}

.card-diff-unchanged {
  border-left: 4px solid var(--semantic-color-border-control);
}

.form-diff-card-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.form-header-title {
  display: flex;
  flex-direction: column;
  gap: 0.2rem;
}

.form-key-tag {
  font-size: 0.75rem;
  font-family: var(--semantic-font-mono);
  font-weight: 700;
  color: var(--text-main);
}

.form-title {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
}

.form-desc {
  margin: 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.diff-badge-node-added {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
  border: 1px solid var(--semantic-color-success-border);
}

.diff-badge-node-modified {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-text);
  border: 1px solid var(--semantic-color-warning-border);
}

.diff-badge-node-deprecated {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
  border: 1px solid var(--semantic-color-error-border);
}

.diff-badge-node-unchanged {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-secondary);
  border: 1px solid var(--semantic-color-border-control);
}

.color-dot.dot-gray {
  background: var(--semantic-color-border-control);
}
</style>
