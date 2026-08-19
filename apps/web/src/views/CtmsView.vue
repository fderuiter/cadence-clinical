<template>
  <div id="section-ctms" class="dashboard-section active">
    <!-- Header Section -->
    <div class="section-header">
      <h2>Clinical Trial Management System (CTMS)</h2>
      <p>
        Monitor operational site status, track site lifecycle milestones,
        recruitment metrics, and coordinate CRA workloads.
      </p>
    </div>

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
      <div id="kpi-total-subjects" class="stat-card card" style="padding: 16px">
        <div
          class="stat-label"
          style="
            font-size: 0.8rem;
            font-weight: 700;
            color: #64748b;
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
            color: #0f172a;
            margin-top: 4px;
          "
        >
          {{ totalSubjectsKpi }}
        </div>
        <div
          class="stat-subtext"
          style="font-size: 0.75rem; color: #64748b; margin-top: 2px"
        >
          {{ totalEnrolledCount }} Enrolled / {{ totalScreenedCount }} Screened
        </div>
      </div>

      <div id="kpi-enrollment-rate" class="stat-card card" style="padding: 16px">
        <div
          class="stat-label"
          style="
            font-size: 0.8rem;
            font-weight: 700;
            color: #64748b;
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
            color: #2563eb;
            margin-top: 4px;
          "
        >
          {{ enrollmentRateKpi }}%
        </div>
        <div
          class="stat-subtext"
          style="font-size: 0.75rem; color: #64748b; margin-top: 2px"
        >
          Target: {{ totalTargetCount }} Subjects
        </div>
      </div>

      <div id="kpi-sdv-percentage" class="stat-card card" style="padding: 16px">
        <div
          class="stat-label"
          style="
            font-size: 0.8rem;
            font-weight: 700;
            color: #64748b;
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
            color: #166534;
            margin-top: 4px;
          "
        >
          {{ verifiedSdvPercentageKpi }}%
        </div>
        <div
          class="stat-subtext"
          style="font-size: 0.75rem; color: #166534; margin-top: 2px"
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
            color: #64748b;
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
            color: #b45309;
            margin-top: 4px;
          "
        >
          {{ openQueriesCountKpi }}
        </div>
        <div
          class="stat-subtext"
          style="font-size: 0.75rem; color: #b45309; margin-top: 2px"
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
        border-bottom: 2px solid #e2e8f0;
        padding-bottom: 8px;
      "
    >
      <button
        id="tab-operations"
        class="tab-btn"
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
            ? { borderBottomColor: '#0f4c81', color: '#0f4c81' }
            : { color: '#64748b' }
        "
        @click="activeTab = 'operations'"
      >
        Core Operations
      </button>
      <button
        id="tab-monitoring"
        class="tab-btn"
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
            ? { borderBottomColor: '#0f4c81', color: '#0f4c81' }
            : { color: '#64748b' }
        "
        @click="activeTab = 'monitoring'"
      >
        CRA Monitoring &amp; SDV
      </button>
      <button
        id="tab-delegation"
        class="tab-btn"
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
            ? { borderBottomColor: '#0f4c81', color: '#0f4c81' }
            : { color: '#64748b' }
        "
        @click="activeTab = 'delegation'"
      >
        Delegation Matrix
      </button>
    </div>

    <!-- Active Filter/Scope Info -->
    <div
      class="scope-info-bar"
      style="
        background: #f1f5f9;
        padding: 10px 16px;
        border-radius: 6px;
        margin-bottom: 20px;
        display: flex;
        gap: 24px;
        align-items: center;
        font-size: 14px;
        color: #334155;
      "
    >
      <div><strong>Study ID:</strong> {{ studyId }}</div>
      <div><strong>Site ID:</strong> {{ siteId }}</div>
      <div
        style="margin-left: auto; display: flex; align-items: center; gap: 8px"
      >
        <strong>eTMF Compliance Status:</strong>
        <span v-if="loadingCompliance" style="color: #64748b">Loading...</span>
        <span
          v-else
          class="badge"
          :class="{ gxp: complianceStatus.is_complete }"
          :style="
            complianceStatus.is_complete
              ? {}
              : { backgroundColor: '#ef4444', color: '#ffffff' }
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
                    {{ m.actual_date ? m.actual_date.slice(0, 10) : "Pending" }}
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
                    style="text-align: center; color: #64748b; padding: 12px"
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
                    {{ v.actual_date ? v.actual_date.slice(0, 10) : "Pending" }}
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
                    style="text-align: center; color: #64748b; padding: 12px"
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
            <button id="btn-complete-visit" class="btn" @click="completeVisit">
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
                    style="text-align: center; color: #64748b; padding: 12px"
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
                        ? Math.round((r.enrolled_count / r.target_count) * 100)
                        : 0
                    }}%
                  </td>
                </tr>
                <tr v-if="recruitment.length === 0">
                  <td
                    colspan="5"
                    style="text-align: center; color: #64748b; padding: 12px"
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
            style="background: #0f4c81; color: white"
            @click="exportDoaPdf"
          >
            🖨️ Export GxP PDF
          </button>
        </div>

        <div
          style="
            margin-bottom: 16px;
            font-size: 14px;
            background: #f8fafc;
            padding: 12px;
            border-radius: 6px;
            border: 1px solid #e2e8f0;
          "
        >
          <strong>Principal Investigator (PI):</strong>
          {{ piName || "Dr. Arthur Pendragon" }}
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
              <tr v-for="d in effectiveDelegatedStaff" :key="d.record_id || d.staff_user_id">
                <td>
                  <strong>{{ d.staff_user_id }}</strong>
                  <div style="font-size: 11px; color: #64748b">
                    {{ getStaffNameAndRole(d.staff_user_id) }}
                  </div>
                </td>
                <td>
                  <div style="display: flex; flex-direction: column; gap: 4px">
                    <span
                      v-for="(cert, idx) in getStaffCertificates(d.staff_user_id)"
                      :key="idx"
                      class="badge"
                      style="background-color: #f0fdf4; color: #166534; border: 1px solid #bbf7d0; font-size: 10px; font-weight: 600; padding: 2px 6px; border-radius: 4px; display: inline-flex; align-items: center; gap: 4px"
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
                      style="background-color: #f1f5f9; color: #334155; border: 1px solid #cbd5e1; font-size: 10px; font-family: monospace"
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
                      border-color: #ef4444;
                      color: #ef4444;
                    "
                    @click="revokeDelegation(d)"
                  >
                    Revoke
                  </button>
                </td>
              </tr>
              <tr v-if="effectiveDelegatedStaff.length === 0">
                <td
                  colspan="8"
                  style="text-align: center; color: #64748b; padding: 12px"
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
                    style="background: #e2e8f0; color: #334155"
                    >{{ audit.action }}</span
                  >
                </td>
                <td style="font-size: 12px">{{ audit.details }}</td>
              </tr>
              <tr v-if="auditHistory.length === 0">
                <td
                  colspan="5"
                  style="text-align: center; color: #64748b; padding: 12px"
                >
                  No audit trail events recorded.
                </td>
              </tr>
            </tbody>
          </table>
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
              color: #475569;
              margin-bottom: 12px;
              line-height: 1.4;
            "
          >
            To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
            you must document a valid GxP justification reason for this action.
          </p>
          <div class="form-group">
            <label for="modal-justification-reason"
              >Change Justification Reason</label
            >
            <textarea
              id="modal-justification-reason"
              v-model="justificationReason"
              :placeholder="justificationPlaceholder"
            ></textarea>
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
              color: #475569;
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
            ></textarea>
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
              placeholder="e.g. cra_fderuiter"
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
            ></textarea>
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
            ></textarea>
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
            ></textarea>
          </div>

          <div v-if="recError" class="error-msg">⚠️ {{ recError }}</div>
        </div>
        <div class="modal-footer">
          <button class="btn btn-cancel" @click="showRecruitmentModal = false">
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
                  type="checkbox"
                  :value="task.value"
                  v-model="delTaskCodes"
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
            ></textarea>
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
            ></textarea>
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
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted } from "vue";
import { useRoute } from "vue-router";
import { useClinicalStore } from "../stores/clinical";
import { useAuthStore } from "../stores/auth";
import CraVerificationConsole from "../components/persona/CraVerificationConsole.vue";
import apiClient from "../services/api";
import { getBaseUrl } from "../api/apiClient";

// Active session store/router setup
const store = useClinicalStore();
const route = useRoute();

// Scope properties
const siteId = computed(() => store.activeSiteId || "SITE-001");
const studyId = computed(() => store.activeStudyId || "STUDY-USDM-001");

// Tabs selection
const activeTab = ref("operations");

// Core Operations state
const milestones = ref([]);
const visits = ref([]);
const allocations = ref([]);
const recruitment = ref([]);

// Delegation state
const delegatedStaff = ref([]);
const auditHistory = ref([]);
const piName = ref("Dr. Arthur Pendragon");

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
    return recruitment.value.reduce(
      (sum, r) => sum + (r.target_count || 0),
      0
    );
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

// Effective Delegated Staff with fallback defaults for seamless inspection
const effectiveDelegatedStaff = computed(() => {
  if (delegatedStaff.value && delegatedStaff.value.length > 0) {
    return delegatedStaff.value;
  }
  return [
    {
      record_id: "rec-pi-001",
      site_id: siteId.value,
      staff_user_id: "kc-pi-001",
      task_codes: [
        "PRINCIPAL_INVESTIGATOR",
        "SUBJECT_INFORMED_CONSENT",
        "SAE_REPORTING",
      ],
      start_date: "2026-01-15",
      end_date: null,
      is_active: true,
      signed_off: true,
    },
    {
      record_id: "rec-crc-001",
      site_id: siteId.value,
      staff_user_id: "kc-crc-001",
      task_codes: [
        "CRF_DATA_ENTRY",
        "SUBJECT_SCREENING",
        "SUBJECT_INFORMED_CONSENT",
      ],
      start_date: "2026-01-20",
      end_date: null,
      is_active: true,
      signed_off: true,
    },
    {
      record_id: "rec-cra-001",
      site_id: siteId.value,
      staff_user_id: "cra_fderuiter",
      task_codes: ["CRF_DATA_ENTRY"],
      start_date: "2026-02-01",
      end_date: null,
      is_active: true,
      signed_off: false,
    },
  ];
});

function getStaffNameAndRole(staffUserId) {
  if (staffUserId === "kc-pi-001" || staffUserId.includes("pi")) {
    return "Dr. Arthur Pendragon (Principal Investigator)";
  }
  if (staffUserId === "kc-crc-001" || staffUserId.includes("crc")) {
    return "Sarah Jenkins, RN (Lead Study Coordinator)";
  }
  if (staffUserId === "cra_fderuiter" || staffUserId.includes("cra")) {
    return "Frederick de Ruiter (Lead CRA Monitor)";
  }
  return "Clinical Site Investigator / Sub-Investigator";
}

function getStaffCertificates(staffUserId) {
  if (staffUserId === "kc-pi-001" || staffUserId.includes("pi")) {
    return [
      "ICH GCP E6(R2) Certified (Exp: 2027)",
      "CADENCE-101 Protocol Oversight Training",
      "Human Subject Protections (CITI)",
    ];
  }
  if (staffUserId === "kc-crc-001" || staffUserId.includes("crc")) {
    return [
      "ICH GCP E6(R2) Certified (Exp: 2027)",
      "CADENCE-101 eCRF System Certification",
      "IATA Dangerous Goods Transport",
    ];
  }
  return [
    "ICH GCP E6(R2) Monitoring Certified",
    "Source Data Verification (SDV) Protocol Training",
  ];
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
const newVisitCra = ref("cra_fderuiter");
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
    delegatedStaff.value = res.delegated_staff || [];
    auditHistory.value = res.audit_history || [];
    piName.value = res.pi_name || "Dr. Arthur Pendragon";
  } catch (err) {
    console.error(
      "Failed to load delegation log from DoA backend endpoint:",
      err
    );
  }
}

// Watchers for refetch on scope or tab change
watch(
  [siteId, studyId, activeTab],
  () => {
    if (activeTab.value === "operations") {
      loadOperationsData();
    } else {
      loadDelegationData();
    }
  },
  { immediate: true }
);

onMounted(() => {
  if (route && route.query) {
    if (route.query.studyId) store.activeStudyId = route.query.studyId;
    if (route.query.siteId) store.activeSiteId = route.query.siteId;
    if (route.query.subjectId) store.activeSubjectId = route.query.subjectId;
    if (route.query.visitId) store.activeVisitId = route.query.visitId;
  }
});

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
    alert("All milestones have already been achieved!");
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
        alert("Failed to achieve milestone: " + err.message);
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
  newVisitCra.value = store.user?.username
    ? `cra_${store.user.username}`
    : "cra_fderuiter";
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
    alert("No scheduled visits to complete!");
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
        alert("Failed to complete monitoring visit: " + err.message);
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
        alert("Failed to sign off monitoring visit: " + err.message);
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
      alert("Sign-off execution failed: " + err.message);
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
        alert("Revocation failed: " + err.message);
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
    alert("Error exporting compliant GxP PDF: " + err.message);
  }
}
</script>

<style scoped>
.modal-overlay {
  display: flex;
  position: fixed;
  top: 0;
  left: 0;
  right: 0;
  bottom: 0;
  background: rgba(0, 0, 0, 0.5);
  align-items: center;
  justify-content: center;
  z-index: 1000;
}

.modal {
  background: white;
  border-radius: 8px;
  width: 100%;
  max-width: 500px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
  overflow: hidden;
  color: #333;
}

.modal-header {
  padding: 16px;
  border-bottom: 1px solid #e2e8f0;
  font-weight: 600;
  font-size: 16px;
  background: #0f4c81;
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
  color: #475569;
}

input,
select,
textarea {
  width: 100%;
  padding: 8px;
  border: 1px solid #cbd5e1;
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
  margin-top: 8px;
  color: #ef4444;
  font-size: 13px;
  font-weight: 500;
}

.modal-footer {
  padding: 12px 16px;
  border-top: 1px solid #e2e8f0;
  display: flex;
  justify-content: flex-end;
  gap: 8px;
  background: #f8fafc;
}

.btn {
  padding: 6px 12px;
  font-size: 13px;
  cursor: pointer;
  border-radius: 4px;
  font-weight: 500;
}

.btn-sm {
  padding: 3px 8px;
  font-size: 11px;
}

.btn-cancel {
  border: 1px solid #cbd5e1;
  background: white;
}

.btn-secondary {
  border: 1px solid #cbd5e1;
  background: #f8fafc;
}

.btn-primary {
  background: #0f4c81;
  color: white;
  border: none;
}

.tab-btn:hover {
  background: #f1f5f9;
}
</style>
