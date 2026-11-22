<template>
  <div id="section-ctms" class="dashboard-section active">
    <!-- Header Section -->
    <div v-if="!sectionMode" class="section-header">
      <h2>Site readiness</h2>
      <p>
        Monitor operational site status, track site lifecycle milestones, and
        coordinate site readiness workflows.
      </p>
    </div>

    <!-- Outside demo builds (when not embedded in sectionMode), expose backed Execution & CTMS workspaces and gate unbacked actions (#4829). -->
    <div
      v-if="!demoBuild && !sectionMode"
      id="ctms-live-workspace"
      class="live-workspace"
    >
      <nav
        id="ctms-live-tabs"
        class="live-tabs"
        aria-label="Site readiness workspaces"
      >
        <button
          v-for="tab in liveTabs"
          :id="`ctms-tab-${tab.id}`"
          :key="tab.id"
          type="button"
          class="live-tab-btn"
          :class="{ active: liveTab === tab.id }"
          :aria-pressed="liveTab === tab.id"
          @click="liveTab = tab.id"
        >
          {{ tab.label }}
        </button>
      </nav>

      <SiteActivationPanel v-if="liveTab === 'activation'" />
      <CtmsGreenlightSection v-else-if="liveTab === 'startup'" />
      <CtmsMilestonesSection v-else-if="liveTab === 'milestones'" />
      <CtmsMonitoringSection v-else-if="liveTab === 'monitoring'" />
      <CtmsDelegationSection v-else-if="liveTab === 'delegation'" />
      <RtsmSupplySection v-else-if="liveTab === 'supply'" />

      <p id="ctms-unbacked-scope-note" class="live-unbacked-note">
        Grant financials, CRA workload allocation, recruitment target updates,
        and adaptive dose escalation remain gated to demo builds in this
        pre-release environment (Orbit and Allocate roadmap). See
        <PlansRoadmapLink />.
      </p>
    </div>

    <template v-else>
      <!-- Demo-only CTMS KPI Metric Cards Grid -->
      <div
        v-if="demoBuild && !sectionMode"
        class="stats-grid"
        style="
          display: grid;
          grid-template-columns: repeat(auto-fit, minmax(200px, 1fr));
          gap: 16px;
          margin-bottom: 24px;
        "
      >
        <div
          id="kpi-total-subjects"
          class="stat-card card"
          style="padding: 16px"
        >
          <div
            class="stat-label"
            style="
              font-size: 0.8rem;
              font-weight: 700;
              color: var(--semantic-color-text-secondary);
              text-transform: uppercase;
            "
          >
            Total Subjects
          </div>
          <div
            class="stat-value"
            style="
              font-size: 1.75rem;
              font-weight: 800;
              color: var(--semantic-color-text);
              margin-top: 4px;
            "
          >
            {{ totalSubjectsKpi }}
          </div>
          <div
            class="stat-subtext"
            style="
              font-size: 0.75rem;
              color: var(--semantic-color-text-secondary);
              margin-top: 2px;
            "
          >
            {{ totalEnrolledCount }} Enrolled /
            {{ totalScreenedCount }} Screened
          </div>
        </div>

        <div
          id="kpi-enrollment-rate"
          class="stat-card card"
          style="padding: 16px"
        >
          <div
            class="stat-label"
            style="
              font-size: 0.8rem;
              font-weight: 700;
              color: var(--semantic-color-text-secondary);
              text-transform: uppercase;
            "
          >
            Enrollment Rate
          </div>
          <div
            class="stat-value"
            style="
              font-size: 1.75rem;
              font-weight: 800;
              color: var(--semantic-color-interactive-primary);
              margin-top: 4px;
            "
          >
            {{ enrollmentRateKpi }}%
          </div>
          <div
            class="stat-subtext"
            style="
              font-size: 0.75rem;
              color: var(--semantic-color-text-secondary);
              margin-top: 2px;
            "
          >
            Target: {{ totalTargetCount }} Subjects
          </div>
        </div>

        <div
          id="kpi-sdv-percentage"
          class="stat-card card"
          style="padding: 16px"
        >
          <div
            class="stat-label"
            style="
              font-size: 0.8rem;
              font-weight: 700;
              color: var(--semantic-color-text-secondary);
              text-transform: uppercase;
            "
          >
            Verified SDV %
          </div>
          <div
            class="stat-value"
            style="
              font-size: 1.75rem;
              font-weight: 800;
              color: var(--semantic-color-success-dark);
              margin-top: 4px;
            "
          >
            {{ verifiedSdvPercentageKpi }}%
          </div>
          <div
            class="stat-subtext"
            style="
              font-size: 0.75rem;
              color: var(--semantic-color-success-dark);
              margin-top: 2px;
            "
          >
            ICH GCP Verified
          </div>
        </div>

        <div id="kpi-open-queries" class="stat-card card" style="padding: 16px">
          <div
            class="stat-label"
            style="
              font-size: 0.8rem;
              font-weight: 700;
              color: var(--semantic-color-text-secondary);
              text-transform: uppercase;
            "
          >
            Open Queries
          </div>
          <div
            class="stat-value"
            style="
              font-size: 1.75rem;
              font-weight: 800;
              color: var(--semantic-color-warning);
              margin-top: 4px;
            "
          >
            {{ openQueriesCountKpi }}
          </div>
          <div
            class="stat-subtext"
            style="
              font-size: 0.75rem;
              color: var(--semantic-color-warning);
              margin-top: 2px;
            "
          >
            Discrepancies Awaiting Action
          </div>
        </div>
      </div>

      <!-- Reactive Tabs Header -->
      <div
        v-if="!sectionMode"
        class="tabs-container"
        style="
          display: flex;
          gap: 16px;
          margin-bottom: 24px;
          border-bottom: 2px solid var(--semantic-color-border);
          padding-bottom: 8px;
        "
      >
        <button
          id="tab-operations"
          class="tab-btn clinical-tab"
          :class="{ active: activeTab === 'operations' }"
          style="
            padding: 8px 16px;
            border: none;
            background: none;
            font-weight: 600;
            cursor: pointer;
            font-size: 16px;
            border-bottom: 3px solid transparent;
          "
          :style="
            activeTab === 'operations'
              ? {
                  borderBottomColor: 'var(--semantic-color-primary-dark)',
                  color: 'var(--semantic-color-primary-dark)',
                }
              : { color: 'var(--semantic-color-text-secondary)' }
          "
          @click="activeTab = 'operations'"
        >
          Core Operations
        </button>
        <button
          id="tab-monitoring"
          class="tab-btn clinical-tab"
          :class="{ active: activeTab === 'monitoring' }"
          style="
            padding: 8px 16px;
            border: none;
            background: none;
            font-weight: 600;
            cursor: pointer;
            font-size: 16px;
            border-bottom: 3px solid transparent;
          "
          :style="
            activeTab === 'monitoring'
              ? {
                  borderBottomColor: 'var(--semantic-color-primary-dark)',
                  color: 'var(--semantic-color-primary-dark)',
                }
              : { color: 'var(--semantic-color-text-secondary)' }
          "
          @click="activeTab = 'monitoring'"
        >
          CRA Monitoring &amp; SDV
        </button>
        <button
          id="tab-delegation"
          class="tab-btn clinical-tab"
          :class="{ active: activeTab === 'delegation' }"
          style="
            padding: 8px 16px;
            border: none;
            background: none;
            font-weight: 600;
            cursor: pointer;
            font-size: 16px;
            border-bottom: 3px solid transparent;
          "
          :style="
            activeTab === 'delegation'
              ? {
                  borderBottomColor: 'var(--semantic-color-primary-dark)',
                  color: 'var(--semantic-color-primary-dark)',
                }
              : { color: 'var(--semantic-color-text-secondary)' }
          "
          @click="activeTab = 'delegation'"
        >
          Delegation Matrix
        </button>
        <button
          v-if="demoBuild"
          id="tab-financials"
          class="tab-btn clinical-tab"
          :class="{ active: activeTab === 'financials' }"
          style="
            padding: 8px 16px;
            border: none;
            background: none;
            font-weight: 600;
            cursor: pointer;
            font-size: 16px;
            border-bottom: 3px solid transparent;
          "
          :style="
            activeTab === 'financials'
              ? {
                  borderBottomColor: 'var(--semantic-color-primary-dark)',
                  color: 'var(--semantic-color-primary-dark)',
                }
              : { color: 'var(--semantic-color-text-secondary)' }
          "
          @click="activeTab = 'financials'"
        >
          Financials &amp; Contracts
        </button>
        <button
          id="tab-startup"
          class="tab-btn clinical-tab"
          :class="{ active: activeTab === 'startup' }"
          style="
            padding: 8px 16px;
            border: none;
            background: none;
            font-weight: 600;
            cursor: pointer;
            font-size: 16px;
            border-bottom: 3px solid transparent;
          "
          :style="
            activeTab === 'startup'
              ? {
                  borderBottomColor: 'var(--semantic-color-primary-dark)',
                  color: 'var(--semantic-color-primary-dark)',
                }
              : { color: 'var(--semantic-color-text-secondary)' }
          "
          @click="activeTab = 'startup'"
        >
          Study Startup &amp; Greenlight
        </button>
        <button
          v-if="demoBuild"
          id="tab-rtsm"
          class="tab-btn clinical-tab"
          :class="{ active: activeTab === 'rtsm' }"
          style="
            padding: 8px 16px;
            border: none;
            background: none;
            font-weight: 600;
            cursor: pointer;
            font-size: 16px;
            border-bottom: 3px solid transparent;
          "
          :style="
            activeTab === 'rtsm'
              ? {
                  borderBottomColor: 'var(--semantic-color-primary-dark)',
                  color: 'var(--semantic-color-primary-dark)',
                }
              : { color: 'var(--semantic-color-text-secondary)' }
          "
          @click="activeTab = 'rtsm'"
        >
          💊 RTSM &amp; Dose Escalation
        </button>
      </div>

      <!-- Active Filter/Scope Info -->
      <div
        class="scope-info-bar"
        style="
          background: var(--semantic-color-surface-header);
          padding: 10px 16px;
          border-radius: 6px;
          margin-bottom: 20px;
          display: flex;
          flex-wrap: wrap;
          gap: 16px 24px;
          align-items: center;
          font-size: 14px;
          color: var(--semantic-color-text-body);
        "
      >
        <template v-if="demoBuild">
          <div><strong>Study ID:</strong> {{ studyId }}</div>
          <div><strong>Site ID:</strong> {{ siteId }}</div>
        </template>
        <template v-else>
          <div
            v-if="props.studyCode"
            style="display: flex; align-items: center; gap: 6px"
          >
            <strong>Study:</strong>
            <span>{{ studyId }}</span>
          </div>
          <div v-else style="display: flex; align-items: center; gap: 8px">
            <label
              for="ctms-study-input"
              style="margin: 0; font-weight: 600; white-space: nowrap"
              >Study</label
            >
            <input
              id="ctms-study-input"
              v-model="localStudyId"
              type="text"
              placeholder="e.g. CAD-101"
              style="width: 150px; padding: 5px 8px"
              @change="syncScopeToStore"
            />
          </div>
          <div style="display: flex; align-items: center; gap: 8px">
            <label
              for="ctms-site-picker"
              style="margin: 0; font-weight: 600; white-space: nowrap"
              >Site</label
            >
            <select
              id="ctms-site-picker"
              v-model="sitePickerChoice"
              style="width: 190px; padding: 5px 8px"
              @change="onSitePickerChange"
            >
              <option value="">Select site…</option>
              <option value="SITE-101">SITE-101</option>
              <option value="SITE-102">SITE-102</option>
              <option value="__custom__">Custom site code…</option>
            </select>
            <label
              v-if="sitePickerChoice === '__custom__'"
              for="ctms-site-input"
              class="sr-only"
              >Custom site code</label
            >
            <input
              v-if="sitePickerChoice === '__custom__'"
              id="ctms-site-input"
              v-model="localSiteId"
              type="text"
              placeholder="e.g. SITE-204"
              style="width: 140px; padding: 5px 8px"
              @change="syncScopeToStore"
            />
          </div>
        </template>
        <div
          style="
            margin-left: auto;
            display: flex;
            align-items: center;
            gap: 8px;
          "
        >
          <strong>Site Document Readiness:</strong>
          <span
            v-if="!hasActiveScope"
            style="color: var(--semantic-color-text-secondary)"
            >Select study &amp; site</span
          >
          <span
            v-else-if="loadingCompliance"
            style="color: var(--semantic-color-text-secondary)"
            >Loading...</span
          >
          <span
            v-else-if="complianceLoadError"
            class="badge"
            style="
              background-color: var(--semantic-color-warning-dark);
              color: var(--semantic-color-text-on-solid);
            "
            :title="complianceLoadError"
          >
            STATUS UNAVAILABLE
          </span>
          <span
            v-else
            class="badge"
            :class="{ gxp: complianceStatus.is_complete }"
            :style="
              complianceStatus.is_complete
                ? {}
                : {
                    backgroundColor: 'var(--semantic-color-error)',
                    color: 'var(--semantic-color-surface)',
                  }
            "
            :title="
              complianceStatus.is_complete
                ? 'All required activation documents approved.'
                : 'Missing: ' +
                  (complianceStatus.missing_documents || []).join(', ')
            "
          >
            {{ complianceStatus.is_complete ? "COMPLIANT" : "NON-COMPLIANT" }}
          </span>
        </div>
      </div>

      <!-- Tab 1: Core Operations / Conduct Milestones & Monitoring -->
      <div
        v-if="
          sectionMode === 'milestones' ||
          sectionMode === 'monitoring' ||
          (!sectionMode && activeTab === 'operations')
        "
      >
        <SiteActivationPanel
          v-if="!sectionMode && !demoBuild"
          :study-code="props.studyCode"
          style="margin-bottom: 20px"
        />

        <div
          v-if="operationsLoadError"
          id="ctms-operations-error"
          class="doa-load-error"
          role="alert"
        >
          Site operations could not be loaded: {{ operationsLoadError }}
        </div>

        <div :class="sectionMode ? '' : 'grid-2-responsive'">
          <!-- Site Milestones Card -->
          <div v-if="!sectionMode || sectionMode === 'milestones'" class="card">
            <div class="card-title">Site Operational Milestones</div>
            <div id="ctms-milestones-container">
              <table class="clinical-visit-matrix">
                <thead>
                  <tr>
                    <th scope="col">Milestone Type</th>
                    <th scope="col">Planned Date</th>
                    <th scope="col">Actual Date</th>
                    <th scope="col">Status</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="m in milestones" :key="m.id">
                    <td>
                      <strong>{{ m.milestone_type }}</strong>
                    </td>
                    <td>
                      {{ m.planned_date ? m.planned_date.slice(0, 10) : "N/A" }}
                    </td>
                    <td>
                      {{
                        m.actual_date ? m.actual_date.slice(0, 10) : "Pending"
                      }}
                    </td>
                    <td>
                      <span
                        class="badge"
                        :class="{ gxp: m.status === 'ACHIEVED' }"
                        >{{ m.status }}</span
                      >
                    </td>
                  </tr>
                  <tr v-if="milestones.length === 0">
                    <td
                      colspan="4"
                      style="
                        text-align: center;
                        color: var(--semantic-color-text-secondary);
                        padding: 12px;
                      "
                    >
                      {{
                        hasActiveScope
                          ? "No milestones found. Create one to begin tracking."
                          : "Select a study and site to inspect milestones."
                      }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div
              style="margin-top: 12px; display: flex; flex-wrap: wrap; gap: 8px"
            >
              <button
                id="btn-achieve-milestone"
                class="btn btn-primary"
                :disabled="!canManageMilestones || !hasActiveScope"
                :title="
                  !canManageMilestones ? SITE_MILESTONE_ROLE_REASON : undefined
                "
                @click="achieveMilestone"
              >
                Achieve Current Milestone
              </button>
              <button
                id="btn-create-milestone"
                class="btn btn-secondary"
                :disabled="!canManageMilestones || !hasActiveScope"
                :title="
                  !canManageMilestones ? SITE_MILESTONE_ROLE_REASON : undefined
                "
                @click="openCreateMilestone"
              >
                Add Milestone
              </button>
            </div>
            <p
              v-if="!canManageMilestones"
              id="ctms-milestone-role-note"
              class="role-note"
            >
              {{ SITE_MILESTONE_ROLE_REASON }}
            </p>
          </div>

          <!-- Monitoring Visits Card -->
          <div v-if="!sectionMode || sectionMode === 'monitoring'" class="card">
            <div class="card-title">Site Monitoring Visits (MVR)</div>
            <div id="ctms-visits-container">
              <table class="clinical-visit-matrix">
                <thead>
                  <tr>
                    <th scope="col">Visit Type</th>
                    <th scope="col">Scheduled Date</th>
                    <th scope="col">Actual Date</th>
                    <th scope="col">CRA Assigned</th>
                    <th scope="col">Status</th>
                    <th scope="col">Actions</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="v in visits" :key="v.id">
                    <td>
                      <strong>{{ v.visit_type }}</strong>
                      <div
                        v-if="Array.isArray(v.findings) && v.findings.length"
                        style="
                          font-size: 11px;
                          color: var(--semantic-color-text-secondary);
                          margin-top: 2px;
                        "
                      >
                        {{ v.findings.length }} finding(s)
                      </div>
                    </td>
                    <td>
                      {{
                        v.scheduled_date ? v.scheduled_date.slice(0, 10) : "N/A"
                      }}
                    </td>
                    <td>
                      {{
                        v.actual_date ? v.actual_date.slice(0, 10) : "Pending"
                      }}
                    </td>
                    <td>{{ v.cra_id || "N/A" }}</td>
                    <td>
                      <span
                        class="badge"
                        :class="{ gxp: v.status === 'SIGNED_OFF' }"
                        >{{ v.status }}</span
                      >
                    </td>
                    <td>
                      <div style="display: flex; flex-wrap: wrap; gap: 4px">
                        <button
                          v-if="v.status === 'SCHEDULED'"
                          type="button"
                          class="btn btn-primary btn-sm"
                          :data-testid="`btn-complete-visit-${v.id}`"
                          :disabled="!canManageVisits"
                          :title="
                            !canManageVisits
                              ? MONITORING_VISIT_COMPLETE_ROLE_REASON
                              : undefined
                          "
                          @click="openCompleteVisitModal(v)"
                        >
                          Complete
                        </button>
                        <button
                          v-if="v.status === 'COMPLETED'"
                          type="button"
                          class="btn btn-primary btn-sm"
                          :data-testid="`btn-signoff-visit-${v.id}`"
                          :disabled="!canSignOffVisits"
                          :title="
                            !canSignOffVisits
                              ? MONITORING_VISIT_SIGNOFF_ROLE_REASON
                              : undefined
                          "
                          @click="signOffVisit(v)"
                        >
                          Sign-off
                        </button>
                        <button
                          type="button"
                          class="btn btn-secondary btn-sm"
                          :data-testid="`btn-compile-letter-${v.id}`"
                          :disabled="!canManageVisits"
                          :title="
                            !canManageVisits
                              ? MONITORING_LETTER_COMPILE_ROLE_REASON
                              : undefined
                          "
                          @click="openCompileLetterModal(v)"
                        >
                          Compile Letter
                        </button>
                      </div>
                    </td>
                  </tr>
                  <tr v-if="visits.length === 0">
                    <td
                      colspan="6"
                      style="
                        text-align: center;
                        color: var(--semantic-color-text-secondary);
                        padding: 12px;
                      "
                    >
                      {{
                        hasActiveScope
                          ? "No monitoring visits scheduled."
                          : "Select a study and site to inspect monitoring visits."
                      }}
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div
              style="margin-top: 12px; display: flex; flex-wrap: wrap; gap: 8px"
            >
              <button
                id="btn-schedule-visit"
                class="btn btn-primary"
                :disabled="!canManageVisits || !hasActiveScope"
                :title="
                  !canManageVisits
                    ? MONITORING_VISIT_COMPLETE_ROLE_REASON
                    : undefined
                "
                @click="openScheduleVisit"
              >
                Schedule New Visit
              </button>
              <button
                id="btn-complete-visit"
                class="btn btn-secondary"
                :disabled="!canManageVisits || !hasActiveScope"
                :title="
                  !canManageVisits
                    ? MONITORING_VISIT_COMPLETE_ROLE_REASON
                    : undefined
                "
                @click="openCompleteVisitModal()"
              >
                Complete Scheduled Visit
              </button>
              <button
                id="btn-compile-visit-letter"
                class="btn btn-secondary"
                :disabled="!canManageVisits || !hasActiveScope"
                :title="
                  !canManageVisits
                    ? MONITORING_LETTER_COMPILE_ROLE_REASON
                    : undefined
                "
                @click="openCompileLetterModal()"
              >
                Compile Visit Letter
              </button>
            </div>
            <p
              v-if="!canManageVisits"
              id="ctms-visit-manage-role-note"
              class="role-note"
            >
              {{ MONITORING_VISIT_COMPLETE_ROLE_REASON }}
            </p>
            <p
              v-if="!canSignOffVisits"
              id="ctms-visit-signoff-role-note"
              class="role-note"
            >
              {{ MONITORING_VISIT_SIGNOFF_ROLE_REASON }}
            </p>
          </div>
        </div>

        <!-- Compiled Visit Letters Card -->
        <div
          v-if="
            (!sectionMode || sectionMode === 'monitoring') &&
            compiledLetters.length > 0
          "
          id="ctms-compiled-letters-container"
          class="card"
          style="margin-top: 20px"
        >
          <div class="card-title">Compiled Visit Letters</div>
          <table class="clinical-visit-matrix">
            <thead>
              <tr>
                <th scope="col">Letter ID</th>
                <th scope="col">Template</th>
                <th scope="col">Status</th>
                <th scope="col">PDF SHA-256</th>
                <th scope="col">Filed Document</th>
                <th scope="col">Actions</th>
              </tr>
            </thead>
            <tbody>
              <tr v-for="letter in compiledLetters" :key="letter.letterId">
                <td>
                  <code>{{ letter.letterId }}</code>
                </td>
                <td>{{ letter.letterType }}</td>
                <td>
                  <span class="badge" :class="{ gxp: letter.isSigned }">{{
                    letter.status
                  }}</span>
                </td>
                <td>
                  <code style="font-size: 11px">{{
                    letter.pdfSha256Hash
                  }}</code>
                </td>
                <td>
                  {{ letter.etmfDocumentId || "Draft (not filed)" }}
                </td>
                <td>
                  <button
                    type="button"
                    class="btn btn-primary btn-sm"
                    :data-testid="`btn-download-letter-${letter.letterId}`"
                    @click="downloadCompiledLetterPdf(letter)"
                  >
                    Download PDF
                  </button>
                </td>
              </tr>
            </tbody>
          </table>
        </div>

        <div
          v-if="demoBuild && !sectionMode"
          class="grid-2-responsive"
          style="margin-top: var(--spacing-xl)"
        >
          <!-- CRA Allocations & Workload Card -->
          <div class="card">
            <div class="card-title">CRA Allocation & Workload Summaries</div>
            <div id="ctms-workload-container">
              <table class="clinical-visit-matrix">
                <thead>
                  <tr>
                    <th scope="col">CRA</th>
                    <th scope="col">Active Allocations</th>
                    <th scope="col">Allocated Sites</th>
                    <th scope="col">Allocated Studies</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="a in allocations" :key="a.cra_id">
                    <td>
                      <strong>{{ a.cra_id }}</strong>
                    </td>
                    <td>{{ a.active_allocations_count }}</td>
                    <td>{{ (a.allocated_sites || []).join(", ") }}</td>
                    <td>{{ (a.allocated_studies || []).join(", ") }}</td>
                  </tr>
                  <tr v-if="allocations.length === 0">
                    <td
                      colspan="4"
                      style="
                        text-align: center;
                        color: var(--semantic-color-text-secondary);
                        padding: 12px;
                      "
                    >
                      No CRA allocations recorded.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div style="margin-top: 12px; display: flex; gap: 8px">
              <button
                id="btn-reallocate-cra"
                class="btn btn-primary"
                @click="openReallocate"
              >
                Reallocate CRA
              </button>
            </div>
          </div>

          <!-- Site Recruitment Metrics Card -->
          <div class="card">
            <div class="card-title">Site Recruitment Metrics</div>
            <div id="ctms-recruitment-container">
              <table class="clinical-visit-matrix">
                <thead>
                  <tr>
                    <th scope="col">Site ID</th>
                    <th scope="col">Screened</th>
                    <th scope="col">Enrolled</th>
                    <th scope="col">Target</th>
                    <th scope="col">Progress</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="r in recruitment" :key="r.id">
                    <td>
                      <strong>{{ r.site_id }}</strong>
                    </td>
                    <td>{{ r.screened_count }}</td>
                    <td>{{ r.enrolled_count }}</td>
                    <td>{{ r.target_count }}</td>
                    <td>
                      {{
                        r.target_count > 0
                          ? Math.round(
                              (r.enrolled_count / r.target_count) * 100
                            )
                          : 0
                      }}%
                    </td>
                  </tr>
                  <tr v-if="recruitment.length === 0">
                    <td
                      colspan="5"
                      style="
                        text-align: center;
                        color: var(--semantic-color-text-secondary);
                        padding: 12px;
                      "
                    >
                      No recruitment records logged.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div style="margin-top: 12px; display: flex; gap: 8px">
              <button
                id="btn-update-recruitment"
                class="btn btn-primary"
                @click="openRecruitment"
              >
                Log Recruitment Update
              </button>
            </div>
          </div>
        </div>
      </div>

      <!-- Tab 2: CRA Monitoring & SDV Console -->
      <div v-if="!sectionMode && activeTab === 'monitoring'">
        <CraVerificationConsole
          :standalone="true"
          :study-id="studyId"
          :site-id="siteId"
        />
      </div>

      <!-- Tab 3: Delegation Matrix -->
      <div v-if="!sectionMode && activeTab === 'delegation'">
        <div class="card">
          <div
            class="card-title"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
            "
          >
            <span>Delegation of Authority (DOA) Matrix</span>
            <button
              id="btn-export-pdf"
              class="btn btn-primary"
              style="
                background: var(--semantic-color-primary-dark);
                color: var(--semantic-color-surface);
              "
              :disabled="!siteId.trim()"
              @click="exportDoaPdf"
            >
              🖨️ Export GxP PDF
            </button>
          </div>

          <div
            style="
              margin-bottom: 16px;
              font-size: 14px;
              background: var(--semantic-color-surface-muted);
              padding: 12px;
              border-radius: 6px;
              border: 1px solid var(--semantic-color-border);
            "
          >
            <strong>Principal Investigator (PI):</strong>
            {{ piName || "Not recorded in the DoA log" }}
          </div>

          <div
            v-if="doaLoadError"
            class="doa-load-error"
            role="alert"
            data-testid="doa-load-error"
          >
            Delegation of Authority log could not be loaded: {{ doaLoadError }}
          </div>

          <div id="ctms-delegation-container">
            <table class="clinical-visit-matrix">
              <thead>
                <tr>
                  <th scope="col">Active Site Staff</th>
                  <th scope="col">Training Certificates</th>
                  <th scope="col">Delegated Protocol Roles &amp; Duties</th>
                  <th scope="col">Start Date</th>
                  <th scope="col">End Date</th>
                  <th scope="col">Status</th>
                  <th scope="col">Signed Off</th>
                  <th scope="col">Actions</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="d in effectiveDelegatedStaff"
                  :key="d.record_id || d.staff_user_id"
                >
                  <td>
                    <strong>{{ d.staff_user_id }}</strong>
                    <div
                      style="
                        font-size: 11px;
                        color: var(--semantic-color-text-secondary);
                      "
                    >
                      {{ getStaffNameAndRole(d) }}
                    </div>
                  </td>
                  <td>
                    <div
                      style="display: flex; flex-direction: column; gap: 4px"
                    >
                      <span
                        v-if="getStaffCertificates(d).length === 0"
                        style="
                          font-size: 11px;
                          color: var(--semantic-color-text-secondary);
                        "
                      >
                        Not recorded
                      </span>
                      <span
                        v-for="(cert, idx) in getStaffCertificates(d)"
                        :key="idx"
                        class="badge"
                        style="
                          background-color: var(
                            --semantic-color-success-bg-soft
                          );
                          color: var(--semantic-color-success-dark);
                          border: 1px solid
                            var(--semantic-color-success-border-soft);
                          font-size: 10px;
                          font-weight: 600;
                          padding: 2px 6px;
                          border-radius: 4px;
                          display: inline-flex;
                          align-items: center;
                          gap: 4px;
                        "
                      >
                        <span>✓</span> {{ cert }}
                      </span>
                    </div>
                  </td>
                  <td>
                    <div style="display: flex; flex-wrap: wrap; gap: 4px">
                      <span
                        v-for="task in d.task_codes"
                        :key="task"
                        class="badge"
                        style="
                          background-color: var(
                            --semantic-color-surface-header
                          );
                          color: var(--semantic-color-text-body);
                          border: 1px solid var(--semantic-color-border-control);
                          font-size: 10px;
                          font-family: monospace;
                        "
                      >
                        {{ task }}
                      </span>
                    </div>
                  </td>
                  <td>{{ d.start_date }}</td>
                  <td>{{ d.end_date || "—" }}</td>
                  <td>
                    <span class="badge" :class="{ gxp: d.is_active }">{{
                      d.is_active ? "ACTIVE" : "INACTIVE"
                    }}</span>
                  </td>
                  <td>
                    <span class="badge" :class="{ gxp: d.signed_off }">{{
                      d.signed_off ? "YES" : "PENDING PI"
                    }}</span>
                  </td>
                  <td style="display: flex; gap: 6px">
                    <button
                      v-if="!d.signed_off"
                      class="btn btn-primary btn-sm"
                      style="padding: 2px 8px; font-size: 11px"
                      :disabled="!canSignDoa"
                      :title="!canSignDoa ? DOA_SIGNOFF_ROLE_REASON : undefined"
                      @click="signDelegation(d)"
                    >
                      PI Sign-off
                    </button>
                    <button
                      v-if="d.is_active"
                      class="btn btn-cancel btn-sm"
                      style="
                        padding: 2px 8px;
                        font-size: 11px;
                        border-color: var(--semantic-color-error);
                        color: var(--semantic-color-error);
                      "
                      :disabled="!canEditDoa"
                      @click="revokeDelegation(d)"
                    >
                      Revoke
                    </button>
                  </td>
                </tr>
                <tr
                  v-if="effectiveDelegatedStaff.length === 0 && !doaLoadError"
                >
                  <td
                    colspan="8"
                    style="
                      text-align: center;
                      color: var(--semantic-color-text-secondary);
                      padding: 12px;
                    "
                  >
                    {{
                      siteId.trim()
                        ? "No delegation assignments logged for this site."
                        : "Select a site to inspect its Delegation of Authority log."
                    }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div style="margin-top: 16px">
            <button
              id="btn-delegate-task"
              class="btn btn-primary"
              :disabled="!canEditDoa || !siteId.trim()"
              @click="openDelegate"
            >
              Delegate New Task
            </button>
          </div>
          <p
            v-if="!canSignDoa"
            id="ctms-doa-signoff-role-note"
            class="role-note"
          >
            {{ DOA_SIGNOFF_ROLE_REASON }}
          </p>
        </div>

        <!-- Audit History Card -->
        <div class="card" style="margin-top: var(--spacing-xl)">
          <div class="card-title">Site DoA Audit Trail (Immutable GxP Log)</div>
          <div id="ctms-audit-container">
            <table class="clinical-visit-matrix">
              <thead>
                <tr>
                  <th scope="col">Timestamp</th>
                  <th scope="col">User</th>
                  <th scope="col">Role</th>
                  <th scope="col">Action</th>
                  <th scope="col">Justification Details</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="audit in auditHistory" :key="audit.id">
                  <td style="white-space: nowrap; font-size: 12px">
                    {{ new Date(audit.timestamp).toLocaleString() }}
                  </td>
                  <td>{{ audit.user_id }}</td>
                  <td>{{ audit.user_role }}</td>
                  <td>
                    <span
                      class="badge"
                      style="
                        background: var(--semantic-color-surface-header);
                        color: var(--semantic-color-text-body);
                      "
                      >{{ audit.action }}</span
                    >
                  </td>
                  <td style="font-size: 12px">
                    {{ audit.details }}
                  </td>
                </tr>
                <tr v-if="auditHistory.length === 0">
                  <td
                    colspan="5"
                    style="
                      text-align: center;
                      color: var(--semantic-color-text-secondary);
                      padding: 12px;
                    "
                  >
                    No audit trail events recorded.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Tab 4: Financials & Fee Matrix Engine -->
      <div v-if="demoBuild && !sectionMode && activeTab === 'financials'">
        <!-- Grant Contract Header & Multi-Tier Template Selector -->
        <div class="card" style="margin-bottom: 20px">
          <div
            class="card-title"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
            "
          >
            <span>Investigator Grant Contract &amp; Site Budget Summary</span>
            <div style="display: flex; gap: 8px">
              <button
                id="btn-create-template"
                class="btn btn-secondary"
                @click="showCreateTemplateModal = true"
              >
                ➕ Create Budget Template
              </button>
              <button
                id="btn-execute-amendment"
                class="btn btn-primary"
                :disabled="!investigatorGrant"
                @click="openAmendmentModal"
              >
                ✍️ Execute Contract Amendment
              </button>
            </div>
          </div>

          <div
            style="
              display: grid;
              grid-template-columns: repeat(auto-fit, minmax(220px, 1fr));
              gap: 16px;
              margin-bottom: 16px;
              background: var(--semantic-color-surface-muted);
              padding: 16px;
              border-radius: 6px;
              border: 1px solid var(--semantic-color-border);
            "
          >
            <div>
              <div
                style="
                  font-size: 11px;
                  color: var(--semantic-color-text-secondary);
                  font-weight: 700;
                  text-transform: uppercase;
                "
              >
                Contract Title
              </div>
              <div
                style="
                  font-size: 15px;
                  font-weight: 700;
                  color: var(--semantic-color-text);
                  margin-top: 2px;
                "
              >
                {{
                  investigatorGrant?.contract_title || "Standard Site Agreement"
                }}
              </div>
              <div
                style="
                  font-size: 12px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                Grant ID:
                {{
                  investigatorGrant?.id
                    ? investigatorGrant.id.slice(0, 8) + "..."
                    : "Not Provisioned"
                }}
              </div>
            </div>

            <div>
              <div
                style="
                  font-size: 11px;
                  color: var(--semantic-color-text-secondary);
                  font-weight: 700;
                  text-transform: uppercase;
                "
              >
                Total Budget (v{{ investigatorGrant?.version_index || 1 }})
              </div>
              <div
                style="
                  font-size: 18px;
                  font-weight: 800;
                  color: var(--semantic-color-primary-dark);
                  margin-top: 2px;
                "
              >
                {{
                  investigatorGrant
                    ? `${investigatorGrant.currency} $${Number(investigatorGrant.total_budget).toLocaleString()}`
                    : "$0.00"
                }}
              </div>
              <div
                style="
                  font-size: 12px;
                  color: var(--semantic-color-success-dark);
                "
              >
                Status: {{ investigatorGrant?.status || "ACTIVE" }}
              </div>
            </div>

            <div>
              <div
                style="
                  font-size: 11px;
                  color: var(--semantic-color-text-secondary);
                  font-weight: 700;
                  text-transform: uppercase;
                "
              >
                Institutional Rates
              </div>
              <div
                style="
                  font-size: 13px;
                  color: var(--semantic-color-text-body);
                  margin-top: 4px;
                "
              >
                <strong>Overhead:</strong>
                {{ investigatorGrant?.overhead_percentage || 0 }}% |
                <strong>Withholding:</strong>
                {{ investigatorGrant?.withholding_percentage || 0 }}%
              </div>
              <div
                style="
                  font-size: 12px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                Startup Fee: ${{ investigatorGrant?.fixed_startup_fee || 0 }} |
                Closeout: ${{ investigatorGrant?.fixed_closeout_fee || 0 }}
              </div>
            </div>

            <div>
              <div
                style="
                  font-size: 11px;
                  color: var(--semantic-color-text-secondary);
                  font-weight: 700;
                  text-transform: uppercase;
                "
              >
                Apply Multi-Tier Template
              </div>
              <div style="display: flex; gap: 6px; margin-top: 4px">
                <select
                  id="select-budget-template"
                  v-model="selectedTemplateId"
                  style="padding: 4px 8px; font-size: 12px"
                >
                  <option value="">-- Select Template --</option>
                  <option
                    v-for="tmpl in financialTemplates"
                    :key="tmpl.id"
                    :value="tmpl.id"
                  >
                    {{ tmpl.template_name }} ({{ tmpl.tier_level }})
                  </option>
                </select>
                <button
                  id="btn-apply-template"
                  class="btn btn-sm btn-primary"
                  :disabled="!selectedTemplateId || !investigatorGrant"
                  @click="applyTemplate"
                >
                  Apply
                </button>
              </div>
            </div>
          </div>
        </div>

        <!-- Procedure Fee Matrix Grid -->
        <div class="card" style="margin-bottom: 20px">
          <div
            class="card-title"
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
            "
          >
            <span>Procedure Reimbursement Fee Schedule Matrix</span>
            <div style="display: flex; gap: 8px; align-items: center">
              <select
                id="select-grid-version"
                v-model="filteredVersionIndex"
                style="padding: 4px 8px; font-size: 12px"
                @change="loadProcedureGrids"
              >
                <option value="ALL">All Versions</option>
                <option value="1">Version 1 (Initial)</option>
                <option value="2">Version 2 (Amended)</option>
              </select>
              <button
                id="btn-add-procedure-grid"
                class="btn btn-primary"
                :disabled="!investigatorGrant"
                @click="openAddProcedureModal"
              >
                ➕ Add Procedure to Grid
              </button>
            </div>
          </div>

          <div style="overflow-x: auto">
            <table class="clinical-visit-matrix">
              <thead>
                <tr>
                  <th scope="col">Visit Name</th>
                  <th scope="col">USDM Activity</th>
                  <th scope="col">eCRF Form Link</th>
                  <th scope="col">CPT / Procedure Code</th>
                  <th scope="col">Procedure Description</th>
                  <th scope="col">Base Fee</th>
                  <th scope="col">Overhead %</th>
                  <th scope="col">Withhold %</th>
                  <th scope="col">Patient Stipend</th>
                  <th scope="col">Travel Reimb.</th>
                  <th scope="col">Version</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="grid in procedureGrids" :key="grid.id">
                  <td
                    style="
                      font-weight: 600;
                      color: var(--semantic-color-primary-dark);
                    "
                  >
                    {{ grid.visit_name }}
                  </td>
                  <td>
                    <span
                      v-if="grid.activity_id"
                      class="badge"
                      style="
                        background: var(--semantic-color-primary-light);
                        color: var(--semantic-color-primary);
                      "
                    >
                      {{ grid.activity_id }}
                    </span>
                    <span
                      v-else
                      style="color: var(--semantic-color-text-secondary)"
                      >—</span
                    >
                  </td>
                  <td>
                    <span
                      v-if="grid.form_id"
                      class="badge"
                      style="
                        background: var(--semantic-color-success-bg-soft);
                        color: var(--semantic-color-success-dark);
                      "
                    >
                      {{ grid.form_id }}
                    </span>
                    <span
                      v-else
                      style="color: var(--semantic-color-text-secondary)"
                      >—</span
                    >
                  </td>
                  <td style="font-family: monospace; font-size: 12px">
                    {{ grid.procedure_code }}
                  </td>
                  <td>{{ grid.procedure_name }}</td>
                  <td style="font-weight: 700">
                    ${{ grid.base_amount.toFixed(2) }}
                  </td>
                  <td>{{ grid.overhead_percentage }}%</td>
                  <td>{{ grid.withholding_percentage }}%</td>
                  <td>${{ (grid.patient_stipend || 0).toFixed(2) }}</td>
                  <td>${{ (grid.travel_reimbursement || 0).toFixed(2) }}</td>
                  <td>
                    <span
                      class="badge"
                      style="
                        background: var(--semantic-color-surface-header);
                        color: var(--semantic-color-text-secondary);
                      "
                      >v{{ grid.version_index }}</span
                    >
                  </td>
                  <td>
                    <span
                      class="badge"
                      :style="
                        grid.is_active
                          ? {
                              background: 'var(--semantic-color-success-bg)',
                              color: 'var(--semantic-color-success-dark)',
                            }
                          : {
                              background: 'var(--semantic-color-error-bg)',
                              color: 'var(--semantic-color-error-text-dark)',
                            }
                      "
                    >
                      {{ grid.is_active ? "ACTIVE" : "HISTORICAL" }}
                    </span>
                  </td>
                </tr>
                <tr v-if="procedureGrids.length === 0">
                  <td
                    colspan="12"
                    style="
                      text-align: center;
                      color: var(--semantic-color-text-secondary);
                      padding: 16px;
                    "
                  >
                    No procedure fee items configured for this grant. Select a
                    budget template or add procedure items manually.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>

        <!-- Dynamic Visit Payable & Multi-Currency Converter -->
        <div
          style="
            display: grid;
            grid-template-columns: repeat(auto-fit, minmax(320px, 1fr));
            gap: 20px;
            margin-bottom: 20px;
          "
        >
          <div class="card">
            <div class="card-title">
              <span>Dynamic Visit Payable Calculator</span>
            </div>

            <div style="display: flex; gap: 12px; margin-bottom: 16px">
              <div style="flex: 1">
                <label for="calc-visit-select">Select Visit</label>
                <select
                  id="calc-visit-select"
                  v-model="selectedVisitForCalc"
                  @change="calculatePayables"
                >
                  <option value="SCREENING">SCREENING</option>
                  <option value="WEEK_4">WEEK_4</option>
                  <option value="WEEK_12">WEEK_12</option>
                  <option value="END_OF_TREATMENT">END_OF_TREATMENT</option>
                </select>
              </div>

              <div style="flex: 1">
                <label for="calc-currency-select">Sponsor Currency (FX)</label>
                <select
                  id="calc-currency-select"
                  v-model="selectedCurrencyForCalc"
                  @change="calculatePayables"
                >
                  <option value="USD">USD ($)</option>
                  <option value="EUR">EUR (€)</option>
                  <option value="GBP">GBP (£)</option>
                  <option value="CAD">CAD ($)</option>
                  <option value="JPY">JPY (¥)</option>
                </select>
              </div>
            </div>

            <div
              v-if="visitPayableCalc"
              style="
                background: var(--semantic-color-surface-muted);
                padding: 16px;
                border-radius: 6px;
                border: 1px solid var(--semantic-color-border);
              "
            >
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 13px;
                  margin-bottom: 6px;
                "
              >
                <span
                  >Base Procedures Sum ({{
                    visitPayableCalc.procedures_count
                  }}
                  items):</span
                >
                <strong
                  >${{ (visitPayableCalc.base_amount || 0).toFixed(2) }}</strong
                >
              </div>
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 13px;
                  margin-bottom: 6px;
                "
              >
                <span>Institutional Overhead:</span>
                <span style="color: var(--semantic-color-interactive-primary)"
                  >+${{
                    (visitPayableCalc.overhead_amount || 0).toFixed(2)
                  }}</span
                >
              </div>
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 13px;
                  margin-bottom: 6px;
                "
              >
                <span>Patient Stipends:</span>
                <span style="color: var(--semantic-color-success-dark)"
                  >+${{
                    (visitPayableCalc.patient_stipend_total || 0).toFixed(2)
                  }}</span
                >
              </div>
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 13px;
                  margin-bottom: 6px;
                "
              >
                <span>Travel Reimbursement:</span>
                <span style="color: var(--semantic-color-success-dark)"
                  >+${{
                    (visitPayableCalc.travel_reimbursement_total || 0).toFixed(
                      2
                    )
                  }}</span
                >
              </div>
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 13px;
                  margin-bottom: 6px;
                  border-top: 1px dashed var(--semantic-color-border-control);
                  padding-top: 6px;
                "
              >
                <span>Gross Payable Total:</span>
                <strong
                  >${{
                    (visitPayableCalc.gross_amount || 0).toFixed(2)
                  }}</strong
                >
              </div>
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 13px;
                  margin-bottom: 6px;
                "
              >
                <span>Withholding / Retention Deduction:</span>
                <span style="color: var(--semantic-color-error)"
                  >-${{
                    (visitPayableCalc.withholding_amount || 0).toFixed(2)
                  }}</span
                >
              </div>
              <div
                style="
                  display: flex;
                  justify-content: space-between;
                  font-size: 16px;
                  font-weight: 800;
                  color: var(--semantic-color-primary-dark);
                  border-top: 2px solid var(--semantic-color-primary-dark);
                  padding-top: 8px;
                  margin-top: 8px;
                "
              >
                <span>Net Payable (Site Currency):</span>
                <span
                  >${{ (visitPayableCalc.net_amount || 0).toFixed(2) }}
                  {{ visitPayableCalc.currency }}</span
                >
              </div>
              <div
                v-if="
                  visitPayableCalc.target_currency &&
                  visitPayableCalc.target_currency !== visitPayableCalc.currency
                "
                style="
                  margin-top: 8px;
                  padding-top: 8px;
                  border-top: 1px solid var(--semantic-color-border);
                  font-size: 13px;
                  color: var(--semantic-color-text-secondary);
                "
              >
                <span
                  >Converted Sponsor Total (FX Rate:
                  {{ visitPayableCalc.exchange_rate }}):
                </span>
                <strong style="color: var(--semantic-color-success-dark)"
                  >{{ visitPayableCalc.target_currency }}
                  {{
                    (visitPayableCalc.converted_net_amount || 0).toFixed(2)
                  }}</strong
                >
              </div>
            </div>
          </div>

          <!-- Budget Adjustments Section -->
          <div class="card">
            <div
              class="card-title"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
              "
            >
              <span>Budget Adjustments &amp; Penalties</span>
              <button
                id="btn-add-adjustment"
                class="btn btn-sm btn-secondary"
                @click="showAdjustmentModal = true"
              >
                ➕ Add Adjustment
              </button>
            </div>

            <div style="overflow-x: auto">
              <table class="clinical-visit-matrix">
                <thead>
                  <tr>
                    <th scope="col">Type</th>
                    <th scope="col">Description</th>
                    <th scope="col">Amount</th>
                    <th scope="col">Status</th>
                    <th scope="col">Action</th>
                  </tr>
                </thead>
                <tbody>
                  <tr v-for="adj in budgetAdjustments" :key="adj.id">
                    <td>
                      <span
                        class="badge"
                        :style="
                          adj.amount >= 0
                            ? {
                                background: 'var(--semantic-color-success-bg)',
                                color: 'var(--semantic-color-success-dark)',
                              }
                            : {
                                background: 'var(--semantic-color-error-bg)',
                                color: 'var(--semantic-color-error-text-dark)',
                              }
                        "
                      >
                        {{ adj.adjustment_type }}
                      </span>
                    </td>
                    <td style="font-size: 12px">
                      {{ adj.description }}
                    </td>
                    <td
                      style="font-weight: 700"
                      :style="
                        adj.amount >= 0
                          ? { color: 'var(--semantic-color-success-dark)' }
                          : { color: 'var(--semantic-color-error)' }
                      "
                    >
                      {{ adj.amount >= 0 ? "+" : "" }}${{
                        adj.amount.toFixed(2)
                      }}
                      {{ adj.currency }}
                    </td>
                    <td>
                      <span
                        class="badge"
                        :style="
                          adj.status === 'APPROVED'
                            ? {
                                background: 'var(--semantic-color-success-bg)',
                                color: 'var(--semantic-color-success-dark)',
                              }
                            : {
                                background:
                                  'var(--semantic-color-warning-bg-badge)',
                                color: 'var(--semantic-color-warning-dark)',
                              }
                        "
                      >
                        {{ adj.status }}
                      </span>
                    </td>
                    <td>
                      <button
                        v-if="adj.status === 'PENDING'"
                        class="btn btn-sm btn-primary"
                        @click="approveAdjustment(adj)"
                      >
                        Approve
                      </button>
                      <span
                        v-else
                        style="
                          font-size: 11px;
                          color: var(--semantic-color-text-secondary);
                        "
                      >
                        ✓ {{ adj.approved_by || "Approved" }}
                      </span>
                    </td>
                  </tr>
                  <tr v-if="budgetAdjustments.length === 0">
                    <td
                      colspan="5"
                      style="
                        text-align: center;
                        color: var(--semantic-color-text-secondary);
                        padding: 12px;
                      "
                    >
                      No passthrough reimbursements or penalty adjustments
                      recorded.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </div>

        <!-- Contract Amendments Ledger -->
        <div class="card">
          <div class="card-title">
            <span>Grant Contract Amendments &amp; Version History Ledger</span>
          </div>

          <div style="overflow-x: auto">
            <table class="clinical-visit-matrix">
              <thead>
                <tr>
                  <th scope="col">Amendment #</th>
                  <th scope="col">Effective Date</th>
                  <th scope="col">Previous Budget</th>
                  <th scope="col">Amended Budget</th>
                  <th scope="col">Version Transition</th>
                  <th scope="col">Summary of Protocol / Rate Changes</th>
                  <th scope="col">Approved / Executed By</th>
                  <th scope="col">Status</th>
                </tr>
              </thead>
              <tbody>
                <tr v-for="amend in contractAmendments" :key="amend.id">
                  <td
                    style="
                      font-weight: 700;
                      color: var(--semantic-color-primary-dark);
                    "
                  >
                    #{{ amend.amendment_number }}
                  </td>
                  <td style="font-size: 12px; white-space: nowrap">
                    {{ new Date(amend.effective_date).toLocaleDateString() }}
                  </td>
                  <td>${{ Number(amend.previous_budget).toLocaleString() }}</td>
                  <td
                    style="
                      font-weight: 700;
                      color: var(--semantic-color-success-dark);
                    "
                  >
                    ${{ Number(amend.revised_budget).toLocaleString() }}
                  </td>
                  <td>
                    <span
                      class="badge"
                      style="
                        background: var(--semantic-color-primary-light);
                        color: var(--semantic-color-primary);
                      "
                    >
                      v{{ amend.previous_version_index }} ➔ v{{
                        amend.new_version_index
                      }}
                    </span>
                  </td>
                  <td style="font-size: 12px">
                    {{ amend.summary_of_changes }}
                  </td>
                  <td>{{ amend.approved_by || amend.created_by }}</td>
                  <td>
                    <span
                      class="badge"
                      style="
                        background: var(--semantic-color-success-bg);
                        color: var(--semantic-color-success-dark);
                      "
                    >
                      {{ amend.status }}
                    </span>
                  </td>
                </tr>
                <tr v-if="contractAmendments.length === 0">
                  <td
                    colspan="8"
                    style="
                      text-align: center;
                      color: var(--semantic-color-text-secondary);
                      padding: 16px;
                    "
                  >
                    Initial version (v1) active. No contract amendments executed
                    yet.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </div>

      <!-- Modal: Create Budget Template -->
      <ClinicalModal
        v-model="showCreateTemplateModal"
        :teleport="false"
        dialog-class="modal"
        title="Create Multi-Tier Budget Template"
      >
        <div class="form-group">
          <label for="tmpl-name">Template Name</label>
          <input
            id="tmpl-name"
            v-model="newTmplName"
            type="text"
            placeholder="e.g. Oncology Phase III - Academic Center"
          />
        </div>
        <div class="form-group">
          <label for="tmpl-tier">Tier Level</label>
          <select id="tmpl-tier" v-model="newTmplTier">
            <option value="TIER_1_ACADEMIC">
              Tier 1 - Academic Medical Center
            </option>
            <option value="TIER_2_COMMUNITY">
              Tier 2 - Community Health Clinic
            </option>
            <option value="TIER_3_REGIONAL">
              Tier 3 - Regional Research Site
            </option>
          </select>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="tmpl-overhead">Overhead %</label>
            <input
              id="tmpl-overhead"
              v-model.number="newTmplOverhead"
              type="number"
              step="0.5"
            />
          </div>
          <div class="form-group">
            <label for="tmpl-withholding">Withholding %</label>
            <input
              id="tmpl-withholding"
              v-model.number="newTmplWithholding"
              type="number"
              step="0.5"
            />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="tmpl-startup">Fixed Startup Fee ($)</label>
            <input
              id="tmpl-startup"
              v-model.number="newTmplStartup"
              type="number"
            />
          </div>
          <div class="form-group">
            <label for="tmpl-closeout">Fixed Closeout Fee ($)</label>
            <input
              id="tmpl-closeout"
              v-model.number="newTmplCloseout"
              type="number"
            />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="tmpl-stipend">Per-Visit Patient Stipend ($)</label>
            <input
              id="tmpl-stipend"
              v-model.number="newTmplStipend"
              type="number"
            />
          </div>
          <div class="form-group">
            <label for="tmpl-travel">Per-Visit Travel Reimb. ($)</label>
            <input
              id="tmpl-travel"
              v-model.number="newTmplTravel"
              type="number"
            />
          </div>
        </div>
        <div class="form-group">
          <label for="tmpl-reason">Reason for Change Justification</label>
          <textarea
            id="tmpl-reason"
            v-model="newTmplReason"
            placeholder="State the justification reason for creating this budget template..."
          />
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showCreateTemplateModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmCreateTemplate"
          >
            Create Template
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal: Add Procedure to Fee Grid -->
      <ClinicalModal
        v-model="showAddProcedureModal"
        :teleport="false"
        dialog-class="modal"
        title="Add Procedure to Fee Matrix Grid"
      >
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="proc-visit">Visit Name</label>
            <select id="proc-visit" v-model="newProcVisit">
              <option value="SCREENING">SCREENING</option>
              <option value="WEEK_4">WEEK_4</option>
              <option value="WEEK_12">WEEK_12</option>
              <option value="END_OF_TREATMENT">END_OF_TREATMENT</option>
              <option value="UNSCHEDULED">UNSCHEDULED</option>
            </select>
          </div>
          <div class="form-group">
            <label for="proc-code">CPT / Procedure Code</label>
            <input
              id="proc-code"
              v-model="newProcCode"
              type="text"
              placeholder="e.g. CPT-93000"
            />
          </div>
        </div>
        <div class="form-group">
          <label for="proc-name">Procedure Description</label>
          <input
            id="proc-name"
            v-model="newProcName"
            type="text"
            placeholder="e.g. 12-Lead Electrocardiogram"
          />
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="proc-activity">USDM Activity ID (Optional)</label>
            <input
              id="proc-activity"
              v-model="newProcActivityId"
              type="text"
              placeholder="e.g. ACT-ECG-01"
            />
          </div>
          <div class="form-group">
            <label for="proc-form">eCRF Form ID (Optional)</label>
            <input
              id="proc-form"
              v-model="newProcFormId"
              type="text"
              placeholder="e.g. FORM-ECG-V1"
            />
          </div>
        </div>
        <div
          style="display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 12px"
        >
          <div class="form-group">
            <label for="proc-base">Base Amount ($)</label>
            <input id="proc-base" v-model.number="newProcBase" type="number" />
          </div>
          <div class="form-group">
            <label for="proc-overhead">Overhead %</label>
            <input
              id="proc-overhead"
              v-model.number="newProcOverhead"
              type="number"
            />
          </div>
          <div class="form-group">
            <label for="proc-withholding">Withholding %</label>
            <input
              id="proc-withholding"
              v-model.number="newProcWithholding"
              type="number"
            />
          </div>
        </div>
        <div style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="proc-stipend">Patient Stipend ($)</label>
            <input
              id="proc-stipend"
              v-model.number="newProcStipend"
              type="number"
            />
          </div>
          <div class="form-group">
            <label for="proc-travel">Travel Reimb. ($)</label>
            <input
              id="proc-travel"
              v-model.number="newProcTravel"
              type="number"
            />
          </div>
        </div>
        <div class="form-group">
          <label for="proc-reason">Reason for Change Justification</label>
          <textarea
            id="proc-reason"
            v-model="newProcReason"
            placeholder="State the justification reason..."
          />
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showAddProcedureModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmAddProcedure"
          >
            Add to Grid
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal: Execute Contract Amendment -->
      <ClinicalModal
        v-model="showAmendmentModal"
        :teleport="false"
        dialog-class="modal"
        title="Execute Contract Amendment (Version Increment)"
      >
        <div class="form-group">
          <label for="amend-budget">Revised Total Budget ($)</label>
          <input
            id="amend-budget"
            v-model.number="amendRevisedBudget"
            type="number"
          />
        </div>
        <div class="form-group">
          <label for="amend-summary"
            >Summary of Protocol &amp; Rate Changes</label
          >
          <textarea
            id="amend-summary"
            v-model="amendSummary"
            placeholder="Detail the reasons and scope of this budget contract amendment..."
          />
        </div>
        <div class="form-group">
          <label for="amend-reason">GxP Reason for Change</label>
          <input
            id="amend-reason"
            v-model="amendReason"
            type="text"
            placeholder="Protocol amendment execution"
          />
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showAmendmentModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmExecuteAmendment"
          >
            Sign &amp; Execute Amendment
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal: Create Budget Adjustment -->
      <ClinicalModal
        v-model="showAdjustmentModal"
        :teleport="false"
        dialog-class="modal"
        title="Create Budget Adjustment / Penalty"
      >
        <div class="form-group">
          <label for="adj-type">Adjustment Classification</label>
          <select id="adj-type" v-model="adjType">
            <option value="PASSTHROUGH_REIMBURSEMENT">
              Passthrough Expense Reimbursement (+)
            </option>
            <option value="PROTOCOL_DEVIATION_PENALTY">
              Protocol Deviation Penalty Deduction (-)
            </option>
            <option value="OUT_OF_WINDOW_PENALTY">
              Out-of-Window Visit Penalty (-)
            </option>
            <option value="SITE_BONUS">
              Site Enrollment Performance Bonus (+)
            </option>
            <option value="DISCRETIONARY_CREDIT">
              Discretionary Sponsor Credit (+)
            </option>
          </select>
        </div>
        <div class="form-group">
          <label for="adj-desc">Description &amp; Clinical Justification</label>
          <input
            id="adj-desc"
            v-model="adjDescription"
            type="text"
            placeholder="e.g. Dry ice shipping courier reimbursement"
          />
        </div>
        <div style="display: grid; grid-template-columns: 2fr 1fr; gap: 12px">
          <div class="form-group">
            <label for="adj-amt">Amount ($)</label>
            <input
              id="adj-amt"
              v-model.number="adjAmount"
              type="number"
              step="0.01"
            />
          </div>
          <div class="form-group">
            <label for="adj-curr">Currency</label>
            <input id="adj-curr" v-model="adjCurrency" type="text" />
          </div>
        </div>
        <div class="form-group">
          <label for="adj-reason">GxP Reason for Change</label>
          <textarea
            id="adj-reason"
            v-model="adjReason"
            placeholder="State reason for change..."
          />
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showAdjustmentModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmCreateAdjustment"
          >
            Save Adjustment
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 1: General Change Justification -->
      <ClinicalModal
        v-model="showJustificationModal"
        :teleport="false"
        dialog-class="modal"
        :title="justificationTitle"
      >
        <p
          style="
            font-size: 13px;
            color: var(--semantic-color-text-secondary);
            margin-bottom: 12px;
            line-height: 1.4;
          "
        >
          To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>, you
          must document a valid GxP justification reason for this action.
        </p>
        <div class="form-group">
          <label for="modal-justification-reason"
            >Change Justification Reason</label
          >
          <textarea
            id="modal-justification-reason"
            v-model="justificationReason"
            :placeholder="justificationPlaceholder"
          />
          <div v-if="justificationError" class="error-msg">
            ⚠️ {{ justificationError }}
          </div>
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showJustificationModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmJustification"
          >
            Confirm Change
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 2: FDA Part 11 Electronic Signature & Re-Authentication -->
      <ClinicalModal
        :open="showSignatureModal"
        :teleport="false"
        :dismissible="!sigBusy"
        dialog-class="modal"
        aria-label="Electronic Signature and Identity Verification"
        title="Electronic Signature & Identity Verification (Part 11)"
        @close="cancelSignatureModal"
      >
        <p
          style="
            font-size: 13px;
            color: var(--semantic-color-text-secondary);
            margin-bottom: 16px;
            line-height: 1.4;
          "
        >
          Please re-authenticate your identity and verify your endorsement of
          this delegation. All signings are permanently logged to the audit
          ledger.
        </p>

        <div class="form-group">
          <label for="sig-username">Username</label>
          <input
            id="sig-username"
            v-model="sigUsername"
            type="text"
            placeholder="Username"
            :disabled="sigBusy"
          />
        </div>

        <div class="form-group">
          <label for="sig-password">Password</label>
          <input
            id="sig-password"
            v-model="sigPassword"
            v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
            placeholder="Password"
            :disabled="sigBusy"
          />
        </div>

        <div class="form-group">
          <label for="sig-totp">MFA/TOTP Token (Optional)</label>
          <input
            id="sig-totp"
            v-model="sigTotp"
            type="text"
            placeholder="MFA Token"
            :disabled="sigBusy"
          />
        </div>

        <div class="form-group">
          <label for="sig-signing-reason">Signing Reason</label>
          <select
            id="sig-signing-reason"
            v-model="sigSigningReason"
            :disabled="sigBusy"
          >
            <option value="INVESTIGATOR_SIGNATURE">
              INVESTIGATOR_SIGNATURE (PI Endorsement)
            </option>
            <option value="APPROVAL">APPROVAL (Supervisory Approval)</option>
            <option value="TECHNICAL_QC">TECHNICAL_QC</option>
          </select>
        </div>

        <div class="form-group">
          <label for="sig-reason-for-change"
            >Reason for Change / Justification</label
          >
          <textarea
            id="sig-reason-for-change"
            v-model="sigReasonForChange"
            placeholder="State your justification reason..."
            :disabled="sigBusy"
          />
        </div>

        <div v-if="sigError" class="error-msg">⚠️ {{ sigError }}</div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            :disabled="sigBusy"
            @click="cancelSignatureModal"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            :disabled="sigBusy"
            @click="confirmSignature"
          >
            {{ sigBusy ? "Verifying..." : "Verify & Digital Sign" }}
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 3: Schedule Monitoring Visit -->
      <ClinicalModal
        v-model="showScheduleVisitModal"
        :teleport="false"
        dialog-class="modal"
        title="Schedule Clinical Monitoring Visit"
      >
        <div class="form-group">
          <label for="sched-visit-type">Visit Type</label>
          <select id="sched-visit-type" v-model="newVisitType">
            <option value="SIV">Site Initiation Visit (SIV)</option>
            <option value="IMV">Interim Monitoring Visit (IMV)</option>
            <option value="COV">Close Out Visit (COV)</option>
          </select>
        </div>

        <div class="form-group">
          <label for="sched-visit-date">Scheduled Date</label>
          <input
            id="sched-visit-date"
            v-model="newVisitScheduledDate"
            type="date"
          />
        </div>

        <div class="form-group">
          <label for="sched-visit-cra">Assigned CRA</label>
          <input
            id="sched-visit-cra"
            v-model="newVisitCra"
            type="text"
            placeholder="e.g. cra.monitor"
          />
        </div>

        <div class="form-group">
          <label for="sched-visit-reason"
            >Reason for Change / Setup Justification</label
          >
          <textarea
            id="sched-visit-reason"
            v-model="newVisitReason"
            placeholder="State the regulatory justification for scheduling this visit..."
          />
        </div>

        <div v-if="scheduleError" class="error-msg">⚠️ {{ scheduleError }}</div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showScheduleVisitModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmScheduleVisit"
          >
            Schedule & Issue confirmation
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 4: Reallocate CRA Workload -->
      <ClinicalModal
        v-model="showReallocateModal"
        :teleport="false"
        dialog-class="modal"
        title="Allocate CRA to Site"
      >
        <div class="form-group">
          <label for="alloc-cra-id">CRA ID</label>
          <input
            id="alloc-cra-id"
            v-model="allocCraId"
            type="text"
            placeholder="e.g. cra_alice"
          />
        </div>

        <div class="form-group">
          <label for="alloc-status">Allocation Status</label>
          <select id="alloc-status" v-model="allocStatus">
            <option value="ACTIVE">ACTIVE</option>
            <option value="INACTIVE">INACTIVE</option>
          </select>
        </div>

        <div class="form-group">
          <label for="alloc-reason"
            >Reason for Change / Allocation Justification</label
          >
          <textarea
            id="alloc-reason"
            v-model="allocReason"
            placeholder="Explain the allocation change context..."
          />
        </div>

        <div v-if="allocError" class="error-msg">⚠️ {{ allocError }}</div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showReallocateModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmReallocate"
          >
            Save Allocation
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 5: Recruitment Metrics Update -->
      <ClinicalModal
        v-model="showRecruitmentModal"
        :teleport="false"
        dialog-class="modal"
        title="Log Site Recruitment Metrics Update"
      >
        <div class="form-group">
          <label for="rec-screened">Screened Subject Count</label>
          <input id="rec-screened" v-model="recScreened" type="number" />
        </div>

        <div class="form-group">
          <label for="rec-enrolled">Enrolled Subject Count</label>
          <input id="rec-enrolled" v-model="recEnrolled" type="number" />
        </div>

        <div class="form-group">
          <label for="rec-target">Target Enrollment Count</label>
          <input id="rec-target" v-model="recTarget" type="number" />
        </div>

        <div class="form-group">
          <label for="rec-reason"
            >Reason for Change / Update Justification</label
          >
          <textarea
            id="rec-reason"
            v-model="recReason"
            placeholder="State the compliance justification for updating recruitment statistics..."
          />
        </div>

        <div v-if="recError" class="error-msg">⚠️ {{ recError }}</div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showRecruitmentModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmRecruitment"
          >
            Submit Metrics
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 6: Delegate New Authority Task -->
      <ClinicalModal
        v-model="showDelegateModal"
        :teleport="false"
        dialog-class="modal"
        title="Delegate Site Trial Task"
      >
        <div class="form-group">
          <label for="del-staff-id">Staff User ID</label>
          <input
            id="del-staff-id"
            v-model="delStaffId"
            type="text"
            placeholder="e.g. kc-crc-001"
          />
        </div>

        <div class="form-group">
          <label for="del-start-date">Start Date</label>
          <input id="del-start-date" v-model="delStartDate" type="date" />
        </div>

        <div class="form-group">
          <span
            id="del-duty-task-codes-label"
            style="display: block; font-weight: 600"
          >
            Delegated Duty Task Codes
          </span>
          <div
            role="group"
            aria-labelledby="del-duty-task-codes-label"
            style="
              display: flex;
              flex-direction: column;
              gap: 6px;
              margin-top: 4px;
            "
          >
            <label
              v-for="task in availableTasks"
              :key="task.value"
              style="
                display: flex;
                gap: 8px;
                font-weight: normal;
                align-items: center;
                cursor: pointer;
              "
            >
              <input
                v-model="delTaskCodes"
                type="checkbox"
                :value="task.value"
                style="width: auto; margin: 0"
              />
              {{ task.text }} (<code>{{ task.value }}</code
              >)
            </label>
          </div>
        </div>

        <div class="form-group">
          <label for="del-reason"
            >Reason for Change / Delegation Justification</label
          >
          <textarea
            id="del-reason"
            v-model="delReason"
            placeholder="Justification for creating this task delegation assignment..."
          />
        </div>

        <div v-if="delError" class="error-msg">⚠️ {{ delError }}</div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showDelegateModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmDelegate"
          >
            Submit Delegation
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 7: Create Site Milestone -->
      <ClinicalModal
        v-model="showCreateMilestoneModal"
        :teleport="false"
        dialog-class="modal"
        title="Add Site Milestone"
      >
        <div class="form-group">
          <label for="ms-type">Milestone Type</label>
          <select id="ms-type" v-model="newMsType">
            <option value="SITE_SELECTION">SITE_SELECTION</option>
            <option value="INITIATION_VISIT">INITIATION_VISIT</option>
            <option value="SITE_ACTIVATION">SITE_ACTIVATION</option>
            <option value="FIRST_SUBJECT_ENROLLED">
              FIRST_SUBJECT_ENROLLED
            </option>
            <option value="DB_LOCK">DB_LOCK</option>
          </select>
        </div>

        <div class="form-group">
          <label for="ms-planned-date">Planned Date</label>
          <input id="ms-planned-date" v-model="newMsPlannedDate" type="date" />
        </div>

        <div class="form-group">
          <label for="ms-status">Status</label>
          <select id="ms-status" v-model="newMsStatus">
            <option value="PLANNED">PLANNED</option>
            <option value="ACHIEVED">ACHIEVED</option>
          </select>
        </div>

        <div class="form-group">
          <label for="ms-reason">Reason for Change / Justification</label>
          <textarea
            id="ms-reason"
            v-model="newMsReason"
            placeholder="State the justification reason for creating this milestone..."
          />
        </div>

        <div v-if="msError" class="error-msg">⚠️ {{ msError }}</div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showCreateMilestoneModal = false"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-primary"
            @click="confirmCreateMilestone"
          >
            Add Milestone
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 8: Complete Scheduled Monitoring Visit with Structured Findings -->
      <ClinicalModal
        v-model="showCompleteVisitModal"
        :teleport="false"
        overlay-id="modal-complete-visit"
        size="lg"
        dialog-class="modal"
        title="Complete Monitoring Visit"
      >
        <div class="form-group">
          <label for="complete-visit-select">Scheduled Visit</label>
          <select id="complete-visit-select" v-model="completeVisitId">
            <option v-for="sv in scheduledVisits" :key="sv.id" :value="sv.id">
              {{ sv.visit_type }} —
              {{ sv.scheduled_date ? sv.scheduled_date.slice(0, 10) : "N/A" }}
              ({{ sv.cra_id || "Unassigned" }})
            </option>
          </select>
        </div>

        <div class="form-group">
          <label for="complete-visit-actual-date">Actual Visit Date</label>
          <input
            id="complete-visit-actual-date"
            v-model="completeVisitActualDate"
            type="date"
          />
        </div>

        <div class="form-group">
          <div
            style="
              display: flex;
              justify-content: space-between;
              align-items: center;
              margin-bottom: 8px;
            "
          >
            <span
              style="
                font-weight: 600;
                font-size: 13px;
                color: var(--semantic-color-text-body);
              "
              >Monitoring Findings</span
            >
            <button
              id="btn-add-visit-finding"
              type="button"
              class="btn btn-secondary btn-sm"
              @click="addVisitFinding"
            >
              + Add Finding
            </button>
          </div>
          <div
            v-if="completeVisitFindings.length === 0"
            style="
              font-size: 12px;
              color: var(--semantic-color-text-secondary);
              margin-bottom: 6px;
            "
          >
            No findings logged for this visit. Use "+ Add Finding" to record
            deviations or action items.
          </div>
          <div
            v-for="(finding, idx) in completeVisitFindings"
            :key="idx"
            style="
              border: 1px solid var(--semantic-color-border);
              border-radius: 6px;
              padding: 10px;
              margin-bottom: 8px;
              background: var(--semantic-color-surface-muted);
            "
          >
            <div class="form-group" style="margin-bottom: 8px">
              <label :for="`finding-text-${idx}`">Finding Description</label>
              <input
                :id="`finding-text-${idx}`"
                v-model="finding.text"
                type="text"
                placeholder="Describe monitoring observation or protocol finding..."
              />
            </div>
            <div
              style="
                display: grid;
                grid-template-columns: 1fr 1fr auto;
                gap: 8px;
                align-items: end;
              "
            >
              <div>
                <label :for="`finding-severity-${idx}`">Severity</label>
                <select
                  :id="`finding-severity-${idx}`"
                  v-model="finding.severity"
                >
                  <option value="MINOR">MINOR</option>
                  <option value="MAJOR">MAJOR</option>
                  <option value="CRITICAL">CRITICAL</option>
                </select>
              </div>
              <div>
                <label :for="`finding-resolution-${idx}`"
                  >Resolution Status</label
                >
                <select
                  :id="`finding-resolution-${idx}`"
                  v-model="finding.resolution_status"
                >
                  <option value="OPEN">OPEN</option>
                  <option value="IN_PROGRESS">IN_PROGRESS</option>
                  <option value="CLOSED">CLOSED</option>
                </select>
              </div>
              <button
                type="button"
                class="btn btn-cancel btn-sm"
                @click="removeVisitFinding(idx)"
              >
                Remove
              </button>
            </div>
          </div>
        </div>

        <div class="form-group">
          <label for="complete-visit-reason"
            >Reason for Change / Visit Closure Summary</label
          >
          <textarea
            id="complete-visit-reason"
            v-model="completeVisitReason"
            placeholder="State the GxP justification for completing this monitoring visit..."
          />
        </div>

        <div v-if="completeVisitError" class="error-msg">
          ⚠️ {{ completeVisitError }}
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            @click="showCompleteVisitModal = false"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-complete-visit"
            type="button"
            class="btn btn-primary"
            @click="confirmCompleteVisit"
          >
            Complete Visit
          </button>
        </template>
      </ClinicalModal>

      <!-- Modal 9: Compile Visit Letter PDF -->
      <ClinicalModal
        v-model="showCompileLetterModal"
        :teleport="false"
        :dismissible="!compileLetterBusy"
        overlay-id="modal-compile-letter"
        dialog-class="modal"
        title="Compile Site Visit Letter"
      >
        <div class="form-group">
          <label for="compile-letter-visit">Monitoring Visit</label>
          <select id="compile-letter-visit" v-model="compileLetterVisitId">
            <option value="">Ad-hoc / Current Site Scope</option>
            <option v-for="mv in visits" :key="mv.id" :value="mv.id">
              {{ mv.visit_type }} ({{ mv.status }}) —
              {{ mv.scheduled_date ? mv.scheduled_date.slice(0, 10) : "N/A" }}
            </option>
          </select>
        </div>

        <div class="form-group">
          <label for="compile-letter-template">Letter Template</label>
          <select id="compile-letter-template" v-model="compileTemplateName">
            <option value="confirmation_letter">
              Visit Confirmation Letter (confirmation_letter)
            </option>
            <option value="follow_up_letter">
              Monitoring Follow-Up Letter (follow_up_letter)
            </option>
          </select>
        </div>

        <div class="form-group">
          <label
            for="compile-letter-finalize"
            style="
              display: flex;
              align-items: center;
              gap: 8px;
              cursor: pointer;
            "
          >
            <input
              id="compile-letter-finalize"
              v-model="compileFinalize"
              type="checkbox"
              style="width: auto; margin: 0"
            />
            Finalize with Part 11 eSignature &amp; file to document vault
          </label>
        </div>

        <div v-if="compileFinalize" class="form-group">
          <label for="compile-signer-name">Signer Full Name</label>
          <input
            id="compile-signer-name"
            v-model="compileSignerName"
            type="text"
            placeholder="e.g. Jane Doe, Lead CRA"
          />
        </div>

        <div v-if="compileFinalize" class="form-group">
          <label for="compile-signing-purpose">Signing Purpose</label>
          <input
            id="compile-signing-purpose"
            v-model="compileSigningPurpose"
            type="text"
            placeholder="Monitoring Visit Report Issuance"
          />
        </div>

        <div class="form-group">
          <label for="compile-investigator-email"
            >Investigator Dispatch Email (Optional)</label
          >
          <input
            id="compile-investigator-email"
            v-model="compileInvestigatorEmail"
            type="email"
            placeholder="pi.site101@example.com"
          />
        </div>

        <div class="form-group">
          <label for="compile-letter-reason"
            >Reason for Change / Issuance Justification</label
          >
          <textarea
            id="compile-letter-reason"
            v-model="compileLetterReason"
            placeholder="Document the regulatory justification for compiling this letter..."
          />
        </div>

        <div v-if="compileLetterError" class="error-msg">
          ⚠️ {{ compileLetterError }}
        </div>
        <template #footer>
          <button
            type="button"
            class="btn btn-cancel"
            :disabled="compileLetterBusy"
            @click="showCompileLetterModal = false"
          >
            Cancel
          </button>
          <button
            id="btn-confirm-compile-letter"
            type="button"
            class="btn btn-primary"
            :disabled="compileLetterBusy"
            @click="confirmCompileLetter"
          >
            {{
              compileLetterBusy
                ? "Compiling..."
                : compileFinalize
                  ? "Verify Signature & Compile PDF"
                  : "Compile Draft PDF"
            }}
          </button>
        </template>
      </ClinicalModal>

      <!-- Startup & Greenlight Tab Content -->
      <div
        v-if="!sectionMode && activeTab === 'startup'"
        style="margin-top: 20px"
      >
        <SsuGreenlightConsole :study-id="studyId" :site-id="siteId" />
      </div>

      <!-- RTSM & Dose Escalation Tab Content -->
      <div
        v-if="demoBuild && !sectionMode && activeTab === 'rtsm'"
        style="
          margin-top: 20px;
          display: flex;
          flex-direction: column;
          gap: 24px;
        "
      >
        <div
          class="rtsm-action-bar"
          style="
            display: flex;
            justify-content: space-between;
            align-items: center;
            background: var(--semantic-color-surface-elevated);
            padding: 16px 20px;
            border-radius: 8px;
          "
        >
          <div>
            <h3
              style="
                margin: 0;
                color: var(--semantic-color-text-inverse);
                font-size: 16px;
              "
            >
              RTSM Site Inventory &amp; Return Accountability
            </h3>
            <p
              style="
                margin: 4px 0 0;
                color: var(--semantic-color-text-inverse-muted);
                font-size: 13px;
              "
            >
              Manage site IP kit returns, barcode scanning, residual volumes,
              and Part 11 e-signatures.
            </p>
          </div>
          <button
            type="button"
            class="btn btn-primary"
            @click="isIpKitReconciliationDrawerOpen = true"
          >
            🔍 Launch Bulk Kit Reconciliation Drawer
          </button>
        </div>
        <AdaptiveDoseEscalationConsole :study-id="studyId" />
        <CellGeneTherapyCocConsole />
        <BulkIpKitReconciliationDrawer
          :is-open="isIpKitReconciliationDrawerOpen"
          :study-id="studyId"
          :site-id="siteId"
          @close="isIpKitReconciliationDrawerOpen = false"
        />
      </div>
    </template>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";
import { useRoute } from "vue-router";
import {
  buildStepUpVerificationPayload,
  ClinicalModal,
  SEMANTIC_ACTIONS,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
} from "ui";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import CraVerificationConsole from "../components/persona/CraVerificationConsole.vue";
import SsuGreenlightConsole from "../components/clinical/SsuGreenlightConsole.vue";
import AdaptiveDoseEscalationConsole from "../components/clinical/AdaptiveDoseEscalationConsole.vue";
import CellGeneTherapyCocConsole from "../components/clinical/CellGeneTherapyCocConsole.vue";
import BulkIpKitReconciliationDrawer from "../components/clinical/BulkIpKitReconciliationDrawer.vue";
import SiteActivationPanel from "../components/clinical/SiteActivationPanel.vue";
import PlansRoadmapLink from "../components/clinical/PlansRoadmapLink.vue";
import CtmsGreenlightSection from "../components/clinical/CtmsGreenlightSection.vue";
import CtmsMilestonesSection from "../components/clinical/CtmsMilestonesSection.vue";
import CtmsMonitoringSection from "../components/clinical/CtmsMonitoringSection.vue";
import CtmsDelegationSection from "../components/clinical/CtmsDelegationSection.vue";
import RtsmSupplySection from "../components/clinical/RtsmSupplySection.vue";
import { isDemoBuild } from "../utils/demoMode.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import apiClient from "../services/api";
import { ctmsApi } from "../api/ctmsApi";
import { executionApi } from "../api/executionApi";
import {
  DOA_SIGNOFF_ROLE_REASON,
  MONITORING_LETTER_COMPILE_ROLE_REASON,
  MONITORING_VISIT_COMPLETE_ROLE_REASON,
  MONITORING_VISIT_SIGNOFF_ROLE_REASON,
  SITE_MILESTONE_ROLE_REASON,
  canManageDoa,
  canManageMonitoringVisits,
  canManageSiteMilestones,
  canSignOffDoa,
  canSignOffMonitoringVisit,
} from "../canvas/authority";
import { useToastStore } from "../stores/toasts.js";

const props = defineProps({
  studyCode: { type: String, default: "" },
  initialSiteId: { type: String, default: "" },
  sectionMode: { type: String, default: "" },
});

const KNOWN_SITE_CODES = ["SITE-101", "SITE-102"];

// Active session store/router setup
const store = useClinicalStore();
const authStore = useAuthStore();
const route = useRoute();
const demoBuild = isDemoBuild();
const isIpKitReconciliationDrawerOpen = ref(false);

const liveTabs = [
  { id: "activation", label: "Site activation" },
  { id: "startup", label: "Startup readiness" },
  { id: "milestones", label: "Site milestones" },
  { id: "monitoring", label: "Monitoring visits" },
  { id: "delegation", label: "Delegation log" },
  { id: "supply", label: "Investigational supply" },
];
const liveTab = ref(
  route?.path === "/ssu" || route?.query?.tab === "startup"
    ? "startup"
    : typeof route?.query?.tab === "string" &&
        liveTabs.some((t) => t.id === route.query.tab)
      ? route.query.tab
      : "activation"
);

// Scope comes from props or the route; apply before the first render so child panels see it.
if (route && route.query) {
  if (typeof route.query.studyId === "string" && route.query.studyId) {
    store.activeStudyId = route.query.studyId;
  }
  if (typeof route.query.siteId === "string" && route.query.siteId) {
    store.activeSiteId = route.query.siteId;
  }
  if (typeof route.query.subjectId === "string" && route.query.subjectId) {
    store.activeSubjectId = route.query.subjectId;
  }
  if (typeof route.query.visitId === "string" && route.query.visitId) {
    store.activeVisitId = route.query.visitId;
  }
}

const initialResolvedSite =
  props.initialSiteId ||
  (typeof route?.query?.site === "string" ? route.query.site : "") ||
  store.activeSiteId ||
  "";

const localStudyId = ref(props.studyCode || store.activeStudyId || "");
const localSiteId = ref(initialResolvedSite);
const sitePickerChoice = ref(
  !initialResolvedSite
    ? ""
    : KNOWN_SITE_CODES.includes(initialResolvedSite)
      ? initialResolvedSite
      : "__custom__"
);

function onSitePickerChange() {
  if (sitePickerChoice.value === "__custom__") {
    if (KNOWN_SITE_CODES.includes(localSiteId.value)) {
      localSiteId.value = "";
    }
  } else {
    localSiteId.value = sitePickerChoice.value;
  }
  syncScopeToStore();
}

function syncScopeToStore() {
  if (!props.studyCode) {
    store.activeStudyId = localStudyId.value.trim();
  }
  store.activeSiteId = localSiteId.value.trim();
}

watch(
  () => store.activeStudyId,
  (next) => {
    if (!props.studyCode && next && next !== localStudyId.value) {
      localStudyId.value = next;
    }
  }
);

watch(
  () => store.activeSiteId,
  (next) => {
    if (next && next !== localSiteId.value) {
      localSiteId.value = next;
      sitePickerChoice.value = KNOWN_SITE_CODES.includes(next)
        ? next
        : "__custom__";
    }
  }
);

// Scope properties: outside demo builds never fabricate default study/site IDs.
const siteId = computed(() => {
  const candidate =
    localSiteId.value.trim() ||
    props.initialSiteId.trim() ||
    (store.activeSiteId || "").trim();
  if (candidate) return candidate;
  return demoBuild ? "SITE-001" : "";
});

const studyId = computed(() => {
  const candidate =
    props.studyCode.trim() ||
    localStudyId.value.trim() ||
    (store.activeStudyId || "").trim();
  if (candidate) return candidate;
  return demoBuild ? "STUDY-USDM-001" : "";
});

const hasActiveScope = computed(() =>
  Boolean(studyId.value.trim() && siteId.value.trim())
);

// Role permissions aligned with Cadence.Security.Rbac.PermissionMatrix
const canManageMilestones = computed(() => canManageSiteMilestones(authStore));
const canManageVisits = computed(() => canManageMonitoringVisits(authStore));
const canSignOffVisits = computed(() => canSignOffMonitoringVisit(authStore));
const canEditDoa = computed(() => canManageDoa(authStore));
const canSignDoa = computed(() => canSignOffDoa(authStore));

// Tabs selection
const activeTab = ref(
  route?.path === "/ssu" || route?.query?.tab === "startup"
    ? "startup"
    : "operations"
);

// Core Operations state
const milestones = ref([]);
const visits = ref([]);
const allocations = ref([]);
const recruitment = ref([]);
const operationsLoadError = ref("");
const compiledLetters = ref([]);

// Delegation state
const delegatedStaff = ref([]);
const auditHistory = ref([]);
const piName = ref("");
const doaLoadError = ref("");

// Financials & Grant Contracts state
const investigatorGrant = ref(null);
const financialTemplates = ref([]);
const selectedTemplateId = ref("");
const procedureGrids = ref([]);
const filteredVersionIndex = ref("ALL");
const contractAmendments = ref([]);
const budgetAdjustments = ref([]);
const exchangeRates = ref([]);
const selectedVisitForCalc = ref("SCREENING");
const selectedCurrencyForCalc = ref("USD");
const visitPayableCalc = ref(null);

// Modals & Form state for Financials
const showCreateTemplateModal = ref(false);
const newTmplName = ref("");
const newTmplTier = ref("TIER_1_ACADEMIC");
const newTmplCurrency = ref("USD");
const newTmplOverhead = ref(25.0);
const newTmplWithholding = ref(10.0);
const newTmplStartup = ref(10000.0);
const newTmplCloseout = ref(4000.0);
const newTmplIrb = ref(2500.0);
const newTmplStipend = ref(50.0);
const newTmplTravel = ref(25.0);
const newTmplReason = ref("");

const showAddProcedureModal = ref(false);
const newProcVisit = ref("SCREENING");
const newProcCode = ref("");
const newProcName = ref("");
const newProcBase = ref(250.0);
const newProcOverhead = ref(25.0);
const newProcWithholding = ref(10.0);
const newProcActivityId = ref("");
const newProcFormId = ref("");
const newProcStipend = ref(50.0);
const newProcTravel = ref(25.0);
const newProcReason = ref("");

const showAmendmentModal = ref(false);
const amendSummary = ref("");
const amendRevisedBudget = ref(0);
const amendReason = ref("");

const showAdjustmentModal = ref(false);
const adjType = ref("PASSTHROUGH_REIMBURSEMENT");
const adjDescription = ref("");
const adjAmount = ref(0);
const adjCurrency = ref("USD");
const adjReason = ref("");

// Live KPI Calculations
const totalEnrolledCount = computed(() => {
  if (recruitment.value && recruitment.value.length > 0) {
    return recruitment.value.reduce(
      (sum, r) => sum + (r.enrolled_count || 0),
      0
    );
  }
  return store.subjects?.length || 8;
});

const totalScreenedCount = computed(() => {
  if (recruitment.value && recruitment.value.length > 0) {
    return recruitment.value.reduce(
      (sum, r) => sum + (r.screened_count || 0),
      0
    );
  }
  return 15;
});

const totalTargetCount = computed(() => {
  if (recruitment.value && recruitment.value.length > 0) {
    return recruitment.value.reduce((sum, r) => sum + (r.target_count || 0), 0);
  }
  return 20;
});

const totalSubjectsKpi = computed(() => {
  return totalEnrolledCount.value;
});

const enrollmentRateKpi = computed(() => {
  if (totalTargetCount.value > 0) {
    return Math.round(
      (totalEnrolledCount.value / totalTargetCount.value) * 100
    );
  }
  return 60;
});

const verifiedSdvPercentageKpi = computed(() => {
  return 85;
});

const openQueriesCountKpi = computed(() => {
  if (!store.formQueries) return 0;
  return Object.values(store.formQueries).filter(
    (q) => q && (q.status === "OPEN" || q.status === "REOPENED")
  ).length;
});

// #4523: the DoA table shows only what the DoA service returned; an empty log is an empty state and a failed
// load is an error, never sample delegation records.
const effectiveDelegatedStaff = computed(() => delegatedStaff.value || []);

// Names, roles and training come from the DoA record when the service provides them; nothing is inferred
// from the user id (#4523).
function getStaffNameAndRole(record) {
  return [record?.staff_name, record?.role].filter(Boolean).join(" · ");
}

function getStaffCertificates(record) {
  return Array.isArray(record?.training_certificates)
    ? record.training_certificates
    : [];
}

// Modal States
const showJustificationModal = ref(false);
const justificationReason = ref("");
const justificationError = ref("");
const justificationTitle = ref("");
const justificationPlaceholder = ref("");
let justificationCallback = null;

const showSignatureModal = ref(false);
const sigUsername = ref("");
const sigPassword = ref("");
const sigTotp = ref("");
const sigSigningReason = ref("INVESTIGATOR_SIGNATURE");
const sigReasonForChange = ref("");
const sigError = ref("");
const sigBusy = ref(false);
let signatureActionPath = "/api/v1/ctms/doa/sign-off";
let signatureSemanticAction = SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF;
let signatureCallback = null;

const showScheduleVisitModal = ref(false);
const newVisitType = ref("IMV");
const newVisitScheduledDate = ref("");
const newVisitCra = ref("cra.monitor");
const newVisitReason = ref("");
const scheduleError = ref("");

const showCompleteVisitModal = ref(false);
const completeVisitId = ref("");
const completeVisitActualDate = ref("");
const completeVisitFindings = ref([]);
const completeVisitReason = ref("");
const completeVisitError = ref("");

const showCompileLetterModal = ref(false);
const compileLetterVisitId = ref("");
const compileTemplateName = ref("follow_up_letter");
const compileFinalize = ref(true);
const compileSignerName = ref("");
const compileSigningPurpose = ref("Monitoring Visit Report Issuance");
const compileInvestigatorEmail = ref("");
const compileLetterReason = ref("");
const compileLetterError = ref("");
const compileLetterBusy = ref(false);

const scheduledVisits = computed(() =>
  (visits.value || []).filter((v) => v.status === "SCHEDULED")
);

const showReallocateModal = ref(false);
const allocCraId = ref("cra_alice");
const allocStatus = ref("ACTIVE");
const allocReason = ref("");
const allocError = ref("");

const showRecruitmentModal = ref(false);
const recScreened = ref(15);
const recEnrolled = ref(8);
const recTarget = ref(20);
const recReason = ref("");
const recError = ref("");

const showDelegateModal = ref(false);
const delStaffId = ref("");
const delStartDate = ref("");
const delTaskCodes = ref([]);
const delReason = ref("");
const delError = ref("");

const showCreateMilestoneModal = ref(false);
const newMsType = ref("SITE_ACTIVATION");
const newMsPlannedDate = ref("");
const newMsStatus = ref("PLANNED");
const newMsReason = ref("");
const msError = ref("");

const availableTasks = [
  { value: "SUBJECT_INFORMED_CONSENT", text: "Subject Informed Consent" },
  { value: "CRF_DATA_ENTRY", text: "CRF Data Entry" },
  { value: "SUBJECT_SCREENING", text: "Subject Screening" },
  { value: "SAE_REPORTING", text: "SAE Reporting" },
  { value: "PRINCIPAL_INVESTIGATOR", text: "Principal Investigator Oversight" },
];

const complianceStatus = ref({ is_complete: false, missing_documents: [] });
const loadingCompliance = ref(false);
const complianceLoadError = ref("");

async function loadComplianceStatus() {
  const currentStudy = studyId.value.trim();
  const currentSite = siteId.value.trim();
  if (!currentStudy || !currentSite) {
    complianceStatus.value = { is_complete: false, missing_documents: [] };
    complianceLoadError.value = "";
    return;
  }
  loadingCompliance.value = true;
  complianceLoadError.value = "";
  try {
    const res = await executionApi.getSiteComplianceStatus(
      currentSite,
      currentStudy
    );
    complianceStatus.value = res || {
      is_complete: false,
      missing_documents: [],
    };
  } catch (err) {
    complianceStatus.value = { is_complete: false, missing_documents: [] };
    complianceLoadError.value =
      err?.message || "Site document readiness check failed.";
  } finally {
    loadingCompliance.value = false;
  }
}

// Load Operations Data from Real-time Backend
async function loadOperationsData() {
  const currentStudy = studyId.value.trim();
  const currentSite = siteId.value.trim();
  if (!currentStudy || !currentSite) {
    milestones.value = [];
    visits.value = [];
    allocations.value = [];
    recruitment.value = [];
    operationsLoadError.value = "";
    return;
  }

  operationsLoadError.value = "";
  try {
    loadComplianceStatus();
    if (demoBuild) {
      const [milestonesRes, visitsRes, workloadRes, recruitmentRes] =
        await Promise.all([
          ctmsApi.listSiteMilestones({
            site_id: currentSite,
            study_id: currentStudy,
          }),
          ctmsApi.listMonitoringVisits({
            site_id: currentSite,
            study_id: currentStudy,
          }),
          apiClient.get(`/api/v1/ctms/cra-allocations/workload`),
          apiClient.get(
            `/api/v1/ctms/recruitment?site_id=${currentSite}&study_id=${currentStudy}`
          ),
        ]);
      milestones.value = milestonesRes || [];
      visits.value = visitsRes || [];
      allocations.value = workloadRes || [];
      recruitment.value = recruitmentRes || [];
      return;
    }

    if (props.sectionMode === "milestones") {
      milestones.value =
        (await ctmsApi.listSiteMilestones({
          site_id: currentSite,
          study_id: currentStudy,
        })) || [];
      return;
    }

    if (props.sectionMode === "monitoring") {
      visits.value =
        (await ctmsApi.listMonitoringVisits({
          site_id: currentSite,
          study_id: currentStudy,
        })) || [];
      return;
    }

    const [milestonesRes, visitsRes] = await Promise.all([
      ctmsApi.listSiteMilestones({
        site_id: currentSite,
        study_id: currentStudy,
      }),
      ctmsApi.listMonitoringVisits({
        site_id: currentSite,
        study_id: currentStudy,
      }),
    ]);
    milestones.value = milestonesRes || [];
    visits.value = visitsRes || [];
  } catch (err) {
    if (err?.name === "AbortError") return;
    const toasts = useToastStore();
    const toastId = toasts.apiError("Site operations could not be loaded", err);
    operationsLoadError.value =
      toasts.toasts.find((t) => t.id === toastId)?.message || "Request failed";
  }
}

// Load Delegation logs from Real-time Backend DoA endpoints
async function loadDelegationData() {
  const currentSite = siteId.value.trim();
  if (!currentSite) {
    delegatedStaff.value = [];
    auditHistory.value = [];
    piName.value = "";
    doaLoadError.value = "";
    return;
  }
  try {
    const res = await ctmsApi.getSiteDoaLog(currentSite);
    delegatedStaff.value = res?.delegated_staff || [];
    auditHistory.value = res?.audit_history || [];
    piName.value = res?.pi_name || "";
    doaLoadError.value = "";
  } catch (err) {
    if (err?.name === "AbortError") return;
    delegatedStaff.value = [];
    auditHistory.value = [];
    piName.value = "";
    const toasts = useToastStore();
    const toastId = toasts.apiError(
      "Delegation of Authority log could not be loaded",
      err
    );
    doaLoadError.value =
      toasts.toasts.find((t) => t.id === toastId)?.message || "Request failed";
  }
}

// Watchers for refetch on scope, section mode, or tab change
watch(
  [siteId, studyId, activeTab, () => props.sectionMode],
  () => {
    if (!demoBuild && !props.sectionMode) {
      return;
    }
    if (
      props.sectionMode === "milestones" ||
      props.sectionMode === "monitoring"
    ) {
      loadOperationsData();
      return;
    }
    if (activeTab.value === "operations") {
      loadOperationsData();
    } else if (activeTab.value === "financials" && demoBuild) {
      loadFinancialData();
    } else if (activeTab.value === "delegation") {
      loadDelegationData();
    }
  },
  { immediate: true }
);

// Prompt for GxP Reason for Change Justification
function promptJustification(title, placeholder, callback) {
  justificationTitle.value = title;
  justificationPlaceholder.value = placeholder;
  justificationReason.value = "";
  justificationError.value = "";
  justificationCallback = callback;
  showJustificationModal.value = true;
}

function confirmJustification() {
  const reason = justificationReason.value.trim();
  if (!reason) {
    justificationError.value =
      "A non-empty reason for change is strictly required.";
    return;
  }
  showJustificationModal.value = false;
  if (justificationCallback) justificationCallback(reason);
}

// Prompt for eSignature (FDA Part 11)
function cancelSignatureModal() {
  sigPassword.value = "";
  sigTotp.value = "";
  sigError.value = "";
  sigBusy.value = false;
  showSignatureModal.value = false;
}

function promptSignature(
  callback,
  options = {
    action: "/api/v1/ctms/doa/sign-off",
    semanticAction: SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF,
    defaultSigningReason: "INVESTIGATOR_SIGNATURE",
    initialReason: "",
  }
) {
  sigUsername.value =
    authStore.user?.username ||
    authStore.identity?.username ||
    store.user?.username ||
    "";
  sigPassword.value = "";
  sigTotp.value = "";
  sigSigningReason.value =
    options.defaultSigningReason || "INVESTIGATOR_SIGNATURE";
  sigReasonForChange.value = options.initialReason || "";
  sigError.value = "";
  sigBusy.value = false;
  signatureActionPath = options.action || "/api/v1/ctms/doa/sign-off";
  signatureSemanticAction =
    options.semanticAction ||
    (signatureActionPath.includes("letters/compile")
      ? "ctms.letter.compile"
      : SEMANTIC_ACTIONS.CTMS_DOA_SIGN_OFF);
  signatureCallback = callback;
  showSignatureModal.value = true;
}

async function confirmSignature() {
  if (!sigUsername.value) {
    sigError.value = "Username is required.";
    return;
  }
  if (!sigPassword.value) {
    sigError.value = "Password is required.";
    return;
  }
  if (!sigReasonForChange.value.trim()) {
    sigError.value = "Signing justification is required.";
    return;
  }

  const username = sigUsername.value;
  const password = sigPassword.value;
  const totp = sigTotp.value || null;
  const reason = sigReasonForChange.value.trim();

  // Immediately scrub sensitive credentials on submit (21 CFR Part 11 hygiene)
  sigPassword.value = "";
  sigTotp.value = "";
  sigBusy.value = true;
  sigError.value = "";

  try {
    const verificationPayload = buildStepUpVerificationPayload({
      username,
      password,
      totp,
      action: signatureActionPath,
      semanticAction: signatureSemanticAction,
    });

    const reauthRes = await apiClient.post(
      "/api/v1/auth/signature-verification",
      verificationPayload
    );

    const sigToken = reauthRes?.sig_token;
    if (!sigToken) {
      throw new Error(
        "Signature verification did not return a signature token."
      );
    }

    if (signatureCallback) {
      await signatureCallback(sigToken, reason);
    }
    showSignatureModal.value = false;
  } catch (err) {
    sigError.value = err?.message || "Part 11 identity verification failed.";
  } finally {
    sigBusy.value = false;
  }
}

// --- Write Triggers (Typed OpenAPI Clients) ---

// 1. Achieve Milestone
function achieveMilestone() {
  if (!canManageMilestones.value) return;
  const nextM = milestones.value.find((m) => m.status === "PLANNED");
  if (!nextM) {
    useToastStore().push({
      tone: "warning",
      title: "Milestones",
      message: "All milestones have already been achieved!",
    });
    return;
  }

  promptJustification(
    "Achieve Milestone Justification",
    "Describe the confirmation/clinical evidence of achieving this milestone...",
    async (reason) => {
      try {
        await ctmsApi.updateSiteMilestone(nextM.id, {
          status: "ACHIEVED",
          actual_date: new Date().toISOString(),
          reason_for_change: reason,
        });
        await loadOperationsData();
      } catch (err) {
        useToastStore().apiError("Failed to achieve milestone", err);
      }
    }
  );
}

// Create Custom Milestone
function openCreateMilestone() {
  if (!canManageMilestones.value) return;
  newMsType.value = "SITE_ACTIVATION";
  newMsPlannedDate.value = new Date(Date.now() + 10 * 24 * 3600 * 1000)
    .toISOString()
    .slice(0, 10);
  newMsStatus.value = "PLANNED";
  newMsReason.value = "";
  msError.value = "";
  showCreateMilestoneModal.value = true;
}

async function confirmCreateMilestone() {
  if (!newMsReason.value.trim()) {
    msError.value = "Reason for change is required.";
    return;
  }
  try {
    await ctmsApi.createSiteMilestone({
      site_id: siteId.value.trim(),
      study_id: studyId.value.trim(),
      milestone_type: newMsType.value,
      planned_date: newMsPlannedDate.value
        ? new Date(newMsPlannedDate.value).toISOString()
        : null,
      status: newMsStatus.value,
      reason_for_change: newMsReason.value.trim(),
    });
    showCreateMilestoneModal.value = false;
    await loadOperationsData();
  } catch (err) {
    msError.value = err?.message || "Failed to add milestone.";
    useToastStore().apiError("Failed to add milestone", err);
  }
}

// 2. Schedule Monitoring Visit
function openScheduleVisit() {
  if (!canManageVisits.value) return;
  newVisitType.value = "IMV";
  newVisitScheduledDate.value = new Date(Date.now() + 5 * 24 * 3600 * 1000)
    .toISOString()
    .slice(0, 10);
  newVisitCra.value =
    authStore.identity?.username || store.user?.username || "cra.monitor";
  newVisitReason.value = "";
  scheduleError.value = "";
  showScheduleVisitModal.value = true;
}

async function confirmScheduleVisit() {
  if (!newVisitScheduledDate.value) {
    scheduleError.value = "Scheduled date is required.";
    return;
  }
  if (!newVisitReason.value.trim()) {
    scheduleError.value = "Reason for change is required.";
    return;
  }
  try {
    await ctmsApi.scheduleMonitoringVisit({
      study_id: studyId.value.trim(),
      site_id: siteId.value.trim(),
      cra_id: newVisitCra.value.trim(),
      visit_type: newVisitType.value,
      scheduled_date: new Date(newVisitScheduledDate.value).toISOString(),
      reason_for_change: newVisitReason.value.trim(),
    });
    showScheduleVisitModal.value = false;
    await loadOperationsData();
  } catch (err) {
    scheduleError.value =
      err?.message || "Failed to schedule monitoring visit.";
    useToastStore().apiError("Failed to schedule monitoring visit", err);
  }
}

// 3. Complete Scheduled Monitoring Visit with Structured Findings
function openCompleteVisitModal(targetVisit = null) {
  if (!canManageVisits.value) return;
  const candidate =
    targetVisit && targetVisit.status === "SCHEDULED"
      ? targetVisit
      : scheduledVisits.value[0];
  if (!candidate) {
    useToastStore().push({
      tone: "warning",
      title: "Monitoring Visits",
      message: "No scheduled visits to complete!",
    });
    return;
  }
  completeVisitId.value = candidate.id;
  completeVisitActualDate.value = new Date().toISOString().slice(0, 10);
  completeVisitFindings.value = [];
  completeVisitReason.value = "";
  completeVisitError.value = "";
  showCompleteVisitModal.value = true;
}

function addVisitFinding() {
  completeVisitFindings.value.push({
    text: "",
    severity: "MINOR",
    resolution_status: "OPEN",
  });
}

function removeVisitFinding(index) {
  completeVisitFindings.value.splice(index, 1);
}

async function confirmCompleteVisit() {
  if (!completeVisitId.value) {
    completeVisitError.value = "Select a scheduled monitoring visit.";
    return;
  }
  if (!completeVisitActualDate.value) {
    completeVisitError.value = "Actual visit date is required.";
    return;
  }
  const hasBlankFinding = completeVisitFindings.value.some(
    (f) => !f.text || !f.text.trim()
  );
  if (hasBlankFinding) {
    completeVisitError.value =
      "Every added monitoring finding must include a non-empty description.";
    return;
  }
  if (!completeVisitReason.value.trim()) {
    completeVisitError.value = "Reason for change is required.";
    return;
  }

  completeVisitError.value = "";
  try {
    await ctmsApi.completeMonitoringVisit(completeVisitId.value, {
      actual_date: new Date(completeVisitActualDate.value).toISOString(),
      findings: completeVisitFindings.value.map((f) => ({
        text: f.text.trim(),
        severity: f.severity,
        resolution_status: f.resolution_status,
      })),
      reason_for_change: completeVisitReason.value.trim(),
    });
    showCompleteVisitModal.value = false;
    await loadOperationsData();
  } catch (err) {
    completeVisitError.value =
      err?.message || "Failed to complete monitoring visit.";
    useToastStore().apiError("Failed to complete monitoring visit", err);
  }
}

// Supervisory Sign-off on Completed Visit (Monitor / Sponsor DM / Sysadmin)
function signOffVisit(visit) {
  if (!canSignOffVisits.value) return;
  promptJustification(
    "Supervisory Sign-off on Visit",
    "Enter the review endorsement statement for signing off this completed visit...",
    async (reason) => {
      try {
        await ctmsApi.signOffMonitoringVisit(visit.id, reason);
        await loadOperationsData();
      } catch (err) {
        useToastStore().apiError("Failed to sign off monitoring visit", err);
      }
    }
  );
}

// Compile Site Visit Letter (POST /api/v1/ctms/letters/compile)
function openCompileLetterModal(targetVisit = null) {
  if (!canManageVisits.value) return;
  const fallbackVisit = targetVisit || visits.value[0] || null;
  compileLetterVisitId.value = fallbackVisit ? fallbackVisit.id : "";
  compileTemplateName.value =
    fallbackVisit?.status === "SCHEDULED"
      ? "confirmation_letter"
      : "follow_up_letter";
  compileFinalize.value = true;
  compileSignerName.value =
    authStore.identity?.username || store.user?.username || "";
  compileSigningPurpose.value = "Monitoring Visit Report Issuance";
  compileInvestigatorEmail.value = "";
  compileLetterReason.value = "";
  compileLetterError.value = "";
  compileLetterBusy.value = false;
  showCompileLetterModal.value = true;
}

function buildVisitLetterPayload(reasonForChange) {
  const selectedVisit =
    visits.value.find((v) => v.id === compileLetterVisitId.value) || null;
  const fallbackCra =
    selectedVisit?.cra_id ||
    authStore.identity?.username ||
    store.user?.username ||
    "cra";
  return {
    craId: fallbackCra,
    createdAt: new Date().toISOString(),
    studyId: studyId.value.trim(),
    siteId: siteId.value.trim(),
    visitType: selectedVisit?.visit_type || "IMV",
    scheduledDate: selectedVisit?.scheduled_date || null,
    actualDate: selectedVisit?.actual_date || null,
    findings: Array.isArray(selectedVisit?.findings)
      ? selectedVisit.findings.map((f) => ({
          text: f.text || "",
          severity: f.severity || "MINOR",
          resolutionStatus: f.resolution_status || f.resolutionStatus || "OPEN",
        }))
      : [],
    visitId: selectedVisit?.id || null,
    finalize: Boolean(compileFinalize.value),
    signerName: compileFinalize.value
      ? compileSignerName.value.trim() || fallbackCra
      : null,
    signingPurpose: compileFinalize.value
      ? compileSigningPurpose.value.trim() || "Monitoring Visit Report Issuance"
      : null,
    investigatorEmail: compileInvestigatorEmail.value.trim() || null,
    reasonForChange,
  };
}

async function executeLetterCompilation(reason, sigToken = undefined) {
  compileLetterBusy.value = true;
  compileLetterError.value = "";
  try {
    const payload = buildVisitLetterPayload(reason);
    const compiled = await ctmsApi.compileVisitLetter(
      compileTemplateName.value,
      payload,
      {
        changeReason: reason,
        sigToken,
      }
    );
    compiledLetters.value = [
      compiled,
      ...compiledLetters.value.filter((l) => l.letterId !== compiled.letterId),
    ];
    showCompileLetterModal.value = false;
    useToastStore().push({
      tone: "info",
      title: "Visit letter compiled",
      message: `${compiled.letterId} (${compiled.status}) compiled.`,
    });
  } catch (err) {
    compileLetterError.value =
      err?.message || "Failed to compile visit letter.";
    useToastStore().apiError("Failed to compile visit letter", err);
    throw err;
  } finally {
    compileLetterBusy.value = false;
  }
}

async function confirmCompileLetter() {
  const reason = compileLetterReason.value.trim();
  if (!reason) {
    compileLetterError.value = "Reason for change is required.";
    return;
  }
  if (compileFinalize.value) {
    promptSignature(
      async (sigToken, signedReason) => {
        await executeLetterCompilation(signedReason || reason, sigToken);
      },
      {
        action: "/api/v1/ctms/letters/compile",
        semanticAction: "ctms.letter.compile",
        defaultSigningReason: "APPROVAL",
        initialReason: reason,
      }
    );
    return;
  }
  try {
    await executeLetterCompilation(reason);
  } catch {
    // Error is already shown in modal and toast.
  }
}

async function downloadCompiledLetterPdf(letter) {
  try {
    const blob = await ctmsApi.downloadVisitLetterPdf(letter.letterId);
    const blobUrl = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = blobUrl;
    link.download = `${letter.letterType}_${letter.letterId}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(blobUrl);
  } catch (err) {
    useToastStore().apiError("Failed to download visit letter PDF", err);
  }
}

// 4. CRA Allocation Update (Demo-only)
function openReallocate() {
  if (!allowUnbackedAction("CRA workload allocation")) return;
  allocCraId.value = "cra_alice";
  allocStatus.value = "ACTIVE";
  allocReason.value = "";
  allocError.value = "";
  showReallocateModal.value = true;
}

async function confirmReallocate() {
  if (!allowUnbackedAction("CRA workload allocation")) return;
  if (!allocReason.value.trim()) {
    allocError.value = "Reason for change is required.";
    return;
  }
  try {
    const payload = {
      cra_id: allocCraId.value,
      site_id: siteId.value,
      study_id: studyId.value,
      status: allocStatus.value,
      effective_start_date: new Date().toISOString(),
    };
    await apiClient.post("/api/v1/ctms/cra-allocations", payload, {
      changeReason: allocReason.value,
    });
    showReallocateModal.value = false;
    await loadOperationsData();
  } catch (err) {
    allocError.value = err.message || "Failed to reallocate CRA workload.";
  }
}

// 5. Recruitment Update (Demo-only)
function openRecruitment() {
  if (!allowUnbackedAction("Recruitment target update")) return;
  if (recruitment.value && recruitment.value.length > 0) {
    const lastRec = recruitment.value[0];
    recScreened.value = lastRec.screened_count;
    recEnrolled.value = lastRec.enrolled_count;
    recTarget.value = lastRec.target_count;
  } else {
    recScreened.value = 15;
    recEnrolled.value = 8;
    recTarget.value = 20;
  }
  recReason.value = "";
  recError.value = "";
  showRecruitmentModal.value = true;
}

async function confirmRecruitment() {
  if (!allowUnbackedAction("Recruitment target update")) return;
  if (!recReason.value.trim()) {
    recError.value = "Reason for change is required.";
    return;
  }
  try {
    const payload = {
      site_id: siteId.value,
      study_id: studyId.value,
      screened_count: parseInt(recScreened.value, 10),
      enrolled_count: parseInt(recEnrolled.value, 10),
      target_count: parseInt(recTarget.value, 10),
      as_of_date: new Date().toISOString(),
    };
    await apiClient.post("/api/v1/ctms/recruitment", payload, {
      changeReason: recReason.value,
    });
    showRecruitmentModal.value = false;
    await loadOperationsData();
  } catch (err) {
    recError.value = err.message || "Failed to log recruitment update.";
  }
}

// --- Delegation Matrices write operations ---

// 6. Delegate Trial Task
function openDelegate() {
  if (!canEditDoa.value) return;
  delStaffId.value = "";
  delStartDate.value = new Date().toISOString().slice(0, 10);
  delTaskCodes.value = [];
  delReason.value = "";
  delError.value = "";
  showDelegateModal.value = true;
}

async function confirmDelegate() {
  if (!delStaffId.value.trim()) {
    delError.value = "Staff User ID is required.";
    return;
  }
  if (delTaskCodes.value.length === 0) {
    delError.value = "At least one delegated task duty must be selected.";
    return;
  }
  if (!delReason.value.trim()) {
    delError.value = "Reason for change justification is required.";
    return;
  }
  try {
    await ctmsApi.delegateSiteTasks({
      site_id: siteId.value.trim(),
      staff_user_id: delStaffId.value.trim(),
      task_codes: delTaskCodes.value,
      start_date: delStartDate.value,
      reason_for_change: delReason.value.trim(),
    });
    showDelegateModal.value = false;
    await loadDelegationData();
  } catch (err) {
    delError.value = err?.message || "Failed to request delegation assignment.";
    useToastStore().apiError("Delegation request failed", err);
  }
}

// 7. Electronic Signature (PI sign-off)
function signDelegation(record) {
  if (!canSignDoa.value) return;
  promptSignature(
    async (sigToken, reason) => {
      try {
        await ctmsApi.signOffDelegation(
          {
            record_id: record.record_id,
            reason_for_change: reason,
          },
          reason,
          sigToken
        );
        await loadDelegationData();
      } catch (err) {
        useToastStore().apiError("Sign-off execution failed", err);
      }
    },
    {
      action: "/api/v1/ctms/doa/sign-off",
      defaultSigningReason: "INVESTIGATOR_SIGNATURE",
      initialReason: "",
    }
  );
}

// 8. Revoke Delegation
function revokeDelegation(record) {
  if (!canEditDoa.value) return;
  promptJustification(
    "Revoke Delegation Assignment",
    "Enter the compliance justification reason for revoking this clinical task assignment...",
    async (reason) => {
      try {
        await ctmsApi.revokeSiteTasks({
          record_id: record.record_id,
          reason_for_change: reason,
        });
        await loadDelegationData();
      } catch (err) {
        useToastStore().apiError("Revocation failed", err);
      }
    }
  );
}

// 9. Export GxP-Compliant PDF
async function exportDoaPdf() {
  const currentSite = siteId.value.trim();
  if (!currentSite) return;
  try {
    const blob = await ctmsApi.exportSiteDoaPdf(currentSite);
    const blobUrl = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = blobUrl;
    link.download = `DOA_Log_${currentSite}.pdf`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    window.URL.revokeObjectURL(blobUrl);
  } catch (err) {
    useToastStore().apiError("Error exporting compliant GxP PDF", err);
  }
}

// ============================================================
// Financials & Investigator Grant Contract Engine Methods
// ============================================================

async function loadFinancialData() {
  try {
    // 1. Fetch grant for site
    const grantRes = await apiClient.get("/api/v1/ctms/grants", {
      params: { study_id: studyId.value, site_id: siteId.value },
    });
    if (grantRes && grantRes.length > 0) {
      investigatorGrant.value = grantRes[0];
      await loadProcedureGrids();
      await loadContractAmendments();
      await calculatePayables();
    } else {
      investigatorGrant.value = null;
      procedureGrids.value = [];
      contractAmendments.value = [];
      visitPayableCalc.value = null;
    }

    // 2. Fetch templates
    const tmplRes = await apiClient.get("/api/v1/ctms/financials/templates");
    financialTemplates.value = tmplRes || [];

    // 3. Fetch adjustments
    const adjRes = await apiClient.get("/api/v1/ctms/financials/adjustments", {
      params: { study_id: studyId.value },
    });
    budgetAdjustments.value = adjRes || [];

    // 4. Fetch exchange rates
    const fxRes = await apiClient.get("/api/v1/ctms/financials/exchange-rates");
    exchangeRates.value = fxRes || [];
  } catch (err) {
    console.error("Failed to load financial data:", err);
  }
}

async function loadProcedureGrids() {
  if (!investigatorGrant.value) return;
  try {
    const params = { grant_id: investigatorGrant.value.id };
    if (filteredVersionIndex.value !== "ALL") {
      params.version_index = parseInt(filteredVersionIndex.value, 10);
    }
    const res = await apiClient.get("/api/v1/ctms/financials/procedure-grids", {
      params,
    });
    procedureGrids.value = res || [];
  } catch (err) {
    console.error("Failed to load procedure grids:", err);
  }
}

async function loadContractAmendments() {
  if (!investigatorGrant.value) return;
  try {
    const res = await apiClient.get(
      `/api/v1/ctms/financials/grants/${investigatorGrant.value.id}/amendments`
    );
    contractAmendments.value = res || [];
  } catch (err) {
    console.error("Failed to load contract amendments:", err);
  }
}

async function calculatePayables() {
  if (!investigatorGrant.value) return;
  try {
    const params = {
      visit_name: selectedVisitForCalc.value,
      currency: investigatorGrant.value.currency || "USD",
      target_currency: selectedCurrencyForCalc.value,
    };
    if (filteredVersionIndex.value !== "ALL") {
      params.version_index = parseInt(filteredVersionIndex.value, 10);
    }
    const res = await apiClient.get(
      `/api/v1/ctms/financials/grants/${investigatorGrant.value.id}/calculate-visit`,
      { params }
    );
    visitPayableCalc.value = res;
  } catch (err) {
    console.error("Failed to calculate visit payables:", err);
  }
}

async function confirmCreateTemplate() {
  if (!allowUnbackedAction("Create investigator grant budget template")) return;
  if (!newTmplName.value.trim()) return;
  try {
    const payload = {
      template_name: newTmplName.value,
      tier_level: newTmplTier.value,
      currency: newTmplCurrency.value,
      overhead_percentage: newTmplOverhead.value,
      withholding_percentage: newTmplWithholding.value,
      fixed_startup_fee: newTmplStartup.value,
      fixed_closeout_fee: newTmplCloseout.value,
      irb_fee: newTmplIrb.value,
      patient_stipend_amount: newTmplStipend.value,
      travel_reimbursement_rate: newTmplTravel.value,
      fixed_fees: [],
      procedure_items: [
        {
          visit_name: "SCREENING",
          activity_id: "ACT-ECG-01",
          form_id: "FORM-ECG-V1",
          procedure_code: "CPT-93000",
          procedure_name: "Electrocardiogram 12-lead",
          base_amount: 250.0,
          overhead_percentage: newTmplOverhead.value,
          withholding_percentage: newTmplWithholding.value,
          patient_stipend: newTmplStipend.value,
          travel_reimbursement: newTmplTravel.value,
        },
        {
          visit_name: "WEEK_4",
          activity_id: "ACT-LAB-01",
          form_id: "FORM-LAB-V1",
          procedure_code: "CPT-80053",
          procedure_name: "Comprehensive Metabolic Panel",
          base_amount: 180.0,
          overhead_percentage: newTmplOverhead.value,
          withholding_percentage: newTmplWithholding.value,
          patient_stipend: 0.0,
          travel_reimbursement: 0.0,
        },
      ],
    };
    await apiClient.post("/api/v1/ctms/financials/templates", payload, {
      changeReason: newTmplReason.value || "Created budget template",
    });
    showCreateTemplateModal.value = false;
    newTmplName.value = "";
    await loadFinancialData();
  } catch (err) {
    useToastStore().apiError("Failed to create budget template", err);
  }
}

async function applyTemplate() {
  if (!allowUnbackedAction("Apply investigator grant budget template")) return;
  if (!selectedTemplateId.value || !investigatorGrant.value) return;
  try {
    await apiClient.post(
      `/api/v1/ctms/financials/grants/${investigatorGrant.value.id}/apply-template`,
      { template_id: selectedTemplateId.value },
      {
        changeReason:
          "Applied multi-tier budget template to investigator grant",
      }
    );
    await loadFinancialData();
  } catch (err) {
    useToastStore().apiError("Failed to apply template", err);
  }
}

function openAddProcedureModal() {
  if (!allowUnbackedAction("Add procedure to grant fee grid")) return;
  newProcCode.value = "";
  newProcName.value = "";
  newProcBase.value = 250.0;
  newProcOverhead.value = investigatorGrant.value?.overhead_percentage || 25.0;
  newProcWithholding.value =
    investigatorGrant.value?.withholding_percentage || 10.0;
  newProcActivityId.value = "";
  newProcFormId.value = "";
  newProcStipend.value =
    investigatorGrant.value?.patient_stipend_per_visit || 50.0;
  newProcTravel.value =
    investigatorGrant.value?.travel_reimbursement_per_visit || 25.0;
  newProcReason.value = "";
  showAddProcedureModal.value = true;
}

async function confirmAddProcedure() {
  if (!allowUnbackedAction("Add procedure to grant fee grid")) return;
  if (
    !newProcCode.value.trim() ||
    !newProcName.value.trim() ||
    !investigatorGrant.value
  )
    return;
  try {
    const payload = {
      grant_id: investigatorGrant.value.id,
      visit_name: newProcVisit.value,
      procedure_code: newProcCode.value,
      procedure_name: newProcName.value,
      base_amount: newProcBase.value,
      overhead_percentage: newProcOverhead.value,
      withholding_percentage: newProcWithholding.value,
      activity_id: newProcActivityId.value || null,
      form_id: newProcFormId.value || null,
      patient_stipend: newProcStipend.value,
      travel_reimbursement: newProcTravel.value,
    };
    await apiClient.post("/api/v1/ctms/financials/procedure-grids", payload, {
      changeReason: newProcReason.value || "Added procedure payment grid item",
    });
    showAddProcedureModal.value = false;
    await loadProcedureGrids();
    await calculatePayables();
  } catch (err) {
    useToastStore().apiError("Failed to add procedure grid item", err);
  }
}

function openAmendmentModal() {
  if (!allowUnbackedAction("Execute grant contract amendment")) return;
  if (!investigatorGrant.value) return;
  amendRevisedBudget.value =
    Number(investigatorGrant.value.total_budget || 0) + 50000;
  amendSummary.value = "";
  amendReason.value = "";
  showAmendmentModal.value = true;
}

async function confirmExecuteAmendment() {
  if (!allowUnbackedAction("Execute grant contract amendment")) return;
  if (!investigatorGrant.value || !amendSummary.value.trim()) return;
  promptSignature(async (sigToken, reason) => {
    try {
      const payload = {
        summary_of_changes: amendSummary.value,
        revised_budget: amendRevisedBudget.value,
        signature_manifestation: {
          signer_name: store.user?.username || "Grants Manager",
          signer_role: "Grants Lead",
          signed_at: new Date().toISOString(),
          meaning: "Contract Amendment Execution",
        },
      };
      await apiClient.post(
        `/api/v1/ctms/financials/grants/${investigatorGrant.value.id}/amend`,
        payload,
        {
          headers: {
            "X-Sig-Token": sigToken,
          },
          changeReason:
            amendReason.value || reason || "Executed contract amendment",
        }
      );
      showAmendmentModal.value = false;
      await loadFinancialData();
    } catch (err) {
      useToastStore().apiError("Failed to execute contract amendment", err);
    }
  });
}

async function confirmCreateAdjustment() {
  if (!allowUnbackedAction("Create grant budget adjustment")) return;
  if (!investigatorGrant.value || !adjDescription.value.trim()) return;
  try {
    const payload = {
      grant_id: investigatorGrant.value.id,
      site_id: siteId.value,
      study_id: studyId.value,
      adjustment_type: adjType.value,
      description: adjDescription.value,
      amount: adjAmount.value,
      currency: adjCurrency.value || "USD",
    };
    await apiClient.post("/api/v1/ctms/financials/adjustments", payload, {
      changeReason: adjReason.value || "Created budget adjustment",
    });
    showAdjustmentModal.value = false;
    adjDescription.value = "";
    adjAmount.value = 0;
    await loadFinancialData();
  } catch (err) {
    useToastStore().apiError("Failed to create budget adjustment", err);
  }
}

async function approveAdjustment(adj) {
  if (!allowUnbackedAction("Approve grant budget adjustment")) return;
  const appPath = `/api/v1/ctms/financials/adjustments/${adj.id}/approve`;
  promptSignature(async (sigToken, reason) => {
    try {
      await apiClient.post(
        appPath,
        {},
        {
          headers: {
            "X-Sig-Token": sigToken,
          },
          changeReason: reason || "Approved budget adjustment",
        }
      );
      await loadFinancialData();
    } catch (err) {
      useToastStore().apiError("Failed to approve budget adjustment", err);
    }
  });
}
</script>

<style scoped>
.live-workspace {
  display: flex;
  flex-direction: column;
  gap: 16px;
}

.live-tabs {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
  border-bottom: 1px solid var(--semantic-color-border);
  padding-bottom: 10px;
}

.live-tab-btn {
  padding: 8px 14px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
  font-weight: 600;
  font-size: 0.88rem;
  cursor: pointer;
}

.live-tab-btn.active {
  background: var(--semantic-color-primary);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}

.live-unbacked-note {
  margin: 0;
  padding: 10px 14px;
  border-radius: 6px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-secondary);
  font-size: 0.88rem;
}

.modal-overlay {
  display: flex;
  position: fixed;
  inset: 0;
  background: rgba(15, 23, 42, 0.5);
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: var(--semantic-color-surface);
  border-radius: 8px;
  width: 100%;
  max-width: 500px;
  box-shadow: 0 10px 25px rgba(15, 23, 42, 0.15);
  overflow: hidden;
  color: var(--semantic-color-text-dark);
}

.modal-header {
  padding: 16px;
  border-bottom: 1px solid var(--semantic-color-border);
  font-weight: 600;
  font-size: 16px;
  background: var(--semantic-color-primary-dark);
  color: var(--semantic-color-surface);
}

.modal-body {
  padding: 16px;
  max-height: 400px;
  overflow-y: auto;
}

.form-group {
  margin-bottom: 12px;
}

label {
  display: block;
  font-weight: 500;
  margin-bottom: 4px;
  font-size: 13px;
  color: var(--semantic-color-text-secondary);
}

input,
select,
textarea {
  width: 100%;
  padding: 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  font-size: 13px;
  background: white;
  box-sizing: border-box;
}

textarea {
  min-height: 80px;
  resize: vertical;
}

.error-msg {
  margin: 8px 0 0;
  color: var(--semantic-color-error);
  font-size: 0.82rem;
  font-weight: 500;
}

.modal-footer {
  padding: 12px 16px;
  border-top: 1px solid var(--semantic-color-border);
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: var(--semantic-color-surface-muted);
}

.btn {
  padding: 6px 12px;
  font-size: 0.82rem;
  cursor: pointer;
  border-radius: 6px;
  font-weight: 500;
}

.btn-sm {
  padding: 3px 8px;
  font-size: 11px;
}

.btn-cancel {
  border: 1px solid var(--semantic-color-border-control);
  background: white;
}

.btn-secondary {
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-primary);
}

.btn-primary {
  background: var(--semantic-color-primary-dark);
  color: var(--semantic-color-surface);
  border: none;
}

.tab-btn:hover {
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-primary);
}

.doa-load-error {
  margin-bottom: 16px;
  padding: 12px;
  border: 1px solid var(--semantic-color-error-border-soft);
  border-radius: 6px;
  background: var(--semantic-color-error-bg-soft);
  color: var(--semantic-color-error);
  font-size: 14px;
}

table,
th,
td {
  font-variant-numeric: tabular-nums;
}

.role-note {
  margin: 8px 0 0;
  font-size: 12px;
  color: var(--semantic-color-text-secondary);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.btn:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}
</style>
