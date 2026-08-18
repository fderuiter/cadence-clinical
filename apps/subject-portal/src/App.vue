<template>
  <div id="app" :class="{ 'drawer-open': state.isSyncDrawerOpen }">
    <!-- Portal Header -->
    <header class="portal-header">
      <div class="header-branding">
        <h1>
          My <span>{{ brandName }}</span>
        </h1>
        <p class="role-badge">Participant Companion Portal</p>
      </div>
      <div class="compliance-badges">
        <span class="badge badge-epro">ePRO Companion</span>
        <span class="badge badge-gxp">21 CFR Part 11</span>
        <span class="badge badge-gamp">GAMP 5</span>

        <!-- Persistent dynamic sync status button -->
        <button
          id="btn-global-sync-drawer"
          type="button"
          class="btn-sync-status"
          :class="{
            'has-queued': queuedSubmissionsCount > 0,
            'sync-failed': isSyncFailed,
          }"
          aria-label="Toggle sync status drawer"
          @click="toggleSyncDrawer"
        >
          <span class="sync-icon">🔄</span>
          <span v-if="queuedSubmissionsCount > 0" class="sync-badge">
            {{ queuedSubmissionsCount }}
          </span>
          <span class="sync-btn-label">Sync Status</span>
        </button>
      </div>
    </header>

    <!-- Global Floating Sync Drawer -->
    <div
      class="global-drawer-overlay"
      :class="{ active: state.isSyncDrawerOpen }"
      @click="toggleSyncDrawer"
    ></div>

    <div
      class="global-drawer"
      :class="{ active: state.isSyncDrawerOpen }"
      role="dialog"
      aria-modal="true"
      aria-labelledby="drawer-title"
    >
      <div class="drawer-header">
        <h3 id="drawer-title">🔄 Submission Sync Status</h3>
        <button
          id="btn-close-sync-drawer"
          type="button"
          class="btn-close-drawer"
          aria-label="Close drawer"
          @click="toggleSyncDrawer"
        >
          ×
        </button>
      </div>

      <div class="drawer-body">
        <div class="sync-status-section">
          <p class="status-text">
            {{ state.syncStatusText || "Checking sync status..." }}
          </p>
          <button
            id="btn-drawer-sync-now"
            type="button"
            class="btn btn-primary btn-sync-trigger"
            style="width: 100%; margin-top: 8px"
            @click="triggerManualSync"
          >
            Sync Now
          </button>
        </div>

        <div class="submissions-list-wrapper">
          <h4 class="submissions-heading">Submissions Status</h4>
          <div
            v-if="!state.submissions || state.submissions.length === 0"
            class="empty-state"
          >
            No submission history in queue.
          </div>
          <div v-else class="submissions-items-list">
            <div
              v-for="item in state.submissions"
              :key="item.sequence_number"
              class="drawer-submission-item"
              :class="getSubmissionClass(item)"
            >
              <div class="item-header-row">
                <span class="item-name">{{
                  getInstrumentName(item.diary_id)
                }}</span>
                <span class="status-pill" :class="getBadgeClass(item)">
                  {{ getStatusLabel(item) }}
                </span>
              </div>
              <div class="item-time">
                Seq: #{{ item.sequence_number }} | Device Time:
                {{ formatTime(item.device_timestamp) }}
              </div>
              <div class="item-details">
                <p class="item-desc">{{ getStatusDescription(item) }}</p>
                <div class="item-data">
                  <strong>Local Answers:</strong>
                  <code class="data-code">{{
                    JSON.stringify(item.answers)
                  }}</code>
                </div>
                <div
                  v-if="item.resolved_answers && item.status === 'MERGED'"
                  class="item-data merged-data"
                >
                  <strong>Merged Result:</strong>
                  <code class="data-code">{{
                    JSON.stringify(item.resolved_answers)
                  }}</code>
                </div>
              </div>
            </div>
          </div>
        </div>
      </div>
    </div>

    <!-- Main Layout -->
    <div class="portal-container">
      <!-- Patient Navigation Tabs -->
      <nav class="portal-navigation" aria-label="Participant navigation">
        <ul
          class="nav-tabs"
          role="tablist"
          tabindex="0"
          @keydown="handleTabKeyDown"
        >
          <li
            v-for="tab in tabs"
            :id="'tab-btn-' + tab.id"
            :key="tab.id"
            class="nav-item"
            :class="{ active: state.currentView === tab.viewId }"
            role="none"
          >
            <button
              :ref="
                (el) => {
                  if (el) tabRefs[tab.id] = el;
                }
              "
              type="button"
              role="tab"
              :aria-selected="
                state.currentView === tab.viewId ? 'true' : 'false'
              "
              :aria-controls="tab.viewId"
              :tabindex="state.currentView === tab.viewId ? 0 : -1"
              @click="selectView(tab.viewId)"
            >
              <span class="icon">{{ tab.icon }}</span> {{ tab.label }}
              <span
                v-if="tab.id === 'inbox' && state.unreadCount > 0"
                id="unread-count"
                class="badge-unread"
              >
                {{ state.unreadCount }}
              </span>
            </button>
          </li>
        </ul>

        <!-- Active Participant Card -->
        <div class="participant-info-card">
          <div class="info-title">Participant Session</div>
          <div class="info-row">
            <span class="info-lbl">ID:</span>
            <span id="session-subject-id" class="info-val">{{
              state.session.userId || "Loading..."
            }}</span>
          </div>
          <div class="info-row">
            <span class="info-lbl">Status:</span>
            <span class="info-val text-success">Active</span>
          </div>
          <div class="info-row" style="margin-top: 12px">
            <button
              id="btn-logout"
              type="button"
              class="btn btn-secondary"
              style="width: 100%; padding: 4px 8px; font-size: 12px"
              @click="handleLogout"
            >
              Logout
            </button>
          </div>
        </div>
      </nav>

      <!-- Main Viewport -->
      <main class="portal-main">
        <!-- Urgent Re-Consent Notification Banner -->
        <div
          v-if="state.pendingReconsent"
          id="reconsent-urgent-banner"
          class="card urgent-reconsent-banner"
          style="
            border: 2px solid #eab308;
            background-color: #fefce8;
            color: #854d0e;
            padding: 16px 20px;
            margin-bottom: 24px;
            border-radius: 8px;
            display: flex;
            align-items: center;
            justify-content: space-between;
            gap: 16px;
            box-shadow: 0 4px 6px -1px rgba(0, 0, 0, 0.1);
          "
          role="alert"
        >
          <div style="display: flex; align-items: center; gap: 12px;">
            <span style="font-size: 24px;">⚠️</span>
            <div>
              <h3
                style="
                  margin: 0 0 4px 0;
                  font-size: 16px;
                  font-weight: 700;
                  color: #713f12;
                "
              >
                Action Required: Protocol Amendment Re-Consent
              </h3>
              <p style="margin: 0; font-size: 14px;">
                A study protocol amendment requires your review and electronic
                signature before diary submission.
              </p>
            </div>
          </div>
          <button
            id="btn-open-reconsent-modal"
            type="button"
            class="btn btn-primary"
            style="
              white-space: nowrap;
              background-color: #ca8a04;
              border-color: #a16207;
            "
            @click="openReconsentModal"
          >
            Review & Sign Now
          </button>
        </div>

        <!-- Version mismatch banner -->
        <div
          v-if="state.autoSyncSuspended"
          class="card error-state"
          style="
            border: 1px solid var(--danger);
            background-color: #fee2e2;
            color: #b91c1c;
            padding: 16px;
            margin-bottom: 24px;
            text-align: center;
          "
        >
          <p style="font-weight: bold; font-size: 16px; margin: 0 0 8px 0">
            ⚠️ Outdated Form Structure Detected
          </p>
          <p style="font-size: 14px; margin: 0 0 12px 0">
            The questionnaire version index does not match the active server
            structure. Automatic sync is suspended. Please perform a manual
            refresh.
          </p>
          <button
            id="btn-alert-sync-now"
            type="button"
            class="btn btn-primary"
            @click="triggerManualSync"
          >
            Manual Sync / Refresh
          </button>
        </div>
        <!-- View 1: My Tasks (Assigned Questionnaires) -->
        <section
          id="view-tasks"
          class="portal-view"
          :class="{ active: state.currentView === 'view-tasks' }"
          role="tabpanel"
          aria-labelledby="tab-btn-tasks"
          tabindex="0"
        >
          <div class="view-header">
            <h2>My Daily Questionnaires</h2>
            <p>
              Please complete your scheduled questionnaires on time to help us
              ensure high data quality.
            </p>
          </div>

          <!-- Tasks Loading Placeholder -->
          <div id="tasks-loading" class="loading-state" style="display: none">
            Loading your assigned tasks...
          </div>

          <!-- Tasks Failure Container -->
          <div
            id="tasks-failure"
            class="card error-state"
            style="
              display: none;
              text-align: center;
              padding: 32px;
              border: 1px solid var(--danger);
              margin-bottom: 16px;
            "
          >
            <p style="color: var(--danger); font-weight: bold; font-size: 16px">
              Failed to load assigned tasks.
            </p>
            <p
              id="tasks-error-msg"
              style="font-size: 14px; margin-bottom: 12px"
            >
              Unknown error
            </p>
            <button id="btn-retry-tasks" type="button" class="btn btn-primary">
              Retry
            </button>
          </div>

          <!-- List of tasks -->
          <div id="tasks-list-container" class="tasks-list">
            <div class="loading-state">Loading your assigned tasks...</div>
          </div>

          <!-- Offline Sync Status Panel -->
          <div
            id="sync-queue-panel"
            class="card sync-queue-panel"
            style="margin-top: 24px"
          >
            <div
              class="panel-header"
              style="
                display: flex;
                justify-content: space-between;
                align-items: center;
                border-bottom: 1px solid var(--border-color);
                padding-bottom: 12px;
                margin-bottom: 12px;
              "
            >
              <h3
                style="margin: 0; display: flex; align-items: center; gap: 8px"
              >
                <span>🔄</span> Offline Sync Queue
              </h3>
              <button
                id="btn-sync-now"
                type="button"
                class="btn btn-secondary"
                style="padding: 6px 12px; font-size: 13px"
              >
                Sync Now
              </button>
            </div>
            <div
              id="sync-queue-status-text"
              class="text-muted"
              style="font-size: 14px; margin-bottom: 12px"
            >
              Checking sync status...
            </div>
            <div
              id="sync-queue-list"
              style="display: flex; flex-direction: column; gap: 8px"
            >
              <!-- Dynamically populated synced & queued submissions go here -->
            </div>
          </div>
        </section>

        <!-- View 2: Questionnaire Form Completion Screen -->
        <section
          id="view-questionnaire"
          class="portal-view"
          :class="{ active: state.currentView === 'view-questionnaire' }"
        >
          <div class="view-header">
            <button id="btn-back-to-tasks" type="button" class="btn-back">
              ← Back to My Tasks
            </button>
            <h2 id="questionnaire-title">Questionnaire</h2>
            <p id="questionnaire-desc">
              Please answer all questions accurately.
            </p>
          </div>

          <div class="card questionnaire-card">
            <div id="questionnaire-form-container">
              <!-- Dynamically compiled CDASH-inputs go here -->
            </div>
            <div class="form-actions">
              <button
                id="btn-cancel-questionnaire"
                type="button"
                class="btn btn-secondary"
              >
                Cancel
              </button>
              <button
                id="btn-submit-questionnaire"
                type="button"
                class="btn btn-primary"
              >
                Sign and Submit
              </button>
            </div>
          </div>
        </section>

        <!-- View 3: My Compliance -->
        <section
          id="view-compliance"
          class="portal-view"
          :class="{ active: state.currentView === 'view-compliance' }"
          role="tabpanel"
          aria-labelledby="tab-btn-compliance"
          tabindex="0"
        >
          <div class="view-header">
            <h2>My Compliance Overview</h2>
            <p>
              Track your scheduled survey progress and overall trial
              participation history.
            </p>
          </div>

          <!-- Compliance Loading Placeholder -->
          <div
            id="compliance-loading"
            class="loading-state"
            style="display: none"
          >
            Loading compliance data...
          </div>

          <!-- Compliance Failure Container -->
          <div
            id="compliance-failure"
            class="card error-state"
            style="
              display: none;
              text-align: center;
              padding: 32px;
              border: 1px solid var(--danger);
              margin-bottom: 16px;
            "
          >
            <p style="color: var(--danger); font-weight: bold; font-size: 16px">
              Failed to load compliance data.
            </p>
            <p
              id="compliance-error-msg"
              style="font-size: 14px; margin-bottom: 12px"
            >
              Unknown error
            </p>
            <button
              id="btn-retry-compliance"
              type="button"
              class="btn btn-primary"
            >
              Retry
            </button>
          </div>

          <div class="grid-layout">
            <!-- Compliance Score Card -->
            <div class="card compliance-score-card">
              <div class="score-radial">
                <div id="compliance-rate-pct" class="score-value">0%</div>
                <div class="score-lbl">Compliance Rate</div>
              </div>
              <div class="score-breakdown">
                <div class="breakdown-item">
                  <span class="lbl">Completed:</span>
                  <strong
                    id="compliance-completed-count"
                    class="val text-success"
                    >0</strong
                  >
                </div>
                <div class="breakdown-item">
                  <span class="lbl">Pending:</span>
                  <strong id="compliance-pending-count" class="val text-warning"
                    >0</strong
                  >
                </div>
                <div class="breakdown-item">
                  <span class="lbl">Overdue:</span>
                  <strong id="compliance-overdue-count" class="val text-danger"
                    >0</strong
                  >
                </div>
              </div>
            </div>

            <!-- History Card -->
            <div class="card history-card">
              <h3>My Survey History</h3>
              <div class="table-responsive">
                <table class="compliance-history-table">
                  <thead>
                    <tr>
                      <th>Questionnaire</th>
                      <th>Scheduled Date</th>
                      <th>Submitted Date</th>
                      <th>Status</th>
                    </tr>
                  </thead>
                  <tbody id="compliance-history-tbody">
                    <tr>
                      <td colspan="4" class="no-data">No history found.</td>
                    </tr>
                  </tbody>
                </table>
              </div>
            </div>
          </div>
        </section>

        <!-- View 4: My Inbox (Notifications) -->
        <section
          id="view-inbox"
          class="portal-view"
          :class="{ active: state.currentView === 'view-inbox' }"
          role="tabpanel"
          aria-labelledby="tab-btn-inbox"
          tabindex="0"
        >
          <div class="view-header">
            <h2>My Notification Inbox</h2>
            <p>
              Receive reminders, alerts, and system notifications regarding your
              trial schedule.
            </p>
          </div>

          <!-- Inbox Loading Placeholder -->
          <div id="inbox-loading" class="loading-state" style="display: none">
            Loading notifications...
          </div>

          <!-- Inbox Failure Container -->
          <div
            id="inbox-failure"
            class="card error-state"
            style="
              display: none;
              text-align: center;
              padding: 32px;
              border: 1px solid var(--danger);
              margin-bottom: 16px;
            "
          >
            <p style="color: var(--danger); font-weight: bold; font-size: 16px">
              Failed to load notifications.
            </p>
            <p
              id="inbox-error-msg"
              style="font-size: 14px; margin-bottom: 12px"
            >
              Unknown error
            </p>
            <button id="btn-retry-inbox" type="button" class="btn btn-primary">
              Retry
            </button>
          </div>

          <div id="inbox-container" class="inbox-list">
            <div class="loading-state">Loading notifications...</div>
          </div>
        </section>

        <!-- View 5: My Consent (eConsent ICF Review, Comprehension, and Signature) -->
        <section
          id="view-consent"
          class="portal-view"
          :class="{ active: state.currentView === 'view-consent' }"
          role="tabpanel"
          aria-labelledby="tab-btn-consent"
          tabindex="0"
        >
          <div class="view-header">
            <h2>My Informed Consent Review</h2>
            <p>
              Please read the study details below, complete the comprehension
              check, and submit your digital signature.
            </p>
          </div>

          <!-- Language selector for approved content -->
          <div
            class="card"
            style="
              margin-bottom: 16px;
              padding: 12px 16px;
              display: flex;
              align-items: center;
              justify-content: space-between;
              gap: 16px;
            "
          >
            <div>
              <label
                for="consent-lang-selector"
                style="font-weight: bold; margin-right: 8px"
                >Select Language:</label
              >
              <select
                id="consent-lang-selector"
                style="
                  padding: 6px 12px;
                  border-radius: 4px;
                  border: 1px solid var(--border-color);
                  background: var(--bg-color);
                  color: var(--text-color);
                "
              >
                <option value="en">English (en)</option>
                <option value="es">Español (es)</option>
                <option value="nl">Nederlands (nl)</option>
                <option value="fr">Français (fr)</option>
              </select>
            </div>
            <span id="consent-status-badge" class="status-pill pending"
              >Pending Check</span
            >
          </div>

          <!-- Loading indicator -->
          <div id="consent-loading" class="loading-state" style="display: none">
            Loading informed consent details...
          </div>

          <!-- Failure container -->
          <div
            id="consent-failure"
            class="card error-state"
            style="
              display: none;
              text-align: center;
              padding: 32px;
              border: 1px solid var(--danger);
              margin-bottom: 16px;
            "
          >
            <p style="color: var(--danger); font-weight: bold; font-size: 16px">
              Failed to load consent form.
            </p>
            <p
              id="consent-error-msg"
              style="font-size: 14px; margin-bottom: 12px"
            >
              Unknown error
            </p>
            <button
              id="btn-retry-consent"
              type="button"
              class="btn btn-primary"
            >
              Retry
            </button>
          </div>

          <!-- Consent Document Content & Visual Renderer -->
          <div id="consent-content-wrapper" style="display: none">
            <!-- Metadata and Clauses -->
            <div class="card" style="margin-bottom: 24px">
              <h3
                id="consent-template-title"
                style="margin-top: 0; color: var(--primary-color)"
              >
                Informed Consent Form
              </h3>
              <div
                id="consent-metadata-display"
                style="
                  font-size: 12px;
                  color: var(--text-muted);
                  border-bottom: 1px solid var(--border-color);
                  padding-bottom: 12px;
                  margin-bottom: 16px;
                "
              >
                <!-- Dynamically populated template metadata -->
              </div>
              <div
                id="consent-clauses-container"
                style="
                  line-height: 1.6;
                  display: flex;
                  flex-direction: column;
                  gap: 16px;
                "
              >
                <!-- Dynamically populated clauses -->
              </div>
            </div>

            <!-- Comprehension Check Form Section -->
            <div
              id="consent-comprehension-card"
              class="card"
              style="margin-bottom: 24px"
            >
              <h3
                style="
                  margin-top: 0;
                  display: flex;
                  align-items: center;
                  gap: 8px;
                "
              >
                <span>🧠</span> Step 1: Comprehension Verification Check
              </h3>
              <p
                style="
                  font-size: 14px;
                  color: var(--text-muted);
                  margin-bottom: 16px;
                "
              >
                Please answer the following brief questions to verify your
                understanding of the trial risks and procedures.
              </p>

              <!-- Status Feedback region -->
              <div
                id="comprehension-status-banner"
                style="
                  display: none;
                  padding: 12px;
                  border-radius: 6px;
                  margin-bottom: 16px;
                  font-size: 14px;
                  font-weight: 600;
                "
                role="status"
                aria-live="polite"
              >
                <!-- Success / Failure live statuses -->
              </div>

              <div id="consent-questions-container">
                <!-- Dynamically rendered questions using radio grids -->
              </div>

              <div class="form-actions" style="margin-top: 20px">
                <button
                  id="btn-submit-consent-answers"
                  type="button"
                  class="btn btn-primary"
                >
                  Verify and Submit Answers
                </button>
              </div>
            </div>

            <!-- Signature Section -->
            <div
              id="consent-signature-card"
              class="card"
              style="margin-bottom: 24px"
            >
              <h3
                style="
                  margin-top: 0;
                  display: flex;
                  align-items: center;
                  gap: 8px;
                "
              >
                <span>✍️</span> Step 2: Electronic Signature
              </h3>
              <p
                style="
                  font-size: 14px;
                  color: var(--text-muted);
                  margin-bottom: 16px;
                "
              >
                By signing this document, you confirm that you have read this
                informed consent, have passed the verification, and agree to
                participate.
              </p>
              <div class="form-actions">
                <button
                  id="btn-trigger-consent-sign"
                  type="button"
                  class="btn btn-primary"
                  disabled
                >
                  Sign Consent Form
                </button>
              </div>
            </div>
          </div>
        </section>
      </main>
    </div>

    <!-- Electronic Signature and Reason Modal -->
    <!-- eslint-disable-next-line vuejs-accessibility/no-static-element-interactions -->
    <div
      id="portal-sign-modal"
      class="modal-overlay"
      style="display: none"
      role="dialog"
      aria-modal="true"
      aria-labelledby="portal-modal-title"
      tabindex="-1"
      @keydown="handleModalKeyDown"
    >
      <div class="modal">
        <div id="portal-modal-title" class="modal-header">
          Electronic Signature Required
        </div>
        <div class="modal-body">
          <p>
            To comply with <strong>FDA 21 CFR Part 11 / EU Annex 11</strong>,
            you must provide your electronic signature credentials and declare
            your change reason or completion affirmation.
          </p>
          <!-- Accessible modal live error region -->
          <div
            id="modal-error-banner"
            style="
              display: none;
              padding: 8px;
              border-radius: 4px;
              background-color: #fee2e2;
              color: #b91c1c;
              margin-bottom: 12px;
              font-size: 13px;
              font-weight: 600;
            "
            role="status"
            aria-live="polite"
          ></div>

          <div class="form-group mb-12">
            <label for="sign-reason">Reason for Action / Declaration</label>
            <select id="sign-reason">
              <option value="Initial Questionnaire Completion">
                Initial Questionnaire Completion
              </option>
              <option value="Correction of previous entry">
                Correction of previous entry
              </option>
              <option value="Requested update by study coordinator">
                Requested update by study coordinator
              </option>
              <option value="Acknowledge important reminder">
                Acknowledge important reminder
              </option>
              <option value="Other">Other (specify below)</option>
            </select>
          </div>
          <div class="form-group mb-12">
            <label for="sign-reason-custom"
              >Custom Reason Detail (Optional)</label
            >
            <textarea
              id="sign-reason-custom"
              placeholder="Provide extra detail if 'Other' selected..."
            ></textarea>
          </div>
          <div class="form-group mb-12">
            <label for="sign-username">User ID / Username</label>
            <input
              id="sign-username"
              type="text"
              placeholder="Enter your participant identifier..."
              required
            />
          </div>
          <div class="form-group">
            <label for="sign-password">Security PIN / Password</label>
            <input
              id="sign-password"
              type="password"
              placeholder="••••••••"
              required
            />
          </div>
        </div>
        <div class="modal-footer">
          <button id="btn-modal-cancel" type="button" class="btn btn-secondary">
            Cancel
          </button>
          <button id="btn-modal-sign" type="button" class="btn btn-primary">
            Sign and Confirm
          </button>
        </div>
      </div>
    </div>

    <!-- Interactive Re-Consent E-Signature Modal -->
    <div
      v-if="state.reconsentModalOpen"
      id="portal-reconsent-modal"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="portal-reconsent-modal-title"
      tabindex="-1"
      @keydown="handleReconsentModalKeyDown"
    >
      <div class="modal" style="max-width: 600px;">
        <div id="portal-reconsent-modal-title" class="modal-header">
          Protocol Amendment Re-Consent Review
        </div>
        <div class="modal-body">
          <p style="font-size: 14px; margin-bottom: 12px;">
            A new protocol amendment has been published for your clinical study.
            Please review the change summary below and execute your electronic
            signature to complete re-consent.
          </p>

          <div
            v-if="state.reconsentModalError"
            id="reconsent-modal-error-banner"
            style="
              padding: 8px 12px;
              border-radius: 4px;
              background-color: #fee2e2;
              color: #b91c1c;
              margin-bottom: 12px;
              font-size: 13px;
              font-weight: 600;
            "
            role="status"
            aria-live="polite"
          >
            {{ state.reconsentModalError }}
          </div>

          <div
            id="reconsent-change-summary"
            class="card"
            style="
              background-color: #f8fafc;
              border: 1px solid #e2e8f0;
              padding: 12px 16px;
              margin-bottom: 16px;
              border-radius: 6px;
            "
          >
            <h4 style="margin: 0 0 6px 0; font-size: 14px; color: #1e293b;">
              Summary of Protocol Amendments:
            </h4>
            <p style="margin: 0; font-size: 13px; color: #475569;">
              {{
                state.pendingReconsent?.change_summary ||
                state.pendingReconsent?.summary_of_changes ||
                "Updated study consent document terms."
              }}
            </p>
          </div>

          <div class="form-group mb-12">
            <label for="reconsent-sign-reason"
              >Reason for Action / Declaration</label
            >
            <select id="reconsent-sign-reason" v-model="state.reconsentForm.reason">
              <option value="Protocol Amendment Re-Consent Acknowledgment">
                Protocol Amendment Re-Consent Acknowledgment
              </option>
              <option value="Acknowledge updated clinical study consent">
                Acknowledge updated clinical study consent
              </option>
              <option value="Other">Other (specify below)</option>
            </select>
          </div>
          <div class="form-group mb-12">
            <label for="reconsent-sign-reason-custom"
              >Custom Reason Detail (Optional)</label
            >
            <textarea
              id="reconsent-sign-reason-custom"
              v-model="state.reconsentForm.customReason"
              placeholder="Provide extra detail if 'Other' selected..."
            ></textarea>
          </div>
          <div class="form-group mb-12">
            <label for="reconsent-sign-username">User ID / Username</label>
            <input
              id="reconsent-sign-username"
              type="text"
              v-model="state.reconsentForm.username"
              placeholder="Enter your participant identifier..."
              required
            />
          </div>
          <div class="form-group">
            <label for="reconsent-sign-password">Security PIN / Password</label>
            <input
              id="reconsent-sign-password"
              type="password"
              v-model="state.reconsentForm.password"
              placeholder="••••••••"
              required
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-reconsent-cancel"
            type="button"
            class="btn btn-secondary"
            @click="closeReconsentModal"
          >
            Cancel
          </button>
          <button
            id="btn-reconsent-submit"
            type="button"
            class="btn btn-primary"
            @click="submitReconsentSignature"
          >
            Sign and Confirm Re-Consent
          </button>
        </div>
      </div>
    </div>

    <!-- Local Security PIN Setup Modal -->
    <div
      v-if="state.pinSetup.isOpen"
      id="portal-pin-setup-modal"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="pin-setup-title"
      tabindex="-1"
      @keydown="handleSetupModalKeyDown"
    >
      <div class="modal">
        <div id="pin-setup-title" class="modal-header">
          Configure Security PIN
        </div>
        <div class="modal-body">
          <p>
            To secure your offline clinical data, please choose a numeric local
            security PIN. This PIN will wrap and encrypt your offline database
            encryption key.
          </p>
          <div
            v-if="state.pinSetup.error"
            class="error-state"
            style="
              color: var(--danger);
              font-weight: bold;
              font-size: 14px;
              margin-bottom: 12px;
            "
            role="status"
            aria-live="polite"
          >
            {{ state.pinSetup.error }}
          </div>
          <div class="form-group mb-12">
            <label for="setup-pin">Choose Numeric PIN</label>
            <input
              id="setup-pin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              v-model="state.pinSetup.pin"
              required
            />
          </div>
          <div class="form-group">
            <label for="confirm-setup-pin">Confirm Numeric PIN</label>
            <input
              id="confirm-setup-pin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              v-model="state.pinSetup.confirmPin"
              required
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-pin-setup-confirm"
            type="button"
            class="btn btn-primary"
            @click="handlePINSetupSubmit"
          >
            Save and Configure
          </button>
        </div>
      </div>
    </div>

    <!-- Local Security PIN Unlock Modal -->
    <div
      v-if="state.pinUnlock.isOpen"
      id="portal-pin-unlock-modal"
      class="modal-overlay"
      role="dialog"
      aria-modal="true"
      aria-labelledby="pin-unlock-title"
      tabindex="-1"
      @keydown="handleUnlockModalKeyDown"
    >
      <div class="modal">
        <div id="pin-unlock-title" class="modal-header">
          Device Locked - Enter Security PIN
        </div>
        <div class="modal-body">
          <p>
            Please enter your numeric security PIN to access the clinical portal
            and decrypt your offline data.
          </p>
          <div
            v-if="state.pinUnlock.error"
            id="pin-unlock-error"
            class="error-state"
            style="
              color: var(--danger);
              font-weight: bold;
              font-size: 14px;
              margin-bottom: 12px;
            "
            role="status"
            aria-live="polite"
          >
            {{ state.pinUnlock.error }}
          </div>
          <div class="form-group">
            <label for="unlock-pin">Security PIN</label>
            <input
              id="unlock-pin"
              type="password"
              inputmode="numeric"
              pattern="[0-9]*"
              placeholder="••••"
              v-model="state.pinUnlock.pin"
              required
              @keydown.enter="handlePINUnlockSubmit"
            />
          </div>
        </div>
        <div class="modal-footer">
          <button
            id="btn-pin-unlock-confirm"
            type="button"
            class="btn btn-primary"
            @click="handlePINUnlockSubmit"
          >
            Unlock
          </button>
        </div>
      </div>
    </div>

    <!-- Activity log at the bottom for patient transparency & 21 CFR compliance review -->
    <footer class="portal-footer">
      <div class="footer-title">
        My Digital Activity Audit Log (21 CFR Part 11 Compliant)
      </div>
      <div id="portal-ledger-timeline" class="ledger-timeline">
        <!-- Ledger records shown here -->
      </div>
    </footer>
  </div>
</template>

<script setup>
import { onMounted, nextTick, computed } from "vue";
import {
  state,
  showView,
  logout,
  syncOfflineQueue,
  handlePINSetupSubmit,
  handlePINUnlockSubmit,
  openReconsentModal,
  closeReconsentModal,
  submitReconsentSignature,
} from "./index.js";

function handleReconsentModalKeyDown(e) {
  if (e.key !== "Tab") return;
  const modal = document.getElementById("portal-reconsent-modal");
  if (!modal) return;
  const selectors = ["input", "select", "textarea", "button"]
    .map((tag) => `${tag}:not([disabled])`)
    .join(", ");
  const elList = Array.from(modal.querySelectorAll(selectors));
  if (!elList.length) return;
  const firstEl = elList[0];
  const lastEl = elList[elList.length - 1];
  if (e.shiftKey) {
    if (document.activeElement === firstEl) {
      lastEl.focus();
      e.preventDefault();
    }
  } else {
    if (document.activeElement === lastEl) {
      firstEl.focus();
      e.preventDefault();
    }
  }
}

function handleSetupModalKeyDown(e) {
  if (e.key !== "Tab") return;
  const modal = document.getElementById("portal-pin-setup-modal");
  if (!modal) return;
  const selectors = ["input", "button"]
    .map((tag) => `${tag}:not([disabled])`)
    .join(", ");
  const elList = Array.from(modal.querySelectorAll(selectors));
  if (!elList.length) return;
  const firstEl = elList[0];
  const lastEl = elList[elList.length - 1];
  if (e.shiftKey) {
    if (document.activeElement === firstEl) {
      lastEl.focus();
      e.preventDefault();
    }
  } else {
    if (document.activeElement === lastEl) {
      firstEl.focus();
      e.preventDefault();
    }
  }
}

function handleUnlockModalKeyDown(e) {
  if (e.key !== "Tab") return;
  const modal = document.getElementById("portal-pin-unlock-modal");
  if (!modal) return;
  const selectors = ["input", "button"]
    .map((tag) => `${tag}:not([disabled])`)
    .join(", ");
  const elList = Array.from(modal.querySelectorAll(selectors));
  if (!elList.length) return;
  const firstEl = elList[0];
  const lastEl = elList[elList.length - 1];
  if (e.shiftKey) {
    if (document.activeElement === firstEl) {
      lastEl.focus();
      e.preventDefault();
    }
  } else {
    if (document.activeElement === lastEl) {
      firstEl.focus();
      e.preventDefault();
    }
  }
}

const brandName = computed(() => import.meta.env.VITE_BRAND_NAME || "Cadence");

function handleLogout() {
  logout();
}

function toggleSyncDrawer() {
  state.isSyncDrawerOpen = !state.isSyncDrawerOpen;
}

function triggerManualSync() {
  syncOfflineQueue();
}

const queuedSubmissionsCount = computed(() => {
  if (!state.submissions) return 0;
  return state.submissions.filter((s) => s.status === "QUEUED").length;
});

const isSyncFailed = computed(() => {
  return (
    state.syncStatusText &&
    state.syncStatusText.toLowerCase().includes("failed")
  );
});

function getInstrumentName(diaryId) {
  const fallback = {
    inst_daily_diary: "Daily Health & Vital Diary",
    inst_weekly_symptoms: "Weekly Symptoms & eCOA Checklist",
  };
  return (
    (state.instruments && state.instruments[diaryId]?.name) ||
    fallback[diaryId] ||
    diaryId
  );
}

function formatTime(isoString) {
  if (!isoString) return "";
  return new Date(isoString).toLocaleString();
}

function getSubmissionClass(item) {
  return {
    "submission-queued": item.status === "QUEUED",
    "submission-synced":
      item.status === "CREATED" || item.status === "UPDATED_CLIENT_WINS",
    "submission-merged": item.status === "MERGED",
    "submission-ignored": item.status === "IGNORED_SERVER_WINS",
    "submission-quarantined": item.status === "QUARANTINED",
    "submission-error":
      item.status === "DECRYPTION_ERROR" || item.status === "QUARANTINED",
  };
}

function getBadgeClass(item) {
  if (item.status === "QUEUED") return "pending";
  if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS" ||
    item.status === "MERGED"
  )
    return "completed";
  if (
    item.status === "IGNORED_SERVER_WINS" ||
    item.status === "DECRYPTION_ERROR" ||
    item.status === "QUARANTINED"
  )
    return "overdue";
  return "pending";
}

function getStatusLabel(item) {
  if (item.status === "QUEUED") return "QUEUED";
  if (item.status === "CREATED" || item.status === "UPDATED_CLIENT_WINS")
    return "SYNCED";
  if (item.status === "MERGED") return "MERGED";
  if (item.status === "IGNORED_SERVER_WINS") return "CONFLICT (Ignored)";
  if (item.status === "QUARANTINED") return "QUARANTINED";
  return item.status;
}

function getStatusDescription(item) {
  if (item.status === "QUEUED") {
    return "Waiting for network connection...";
  } else if (
    item.status === "CREATED" ||
    item.status === "UPDATED_CLIENT_WINS"
  ) {
    return "Successfully synchronized with clinical database.";
  } else if (item.status === "MERGED") {
    return "Conflict resolved: Local and server entries were combined.";
  } else if (item.status === "IGNORED_SERVER_WINS") {
    return "Conflict resolved: Server data was preserved; local entry archived.";
  } else if (item.status === "QUARANTINED") {
    return "Quarantined: Under review by clinical trial managers due to validation/version mismatch errors.";
  } else if (item.status === "DECRYPTION_ERROR") {
    return item.error || "Decryption failed: Secure key cleared.";
  }
  return "";
}

const tabs = [
  { id: "tasks", label: "My Tasks", icon: "📋", viewId: "view-tasks" },
  {
    id: "compliance",
    label: "My Compliance",
    icon: "📈",
    viewId: "view-compliance",
  },
  { id: "inbox", label: "My Inbox", icon: "✉️", viewId: "view-inbox" },
  { id: "consent", label: "My Consent", icon: "✍️", viewId: "view-consent" },
];

const tabRefs = {};

function handleTabKeyDown(event) {
  const currentTabId = tabs.find((t) => t.viewId === state.currentView)?.id;
  if (!currentTabId) return;

  const currentIndex = tabs.findIndex((t) => t.id === currentTabId);
  let nextIndex;

  if (event.key === "ArrowRight" || event.key === "ArrowDown") {
    nextIndex = (currentIndex + 1) % tabs.length;
    event.preventDefault();
  } else if (event.key === "ArrowLeft" || event.key === "ArrowUp") {
    nextIndex = (currentIndex - 1 + tabs.length) % tabs.length;
    event.preventDefault();
  } else {
    return;
  }

  const nextTab = tabs[nextIndex];
  selectView(nextTab.viewId);

  // Focus the newly active tab button
  nextTick(() => {
    const btn = tabRefs[nextTab.id];
    if (btn) btn.focus();
  });
}

function selectView(viewId) {
  showView(viewId);
}

function handleModalKeyDown(e) {
  if (e.key !== "Tab") return;
  const modal = document.getElementById("portal-sign-modal");
  if (!modal) return;

  const selectors = ["input", "select", "textarea", "button"]
    .map((tag) => `${tag}:not([disabled])`)
    .join(", ");
  const elList = Array.from(modal.querySelectorAll(selectors));
  if (!elList.length) return;

  const firstEl = elList[0];
  const lastEl = elList[elList.length - 1];

  if (e.shiftKey) {
    if (document.activeElement === firstEl) {
      lastEl.focus();
      e.preventDefault();
    }
  } else {
    if (document.activeElement === lastEl) {
      firstEl.focus();
      e.preventDefault();
    }
  }
}

onMounted(() => {
  // Wait for DOM
});
</script>
