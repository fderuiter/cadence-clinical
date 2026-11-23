<template>
  <section
    id="view-compliance"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-compliance' }"
    :hidden="ui.currentView !== 'view-compliance'"
    :inert="ui.currentView !== 'view-compliance' || undefined"
    :role="hasComplianceTab ? 'tabpanel' : 'region'"
    :aria-labelledby="hasComplianceTab ? 'btn-tab-btn-compliance' : undefined"
    :aria-label="hasComplianceTab ? undefined : 'My Compliance Overview'"
    tabindex="0"
  >
    <div class="view-header">
      <h2>My Compliance Overview</h2>
      <p>
        Track your scheduled survey progress and overall trial participation
        history.
      </p>
    </div>

    <!-- Compliance Loading Placeholder -->
    <div id="compliance-loading" class="loading-state" style="display: none">
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
      <p
        style="
          color: var(--danger);
          font-weight: bold;
          font-size: var(--font-size-md, 1rem);
        "
      >
        Failed to load compliance data.
      </p>
      <p
        id="compliance-error-msg"
        style="font-size: var(--font-size-sm, 0.875rem); margin-bottom: 12px"
      >
        Unknown error
      </p>
      <button id="btn-retry-compliance" type="button" class="btn btn-primary">
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
            <strong id="compliance-completed-count" class="val text-success"
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
</template>

<script setup>
import { computed } from "vue";
import { isScreenAvailable } from "../router/availability.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();

const hasComplianceTab = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemoModeActive() || isScreenAvailable("compliance");
});
</script>
