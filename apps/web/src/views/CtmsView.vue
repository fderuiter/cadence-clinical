<template>
  <div id="section-ctms" class="dashboard-section active">
    <!-- Header Section -->
    <div class="section-header">
      <h2>Site readiness</h2>
      <p>
        Monitor operational site status, track site lifecycle milestones,
        recruitment metrics, and coordinate site readiness workflows.
      </p>
    </div>

    <!-- Outside demo builds only site activation has a backend (Execution); the rest waits on #4829. -->
    <SiteActivationPanel v-if="!demoBuild" />

    <template v-else>
      <!-- Live CTMS KPI Metric Cards Grid -->
      <div
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
          🚀 Study Startup &amp; Greenlight
        </button>
        <button
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
          gap: 24px;
          align-items: center;
          font-size: 14px;
          color: var(--semantic-color-text-body);
        "
      >
        <div><strong>Study ID:</strong> {{ studyId }}</div>
        <div><strong>Site ID:</strong> {{ siteId }}</div>
        <div
          style="
            margin-left: auto;
            display: flex;
            align-items: center;
            gap: 8px;
          "
        >
          <strong>eTMF Compliance Status:</strong>
          <span
            v-if="loadingCompliance"
            style="color: var(--semantic-color-text-secondary)"
            >Loading...</span
          >
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
                ? 'All required documents approved in eTMF.'
                : 'Missing: ' +
                  (complianceStatus.missing_documents || []).join(', ')
            "
          >
            {{ complianceStatus.is_complete ? "COMPLIANT" : "NON-COMPLIANT" }}
          </span>
        </div>
      </div>

      <!-- Tab 1: Core Operations -->
      <div v-if="activeTab === 'operations'">
        <div class="grid-2-responsive">
          <!-- Site Milestones Card -->
          <div class="card">
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
                      No milestones found. Create one to begin tracking.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div style="margin-top: 12px; display: flex; gap: 8px">
              <button
                id="btn-achieve-milestone"
                class="btn btn-primary"
                @click="achieveMilestone"
              >
                Achieve Current Milestone
              </button>
              <button
                id="btn-create-milestone"
                class="btn btn-secondary"
                @click="openCreateMilestone"
              >
                Add Milestone
              </button>
            </div>
          </div>

          <!-- Monitoring Visits Card -->
          <div class="card">
            <div class="card-title">CRA Site Monitoring Visits (MVR)</div>
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
                      <button
                        v-if="v.status === 'COMPLETED'"
                        class="btn btn-primary btn-sm"
                        style="padding: 2px 6px; font-size: 11px"
                        @click="signOffVisit(v)"
                      >
                        Sign-off
                      </button>
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
                      No monitoring visits scheduled.
                    </td>
                  </tr>
                </tbody>
              </table>
            </div>
            <div style="margin-top: 12px; display: flex; gap: 8px">
              <button
                id="btn-schedule-visit"
                class="btn btn-primary"
                @click="openScheduleVisit"
              >
                Schedule New Visit
              </button>
              <button
                id="btn-complete-visit"
                class="btn"
                @click="completeVisit"
              >
                Complete Current Visit
              </button>
            </div>
          </div>
        </div>

        <div class="grid-2-responsive" style="margin-top: var(--spacing-xl)">
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
      <div v-if="activeTab === 'monitoring'">
        <CraVerificationConsole
          :standalone="true"
          :study-id="studyId"
          :site-id="siteId"
        />
      </div>

      <!-- Tab 3: Delegation Matrix -->
      <div v-if="activeTab === 'delegation'">
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
                color: white;
              "
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
                    No delegation assignments logged for this site.
                  </td>
                </tr>
              </tbody>
            </table>
          </div>

          <div style="margin-top: 16px">
            <button
              id="btn-delegate-task"
              class="btn btn-primary"
              @click="openDelegate"
            >
              Delegate New Task
            </button>
          </div>
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
      <div v-if="activeTab === 'financials'">
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
      <div v-if="showCreateTemplateModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Create Multi-Tier Budget Template</div>
          <div class="modal-body">
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
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
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
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
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
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
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
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              @click="showCreateTemplateModal = false"
            >
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmCreateTemplate">
              Create Template
            </button>
          </div>
        </div>
      </div>

      <!-- Modal: Add Procedure to Fee Grid -->
      <div v-if="showAddProcedureModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Add Procedure to Fee Matrix Grid</div>
          <div class="modal-body">
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
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
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
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
              style="
                display: grid;
                grid-template-columns: 1fr 1fr 1fr;
                gap: 12px;
              "
            >
              <div class="form-group">
                <label for="proc-base">Base Amount ($)</label>
                <input
                  id="proc-base"
                  v-model.number="newProcBase"
                  type="number"
                />
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
            <div
              style="display: grid; grid-template-columns: 1fr 1fr; gap: 12px"
            >
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
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              @click="showAddProcedureModal = false"
            >
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmAddProcedure">
              Add to Grid
            </button>
          </div>
        </div>
      </div>

      <!-- Modal: Execute Contract Amendment -->
      <div v-if="showAmendmentModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">
            Execute Contract Amendment (Version Increment)
          </div>
          <div class="modal-body">
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
          </div>
          <div class="modal-footer">
            <button class="btn btn-cancel" @click="showAmendmentModal = false">
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmExecuteAmendment">
              Sign &amp; Execute Amendment
            </button>
          </div>
        </div>
      </div>

      <!-- Modal: Create Budget Adjustment -->
      <div v-if="showAdjustmentModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Create Budget Adjustment / Penalty</div>
          <div class="modal-body">
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
              <label for="adj-desc"
                >Description &amp; Clinical Justification</label
              >
              <input
                id="adj-desc"
                v-model="adjDescription"
                type="text"
                placeholder="e.g. Dry ice shipping courier reimbursement"
              />
            </div>
            <div
              style="display: grid; grid-template-columns: 2fr 1fr; gap: 12px"
            >
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
          </div>
          <div class="modal-footer">
            <button class="btn btn-cancel" @click="showAdjustmentModal = false">
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmCreateAdjustment">
              Save Adjustment
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 1: General Change Justification -->
      <div v-if="showJustificationModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">
            {{ justificationTitle }}
          </div>
          <div class="modal-body">
            <p
              style="
                font-size: 13px;
                color: var(--semantic-color-text-secondary);
                margin-bottom: 12px;
                line-height: 1.4;
              "
            >
              To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
              you must document a valid GxP justification reason for this
              action.
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
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              @click="showJustificationModal = false"
            >
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmJustification">
              Confirm Change
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 2: FDA Part 11 Electronic Signature & Re-Authentication -->
      <div v-if="showSignatureModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">
            Electronic Signature & Identity Verification (Part 11)
          </div>
          <div class="modal-body">
            <p
              style="
                font-size: 13px;
                color: var(--semantic-color-text-secondary);
                margin-bottom: 16px;
                line-height: 1.4;
              "
            >
              Please re-authenticate your identity and verify your endorsement
              of this delegation. All signings are permanently logged to the
              audit ledger.
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
                type="password"
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
                <option value="APPROVAL">
                  APPROVAL (Supervisory Approval)
                </option>
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
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              :disabled="sigBusy"
              @click="showSignatureModal = false"
            >
              Cancel
            </button>
            <button
              class="btn btn-primary"
              :disabled="sigBusy"
              @click="confirmSignature"
            >
              {{ sigBusy ? "Verifying..." : "Verify & Digital Sign" }}
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 3: Schedule Monitoring Visit -->
      <div v-if="showScheduleVisitModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Schedule Clinical Monitoring Visit</div>
          <div class="modal-body">
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

            <div v-if="scheduleError" class="error-msg">
              ⚠️ {{ scheduleError }}
            </div>
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              @click="showScheduleVisitModal = false"
            >
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmScheduleVisit">
              Schedule & Issue confirmation
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 4: Reallocate CRA Workload -->
      <div v-if="showReallocateModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Allocate CRA to Site</div>
          <div class="modal-body">
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
          </div>
          <div class="modal-footer">
            <button class="btn btn-cancel" @click="showReallocateModal = false">
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmReallocate">
              Save Allocation
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 5: Recruitment Metrics Update -->
      <div v-if="showRecruitmentModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Log Site Recruitment Metrics Update</div>
          <div class="modal-body">
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
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              @click="showRecruitmentModal = false"
            >
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmRecruitment">
              Submit Metrics
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 6: Delegate New Authority Task -->
      <div v-if="showDelegateModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Delegate Site Trial Task</div>
          <div class="modal-body">
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
              <!-- eslint-disable-next-line vuejs-accessibility/label-has-for -->
              <label>Delegated Duty Task Codes</label>
              <div
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
          </div>
          <div class="modal-footer">
            <button class="btn btn-cancel" @click="showDelegateModal = false">
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmDelegate">
              Submit Delegation
            </button>
          </div>
        </div>
      </div>

      <!-- Modal 7: Create Site Milestone -->
      <div v-if="showCreateMilestoneModal" class="modal-overlay">
        <div class="modal">
          <div class="modal-header">Add Site Milestone</div>
          <div class="modal-body">
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
              <input
                id="ms-planned-date"
                v-model="newMsPlannedDate"
                type="date"
              />
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
          </div>
          <div class="modal-footer">
            <button
              class="btn btn-cancel"
              @click="showCreateMilestoneModal = false"
            >
              Cancel
            </button>
            <button class="btn btn-primary" @click="confirmCreateMilestone">
              Add Milestone
            </button>
          </div>
        </div>
      </div>

      <!-- Startup & Greenlight Tab Content -->
      <div v-if="activeTab === 'startup'" style="margin-top: 20px">
        <SsuGreenlightConsole :study-id="studyId" :site-id="siteId" />
      </div>

      <!-- RTSM & Dose Escalation Tab Content -->
      <div
        v-if="activeTab === 'rtsm'"
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
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import CraVerificationConsole from "../components/persona/CraVerificationConsole.vue";
import SsuGreenlightConsole from "../components/clinical/SsuGreenlightConsole.vue";
import AdaptiveDoseEscalationConsole from "../components/clinical/AdaptiveDoseEscalationConsole.vue";
import CellGeneTherapyCocConsole from "../components/clinical/CellGeneTherapyCocConsole.vue";
import BulkIpKitReconciliationDrawer from "../components/clinical/BulkIpKitReconciliationDrawer.vue";
import SiteActivationPanel from "../components/clinical/SiteActivationPanel.vue";
import { isDemoBuild } from "../utils/demoMode.js";
import apiClient from "../services/api";
import { getBaseUrl } from "../api/apiClient";
import { useToastStore } from "../stores/toasts.js";

// Active session store/router setup
const store = useClinicalStore();
const route = useRoute();
const demoBuild = isDemoBuild();
const isIpKitReconciliationDrawerOpen = ref(false);

// Scope comes from the route; apply it before the first render so the site activation panel sees it.
if (route && route.query) {
  if (route.query.studyId) store.activeStudyId = route.query.studyId;
  if (route.query.siteId) store.activeSiteId = route.query.siteId;
  if (route.query.subjectId) store.activeSubjectId = route.query.subjectId;
  if (route.query.visitId) store.activeVisitId = route.query.visitId;
}

// Scope properties
const siteId = computed(() => store.activeSiteId || "SITE-001");
const studyId = computed(() => store.activeStudyId || "STUDY-USDM-001");

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
let signatureCallback = null;

const showScheduleVisitModal = ref(false);
const newVisitType = ref("IMV");
const newVisitScheduledDate = ref("");
const newVisitCra = ref("cra.monitor");
const newVisitReason = ref("");
const scheduleError = ref("");

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

async function loadComplianceStatus() {
  loadingCompliance.value = true;
  try {
    const res = await apiClient.get(
      `/api/v1/execution/sites/${siteId.value}/compliance-status?study_id=${studyId.value}`
    );
    complianceStatus.value = res || {
      is_complete: false,
      missing_documents: [],
    };
  } catch (err) {
    console.error("Failed to load site compliance status:", err);
    complianceStatus.value = { is_complete: false, missing_documents: [] };
  } finally {
    loadingCompliance.value = false;
  }
}

// Load Operations Data from Real-time Backend
async function loadOperationsData() {
  try {
    loadComplianceStatus();
    const [milestonesRes, visitsRes, workloadRes, recruitmentRes] =
      await Promise.all([
        apiClient.get(
          `/api/v1/ctms/site-milestones?site_id=${siteId.value}&study_id=${studyId.value}`
        ),
        apiClient.get(
          `/api/v1/ctms/monitoring-visits?site_id=${siteId.value}&study_id=${studyId.value}`
        ),
        apiClient.get(`/api/v1/ctms/cra-allocations/workload`),
        apiClient.get(
          `/api/v1/ctms/recruitment?site_id=${siteId.value}&study_id=${studyId.value}`
        ),
      ]);
    milestones.value = milestonesRes || [];
    visits.value = visitsRes || [];
    allocations.value = workloadRes || [];
    recruitment.value = recruitmentRes || [];
  } catch (err) {
    console.error(
      "Failed to load operations data from backend microservice:",
      err
    );
  }
}

// Load Delegation logs from Real-time Backend DoA endpoints
async function loadDelegationData() {
  try {
    const res = await apiClient.get(
      `/api/v1/ctms/doa/sites/${siteId.value}/log`
    );
    delegatedStaff.value = res?.delegated_staff || [];
    auditHistory.value = res?.audit_history || [];
    piName.value = res?.pi_name || "";
    doaLoadError.value = "";
  } catch (err) {
    // A superseded duplicate request is aborted by apiClient; the newer request reports its own outcome.
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

// Watchers for refetch on scope or tab change
watch(
  [siteId, studyId, activeTab],
  () => {
    // These CTMS endpoints exist only in the demo build's mock API.
    if (!demoBuild) return;
    if (activeTab.value === "operations") {
      loadOperationsData();
    } else if (activeTab.value === "financials") {
      loadFinancialData();
    } else {
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
function promptSignature(callback) {
  sigUsername.value = store.user?.username || "";
  sigPassword.value = "";
  sigTotp.value = "";
  sigSigningReason.value = "INVESTIGATOR_SIGNATURE";
  sigReasonForChange.value = "";
  sigError.value = "";
  sigBusy.value = false;
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

  sigBusy.value = true;
  sigError.value = "";

  try {
    const reauthRes = await apiClient.post(
      "/api/v1/auth/signature-verification",
      {
        username: sigUsername.value,
        password: sigPassword.value,
        totp: sigTotp.value || null,
        action: "/api/v1/ctms/doa/sign-off",
      }
    );

    const sigToken = reauthRes.sig_token;

    if (signatureCallback) {
      await signatureCallback(sigToken, sigReasonForChange.value);
    }
    showSignatureModal.value = false;
  } catch (err) {
    sigError.value = err.message || "Part 11 identity verification failed.";
  } finally {
    sigBusy.value = false;
  }
}

// --- Write Triggers (Bypassing direct store modification) ---

// 1. Achieve Milestone
function achieveMilestone() {
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
        await apiClient.put(
          `/api/v1/ctms/site-milestones/${nextM.id}`,
          {
            status: "ACHIEVED",
            actual_date: new Date().toISOString(),
          },
          {
            changeReason: reason,
          }
        );
        await loadOperationsData();
      } catch (err) {
        useToastStore().apiError("Failed to achieve milestone", err);
      }
    }
  );
}

// Create Custom Milestone
function openCreateMilestone() {
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
    const payload = {
      site_id: siteId.value,
      study_id: studyId.value,
      milestone_type: newMsType.value,
      planned_date: newMsPlannedDate.value
        ? new Date(newMsPlannedDate.value).toISOString()
        : null,
      status: newMsStatus.value,
    };
    await apiClient.post("/api/v1/ctms/site-milestones", payload, {
      changeReason: newMsReason.value,
    });
    showCreateMilestoneModal.value = false;
    await loadOperationsData();
  } catch (err) {
    msError.value = err.message || "Failed to add milestone.";
  }
}

// 2. Schedule Monitoring Visit
function openScheduleVisit() {
  newVisitType.value = "IMV";
  newVisitScheduledDate.value = new Date(Date.now() + 5 * 24 * 3600 * 1000)
    .toISOString()
    .slice(0, 10);
  newVisitCra.value = store.user?.username || "cra.monitor";
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
    const payload = {
      study_id: studyId.value,
      site_id: siteId.value,
      cra_id: newVisitCra.value,
      visit_type: newVisitType.value,
      scheduled_date: new Date(newVisitScheduledDate.value).toISOString(),
    };
    await apiClient.post("/api/v1/ctms/monitoring-visits", payload, {
      changeReason: newVisitReason.value,
    });
    showScheduleVisitModal.value = false;
    await loadOperationsData();
  } catch (err) {
    scheduleError.value = err.message || "Failed to schedule monitoring visit.";
  }
}

// 3. Complete Monitoring Visit
function completeVisit() {
  const scheduledVisit = visits.value.find((v) => v.status === "SCHEDULED");
  if (!scheduledVisit) {
    useToastStore().push({
      tone: "warning",
      title: "Monitoring Visits",
      message: "No scheduled visits to complete!",
    });
    return;
  }

  promptJustification(
    "Complete Monitoring Visit",
    "Enter the completion details or reason for closure...",
    async (reason) => {
      try {
        await apiClient.post(
          `/api/v1/ctms/monitoring-visits/${scheduledVisit.id}/complete`,
          {
            actual_date: new Date().toISOString(),
            findings: [],
          },
          {
            changeReason: reason,
          }
        );
        await loadOperationsData();
      } catch (err) {
        useToastStore().apiError("Failed to complete monitoring visit", err);
      }
    }
  );
}

// Complete Supervisory Sign-off on Visit
function signOffVisit(visit) {
  promptJustification(
    "Supervisory Sign-off on Visit",
    "Enter the review endorsement statement for signing off this completed visit...",
    async (reason) => {
      try {
        await apiClient.post(
          `/api/v1/ctms/monitoring-visits/${visit.id}/sign-off`,
          {},
          {
            changeReason: reason,
          }
        );
        await loadOperationsData();
      } catch (err) {
        useToastStore().apiError("Failed to sign off monitoring visit", err);
      }
    }
  );
}

// 4. CRA Allocation Update
function openReallocate() {
  allocCraId.value = "cra_alice";
  allocStatus.value = "ACTIVE";
  allocReason.value = "";
  allocError.value = "";
  showReallocateModal.value = true;
}

async function confirmReallocate() {
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

// 5. Recruitment Update
function openRecruitment() {
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
    const payload = {
      site_id: siteId.value,
      staff_user_id: delStaffId.value,
      task_codes: delTaskCodes.value,
      start_date: delStartDate.value,
      reason_for_change: delReason.value,
    };
    await apiClient.post("/api/v1/ctms/doa/delegate", payload, {
      changeReason: delReason.value,
    });
    showDelegateModal.value = false;
    await loadDelegationData();
  } catch (err) {
    delError.value = err.message || "Failed to request delegation assignment.";
  }
}

// 7. Electronic Signature (PI sign-off)
function signDelegation(record) {
  promptSignature(async (sigToken, reason) => {
    try {
      await apiClient.post(
        "/api/v1/ctms/doa/sign-off",
        {
          record_id: record.record_id,
          reason_for_change: reason,
        },
        {
          headers: {
            "X-Sig-Token": sigToken,
          },
          changeReason: reason,
        }
      );
      await loadDelegationData();
    } catch (err) {
      useToastStore().apiError("Sign-off execution failed", err);
    }
  });
}

// 8. Revoke Delegation
function revokeDelegation(record) {
  promptJustification(
    "Revoke Delegation Assignment",
    "Enter the compliance justification reason for revoking this clinical task assignment...",
    async (reason) => {
      try {
        await apiClient.post(
          "/api/v1/ctms/doa/revoke",
          {
            record_id: record.record_id,
            reason_for_change: reason,
          },
          {
            changeReason: reason,
          }
        );
        await loadDelegationData();
      } catch (err) {
        useToastStore().apiError("Revocation failed", err);
      }
    }
  );
}

// 9. Export GxP-Compliant PDF
async function exportDoaPdf() {
  const authStore = useAuthStore();
  const token = authStore?.token || authStore?.accessToken;
  const baseUrl = getBaseUrl();
  const url = `${baseUrl}/api/v1/ctms/doa/sites/${siteId.value}/export-pdf`;

  const headers = {};
  if (token) {
    headers["Authorization"] = `Bearer ${token}`;
  }

  try {
    const response = await fetch(url, {
      method: "GET",
      headers,
    });

    if (!response.ok) {
      throw new Error(`Failed to export PDF: ${response.statusText}`);
    }

    const blob = await response.blob();
    const blobUrl = window.URL.createObjectURL(blob);
    const link = document.createElement("a");
    link.href = blobUrl;
    link.download = `DOA_Log_${siteId.value}.pdf`;
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
  if (!investigatorGrant.value) return;
  amendRevisedBudget.value =
    Number(investigatorGrant.value.total_budget || 0) + 50000;
  amendSummary.value = "";
  amendReason.value = "";
  showAmendmentModal.value = true;
}

async function confirmExecuteAmendment() {
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
  color: white;
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
}

.btn-primary {
  background: var(--semantic-color-primary-dark);
  color: white;
  border: none;
}

.tab-btn:hover {
  background: var(--semantic-color-surface-header);
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
</style>
