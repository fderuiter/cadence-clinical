<template>
  <div id="app">
    <!-- Portal Header -->
    <header class="portal-header">
      <div class="header-branding">
        <h1>My <span>Cadence</span></h1>
        <p class="role-badge">Participant Companion Portal</p>
      </div>
      <div class="compliance-badges">
        <span class="badge badge-epro">ePRO Companion</span>
        <span class="badge badge-gxp">21 CFR Part 11</span>
        <span class="badge badge-gamp">GAMP 5</span>
      </div>
    </header>

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
        </div>
      </nav>

      <!-- Main Viewport -->
      <main class="portal-main">
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
    <div
      id="portal-sign-modal"
      class="modal-overlay"
      style="display: none"
      role="dialog"
      aria-modal="true"
      aria-labelledby="portal-modal-title"
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
          />

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
            />
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
import { onMounted, nextTick } from "vue";
import { state, showView } from "./index.js";

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

  const selectors = ["input", "select", "textarea", "button"].map(tag => `${tag}:not([disabled])`).join(", ");
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
