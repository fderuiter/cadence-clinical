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
      <h2 id="compliance-view-heading" tabindex="-1">My Compliance Overview</h2>
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
      <p style="color: var(--danger); font-weight: bold; font-size: 16px">
        Failed to load compliance data.
      </p>
      <p id="compliance-error-msg" style="font-size: 14px; margin-bottom: 12px">
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
          <div id="compliance-rate-pct" class="score-value tabular-nums">
            0%
          </div>
          <div class="score-lbl">Compliance Rate</div>
        </div>
        <div class="score-breakdown">
          <div class="breakdown-item">
            <span class="lbl">Completed:</span>
            <strong
              id="compliance-completed-count"
              class="val text-success tabular-nums"
              >0</strong
            >
          </div>
          <div class="breakdown-item">
            <span class="lbl">Pending:</span>
            <strong
              id="compliance-pending-count"
              class="val text-warning tabular-nums"
              >0</strong
            >
          </div>
          <div class="breakdown-item">
            <span class="lbl">Overdue:</span>
            <strong
              id="compliance-overdue-count"
              class="val text-danger tabular-nums"
              >0</strong
            >
          </div>
        </div>
      </div>

      <!-- History Card -->
      <div class="card history-card">
        <h3 id="compliance-history-heading">My Survey History</h3>
        <ClinicalDataTable
          title="My Survey History"
          labelled-by="compliance-history-heading"
          hide-header
          plain
          wrapper-class="table-responsive"
          table-class="compliance-history-table"
          tbody-id="compliance-history-tbody"
          :columns="historyColumns"
          :data="complianceStore.complianceHistory"
          :row-key="(row, idx) => row.id ?? `compliance-row-${idx}`"
          :row-id="(_, idx) => `compliance-row-${idx}`"
          :row-attrs="
            (_, idx) => ({
              'data-row-index': String(idx),
              style: 'cursor: pointer;',
            })
          "
          external-pagination
          empty-message="No history found."
          @row-click="onHistoryRowClick"
        >
          <template #cell-instName="{ row, index }">
            <div class="compliance-row-title">
              <strong>{{ row.instName }}</strong>
              <button
                :id="`btn-peek-compliance-${index}`"
                type="button"
                class="btn btn-secondary btn-peek-compliance"
                :data-id="String(index)"
                @click.stop="openPeek(row, index, $event.currentTarget)"
              >
                View Details<span class="sr-only"> for {{ row.instName }}</span>
              </button>
            </div>
          </template>
          <template #cell-statusLabel="{ row }">
            <span :class="['status-pill', row.pillClass]">{{
              row.statusLabel
            }}</span>
          </template>
        </ClinicalDataTable>
      </div>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalDataTable } from "ui";
import { openCompliancePeek } from "../features/peek.js";
import { isScreenAvailable } from "../router/availability.js";
import { useComplianceStore } from "../stores/compliance.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const complianceStore = useComplianceStore();

const historyColumns = [
  { key: "instName", label: "Questionnaire" },
  {
    key: "scheduledText",
    label: "Scheduled Date",
    cellClass: "tabular-nums",
  },
  {
    key: "submittedText",
    label: "Submitted Date",
    cellClass: "tabular-nums",
  },
  { key: "statusLabel", label: "Status" },
];

function openPeek(row, index, triggerEl) {
  if (!row) return;
  openCompliancePeek(row, index, triggerEl);
}

function onHistoryRowClick(row) {
  if (!row) return;
  const idx = Number.isFinite(row.rowIndex) ? row.rowIndex : 0;
  const triggerEl =
    document.getElementById(`btn-peek-compliance-${idx}`) ||
    document.getElementById(`compliance-row-${idx}`);
  openCompliancePeek(row, idx, triggerEl);
}

const hasComplianceTab = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemoModeActive() || isScreenAvailable("compliance");
});
</script>

<style scoped>
.history-card {
  min-width: 0;
}
</style>
