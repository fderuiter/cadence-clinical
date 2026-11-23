<template>
  <div class="ssu-greenlight-console card">
    <!-- Console View Mode Bar -->
    <div class="view-mode-bar">
      <button
        class="view-tab-btn"
        :class="{ active: viewMode === 'matrix' }"
        @click="switchToMatrixView"
      >
        📊 Country Multi-Site Activation Matrix
      </button>
      <button
        class="view-tab-btn"
        :class="{ active: viewMode === 'single' }"
        @click="switchToSingleView"
      >
        🔎 Single Site Detail Console ({{ activeSiteId }})
      </button>
    </div>

    <!-- Upload Document Modal (Global Level) -->
    <ClinicalModal
      v-model="showUploadDocModal"
      :teleport="false"
      :dismissible="!isSubmitting"
      dialog-class="modal-card"
      title="Upload Essential Document"
    >
      <div class="form-group">
        <label for="doc-type-select">Document Type:</label>
        <select
          id="doc-type-select"
          v-model="docForm.type"
          class="select-input"
        >
          <option value="FDA_1572">Form FDA 1572 (05.02.02)</option>
          <option value="IRB_APPROVAL">IRB Approval Letter (04.01.02)</option>
          <option value="CTA_AGREEMENT">
            Clinical Trial Agreement (05.02.12)
          </option>
          <option value="PROTOCOL_SIGNATURE">
            Protocol Signature Page (05.02.03)
          </option>
        </select>
      </div>

      <div class="form-group">
        <label for="doc-file-input">File:</label>
        <input
          id="doc-file-input"
          data-testid="ssu-doc-file"
          type="file"
          class="text-input"
          @change="onDocFileSelected"
        />
      </div>

      <div class="form-group">
        <label for="doc-upload-reason">Reason for Change:</label>
        <input
          id="doc-upload-reason"
          v-model="docForm.reasonForChange"
          type="text"
          class="text-input"
          placeholder="Stage essential startup document for clinical QC"
        />
      </div>

      <template #footer>
        <button
          type="button"
          class="btn btn-secondary"
          :disabled="isSubmitting"
          @click="showUploadDocModal = false"
        >
          Cancel
        </button>
        <button
          type="button"
          class="btn btn-primary"
          :disabled="isSubmitting"
          @click="submitDoc"
        >
          {{ isSubmitting ? "Uploading..." : "Upload Document" }}
        </button>
      </template>
    </ClinicalModal>

    <!-- ========================================================================= -->
    <!-- VIEW 1: COUNTRY-LEVEL MULTI-SITE ACTIVATION MATRIX VIEW                   -->
    <!-- ========================================================================= -->
    <div v-if="viewMode === 'matrix'" class="matrix-view-container">
      <!-- Matrix Header Section -->
      <div class="console-header">
        <div class="header-titles">
          <div class="badge-row">
            <span class="badge badge-gxp">21 CFR Part 11</span>
            <span class="badge badge-regulatory">ICH GCP E6(R2) §8</span>
            <span class="badge badge-standard">PRD-SSU-001/002/003</span>
            <span class="badge badge-country">
              Jurisdiction: {{ activeCountry }}
            </span>
          </div>
          <h2 class="console-title">
            Country-Level Multi-Site Activation Matrix
          </h2>
          <p class="console-subtitle">
            Evaluate 6-facet readiness across all trial sites in
            {{ activeCountry }} simultaneously, identify exception bottlenecks,
            and batch-certify compliant sites with a single 21 CFR Part 11
            electronic signature.
          </p>
        </div>

        <div class="header-actions">
          <button
            class="btn btn-secondary"
            :disabled="loading"
            @click="fetchCountryMatrix"
          >
            <span class="btn-icon">🔄</span> Refresh Matrix
          </button>
          <button
            class="btn btn-primary btn-batch-certify"
            :disabled="!canCertify || loading || selectedSiteIds.size === 0"
            :title="!canCertify ? GREENLIGHT_CERTIFY_ROLE_REASON : undefined"
            @click="openBatchCertifyModal"
          >
            <span class="btn-icon">🛡️</span> Batch Certify Country Sites
            <span v-if="selectedSiteIds.size > 0" class="btn-counter-badge">
              {{ selectedSiteIds.size }}
            </span>
          </button>
        </div>
      </div>

      <p
        v-if="!canCertify"
        id="greenlight-certify-role-note"
        class="role-note"
        role="note"
      >
        {{ GREENLIGHT_CERTIFY_ROLE_REASON }}
      </p>

      <div
        v-if="matrixErrorMessage"
        id="matrix-load-error"
        data-testid="ssu-matrix-error"
        class="alert alert-danger"
        role="alert"
      >
        <span class="alert-icon">⚠️</span>
        <div>
          <strong>Couldn't load country readiness matrix:</strong>
          {{ matrixErrorMessage }}
        </div>
      </div>

      <!-- Scope & Filter Control Bar -->
      <div class="scope-bar">
        <div class="form-group-inline">
          <label for="ssu-matrix-study-id">Study ID:</label>
          <input
            id="ssu-matrix-study-id"
            v-model="activeStudyId"
            type="text"
            class="text-input"
            placeholder="e.g. PROTOCOL-2026-01"
            @change="fetchCountryMatrix"
          />
        </div>

        <div class="form-group-inline">
          <label for="ssu-matrix-jurisdiction">Country / Jurisdiction:</label>
          <select
            id="ssu-matrix-jurisdiction"
            v-model="activeCountry"
            class="select-input"
            @change="fetchCountryMatrix"
          >
            <option value="US">🇺🇸 United States (FDA 1572 + IRB)</option>
            <option value="DE">
              🇩🇪 Germany (EU CTR Part I/II + Ethikkommission)
            </option>
            <option value="EU">🇪🇺 European Union (EU CTR + EC)</option>
            <option value="JP">🇯🇵 Japan (PMDA Notification + IRB)</option>
            <option value="GB">🇬🇧 United Kingdom (MHRA + HRA REC)</option>
            <option value="GLOBAL">🌐 Global Default (ICH GCP E6 R2)</option>
          </select>
        </div>

        <div class="form-group-inline">
          <label for="ssu-matrix-filter">Filter Sites:</label>
          <select
            id="ssu-matrix-filter"
            v-model="matrixStatusFilter"
            class="select-input"
          >
            <option value="ALL">All Sites in Country</option>
            <option value="COMPLIANT">Fully Compliant (100% Ready)</option>
            <option value="NON_COMPLIANT">
              Non-Compliant / Exceptions (&lt;100%)
            </option>
            <option value="CERTIFIED">Greenlight Certified Only</option>
          </select>
        </div>

        <div class="form-group-inline">
          <label for="ssu-matrix-search">Search:</label>
          <input
            id="ssu-matrix-search"
            v-model="matrixSearchQuery"
            type="text"
            class="text-input"
            placeholder="Search Site ID or Name..."
          />
        </div>
      </div>

      <!-- Summary Metrics Row -->
      <div class="metrics-row">
        <div class="metric-card">
          <span class="metric-label">Total Country Sites</span>
          <span class="metric-value">{{ matrixSites.length }}</span>
          <span class="metric-sub">Cohort in {{ activeCountry }}</span>
        </div>
        <div class="metric-card metric-success">
          <span class="metric-label">Fully Compliant (100%)</span>
          <span class="metric-value">{{ compliantSites.length }}</span>
          <span class="metric-sub">Ready for Batch Activation</span>
        </div>
        <div class="metric-card metric-danger">
          <span class="metric-label">Non-Compliant Exceptions</span>
          <span class="metric-value">{{ nonCompliantSites.length }}</span>
          <span class="metric-sub">Action Required</span>
        </div>
        <div class="metric-card metric-info">
          <span class="metric-label">Greenlight Certified</span>
          <span class="metric-value">{{ certifiedSitesCount }}</span>
          <span class="metric-sub">Trial Execution Un-gated</span>
        </div>
      </div>

      <!-- Batch Action Bar -->
      <div class="batch-action-bar">
        <div class="batch-selection-info">
          <button
            class="btn btn-secondary btn-select-all"
            :disabled="compliantSites.length === 0"
            @click="toggleSelectAllCompliant"
          >
            {{
              isAllCompliantSelected
                ? "Deselect All"
                : "Select All Compliant (100%)"
            }}
          </button>
          <span class="selection-count-text">
            <strong>{{ selectedSiteIds.size }}</strong> compliant site(s)
            selected for 21 CFR Part 11 batch certification
          </span>
        </div>

        <button
          class="btn btn-primary btn-batch-certify"
          :disabled="!canCertify || loading || selectedSiteIds.size === 0"
          :title="!canCertify ? GREENLIGHT_CERTIFY_ROLE_REASON : undefined"
          @click="openBatchCertifyModal"
        >
          <span class="btn-icon">🛡️</span> Batch Certify Country Sites
        </button>
      </div>

      <!-- Batch Success Banner -->
      <div v-if="batchSuccessData" class="batch-success-banner">
        <div class="banner-header">
          <span class="banner-icon">🎉</span>
          <div>
            <h4>Batch Greenlight Certification Successfully Processed!</h4>
            <p>
              Batch certified
              {{
                batchSuccessData.certified_count ??
                batchSuccessData.certifiedCount
              }}
              site(s) in
              {{
                batchSuccessData.country_code ?? batchSuccessData.countryCode
              }}
              for study
              {{ batchSuccessData.study_id ?? batchSuccessData.studyId }}.
              Distinct HMAC-SHA256 activation tokens generated and 21 CFR Part
              11 audit records written.
            </p>
          </div>
          <ClinicalIconButton
            class="btn-icon-only"
            label="Dismiss batch greenlight certification banner"
            title="Dismiss"
            @click="batchSuccessData = null"
          >
            ✖
          </ClinicalIconButton>
        </div>
        <div class="banner-tokens-list">
          <div
            v-for="site in batchSuccessData.certified_sites ||
            batchSuccessData.certifiedSites"
            :key="site.site_id || site.siteId"
            class="token-item"
          >
            <span class="token-site-id"
              ><strong>{{ site.site_id || site.siteId }}</strong> ({{
                site.site_name || site.siteName
              }}):</span
            >
            <code class="token-code">{{
              site.greenlight_token || site.greenlightToken
            }}</code>
          </div>
        </div>
      </div>

      <!-- Country Matrix Table -->
      <div class="matrix-table-card">
        <table class="matrix-table">
          <thead>
            <tr>
              <th class="col-checkbox">
                <input
                  type="checkbox"
                  title="Select / Deselect All Compliant Sites"
                  :checked="isAllCompliantSelected"
                  :disabled="compliantSites.length === 0"
                  @change="toggleSelectAllCompliant"
                />
              </th>
              <th class="col-site">Site ID &amp; Name</th>
              <th class="col-facets">6-Facet Operational Readiness</th>
              <th class="col-readiness">Readiness</th>
              <th class="col-status">Greenlight Status</th>
              <th class="col-action">Actions</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="site in filteredMatrixSites"
              :key="site.site_id"
              :class="{
                'row-selected': selectedSiteIds.has(site.site_id),
                'row-certified':
                  site.overall_status === 'APPROVED' ||
                  site.overall_status === 'OVERRIDDEN',
                'row-non-compliant': !site.is_compliant,
              }"
            >
              <td class="col-checkbox">
                <input
                  type="checkbox"
                  class="site-select-checkbox"
                  aria-label="Select site for batch certification"
                  :disabled="
                    !site.is_compliant ||
                    site.overall_status === 'APPROVED' ||
                    site.overall_status === 'OVERRIDDEN'
                  "
                  :checked="selectedSiteIds.has(site.site_id)"
                  @change="
                    toggleSiteSelection(
                      site.site_id,
                      site.is_compliant,
                      site.overall_status
                    )
                  "
                />
              </td>

              <td class="col-site">
                <div class="site-cell">
                  <span class="site-id-badge">{{ site.site_id }}</span>
                  <span class="site-name-text">{{ site.site_name }}</span>
                  <span class="site-jurisdiction-tag"
                    >📍 {{ site.country_code }}</span
                  >
                </div>
              </td>

              <td class="col-facets">
                <div class="facets-pill-grid">
                  <span
                    class="facet-pill"
                    :class="
                      site.facet_regulatory_irb ? 'pill-pass' : 'pill-fail'
                    "
                    :title="
                      'Regulatory & IRB: ' +
                      (site.facet_regulatory_irb ? 'PASSED' : 'ACTION REQ')
                    "
                  >
                    ⚖️ Reg/IRB
                  </span>
                  <span
                    class="facet-pill"
                    :class="site.facet_cta_budget ? 'pill-pass' : 'pill-fail'"
                    :title="
                      'CTA & Budget: ' +
                      (site.facet_cta_budget ? 'PASSED' : 'ACTION REQ')
                    "
                  >
                    📝 CTA
                  </span>
                  <span
                    class="facet-pill"
                    :class="
                      site.facet_eisf_baseline ? 'pill-pass' : 'pill-fail'
                    "
                    :title="
                      'eISF Baseline: ' +
                      (site.facet_eisf_baseline ? 'PASSED' : 'ACTION REQ')
                    "
                  >
                    📁 eISF
                  </span>
                  <span
                    class="facet-pill"
                    :class="site.facet_doa_log ? 'pill-pass' : 'pill-fail'"
                    :title="
                      'DOA Log: ' +
                      (site.facet_doa_log ? 'PASSED' : 'ACTION REQ')
                    "
                  >
                    👥 DOA
                  </span>
                  <span
                    class="facet-pill"
                    :class="
                      site.facet_staff_training ? 'pill-pass' : 'pill-fail'
                    "
                    :title="
                      'Staff Training: ' +
                      (site.facet_staff_training ? 'PASSED' : 'ACTION REQ')
                    "
                  >
                    🎓 Train
                  </span>
                  <span
                    class="facet-pill"
                    :class="site.facet_ip_release ? 'pill-pass' : 'pill-fail'"
                    :title="
                      'IP Depot Release: ' +
                      (site.facet_ip_release ? 'PASSED' : 'ACTION REQ')
                    "
                  >
                    💊 IP
                  </span>
                </div>
              </td>

              <td class="col-readiness">
                <div class="readiness-cell">
                  <span
                    class="readiness-score-badge"
                    :class="
                      site.is_compliant ? 'badge-score-100' : 'badge-score-fail'
                    "
                  >
                    {{ site.readiness_percentage }}%
                  </span>
                  <span
                    v-if="site.unfulfilled_reasons?.length > 0"
                    class="exception-warning"
                    :title="site.unfulfilled_reasons.join('\n')"
                  >
                    ⚠️ {{ site.unfulfilled_reasons.length }} Missing
                  </span>
                </div>
              </td>

              <td class="col-status">
                <div class="status-cell">
                  <span
                    v-if="site.overall_status === 'APPROVED'"
                    class="status-chip chip-approved"
                  >
                    🟢 Greenlight Certified
                  </span>
                  <span
                    v-else-if="site.overall_status === 'OVERRIDDEN'"
                    class="status-chip chip-overridden"
                  >
                    ⚡ GxP Overridden
                  </span>
                  <span
                    v-else-if="site.is_compliant"
                    class="status-chip chip-compliant"
                  >
                    ✅ 100% Compliant (Ready)
                  </span>
                  <span v-else class="status-chip chip-blocked">
                    🚫 Non-Compliant
                  </span>

                  <div v-if="site.greenlight_token" class="token-mini-preview">
                    <code :title="site.greenlight_token">
                      {{ site.greenlight_token.slice(0, 10) }}...{{
                        site.greenlight_token.slice(-6)
                      }}
                    </code>
                  </div>
                </div>
              </td>

              <td class="col-action">
                <button
                  class="btn btn-secondary btn-sm"
                  title="Inspect 6-Facet Scorecard and Essential Documents"
                  @click="inspectSiteDetail(site)"
                >
                  🔎 Inspect Site
                </button>
              </td>
            </tr>

            <tr v-if="filteredMatrixSites.length === 0">
              <td colspan="6" class="empty-table-cell">
                No sites found matching the selected country and filters.
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <!-- Batch Certification Modal -->
      <ClinicalModal
        v-model="showBatchCertifyModal"
        :teleport="false"
        :dismissible="!isSubmitting"
        size="lg"
        dialog-class="modal-card modal-lg"
        title="21 CFR Part 11 Country-Level Batch Greenlight Certification"
      >
        <div v-if="batchErrorMessage" class="alert alert-danger" role="alert">
          <span class="alert-icon">⚠️</span>
          <div>
            <strong>Batch Certification Rejected:</strong>
            {{ batchErrorMessage }}
          </div>
        </div>

        <div class="alert alert-info">
          <span class="alert-icon">🛡️</span>
          <div>
            <strong>Legal Certification Scope:</strong>
            You are applying a single 21 CFR Part 11 compliant electronic
            signature to batch-certify greenlight activation for
            <strong>{{ selectedSiteIds.size }} compliant site(s)</strong> in
            <strong>{{ activeCountry }}</strong> for study
            <strong>{{ activeStudyId }}</strong>.
          </div>
        </div>

        <div class="batch-summary-box">
          <h4>Selected Cohort Sites ({{ selectedSiteIds.size }}):</h4>
          <div class="selected-sites-grid">
            <div
              v-for="siteIdItem in Array.from(selectedSiteIds)"
              :key="siteIdItem"
              class="selected-site-tag"
            >
              <span class="site-tag-id">✓ {{ siteIdItem }}</span>
            </div>
          </div>
        </div>

        <div class="esign-form">
          <h4 class="esign-title">Electronic Signature Credentials</h4>

          <div class="form-row">
            <div class="form-group">
              <label for="batch-sig-name">Signer Full Name:</label>
              <input
                id="batch-sig-name"
                v-model="batchSigForm.name"
                type="text"
                class="text-input"
                placeholder="e.g. Dr. Eleanor Vance"
              />
            </div>
            <div class="form-group">
              <label for="batch-sig-role">Role / Title:</label>
              <input
                id="batch-sig-role"
                v-model="batchSigForm.role"
                type="text"
                class="text-input"
                placeholder="e.g. VP of Clinical Operations"
              />
            </div>
          </div>

          <div class="form-group">
            <label for="batch-sig-reason"
              >Certification Statement / Change Reason:</label
            >
            <textarea
              id="batch-sig-reason"
              v-model="batchSigForm.changeReason"
              class="textarea-input"
              rows="3"
              placeholder="Provide GxP certification statement..."
            />
          </div>
        </div>

        <template #footer>
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="isSubmitting"
            @click="showBatchCertifyModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary btn-submit-batch-certify"
            :disabled="
              !canCertify ||
              !batchSigForm.name ||
              !batchSigForm.role ||
              !batchSigForm.changeReason.trim() ||
              isSubmitting
            "
            :title="!canCertify ? GREENLIGHT_CERTIFY_ROLE_REASON : undefined"
            @click="executeBatchCertification"
          >
            {{
              isSubmitting
                ? "Certifying Cohort..."
                : "Sign & Batch Certify Country Sites"
            }}
          </button>
        </template>
      </ClinicalModal>
    </div>

    <!-- ========================================================================= -->
    <!-- VIEW 2: SINGLE SITE GREENLIGHT CONSOLE & SCORECARD                        -->
    <!-- ========================================================================= -->
    <div v-else class="single-view-container">
      <!-- Header Section -->
      <div class="console-header">
        <div class="header-titles">
          <div class="badge-row">
            <button
              class="btn btn-secondary btn-sm"
              @click="switchToMatrixView"
            >
              ← Back to Country Matrix
            </button>
            <span class="badge badge-gxp">21 CFR Part 11</span>
            <span class="badge badge-regulatory">ICH GCP E6(R2) §8</span>
            <span
              class="badge"
              :class="isGreenlighted ? 'badge-approved' : 'badge-pending'"
            >
              {{ greenlightStatusBadgeText }}
            </span>
          </div>
          <h2 class="console-title">
            Site Greenlight Console: {{ activeSiteId }}
          </h2>
          <p class="console-subtitle">
            Single-site 6-facet greenlight scorecard, essential document staging
            into eTMF QC, and single-site certification.
          </p>
        </div>

        <div class="header-actions">
          <button
            class="btn btn-secondary"
            :disabled="loading"
            @click="refreshAll"
          >
            <span class="btn-icon">🔄</span> Refresh
          </button>
          <button
            id="btn-ssu-upload-doc"
            class="btn btn-primary"
            @click="showUploadDocModal = true"
          >
            <span class="btn-icon">📄</span> Upload Document
          </button>
          <button
            id="btn-ssu-certify-single"
            class="btn btn-primary"
            :disabled="!canCertify || loading || isGreenlighted"
            :title="!canCertify ? GREENLIGHT_CERTIFY_ROLE_REASON : undefined"
            @click="openCertifyModal"
          >
            <span class="btn-icon">🛡️</span> Certify Greenlight
          </button>
        </div>
      </div>

      <p
        v-if="!canCertify"
        id="greenlight-single-role-note"
        class="role-note"
        role="note"
      >
        {{ GREENLIGHT_CERTIFY_ROLE_REASON }}
      </p>

      <div
        v-if="gateErrorMessage"
        data-testid="ssu-gate-error"
        class="alert alert-danger"
        role="alert"
      >
        <span class="alert-icon">⚠️</span>
        <div>
          <strong>Couldn't load site greenlight status:</strong>
          {{ gateErrorMessage }}
        </div>
      </div>

      <!-- Scope Bar -->
      <div class="scope-bar">
        <div class="form-group-inline">
          <label for="ssu-study-id">Study ID:</label>
          <input
            id="ssu-study-id"
            v-model="activeStudyId"
            type="text"
            class="text-input"
            placeholder="e.g. PROTOCOL-2026-01"
            @change="onScopeChange"
          />
        </div>

        <div class="form-group-inline">
          <label for="ssu-site-id">Site ID:</label>
          <input
            id="ssu-site-id"
            v-model="activeSiteId"
            type="text"
            class="text-input"
            placeholder="e.g. SITE-101"
            @change="onScopeChange"
          />
        </div>

        <div class="form-group-inline">
          <label for="ssu-jurisdiction">Jurisdiction:</label>
          <select
            id="ssu-jurisdiction"
            v-model="activeCountry"
            class="select-input"
            @change="onJurisdictionChange"
          >
            <option value="US">🇺🇸 United States (FDA 1572 + IRB)</option>
            <option value="EU">
              🇪🇺 European Union (EU CTR Part I/II + EC)
            </option>
            <option value="JP">🇯🇵 Japan (PMDA Notification + IRB)</option>
            <option value="GB">🇬🇧 United Kingdom (MHRA + HRA REC)</option>
            <option value="GLOBAL">🌐 Global Default (ICH GCP E6 R2)</option>
          </select>
        </div>

        <div v-if="gateData?.greenlight_token" class="token-status-pill">
          <span class="token-label">HMAC-SHA256 Token:</span>
          <code class="token-snip" :title="gateData.greenlight_token">
            {{ gateData.greenlight_token.slice(0, 16) }}...{{
              gateData.greenlight_token.slice(-8)
            }}
          </code>
          <ClinicalIconButton
            class="btn-icon-only"
            label="Copy HMAC-SHA256 greenlight token"
            title="Copy Token"
            @click="copyToken"
          >
            📋
          </ClinicalIconButton>
        </div>
      </div>

      <!-- 6-Facet Greenlight Scorecard Grid -->
      <div class="scorecard-section">
        <div class="section-title-row">
          <h3 class="section-title">6-Facet Operational Readiness Scorecard</h3>
          <div class="readiness-gauge">
            <div class="gauge-bar-outer">
              <div
                class="gauge-bar-inner"
                :style="{
                  width: readinessPercentage + '%',
                  backgroundColor: gaugeColor,
                }"
              />
            </div>
            <span class="gauge-text" :style="{ color: gaugeColor }">
              {{ readinessPercentage }}% Ready ({{ passedFacetsCount }}/6 Facets
              Passed)
            </span>
          </div>
        </div>

        <div class="facets-grid">
          <div
            class="facet-card"
            :class="{
              'facet-passed': gateData?.facet_regulatory_irb,
              'facet-failed': !gateData?.facet_regulatory_irb,
            }"
          >
            <div class="facet-header">
              <span class="facet-icon">⚖️</span>
              <div class="facet-titles">
                <span class="facet-name">Regulatory &amp; IRB / Ethics</span>
                <span class="facet-rule-tag"
                  >{{ activeCountry }} Startup Rule</span
                >
              </div>
              <span
                class="facet-status-chip"
                :class="
                  gateData?.facet_regulatory_irb ? 'chip-pass' : 'chip-fail'
                "
              >
                {{ gateData?.facet_regulatory_irb ? "PASSED" : "ACTION REQ" }}
              </span>
            </div>
            <p class="facet-desc">
              {{ getFacetRegulatoryDesc(activeCountry) }}
            </p>
          </div>

          <div
            class="facet-card"
            :class="{
              'facet-passed': gateData?.facet_cta_budget,
              'facet-failed': !gateData?.facet_cta_budget,
            }"
          >
            <div class="facet-header">
              <span class="facet-icon">📝</span>
              <div class="facet-titles">
                <span class="facet-name">CTA &amp; Budget Schedule</span>
                <span class="facet-rule-tag">Legal &amp; Financial</span>
              </div>
              <span
                class="facet-status-chip"
                :class="gateData?.facet_cta_budget ? 'chip-pass' : 'chip-fail'"
              >
                {{ gateData?.facet_cta_budget ? "PASSED" : "ACTION REQ" }}
              </span>
            </div>
            <p class="facet-desc">
              Fully executed Clinical Trial Agreement and investigator grant
              payment schedules.
            </p>
          </div>

          <div
            class="facet-card"
            :class="{
              'facet-passed': gateData?.facet_eisf_baseline,
              'facet-failed': !gateData?.facet_eisf_baseline,
            }"
          >
            <div class="facet-header">
              <span class="facet-icon">📁</span>
              <div class="facet-titles">
                <span class="facet-name">eISF Baseline Documents</span>
                <span class="facet-rule-tag">Essential Records</span>
              </div>
              <span
                class="facet-status-chip"
                :class="
                  gateData?.facet_eisf_baseline ? 'chip-pass' : 'chip-fail'
                "
              >
                {{ gateData?.facet_eisf_baseline ? "PASSED" : "ACTION REQ" }}
              </span>
            </div>
            <p class="facet-desc">
              Validated electronic Investigator Site File with required core
              startup documents.
            </p>
          </div>

          <div
            class="facet-card"
            :class="{
              'facet-passed': gateData?.facet_doa_log,
              'facet-failed': !gateData?.facet_doa_log,
            }"
          >
            <div class="facet-header">
              <span class="facet-icon">👥</span>
              <div class="facet-titles">
                <span class="facet-name"
                  >Delegation of Authority (DOA) Log</span
                >
                <span class="facet-rule-tag">PI Governance</span>
              </div>
              <span
                class="facet-status-chip"
                :class="gateData?.facet_doa_log ? 'chip-pass' : 'chip-fail'"
              >
                {{ gateData?.facet_doa_log ? "PASSED" : "ACTION REQ" }}
              </span>
            </div>
            <p class="facet-desc">
              Principal Investigator signed-off site delegation log covering all
              active personnel.
            </p>
          </div>

          <div
            class="facet-card"
            :class="{
              'facet-passed': gateData?.facet_staff_training,
              'facet-failed': !gateData?.facet_staff_training,
            }"
          >
            <div class="facet-header">
              <span class="facet-icon">🎓</span>
              <div class="facet-titles">
                <span class="facet-name"
                  >Protocol &amp; GCP Staff Training</span
                >
                <span class="facet-rule-tag">GCP Compliance</span>
              </div>
              <span
                class="facet-status-chip"
                :class="
                  gateData?.facet_staff_training ? 'chip-pass' : 'chip-fail'
                "
              >
                {{ gateData?.facet_staff_training ? "PASSED" : "ACTION REQ" }}
              </span>
            </div>
            <p class="facet-desc">
              Verified completion of protocol curriculum, GCP E6(R2), and EDC
              system qualifications.
            </p>
          </div>

          <div
            class="facet-card"
            :class="{
              'facet-passed': gateData?.facet_ip_release,
              'facet-failed': !gateData?.facet_ip_release,
            }"
          >
            <div class="facet-header">
              <span class="facet-icon">💊</span>
              <div class="facet-titles">
                <span class="facet-name">IP Depot Greenlight Release</span>
                <span class="facet-rule-tag">Supply Chain</span>
              </div>
              <span
                class="facet-status-chip"
                :class="gateData?.facet_ip_release ? 'chip-pass' : 'chip-fail'"
              >
                {{ gateData?.facet_ip_release ? "PASSED" : "ACTION REQ" }}
              </span>
            </div>
            <p class="facet-desc">
              Authorized depot greenlight release for investigational drug kit
              shipment to site.
            </p>
          </div>
        </div>
      </div>

      <!-- Single Site Certification Modal -->
      <ClinicalModal
        v-model="showCertifyModal"
        :teleport="false"
        :dismissible="!isSubmitting"
        dialog-class="modal-card"
        title="21 CFR Part 11 Single-Site Greenlight Certification"
      >
        <div v-if="certifyErrorMessage" class="alert alert-danger" role="alert">
          <span class="alert-icon">⚠️</span>
          <div>
            <strong>Certification Rejected:</strong>
            {{ certifyErrorMessage }}
          </div>
        </div>
        <p>
          You are certifying regulatory greenlight activation for site
          <strong>{{ activeSiteId }}</strong> in
          <strong>{{ activeCountry }}</strong>.
        </p>
        <div class="form-group">
          <label for="ssu-sig-name">Signer Name:</label>
          <input
            id="ssu-sig-name"
            v-model="sigForm.name"
            type="text"
            class="text-input"
          />
        </div>
        <div class="form-group">
          <label for="ssu-sig-role">Role:</label>
          <input
            id="ssu-sig-role"
            v-model="sigForm.role"
            type="text"
            class="text-input"
          />
        </div>
        <div class="form-group">
          <label for="ssu-sig-reason">Change Reason:</label>
          <input
            id="ssu-sig-reason"
            v-model="sigForm.changeReason"
            type="text"
            class="text-input"
          />
        </div>

        <template #footer>
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="isSubmitting"
            @click="showCertifyModal = false"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-single-certify"
            type="button"
            class="btn btn-primary"
            :disabled="
              !canCertify || !sigForm.changeReason.trim() || isSubmitting
            "
            :title="!canCertify ? GREENLIGHT_CERTIFY_ROLE_REASON : undefined"
            @click="executeGreenlightCertification"
          >
            {{ isSubmitting ? "Certifying..." : "Sign & Certify Greenlight" }}
          </button>
        </template>
      </ClinicalModal>
    </div>
  </div>
</template>

<script setup>
import { ClinicalIconButton, ClinicalModal } from "ui";
import { computed, onMounted, reactive, ref, watch } from "vue";
import { ctmsApi } from "../../api/ctmsApi";
import { etmfService } from "../../api/etmf";
import {
  GREENLIGHT_CERTIFY_ROLE_REASON,
  canCertifyGreenlight,
} from "../../canvas/authority";
import { useAuthStore } from "../../stores/auth";
import { useToastStore } from "../../stores/toasts.js";

const DOC_TYPE_TAXONOMY = {
  FDA_1572: {
    zone: 5,
    section: "05.02",
    artifactCode: "05.02.02",
    artifactType: "Form FDA 1572",
  },
  IRB_APPROVAL: {
    zone: 4,
    section: "04.01",
    artifactCode: "04.01.02",
    artifactType: "IRB / IEC Approval",
  },
  CTA_AGREEMENT: {
    zone: 5,
    section: "05.02",
    artifactCode: "05.02.12",
    artifactType: "Clinical Trial Agreement",
  },
  PROTOCOL_SIGNATURE: {
    zone: 5,
    section: "05.02",
    artifactCode: "05.02.03",
    artifactType: "Protocol Signature Page",
  },
};

const props = defineProps({
  studyId: {
    type: String,
    default: "PROTOCOL-2026-01",
  },
  siteId: {
    type: String,
    default: "SITE-101",
  },
  countryCode: {
    type: String,
    default: "US",
  },
  initialViewMode: {
    type: String,
    default: "matrix",
  },
});

const emit = defineEmits(["greenlight-certified"]);

const authStore = useAuthStore();
const canCertify = computed(() => canCertifyGreenlight(authStore));

// View Mode: 'matrix' (default) or 'single'
const viewMode = ref(props.initialViewMode === "single" ? "single" : "matrix");

// Reactive Scope State
const activeStudyId = ref(props.studyId || "PROTOCOL-2026-01");
const activeSiteId = ref(props.siteId || "SITE-101");
const activeCountry = ref(props.countryCode || "US");
const loading = ref(false);
const isSubmitting = ref(false);
const currentTab = ref("documents");

// Matrix State
const matrixSites = ref([]);
const selectedSiteIds = ref(new Set());
const matrixSearchQuery = ref("");
const matrixStatusFilter = ref("ALL");
const showBatchCertifyModal = ref(false);
const batchSuccessData = ref(null);
const batchErrorMessage = ref(null);
const matrixErrorMessage = ref("");

// Single-site State & Modals
const showCertifyModal = ref(false);
const showUploadDocModal = ref(false);
const gateErrorMessage = ref("");
const certifyErrorMessage = ref("");

// Forms
const sigForm = reactive({
  name: authStore.identity?.username || "Sponsor Operations",
  role: authStore.activeRole || "Clinical Data Manager",
  changeReason: "All 6 startup facets verified and approved",
});

const batchSigForm = reactive({
  name: authStore.identity?.username || "Sponsor Operations",
  role: authStore.activeRole || "Clinical Data Manager",
  changeReason:
    "I certify under 21 CFR Part 11 that all selected sites meet statutory greenlight requirements and authorize trial execution.",
});

const docForm = reactive({
  type: "FDA_1572",
  fileName: "Form_FDA_1572_Site101_v1.pdf",
  file: null,
  reasonForChange: "Stage essential startup document for clinical QC review",
});

watch(
  () => props.studyId,
  (val) => {
    if (val && val !== activeStudyId.value) {
      activeStudyId.value = val;
      refreshAll();
    }
  }
);

watch(
  () => props.siteId,
  (val) => {
    if (val && val !== activeSiteId.value) {
      activeSiteId.value = val;
      if (viewMode.value === "single") {
        evaluateGreenlight();
      }
    }
  }
);

function onDocFileSelected(event) {
  const file = event.target?.files?.[0] || null;
  docForm.file = file;
  if (file) docForm.fileName = file.name;
}

async function sha256Hex(bytesBuffer) {
  const hashBuffer = await crypto.subtle.digest("SHA-256", bytesBuffer);
  return Array.from(new Uint8Array(hashBuffer))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

function arrayBufferToBase64(buffer) {
  const bytes = new Uint8Array(buffer);
  let binary = "";
  for (let i = 0; i < bytes.byteLength; i++) {
    binary += String.fromCharCode(bytes[i]);
  }
  return typeof btoa === "function" ? btoa(binary) : "";
}

async function submitDoc() {
  if (!docForm.file) {
    useToastStore().addToast({
      message: "Please select a file to upload.",
      tone: "error",
    });
    return;
  }

  isSubmitting.value = true;
  try {
    const rawBuffer = await docForm.file.arrayBuffer();
    const fileHash = await sha256Hex(rawBuffer);
    const contentText =
      new TextDecoder("utf-8", { fatal: false }).decode(rawBuffer) ||
      arrayBufferToBase64(rawBuffer);
    const taxonomy =
      DOC_TYPE_TAXONOMY[docForm.type] || DOC_TYPE_TAXONOMY.FDA_1572;
    const fileName = docForm.file.name || docForm.fileName;

    const stagedDoc = await etmfService.stageQcDocument({
      content: contentText,
      studyId: activeStudyId.value,
      siteId: activeSiteId.value,
      filename: fileName,
      mimeType: docForm.file.type || "application/pdf",
      artifactHint: `${taxonomy.artifactType} (${taxonomy.artifactCode}) [sha256:${fileHash}]`,
      reasonForChange:
        docForm.reasonForChange?.trim() ||
        "Stage essential startup document for clinical QC review",
    });

    essentialDocuments.value.push(stagedDoc);
    showUploadDocModal.value = false;
    docForm.file = null;
  } catch (err) {
    const statusLabel = err?.status ? `HTTP ${err.status}` : "";
    const detail = err?.message || "Request failed";
    useToastStore().addToast({
      message: statusLabel
        ? `Failed to upload document: ${statusLabel} (${detail})`
        : `Failed to upload document: ${detail}`,
      tone: "error",
    });
  } finally {
    isSubmitting.value = false;
  }
}

// Single-site Data
const gateData = ref(null);
const essentialDocuments = ref([]);
const countryRules = ref([]);
const regulatoryMilestones = ref([]);

// Computed Matrix Filters & Aggregates
const compliantSites = computed(() => {
  return matrixSites.value.filter(
    (s) =>
      s.is_compliant &&
      s.overall_status !== "APPROVED" &&
      s.overall_status !== "OVERRIDDEN"
  );
});

const nonCompliantSites = computed(() => {
  return matrixSites.value.filter((s) => !s.is_compliant);
});

const certifiedSitesCount = computed(() => {
  return matrixSites.value.filter(
    (s) => s.overall_status === "APPROVED" || s.overall_status === "OVERRIDDEN"
  ).length;
});

const isAllCompliantSelected = computed(() => {
  if (compliantSites.value.length === 0) return false;
  return compliantSites.value.every((s) =>
    selectedSiteIds.value.has(s.site_id)
  );
});

const filteredMatrixSites = computed(() => {
  let list = matrixSites.value;

  if (matrixStatusFilter.value === "COMPLIANT") {
    list = list.filter((s) => s.is_compliant);
  } else if (matrixStatusFilter.value === "NON_COMPLIANT") {
    list = list.filter((s) => !s.is_compliant);
  } else if (matrixStatusFilter.value === "CERTIFIED") {
    list = list.filter(
      (s) =>
        s.overall_status === "APPROVED" || s.overall_status === "OVERRIDDEN"
    );
  }

  if (matrixSearchQuery.value.trim()) {
    const q = matrixSearchQuery.value.trim().toLowerCase();
    list = list.filter(
      (s) =>
        s.site_id.toLowerCase().includes(q) ||
        s.site_name.toLowerCase().includes(q)
    );
  }

  return list;
});

// Single-site Computeds
const isGreenlighted = computed(() => {
  const status = gateData.value?.overall_status?.toUpperCase();
  return status === "APPROVED" || status === "OVERRIDDEN";
});

const greenlightStatusBadgeText = computed(() => {
  const status = gateData.value?.overall_status?.toUpperCase();
  if (status === "APPROVED") return "🟢 Greenlight Certified";
  if (status === "OVERRIDDEN") return "⚡ GxP Overridden";
  return "🔒 Execution Gated (Pending Startup)";
});

const passedFacetsCount = computed(() => {
  if (!gateData.value) return 0;
  let count = 0;
  if (gateData.value.facet_regulatory_irb) count++;
  if (gateData.value.facet_cta_budget) count++;
  if (gateData.value.facet_eisf_baseline) count++;
  if (gateData.value.facet_doa_log) count++;
  if (gateData.value.facet_staff_training) count++;
  if (gateData.value.facet_ip_release) count++;
  return count;
});

const readinessPercentage = computed(() => {
  return Math.round((passedFacetsCount.value / 6) * 100);
});

const gaugeColor = computed(() => {
  if (readinessPercentage.value === 100) return "var(--semantic-color-success)";
  if (readinessPercentage.value >= 50) return "var(--semantic-color-warning)";
  return "var(--semantic-color-error)";
});

function getFacetRegulatoryDesc(country) {
  switch (country) {
    case "US":
      return "Form FDA 1572 Statement of Investigator and Institutional Review Board (IRB) approval letter.";
    case "DE":
    case "EU":
      return "Regulation (EU) No 536/2014 EU CTR Part I authorisation and Part II Ethics Committee decision.";
    case "JP":
      return "PMDA Clinical Trial Notification (30-day review completion) and local hospital IRB approval.";
    case "GB":
      return "UK MHRA Clinical Trial Authorisation and Health Research Authority (HRA) REC favorable opinion.";
    default:
      return "National competent authority authorization and Independent Ethics Committee (IEC) approval.";
  }
}

// Matrix Actions
function toggleSelectAllCompliant() {
  if (isAllCompliantSelected.value) {
    selectedSiteIds.value.clear();
  } else {
    compliantSites.value.forEach((s) => selectedSiteIds.value.add(s.site_id));
  }
}

function toggleSiteSelection(siteIdItem, isCompliant, status) {
  if (!isCompliant || status === "APPROVED" || status === "OVERRIDDEN") return;

  if (selectedSiteIds.value.has(siteIdItem)) {
    selectedSiteIds.value.delete(siteIdItem);
  } else {
    selectedSiteIds.value.add(siteIdItem);
  }
}

function openBatchCertifyModal() {
  if (!canCertify.value || selectedSiteIds.value.size === 0) return;
  batchErrorMessage.value = null;
  showBatchCertifyModal.value = true;
}

function inspectSiteDetail(site) {
  activeSiteId.value = site.site_id;
  switchToSingleView();
}

function switchToMatrixView() {
  viewMode.value = "matrix";
  fetchCountryMatrix();
}

function switchToSingleView() {
  viewMode.value = "single";
  evaluateGreenlight();
}

// Authenticated CTMS OpenAPI Methods (zero fake fallbacks, UX-9)
async function fetchCountryMatrix() {
  if (!activeStudyId.value?.trim()) {
    matrixSites.value = [];
    return;
  }
  loading.value = true;
  matrixErrorMessage.value = "";
  try {
    const sites = await ctmsApi.getCountryMatrix(
      activeStudyId.value.trim(),
      activeCountry.value
    );
    matrixSites.value = Array.isArray(sites) ? sites : [];
  } catch (err) {
    matrixSites.value = [];
    const toasts = useToastStore();
    const toastId = toasts.apiError(
      "Country readiness matrix could not be loaded",
      err
    );
    matrixErrorMessage.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Request failed";
  } finally {
    loading.value = false;
  }
}

async function executeBatchCertification() {
  if (!canCertify.value) return;
  isSubmitting.value = true;
  batchErrorMessage.value = null;
  const selectedIds = Array.from(selectedSiteIds.value);
  try {
    const data = await ctmsApi.batchCertifyCountrySites(
      {
        study_id: activeStudyId.value.trim(),
        country_code: activeCountry.value,
        site_ids: selectedIds,
        meaning: batchSigForm.changeReason,
      },
      batchSigForm.changeReason
    );
    batchSuccessData.value = data;
    showBatchCertifyModal.value = false;
    selectedSiteIds.value.clear();
    await fetchCountryMatrix();
  } catch (err) {
    const toasts = useToastStore();
    const toastId = toasts.apiError(
      "Batch greenlight certification failed",
      err
    );
    batchErrorMessage.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Batch certification rejected";
  } finally {
    isSubmitting.value = false;
  }
}

async function evaluateGreenlight() {
  if (!activeStudyId.value?.trim() || !activeSiteId.value?.trim()) {
    gateData.value = null;
    return;
  }
  loading.value = true;
  gateErrorMessage.value = "";
  try {
    gateData.value = await ctmsApi.evaluateSiteGreenlight(
      activeSiteId.value.trim(),
      activeStudyId.value.trim(),
      activeCountry.value
    );
  } catch (err) {
    gateData.value = null;
    const toasts = useToastStore();
    const toastId = toasts.apiError(
      `Couldn't load greenlight status for ${activeSiteId.value}`,
      err
    );
    gateErrorMessage.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Request failed";
  } finally {
    loading.value = false;
  }
}

async function refreshAll() {
  if (viewMode.value === "matrix") {
    await fetchCountryMatrix();
  } else {
    await evaluateGreenlight();
  }
}

function onScopeChange() {
  refreshAll();
}

function onJurisdictionChange() {
  refreshAll();
}

function openCertifyModal() {
  if (!canCertify.value) return;
  certifyErrorMessage.value = "";
  showCertifyModal.value = true;
}

async function executeGreenlightCertification() {
  if (!canCertify.value) return;
  isSubmitting.value = true;
  certifyErrorMessage.value = "";
  try {
    const certified = await ctmsApi.certifySiteGreenlight(
      activeSiteId.value.trim(),
      activeStudyId.value.trim(),
      sigForm.changeReason,
      activeCountry.value
    );
    gateData.value = certified;
    showCertifyModal.value = false;
    emit("greenlight-certified", certified);
  } catch (err) {
    const toasts = useToastStore();
    const toastId = toasts.apiError(
      `Greenlight certification failed for ${activeSiteId.value}`,
      err
    );
    certifyErrorMessage.value =
      toasts.toasts.find((t) => t.id === toastId)?.message ||
      err?.message ||
      "Certification rejected";
  } finally {
    isSubmitting.value = false;
  }
}

function copyToken() {
  if (gateData.value?.greenlight_token) {
    navigator.clipboard?.writeText(gateData.value.greenlight_token);
    useToastStore().push({
      tone: "info",
      title: "Token copied",
      message: "Greenlight activation token copied to clipboard.",
    });
  }
}

// Expose reactivity for Vitest helper access
defineExpose({
  showUploadDocModal,
  docForm,
  submitDoc,
  essentialDocuments,
  countryRules,
  regulatoryMilestones,
  currentTab,
  viewMode,
  matrixSites,
  selectedSiteIds,
  gateData,
  evaluateGreenlight,
  fetchCountryMatrix,
  executeGreenlightCertification,
  executeBatchCertification,
});

onMounted(() => {
  refreshAll();
});
</script>

<style scoped>
/* 100% Vanilla CSS Design Tokens */

.ssu-greenlight-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 24px;
  background-color: var(--semantic-color-surface);
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
}

.view-mode-bar {
  display: flex;
  gap: 12px;
  border-bottom: 2px solid var(--semantic-color-border);
  padding-bottom: 12px;
}

.view-tab-btn {
  padding: 8px 16px;
  background: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-weight: 600;
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  transition: all 0.2s ease;
}

.view-tab-btn.active {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
  border-color: var(--semantic-color-interactive-primary);
  box-shadow: 0 2px 4px rgba(29, 78, 216, 0.2);
}

.console-header {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 16px;
  flex-wrap: wrap;
}

.header-titles {
  display: flex;
  flex-direction: column;
  gap: 6px;
  max-width: 800px;
}

.badge-row {
  display: flex;
  gap: 8px;
  align-items: center;
  flex-wrap: wrap;
}

.badge {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 2px 8px;
  border-radius: 4px;
  text-transform: uppercase;
}

.badge-gxp {
  background: var(--semantic-color-warning-bg-badge);
  color: var(--semantic-color-warning-dark);
}

.badge-regulatory {
  background: var(--semantic-color-indigo-bg);
  color: var(--semantic-color-indigo-text);
}

.badge-standard {
  background: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
}

.badge-country {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}

.badge-approved {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.badge-pending {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.console-title {
  font-size: 1.5rem;
  font-weight: 700;
  color: var(--semantic-color-text);
  margin: 0;
}

.console-subtitle {
  font-size: 0.9rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

.header-actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.role-note {
  margin: 0;
  padding: 8px 12px;
  font-size: 0.85rem;
  color: var(--semantic-color-warning-dark);
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  border-radius: 6px;
}

.alert {
  display: flex;
  gap: 10px;
  align-items: flex-start;
  padding: 12px 14px;
  border-radius: 6px;
  font-size: 0.875rem;
}

.alert-danger {
  background: var(--semantic-color-error-bg-soft);
  border: 1px solid var(--semantic-color-error-border-soft);
  color: var(--semantic-color-error-text-dark);
}

.alert-info {
  background: var(--semantic-color-info-bg);
  border: 1px solid var(--semantic-color-info-border);
  color: var(--semantic-color-info);
}

.scope-bar {
  display: flex;
  gap: 16px;
  background: var(--semantic-color-surface-muted);
  padding: 12px 16px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  align-items: center;
  flex-wrap: wrap;
}

.form-group-inline {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.875rem;
  font-weight: 600;
  color: var(--semantic-color-text-body);
}

.text-input,
.select-input {
  padding: 6px 12px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.875rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.metrics-row {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
  gap: 16px;
}

.metric-card {
  display: flex;
  flex-direction: column;
  padding: 16px;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
}

.metric-card.metric-success {
  border-left: 4px solid var(--semantic-color-success);
}

.metric-card.metric-danger {
  border-left: 4px solid var(--semantic-color-error);
}

.metric-card.metric-info {
  border-left: 4px solid var(--semantic-color-interactive-primary);
}

.metric-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
  text-transform: uppercase;
}

.metric-value {
  font-size: 1.8rem;
  font-weight: 800;
  color: var(--semantic-color-text);
}

.metric-sub {
  font-size: 0.75rem;
  color: var(--semantic-color-text-subtle);
}

.batch-action-bar {
  display: flex;
  justify-content: space-between;
  align-items: center;
  background: var(--semantic-color-info-bg);
  padding: 12px 16px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-info-border);
}

.batch-selection-info {
  display: flex;
  align-items: center;
  gap: 16px;
}

.selection-count-text {
  font-size: 0.9rem;
  color: var(--semantic-color-info);
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  font-weight: 600;
  font-size: 0.875rem;
  cursor: pointer;
  display: inline-flex;
  align-items: center;
  gap: 6px;
  transition: all 0.15s ease;
  border: none;
}

.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}

.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary-hover);
}

.btn-secondary {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  border: 1px solid var(--semantic-color-border-control);
}

.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-surface-header);
}

.btn-override {
  background: var(--semantic-color-ai);
  color: var(--semantic-color-surface);
}
.btn-counter-badge {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-interactive-primary);
  font-size: 0.75rem;
  font-weight: 800;
  border-radius: 999px;
  padding: 2px 8px;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.matrix-table-card {
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  overflow-x: auto;
}

.matrix-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.875rem;
  text-align: left;
}

.matrix-table th {
  background: var(--semantic-color-surface-muted);
  padding: 12px 16px;
  font-weight: 700;
  color: var(--semantic-color-text-secondary);
  border-bottom: 1px solid var(--semantic-color-border);
}

.matrix-table td {
  padding: 12px 16px;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  vertical-align: middle;
}

.row-selected {
  background-color: var(--semantic-color-info-bg);
}

.row-certified {
  background-color: var(--semantic-color-success-bg-soft);
}

.row-non-compliant {
  background-color: var(--semantic-color-error-bg-row);
}

.site-cell {
  display: flex;
  flex-direction: column;
  gap: 2px;
}

.site-id-badge {
  font-weight: 700;
  color: var(--semantic-color-text);
}

.site-name-text {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.site-jurisdiction-tag {
  font-size: 0.75rem;
  color: var(--semantic-color-primary);
  font-weight: 600;
}

.facets-pill-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 4px;
}

.facet-pill {
  font-size: 0.7rem;
  font-weight: 600;
  padding: 2px 6px;
  border-radius: 4px;
}

.pill-pass {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.pill-fail {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.readiness-score-badge {
  font-size: 0.85rem;
  font-weight: 800;
  padding: 2px 8px;
  border-radius: 12px;
}

.badge-score-100 {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}

.badge-score-fail {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
}

.exception-warning {
  display: block;
  font-size: 0.7rem;
  color: var(--semantic-color-error-text-dark);
  font-weight: 600;
  margin-top: 2px;
}

.status-chip {
  font-size: 0.75rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 12px;
  display: inline-block;
}

.chip-approved {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.chip-overridden {
  background: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
}

.chip-compliant {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary-dark);
}

.chip-blocked {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.batch-success-banner {
  background: var(--semantic-color-success-bg-soft);
  border: 1px solid var(--semantic-color-success-border);
  padding: 16px;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.banner-header {
  display: flex;
  gap: 12px;
  align-items: flex-start;
}

.banner-icon {
  font-size: 1.5rem;
}

.banner-tokens-list {
  display: flex;
  flex-direction: column;
  gap: 4px;
  background: var(--semantic-color-surface);
  padding: 8px 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-success-border);
}

.token-code {
  font-family: monospace;
  font-size: 0.75rem;
  color: var(--semantic-color-success-dark);
}

/* Modals */

.modal-backdrop {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(15, 23, 42, 0.6);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-card {
  background: var(--semantic-color-surface);
  border-radius: 8px;
  width: 90%;
  max-width: 500px;
  padding: 24px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.1);
}

.modal-lg {
  max-width: 700px;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 12px;
}

.modal-header h3 {
  margin: 0;
  font-size: 1.1rem;
  font-weight: 700;
  color: var(--semantic-color-text);
}

.modal-body {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.batch-summary-box {
  background: var(--semantic-color-surface-muted);
  padding: 12px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border-muted);
}

.selected-sites-grid {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 8px;
}

.selected-site-tag {
  background: var(--semantic-color-info-light);
  color: var(--semantic-color-info);
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.8rem;
  font-weight: 700;
}

.esign-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
  background: var(--semantic-color-warning-bg-soft);
  border: 1px solid var(--semantic-color-warning-border);
  padding: 16px;
  border-radius: 6px;
}

.esign-title {
  margin: 0;
  font-size: 0.95rem;
  font-weight: 700;
  color: var(--semantic-color-warning-dark);
}

.form-row {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.textarea-input {
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  font-size: 0.875rem;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 12px;
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 12px;
}

.btn-icon-only {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 1rem;
}

/* Scorecard Grid for Single View */

.scorecard-section {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.facets-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(280px, 1fr));
  gap: 16px;
}

.facet-card {
  display: flex;
  flex-direction: column;
  gap: 8px;
  padding: 16px;
  border-radius: 8px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
}

.facet-passed {
  border-left: 4px solid var(--semantic-color-success);
}

.facet-failed {
  border-left: 4px solid var(--semantic-color-error);
}

.facet-header {
  display: flex;
  align-items: center;
  gap: 8px;
}

.facet-icon {
  font-size: 1.2rem;
}

.facet-titles {
  display: flex;
  flex-direction: column;
  flex: 1;
}

.facet-name {
  font-weight: 700;
  font-size: 0.9rem;
  color: var(--semantic-color-text);
}

.facet-rule-tag {
  font-size: 0.75rem;
  color: var(--semantic-color-text-secondary);
}

.facet-status-chip {
  font-size: 0.7rem;
  font-weight: 800;
  padding: 2px 6px;
  border-radius: 4px;
}

.chip-pass {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success-dark);
}

.chip-fail {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error-text-dark);
}

.facet-desc {
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
  margin: 0;
}

.readiness-gauge {
  display: flex;
  align-items: center;
  gap: 12px;
}

.gauge-bar-outer {
  width: 120px;
  height: 8px;
  background: var(--semantic-color-border);
  border-radius: 4px;
  overflow: hidden;
}

.gauge-bar-inner {
  height: 100%;
  transition: width 0.3s ease;
}

.gauge-text {
  font-size: 0.875rem;
  font-weight: 700;
}
</style>
