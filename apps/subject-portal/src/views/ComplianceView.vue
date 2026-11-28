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

    <ClinicalLiveRegion
      id="compliance-live-announcer"
      persistent-id
      variant="sr-only"
      :polite-message="compliancePoliteStatus"
      :assertive-message="complianceAssertiveAlert"
    />

    <!-- Compliance Loading Placeholder -->
    <div
      id="compliance-loading"
      class="loading-state"
      role="status"
      aria-live="polite"
      aria-atomic="true"
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
      <ClinicalLiveRegion
        id="compliance-failure-alert"
        persistent-id
        variant="inline"
        politeness="assertive"
        tone="error"
        :active="true"
        container-class="portal-failure-region"
      >
        <span class="portal-failure-copy">
          <span class="compliance-failure-title">
            Failed to load compliance data.
          </span>
          <span id="compliance-error-msg" class="compliance-failure-msg">
            Unknown error
          </span>
        </span>
        <template #actions>
          <button
            id="btn-retry-compliance"
            type="button"
            class="btn btn-primary portal-retry-btn"
          >
            Retry
          </button>
        </template>
      </ClinicalLiveRegion>
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
          :loading="complianceStore.complianceLoading"
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
import { ClinicalDataTable, ClinicalLiveRegion, useStatusAnnouncer } from "ui";
import { openCompliancePeek } from "../features/peek.js";
import { isScreenAvailable } from "../router/availability.js";
import { useComplianceStore } from "../stores/compliance.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const complianceStore = useComplianceStore();

const complianceStatusSummary = computed(() => {
  if (complianceStore.complianceLoading) {
    return "Loading compliance data...";
  }
  if (complianceStore.complianceError) {
    return "";
  }
  const count = Array.isArray(complianceStore.complianceHistory)
    ? complianceStore.complianceHistory.length
    : 0;
  return count === 0
    ? "No survey history found."
    : `Loaded ${count} survey history ${count === 1 ? "record" : "records"}.`;
});

const complianceErrorSummary = computed(() => {
  if (!complianceStore.complianceError) return "";
  return `Failed to load compliance data. ${complianceStore.complianceError}`;
});

const {
  politeMessage: compliancePoliteStatus,
  assertiveMessage: complianceAssertiveAlert,
} = useStatusAnnouncer({
  debounceMs: 80,
  statusSource: () => complianceStatusSummary.value,
  errorSource: () => complianceErrorSummary.value,
});

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
#compliance-rate-pct,
#compliance-completed-count,
#compliance-pending-count,
#compliance-overdue-count {
  font-variant-numeric: tabular-nums;
}

.history-card {
  min-width: 0;
}

.portal-failure-region {
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  gap: 12px;
  width: 100%;
}

.portal-failure-copy {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.compliance-failure-title {
  color: var(--danger);
  font-weight: 700;
  font-size: 16px;
}

.compliance-failure-msg {
  font-size: 14px;
}

.portal-retry-btn {
  min-height: 48px;
  min-width: 120px;
}

.portal-failure-region :deep(.clinical-live-region__actions) {
  margin-left: 0;
  justify-content: center;
}
</style>
