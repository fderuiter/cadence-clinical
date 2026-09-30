<template>
  <div class="ssu-greenlight-console card">
    <!-- Header Section -->
    <div class="console-header">
      <div class="header-titles">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-regulatory">ICH GCP E6(R2) §8</span>
          <span class="badge badge-standard">PRD-SSU-001/002/003</span>
          <span
            class="badge"
            :class="isGreenlighted ? 'badge-approved' : 'badge-pending'"
          >
            {{ greenlightStatusBadgeText }}
          </span>
        </div>
        <h2 class="console-title">
          Country-Aware Site Activation Engine &amp; Greenlight Console
        </h2>
        <p class="console-subtitle">
          Real-time multi-factor greenlight evaluator across the 6 operational
          facets with statutory jurisdiction rules, cryptographic HMAC-SHA256
          tokens, and dual-signature GxP overrides.
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
          v-if="!isGreenlighted"
          class="btn btn-override"
          @click="showOverrideModal = true"
        >
          <span class="btn-icon">⚡</span> Executive Override
        </button>
        <button
          class="btn btn-primary"
          :disabled="loading || isGreenlighted"
          @click="openCertifyModal"
        >
          <span class="btn-icon">🛡️</span> Certify Greenlight
        </button>
      </div>
    </div>

    <!-- Active Site & Jurisdiction Scope Bar -->
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
          <option value="EU">🇪🇺 European Union (EU CTR Part I/II + EC)</option>
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
        <button class="btn-icon-only" title="Copy Token" @click="copyToken">
          📋
        </button>
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
        <!-- Facet 1: Regulatory & IRB -->
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
          <div class="facet-footer">
            <span class="facet-meta">
              {{
                gateData?.facet_regulatory_irb
                  ? "Verified by Authority"
                  : "Pending IRB / Regulatory Approval"
              }}
            </span>
          </div>
        </div>

        <!-- Facet 2: Executed CTA & Budget -->
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
          <div class="facet-footer">
            <span class="facet-meta">
              {{
                gateData?.facet_cta_budget
                  ? "Contract Fully Executed"
                  : "Missing Executed CTA"
              }}
            </span>
          </div>
        </div>

        <!-- Facet 3: Certified eISF Binder -->
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
              <span class="facet-name">Certified eISF Baseline</span>
              <span class="facet-rule-tag">DIA Zone 05 / ICH §8</span>
            </div>
            <span
              class="facet-status-chip"
              :class="gateData?.facet_eisf_baseline ? 'chip-pass' : 'chip-fail'"
            >
              {{ gateData?.facet_eisf_baseline ? "PASSED" : "ACTION REQ" }}
            </span>
          </div>
          <p class="facet-desc">
            Investigator CVs, medical licenses, laboratory certifications, and
            baseline DIA binder artifacts.
          </p>
          <div class="facet-footer">
            <span class="facet-meta">
              {{
                gateData?.facet_eisf_baseline
                  ? "Baseline Binder Certified"
                  : "Incomplete Essential Docs"
              }}
            </span>
          </div>
        </div>

        <!-- Facet 4: PI Signed DOA Log -->
        <div
          class="facet-card"
          :class="{
            'facet-passed': gateData?.facet_doa_log,
            'facet-failed': !gateData?.facet_doa_log,
          }"
        >
          <div class="facet-header">
            <span class="facet-icon">✍️</span>
            <div class="facet-titles">
              <span class="facet-name">PI Delegation of Authority</span>
              <span class="facet-rule-tag">ICH GCP E6(R2) §4.1.5</span>
            </div>
            <span
              class="facet-status-chip"
              :class="gateData?.facet_doa_log ? 'chip-pass' : 'chip-fail'"
            >
              {{ gateData?.facet_doa_log ? "PASSED" : "ACTION REQ" }}
            </span>
          </div>
          <p class="facet-desc">
            Principal Investigator signed and countersigned site personnel task
            delegation log.
          </p>
          <div class="facet-footer">
            <span class="facet-meta">
              {{
                gateData?.facet_doa_log
                  ? "PI Signed-off on DOA"
                  : "Pending PI Electronic Signature"
              }}
            </span>
          </div>
        </div>

        <!-- Facet 5: GCP & Protocol Training -->
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
              <span class="facet-name">Staff GCP &amp; Protocol Training</span>
              <span class="facet-rule-tag">PRD-TRN-001 / ICH §4</span>
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
            Verified TransCelerate GCP certificates and protocol amendment
            training for all active site staff.
          </p>
          <div class="facet-footer">
            <span class="facet-meta">
              {{
                gateData?.facet_staff_training
                  ? "100% Active Staff Qualified"
                  : "Pending Staff Training"
              }}
            </span>
          </div>
        </div>

        <!-- Facet 6: IP Depot Release -->
        <div
          class="facet-card"
          :class="{
            'facet-passed': gateData?.facet_ip_release,
            'facet-failed': !gateData?.facet_ip_release,
          }"
        >
          <div class="facet-header">
            <span class="facet-icon">📦</span>
            <div class="facet-titles">
              <span class="facet-name">IP Depot Greenlight Release</span>
              <span class="facet-rule-tag">RTSM Supply Chain</span>
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
            shipment and dispensing.
          </p>
          <div class="facet-footer">
            <span class="facet-meta">
              {{
                gateData?.facet_ip_release
                  ? "Depot Release Authorized"
                  : "Pending Depot Greenlight"
              }}
            </span>
          </div>
        </div>
      </div>
    </div>

    <!-- Navigation Tabs: Essential Documents | Country Rules Matrix | Regulatory Milestones -->
    <div class="workspace-tabs">
      <button
        class="tab-nav-btn"
        :class="{ active: currentTab === 'documents' }"
        @click="currentTab = 'documents'"
      >
        📄 Essential Documents Review Queue ({{ essentialDocuments.length }})
      </button>
      <button
        class="tab-nav-btn"
        :class="{ active: currentTab === 'rules' }"
        @click="currentTab = 'rules'"
      >
        🌐 Country Prerequisite Rules Matrix ({{ countryRules.length }})
      </button>
      <button
        class="tab-nav-btn"
        :class="{ active: currentTab === 'milestones' }"
        @click="currentTab = 'milestones'"
      >
        🏛️ Regulatory Submissions &amp; Approvals ({{
          regulatoryMilestones.length
        }})
      </button>
    </div>

    <!-- Tab 1: Essential Documents Queue -->
    <div v-if="currentTab === 'documents'" class="tab-panel">
      <div class="panel-header">
        <h4>Essential Startup Documents &amp; Regulatory Binder</h4>
        <button
          class="btn btn-sm btn-primary"
          @click="showUploadDocModal = true"
        >
          + Submit Essential Document
        </button>
      </div>

      <table class="data-table">
        <thead>
          <tr>
            <th>Document Type</th>
            <th>File Name</th>
            <th>SHA-256 Hash</th>
            <th>Status</th>
            <th>Reviewed By</th>
            <th>Actions</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="doc in essentialDocuments" :key="doc.id">
            <td>
              <span class="doc-type-badge">{{ doc.document_type }}</span>
            </td>
            <td>
              <strong>{{ doc.file_name }}</strong>
            </td>
            <td>
              <code class="hash-code">{{ doc.file_hash.slice(0, 12) }}...</code>
            </td>
            <td>
              <span
                class="status-pill"
                :class="'pill-' + doc.status.toLowerCase()"
              >
                {{ doc.status }}
              </span>
            </td>
            <td>{{ doc.reviewed_by || "—" }}</td>
            <td>
              <div v-if="doc.status !== 'APPROVED'" class="table-actions">
                <button
                  class="btn-action btn-approve"
                  title="Approve Document"
                  @click="reviewDocument(doc.id, 'APPROVED')"
                >
                  ✓ Approve
                </button>
                <button
                  class="btn-action btn-reject"
                  title="Reject Document"
                  @click="reviewDocument(doc.id, 'REJECTED')"
                >
                  ✕ Reject
                </button>
              </div>
              <span v-else class="text-success font-semibold">✓ Verified</span>
            </td>
          </tr>
          <tr v-if="essentialDocuments.length === 0">
            <td colspan="6" class="text-center empty-state">
              No essential documents submitted yet for Site {{ activeSiteId }}.
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Tab 2: Country Rules Matrix -->
    <div v-if="currentTab === 'rules'" class="tab-panel">
      <div class="panel-header">
        <h4>Statutory Prerequisite Matrix for {{ activeCountry }}</h4>
        <span class="badge badge-regulatory">Jurisdiction Engine</span>
      </div>

      <table class="data-table">
        <thead>
          <tr>
            <th>Criterion Code</th>
            <th>Criterion Name</th>
            <th>Operational Facet</th>
            <th>Mandatory</th>
            <th>Description</th>
          </tr>
        </thead>
        <tbody>
          <tr
            v-for="rule in countryRules"
            :key="rule.id || rule.criterion_code"
          >
            <td>
              <code>{{ rule.criterion_code }}</code>
            </td>
            <td>
              <strong>{{ rule.criterion_name }}</strong>
            </td>
            <td>
              <span class="facet-tag">{{ rule.facet }}</span>
            </td>
            <td>
              <span
                class="badge"
                :class="
                  rule.is_mandatory ? 'badge-mandatory' : 'badge-optional'
                "
              >
                {{ rule.is_mandatory ? "Mandatory" : "Optional" }}
              </span>
            </td>
            <td>{{ rule.description || "—" }}</td>
          </tr>
          <tr v-if="countryRules.length === 0">
            <td colspan="5" class="text-center empty-state">
              No rules configured for {{ activeCountry }}.
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Tab 3: Regulatory Milestones -->
    <div v-if="currentTab === 'milestones'" class="tab-panel">
      <div class="panel-header">
        <h4>National Regulatory Submissions Tracker</h4>
        <button
          class="btn btn-sm btn-primary"
          @click="showAddMilestoneModal = true"
        >
          + Add Regulatory Milestone
        </button>
      </div>

      <table class="data-table">
        <thead>
          <tr>
            <th>Country</th>
            <th>Milestone / Authority</th>
            <th>Approval Number</th>
            <th>Planned Date</th>
            <th>Actual Date</th>
            <th>Status</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="ms in regulatoryMilestones" :key="ms.id">
            <td>
              <strong>{{ ms.country_code }}</strong>
            </td>
            <td>
              <div>{{ ms.milestone_type }}</div>
              <small class="text-muted">{{ ms.authority_name }}</small>
            </td>
            <td>
              <code>{{ ms.approval_number || "Pending" }}</code>
            </td>
            <td>{{ ms.planned_date || "—" }}</td>
            <td>{{ ms.actual_date || "—" }}</td>
            <td>
              <span
                class="status-pill"
                :class="'pill-' + ms.status.toLowerCase()"
              >
                {{ ms.status }}
              </span>
            </td>
          </tr>
          <tr v-if="regulatoryMilestones.length === 0">
            <td colspan="6" class="text-center empty-state">
              No regulatory milestones recorded yet for Study
              {{ activeStudyId }}.
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Certification Modal -->
    <div v-if="showCertifyModal" class="modal-overlay">
      <div class="modal-card">
        <div class="modal-header">
          <h3>21 CFR Part 11 Greenlight Activation Certification</h3>
          <button class="btn-close" @click="showCertifyModal = false">✕</button>
        </div>
        <div class="modal-body">
          <p class="modal-intro">
            You are formally certifying that Site
            <strong>{{ activeSiteId }}</strong> on Study
            <strong>{{ activeStudyId }}</strong> has satisfied all 6 operational
            startup facets under ICH GCP E6(R2) Section 8 and statutory
            {{ activeCountry }} regulations.
          </p>

          <div class="signature-form">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Full Legal Name:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="sigForm.name"
                type="text"
                class="text-input"
                placeholder="e.g. Dr. Jane Doe"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Professional Role / Title:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="sigForm.role"
                type="text"
                class="text-input"
                placeholder="e.g. Regulatory Affairs Director"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Electronic Signature Meaning:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                type="text"
                class="text-input"
                disabled
                value="I certify regulatory greenlight activation compliance and authorize trial execution."
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Change Justification Reason:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="sigForm.changeReason"
                type="text"
                class="text-input"
                placeholder="e.g. All 6 startup facets verified and approved"
              />
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showCertifyModal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="!sigForm.name || !sigForm.role || isSubmitting"
            @click="executeGreenlightCertification"
          >
            {{ isSubmitting ? "Certifying..." : "Sign & Certify Greenlight" }}
          </button>
        </div>
      </div>
    </div>

    <!-- Executive Dual-Signature Override Modal -->
    <div v-if="showOverrideModal" class="modal-overlay">
      <div class="modal-card">
        <div class="modal-header">
          <h3>Executive Dual-Signature GxP Greenlight Override</h3>
          <button class="btn-close" @click="showOverrideModal = false">
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="alert-warning">
            ⚠️ <strong>Executive GxP Override:</strong> Bypasses unfulfilled
            operational facets under dual Sponsor Executive and Medical Monitor
            electronic signatures in strict compliance with 21 CFR Part 11.
          </div>

          <div class="signature-form">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Sponsor Executive User ID:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="overrideForm.sponsorId"
                type="text"
                class="text-input"
                placeholder="e.g. sponsor-exec-01"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Sponsor Signature Hash / Key:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="overrideForm.sponsorSig"
                type="text"
                class="text-input"
                placeholder="Cryptographic Sponsor Signature"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Medical Monitor User ID:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="overrideForm.medicalId"
                type="text"
                class="text-input"
                placeholder="e.g. med-monitor-02"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Medical Monitor Signature Hash / Key:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="overrideForm.medicalSig"
                type="text"
                class="text-input"
                placeholder="Cryptographic Medical Signature"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label
                >Mandatory Regulatory Justification (Min 10 characters):</label
              >
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <textarea
                v-model="overrideForm.justification"
                class="textarea-input"
                rows="3"
                placeholder="Provide detailed clinical and regulatory rationale for greenlight override..."
              />
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showOverrideModal = false">
            Cancel
          </button>
          <button
            class="btn btn-override"
            :disabled="
              !overrideForm.sponsorId ||
              !overrideForm.medicalId ||
              overrideForm.justification.length < 10 ||
              isSubmitting
            "
            @click="executeExecutiveOverride"
          >
            {{
              isSubmitting ? "Authorizing..." : "Authorize Executive Override"
            }}
          </button>
        </div>
      </div>
    </div>

    <!-- Submit Essential Document Modal -->
    <div v-if="showUploadDocModal" class="modal-overlay">
      <div class="modal-card">
        <div class="modal-header">
          <h3>Submit Essential Document</h3>
          <button class="btn-close" @click="showUploadDocModal = false">
            ✕
          </button>
        </div>
        <div class="modal-body">
          <div class="signature-form">
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Document Type:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <select v-model="docForm.type" class="select-input">
                <option value="FDA_1572">
                  Form FDA 1572 Statement of Investigator
                </option>
                <option value="LOCAL_IRB_APPROVAL">
                  Institutional Review Board (IRB) Approval
                </option>
                <option value="EU_CTR_PART_I">
                  EU CTR Part I Authorisation
                </option>
                <option value="EU_CTR_PART_II_EC">
                  EU CTR Part II &amp; Ethics Decision
                </option>
                <option value="PMDA_NOTIFICATION">
                  PMDA Clinical Trial Notification
                </option>
                <option value="MHRA_AUTHORIZATION">
                  UK MHRA Clinical Trial Authorisation
                </option>
                <option value="UK_REC_APPROVAL">
                  UK HRA &amp; REC Ethics Approval
                </option>
                <option value="SITE_CONTRACT">
                  Executed Clinical Trial Agreement (CTA)
                </option>
                <option value="EISF_BASELINE_CERT">
                  Certified eISF Regulatory Binder Baseline
                </option>
                <option value="DOA_LOG">
                  Principal Investigator Signed DOA Log
                </option>
                <option value="GCP_TRAINING">
                  GCP &amp; Protocol Amendment Training Certificate
                </option>
                <option value="IP_RELEASE_AUTHORIZATION">
                  Investigational Product Depot Release
                </option>
                <option value="FINANCIAL_DISCLOSURE">
                  Financial Disclosure Form (FDF)
                </option>
              </select>
            </div>
            <div class="form-group">
              <label for="ssu-doc-file">Document File:</label>
              <input
                id="ssu-doc-file"
                type="file"
                class="text-input"
                data-testid="ssu-doc-file"
                @change="onDocFileSelected"
              />
            </div>
            <div class="form-group">
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>File Name:</label>
              <!-- eslint-disable-next-line vuejs-accessibility/form-control-has-label -->
              <input
                v-model="docForm.fileName"
                type="text"
                class="text-input"
                placeholder="e.g. FDA_1572_Signed_Site101.pdf"
              />
            </div>
          </div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-secondary" @click="showUploadDocModal = false">
            Cancel
          </button>
          <button
            class="btn btn-primary"
            :disabled="!docForm.file || !docForm.fileName || isSubmitting"
            @click="submitDoc"
          >
            Submit Document
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { computed, onMounted, reactive, ref } from "vue";

import { useToastStore } from "../../stores/toasts.js";
import { ensureOk } from "../../utils/apiErrors.js";

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
});

const emit = defineEmits(["greenlight-certified", "override-authorized"]);

// Reactive State
const activeStudyId = ref(props.studyId);
const activeSiteId = ref(props.siteId);
const activeCountry = ref(props.countryCode);
const loading = ref(false);
const isSubmitting = ref(false);
const currentTab = ref("documents");

// Modals
const showCertifyModal = ref(false);
const showOverrideModal = ref(false);
const showUploadDocModal = ref(false);
const showAddMilestoneModal = ref(false);

// Forms
const sigForm = reactive({
  name: "Dr. Eleanor Vance",
  role: "VP of Regulatory & Clinical Operations",
  changeReason: "All 6 startup facets verified and approved",
});

const overrideForm = reactive({
  sponsorId: "sponsor-admin-01",
  sponsorSig: "sig_sponsor_hmac_9942a78e",
  medicalId: "medical-monitor-02",
  medicalSig: "sig_med_monitor_hmac_3311b5df",
  justification:
    "Accelerated pandemic response protocol activation under sponsor risk assessment.",
});

const docForm = reactive({
  type: "FDA_1572",
  fileName: "Form_FDA_1572_Site101_v1.pdf",
  file: null,
});

function onDocFileSelected(event) {
  const file = event.target?.files?.[0] || null;
  docForm.file = file;
  if (file) docForm.fileName = file.name;
}

/** SHA-256 of the file's bytes as lowercase hex. */
async function sha256Hex(file) {
  const hashBuffer = await crypto.subtle.digest(
    "SHA-256",
    await file.arrayBuffer()
  );
  return Array.from(new Uint8Array(hashBuffer))
    .map((b) => b.toString(16).padStart(2, "0"))
    .join("");
}

// Data
const gateData = ref(null);
const essentialDocuments = ref([]);
const countryRules = ref([]);
const regulatoryMilestones = ref([]);

// Computed
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
  if (readinessPercentage.value === 100) return "#10b981";
  if (readinessPercentage.value >= 50) return "#f59e0b";
  return "#ef4444";
});

function getFacetRegulatoryDesc(country) {
  switch (country) {
    case "US":
      return "Form FDA 1572 Statement of Investigator and Institutional Review Board (IRB) approval letter.";
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

// API Methods
async function evaluateGreenlight() {
  loading.value = true;
  try {
    const res = await fetch(
      `/api/v1/ctms/startup/sites/${activeSiteId.value}/greenlight?study_id=${activeStudyId.value}&country_code=${activeCountry.value}`
    );
    if (res.ok) {
      gateData.value = await res.json();
    }
  } catch (err) {
    console.error("Error evaluating greenlight:", err);
  } finally {
    loading.value = false;
  }
}

async function fetchEssentialDocuments() {
  try {
    const res = await fetch(
      `/api/v1/ctms/startup/documents?study_id=${activeStudyId.value}&site_id=${activeSiteId.value}`
    );
    if (res.ok) {
      essentialDocuments.value = await res.json();
    }
  } catch (err) {
    console.error("Error fetching documents:", err);
  }
}

async function fetchCountryRules() {
  try {
    const res = await fetch(
      `/api/v1/ctms/startup/rules?study_id=${activeStudyId.value}&country_code=${activeCountry.value}`
    );
    if (res.ok) {
      countryRules.value = await res.json();
    }
  } catch (err) {
    console.error("Error fetching rules:", err);
  }
}

async function fetchRegulatoryMilestones() {
  try {
    const res = await fetch(
      `/api/v1/ctms/startup/milestones?study_id=${activeStudyId.value}&country_code=${activeCountry.value}`
    );
    if (res.ok) {
      regulatoryMilestones.value = await res.json();
    }
  } catch (err) {
    console.error("Error fetching milestones:", err);
  }
}

async function refreshAll() {
  await Promise.all([
    evaluateGreenlight(),
    fetchEssentialDocuments(),
    fetchCountryRules(),
    fetchRegulatoryMilestones(),
  ]);
}

function onScopeChange() {
  refreshAll();
}

function onJurisdictionChange() {
  refreshAll();
}

async function reviewDocument(docId, status) {
  try {
    const res = await fetch(`/api/v1/ctms/startup/documents/${docId}/review`, {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason": `Document ${status.toLowerCase()} by regulatory reviewer`,
      },
      body: JSON.stringify({
        status,
        review_notes: `Reviewed and marked ${status} in SSU Console`,
      }),
    });
    if (res.ok) {
      await refreshAll();
    }
  } catch (err) {
    console.error("Error reviewing document:", err);
  }
}

async function submitDoc() {
  const toasts = useToastStore();
  // #4523: the document hash must be computed from the actual file; never submit a placeholder hash.
  if (!docForm.file) {
    toasts.push({
      title: "Essential document not submitted",
      message: "Select the document file so its SHA-256 hash can be computed.",
    });
    return;
  }
  isSubmitting.value = true;
  try {
    const fileHash = await sha256Hex(docForm.file);
    const res = await fetch("/api/v1/ctms/startup/documents", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason": "Uploaded essential document via SSU Console",
      },
      body: JSON.stringify({
        study_id: activeStudyId.value,
        site_id: activeSiteId.value,
        document_type: docForm.type,
        file_name: docForm.fileName,
        file_hash: fileHash,
      }),
    });
    await ensureOk(res);
    showUploadDocModal.value = false;
    docForm.file = null;
    await refreshAll();
  } catch (err) {
    toasts.apiError("Essential document submission failed", err);
  } finally {
    isSubmitting.value = false;
  }
}

function openCertifyModal() {
  showCertifyModal.value = true;
}

async function executeGreenlightCertification() {
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/ctms/startup/sites/${activeSiteId.value}/greenlight/certify?study_id=${activeStudyId.value}&country_code=${activeCountry.value}`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Change-Reason": sigForm.changeReason,
        },
      }
    );
    if (res.ok) {
      gateData.value = await res.json();
      showCertifyModal.value = false;
      emit("greenlight-certified", gateData.value);
    } else {
      const err = await res.json();
      alert(`Certification Failed: ${err.detail || "Requirements incomplete"}`);
    }
  } catch (err) {
    console.error("Error certifying greenlight:", err);
  } finally {
    isSubmitting.value = false;
  }
}

async function executeExecutiveOverride() {
  isSubmitting.value = true;
  try {
    const res = await fetch(
      `/api/v1/ctms/startup/sites/${activeSiteId.value}/greenlight/override`,
      {
        method: "POST",
        headers: {
          "Content-Type": "application/json",
          "X-Change-Reason": overrideForm.justification,
        },
        body: JSON.stringify({
          study_id: activeStudyId.value,
          sponsor_user_id: overrideForm.sponsorId,
          medical_user_id: overrideForm.medicalId,
          sponsor_signature: overrideForm.sponsorSig,
          medical_signature: overrideForm.medicalSig,
          justification: overrideForm.justification,
          country_code: activeCountry.value,
        }),
      }
    );
    if (res.ok) {
      gateData.value = await res.json();
      showOverrideModal.value = false;
      emit("override-authorized", gateData.value);
    } else {
      const err = await res.json();
      alert(
        `Override Failed: ${err.detail || "Invalid signatures or missing justification"}`
      );
    }
  } catch (err) {
    console.error("Error executing override:", err);
  } finally {
    isSubmitting.value = false;
  }
}

function copyToken() {
  if (gateData.value?.greenlight_token) {
    navigator.clipboard.writeText(gateData.value.greenlight_token);
    alert("Greenlight activation token copied to clipboard!");
  }
}

onMounted(() => {
  refreshAll();
});
</script>

<style scoped>
/* 100% Vanilla CSS Design Tokens (0 Tailwind Classes) */

.ssu-greenlight-console {
  display: flex;
  flex-direction: column;
  gap: 20px;
  padding: 24px;
  background-color: #ffffff;
  border-radius: 8px;
  border: 1px solid var(--border, #cbd5e1);
  box-shadow: 0 1px 3px rgba(0, 0, 0, 0.05);
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
}

.badge-row {
  display: flex;
  gap: 8px;
  flex-wrap: wrap;
  align-items: center;
}

.badge {
  font-size: 11px;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  padding: 3px 8px;
  border-radius: 4px;
}

.badge-gxp {
  background-color: #e0e7ff;
  color: #3730a3;
}

.badge-regulatory {
  background-color: #fef3c7;
  color: #92400e;
}

.badge-standard {
  background-color: #e2e8f0;
  color: #334155;
}

.badge-approved {
  background-color: #d1fae5;
  color: #065f46;
}

.badge-pending {
  background-color: #fee2e2;
  color: #991b1b;
}

.badge-mandatory {
  background-color: #fee2e2;
  color: #b91c1c;
}

.badge-optional {
  background-color: #f1f5f9;
  color: #64748b;
}

.console-title {
  font-size: 20px;
  font-weight: 800;
  color: #0f172a;
}

.console-subtitle {
  font-size: 13px;
  color: #64748b;
  max-width: 850px;
}

.header-actions {
  display: flex;
  gap: 10px;
  align-items: center;
}

.btn {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: 13px;
  font-weight: 600;
  padding: 8px 14px;
  border-radius: 6px;
  border: 1px solid transparent;
  cursor: pointer;
  transition: all 0.15s ease-in-out;
}

.btn-primary {
  background-color: #0f4c81;
  color: #ffffff;
}

.btn-primary:hover:not(:disabled) {
  background-color: #09345a;
}

.btn-secondary {
  background-color: #f8fafc;
  color: #334155;
  border-color: #cbd5e1;
}

.btn-secondary:hover:not(:disabled) {
  background-color: #e2e8f0;
}

.btn-override {
  background-color: #7c3aed;
  color: #ffffff;
}

.btn-override:hover:not(:disabled) {
  background-color: #6d28d9;
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.btn-sm {
  font-size: 12px;
  padding: 5px 10px;
}

.scope-bar {
  display: flex;
  gap: 20px;
  align-items: center;
  flex-wrap: wrap;
  padding: 12px 16px;
  background-color: #f8fafc;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
}

.form-group-inline {
  display: flex;
  align-items: center;
  gap: 8px;
}

.form-group-inline label {
  font-size: 12px;
  font-weight: 700;
  color: #475569;
}

.text-input,
.select-input,
.textarea-input {
  font-family: inherit;
  font-size: 13px;
  padding: 6px 10px;
  border: 1px solid #cbd5e1;
  border-radius: 4px;
  background-color: #ffffff;
  color: #0f172a;
}

.text-input:focus,
.select-input:focus,
.textarea-input:focus {
  outline: none;
  border-color: #0f4c81;
  box-shadow: 0 0 0 2px rgba(15, 76, 129, 0.15);
}

.token-status-pill {
  margin-left: auto;
  display: flex;
  align-items: center;
  gap: 6px;
  background-color: #ecfdf5;
  border: 1px solid #a7f3d0;
  padding: 4px 10px;
  border-radius: 20px;
}

.token-label {
  font-size: 11px;
  font-weight: 700;
  color: #065f46;
}

.token-snip {
  font-family: monospace;
  font-size: 11px;
  color: #047857;
  background-color: #d1fae5;
  padding: 1px 4px;
  border-radius: 3px;
}

.btn-icon-only {
  background: none;
  border: none;
  cursor: pointer;
  font-size: 12px;
}

.scorecard-section {
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.section-title-row {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 10px;
}

.section-title {
  font-size: 15px;
  font-weight: 700;
  color: #1e293b;
}

.readiness-gauge {
  display: flex;
  align-items: center;
  gap: 10px;
}

.gauge-bar-outer {
  width: 140px;
  height: 8px;
  background-color: #e2e8f0;
  border-radius: 4px;
  overflow: hidden;
}

.gauge-bar-inner {
  height: 100%;
  transition: width 0.3s ease-in-out;
}

.gauge-text {
  font-size: 12px;
  font-weight: 700;
}

.facets-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
  gap: 14px;
}

.facet-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  padding: 14px;
  border-radius: 6px;
  border: 1px solid #e2e8f0;
  background-color: #ffffff;
  transition: border-color 0.15s ease;
}

.facet-passed {
  border-left: 4px solid #10b981;
}

.facet-failed {
  border-left: 4px solid #ef4444;
}

.facet-header {
  display: flex;
  align-items: center;
  gap: 10px;
}

.facet-icon {
  font-size: 18px;
}

.facet-titles {
  display: flex;
  flex-direction: column;
  flex: 1;
}

.facet-name {
  font-size: 13px;
  font-weight: 700;
  color: #0f172a;
}

.facet-rule-tag {
  font-size: 10px;
  color: #64748b;
  text-transform: uppercase;
}

.facet-status-chip {
  font-size: 10px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
}

.chip-pass {
  background-color: #d1fae5;
  color: #065f46;
}

.chip-fail {
  background-color: #fee2e2;
  color: #991b1b;
}

.facet-desc {
  font-size: 12px;
  color: #475569;
  line-height: 1.4;
  flex: 1;
}

.facet-footer {
  border-top: 1px solid #f1f5f9;
  padding-top: 6px;
}

.facet-meta {
  font-size: 11px;
  font-weight: 600;
  color: #64748b;
}

.workspace-tabs {
  display: flex;
  gap: 4px;
  border-bottom: 2px solid #e2e8f0;
  margin-top: 6px;
}

.tab-nav-btn {
  font-family: inherit;
  font-size: 13px;
  font-weight: 600;
  padding: 8px 14px;
  background: none;
  border: none;
  border-bottom: 2px solid transparent;
  color: #64748b;
  cursor: pointer;
  margin-bottom: -2px;
}

.tab-nav-btn.active {
  color: #0f4c81;
  border-bottom-color: #0f4c81;
}

.tab-panel {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.panel-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.panel-header h4 {
  font-size: 14px;
  font-weight: 700;
  color: #1e293b;
}

.data-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
}

.data-table th {
  background-color: #f8fafc;
  color: #475569;
  font-weight: 700;
  text-align: left;
  padding: 8px 12px;
  border-bottom: 1px solid #cbd5e1;
}

.data-table td {
  padding: 8px 12px;
  border-bottom: 1px solid #f1f5f9;
  color: #1e293b;
}

.doc-type-badge {
  font-family: monospace;
  font-size: 11px;
  background-color: #f1f5f9;
  padding: 2px 6px;
  border-radius: 4px;
  color: #0f4c81;
  font-weight: 600;
}

.hash-code {
  font-family: monospace;
  font-size: 11px;
  color: #64748b;
}

.status-pill {
  font-size: 10px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 3px;
  text-transform: uppercase;
}

.pill-approved,
.pill-completed {
  background-color: #d1fae5;
  color: #065f46;
}

.pill-submitted,
.pill-pending {
  background-color: #fef3c7;
  color: #92400e;
}

.pill-rejected,
.pill-expired {
  background-color: #fee2e2;
  color: #991b1b;
}

.table-actions {
  display: flex;
  gap: 6px;
}

.btn-action {
  font-size: 11px;
  font-weight: 600;
  padding: 3px 8px;
  border-radius: 4px;
  border: 1px solid transparent;
  cursor: pointer;
}

.btn-approve {
  background-color: #d1fae5;
  color: #065f46;
  border-color: #a7f3d0;
}

.btn-approve:hover {
  background-color: #a7f3d0;
}

.btn-reject {
  background-color: #fee2e2;
  color: #991b1b;
  border-color: #fca5a5;
}

.btn-reject:hover {
  background-color: #fca5a5;
}

.facet-tag {
  font-size: 10px;
  font-weight: 700;
  background-color: #e0f2fe;
  color: #0369a1;
  padding: 2px 6px;
  border-radius: 3px;
}

.empty-state {
  color: #94a3b8;
  padding: 24px;
}

.modal-overlay {
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background-color: rgba(15, 23, 42, 0.6);
  display: flex;
  justify-content: center;
  align-items: center;
  z-index: 1000;
}

.modal-card {
  background-color: #ffffff;
  border-radius: 8px;
  width: 540px;
  max-width: 90vw;
  box-shadow: 0 10px 25px rgba(0, 0, 0, 0.2);
  display: flex;
  flex-direction: column;
}

.modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  padding: 16px 20px;
  border-bottom: 1px solid #e2e8f0;
}

.modal-header h3 {
  font-size: 16px;
  font-weight: 700;
  color: #0f172a;
}

.btn-close {
  background: none;
  border: none;
  font-size: 16px;
  cursor: pointer;
  color: #64748b;
}

.modal-body {
  padding: 20px;
  display: flex;
  flex-direction: column;
  gap: 14px;
}

.modal-intro {
  font-size: 13px;
  color: #334155;
  line-height: 1.5;
}

.signature-form {
  display: flex;
  flex-direction: column;
  gap: 12px;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.form-group label {
  font-size: 11px;
  font-weight: 700;
  color: #475569;
  text-transform: uppercase;
}

.alert-warning {
  background-color: #fffbeb;
  border-left: 4px solid #f59e0b;
  padding: 10px 12px;
  border-radius: 4px;
  font-size: 12px;
  color: #92400e;
}

.modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  padding: 14px 20px;
  border-top: 1px solid #e2e8f0;
  background-color: #f8fafc;
  border-bottom-left-radius: 8px;
  border-bottom-right-radius: 8px;
}
</style>
