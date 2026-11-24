// 3. My Compliance: rate, counts and survey history.
import { createSrOnlyActionButton } from "ui";
import { fetchCompliance, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import { demoNowMs, isDemoModeActive } from "../utils/demoMode.js";
import { openCompliancePeek } from "./peek.js";

// 3. My Compliance
export function renderCompliance() {
  const loadingEl = document.getElementById("compliance-loading");
  const failureEl = document.getElementById("compliance-failure");
  const errorMsgEl = document.getElementById("compliance-error-msg");
  const gridLayoutEl = document.querySelector("#view-compliance .grid-layout");

  if (loadingEl) loadingEl.style.display = "none";
  if (failureEl) failureEl.style.display = "none";
  if (gridLayoutEl) gridLayoutEl.style.display = "none";

  if (state.complianceLoading) {
    state.complianceHistory = [];
    if (loadingEl) loadingEl.style.display = "block";
    return;
  }

  if (state.complianceError) {
    state.complianceHistory = [];
    if (failureEl) {
      failureEl.style.display = "block";
      if (errorMsgEl) errorMsgEl.textContent = state.complianceError;
    }
    return;
  }

  if (
    !isAuthenticatedSession() &&
    !isDemoModeActive() &&
    !state.session.userId &&
    state.assignments.length === 0
  ) {
    state.complianceHistory = [];
    if (failureEl) {
      failureEl.style.display = "block";
      if (errorMsgEl) {
        errorMsgEl.textContent =
          "Sign in to view your study compliance and submission history.";
      }
    }
    return;
  }

  if (gridLayoutEl) {
    gridLayoutEl.style.display = "grid";
  }

  let rate;
  let completed;
  let pending;
  let overdue;
  let historyList;

  if (isAuthenticatedSession() && state.compliance) {
    rate = Math.round(state.compliance.compliance_rate);
    completed = state.compliance.completed_count;
    pending = state.compliance.pending_count;
    overdue = state.compliance.overdue_count;
    historyList = state.compliance.assignments || [];
  } else if (isAuthenticatedSession() && !state.compliance) {
    state.complianceHistory = [];
    if (failureEl) {
      failureEl.style.display = "block";
      if (gridLayoutEl) gridLayoutEl.style.display = "none";
      if (errorMsgEl) errorMsgEl.textContent = "No compliance data loaded.";
    }
    return;
  } else {
    // Offline cached or explicit demo mode: compute from state.assignments
    const total = state.assignments.length;
    completed = state.assignments.filter((a) => {
      return a.status === "COMPLETED" || a.version_index > 1;
    }).length;
    const pendingSyncCount = state.assignments.filter((a) => {
      return a.status === "PENDING_SYNC";
    }).length;
    const referenceNow = new Date(demoNowMs(0));
    overdue = state.assignments.filter((a) => {
      const isCompleted = a.status === "COMPLETED" || a.version_index > 1;
      const isPendingSync = a.status === "PENDING_SYNC";
      return (
        !isCompleted &&
        !isPendingSync &&
        (a.status === "OVERDUE" ||
          (a.due_at && new Date(a.due_at) < referenceNow))
      );
    }).length;
    pending = Math.max(0, total - completed - overdue);
    rate = total > 0 ? Math.round((completed / total) * 100) : 0;
    void pendingSyncCount;

    historyList = state.assignments.map((item) => {
      const isCompleted = item.status === "COMPLETED" || item.version_index > 1;
      const isPendingSync = item.status === "PENDING_SYNC";
      const isOverdue =
        !isCompleted &&
        !isPendingSync &&
        (item.status === "OVERDUE" ||
          (item.due_at && new Date(item.due_at) < referenceNow));
      const status = isCompleted
        ? "COMPLETED"
        : isPendingSync
          ? "PENDING_SYNC"
          : isOverdue
            ? "OVERDUE"
            : "PENDING";
      return {
        id: item.id,
        instrument_id: item.instrument_id,
        instrument_name: item.instrument_name,
        due_at: item.due_at,
        end_date: item.end_date,
        submitted_at: item.submitted_at,
        queued_at: item.queued_at,
        status: status,
      };
    });
  }

  // Update DOM metrics
  const pctEl = document.getElementById("compliance-rate-pct");
  const completedEl = document.getElementById("compliance-completed-count");
  const pendingEl = document.getElementById("compliance-pending-count");
  const overdueEl = document.getElementById("compliance-overdue-count");

  if (pctEl) pctEl.textContent = `${rate}%`;
  if (completedEl) completedEl.textContent = completed;
  if (pendingEl) pendingEl.textContent = pending;
  if (overdueEl) overdueEl.textContent = overdue;

  // Sort assignments chronologically by due_at or end_date and enrich rows for ClinicalDataTable
  const sorted = historyList
    .slice()
    .sort((a, b) => new Date(b.due_at) - new Date(a.due_at))
    .map((item, rowIndex) => {
      const scheduledText = item.due_at
        ? new Date(item.due_at).toLocaleDateString()
        : "—";
      const submittedText = item.submitted_at
        ? new Date(item.submitted_at).toLocaleString()
        : item.queued_at
          ? `${new Date(item.queued_at).toLocaleString()} (Queued)`
          : "—";

      let pillClass = "pending";
      let statusLabel = "Pending";
      if (item.status === "COMPLETED") {
        pillClass = "completed";
        statusLabel = "Completed";
      } else if (item.status === "PENDING_SYNC") {
        pillClass = "pending";
        statusLabel = "Pending Sync";
      } else if (
        item.status === "OVERDUE" ||
        (item.due_at && new Date(item.due_at) < new Date(demoNowMs(0)))
      ) {
        pillClass = "overdue";
        statusLabel = "Overdue";
      }

      const instName =
        item.instrument_name ||
        (state.instruments && state.instruments[item.instrument_id]?.name) ||
        "Assigned Instrument";

      return {
        ...item,
        rowIndex,
        instName,
        scheduledText,
        submittedText,
        pillClass,
        statusLabel,
      };
    });

  state.complianceHistory = sorted;

  // Update standalone JSDOM History Table fallback when not managed by Vue ClinicalDataTable
  const tbody = document.getElementById("compliance-history-tbody");
  if (!tbody || tbody.hasAttribute("data-vue-managed")) return;

  if (sorted.length === 0) {
    tbody.innerHTML = `<tr><td colspan="4" class="no-data">No history found.</td></tr>`;
    return;
  }

  tbody.innerHTML = sorted
    .map((item, rowIndex) => {
      const peekBtnHtml = createSrOnlyActionButton({
        id: `btn-peek-compliance-${rowIndex}`,
        label: "View Details",
        srContext: item.instName,
        className: "btn btn-secondary btn-peek-compliance",
        dataId: String(rowIndex),
      });

      return `
        <tr id="compliance-row-${rowIndex}" data-row-index="${rowIndex}" style="cursor: pointer;">
          <td>
            <div class="compliance-row-title">
              <strong>${item.instName}</strong>
              ${peekBtnHtml}
            </div>
          </td>
          <td class="tabular-nums">${item.scheduledText}</td>
          <td class="tabular-nums">${item.submittedText}</td>
          <td><span class="status-pill ${item.pillClass}">${item.statusLabel}</span></td>
        </tr>
      `;
    })
    .join("");

  tbody.querySelectorAll(".btn-peek-compliance").forEach((btn) => {
    btn.addEventListener("click", (event) => {
      event.stopPropagation();
      const rowIdx = Number(btn.getAttribute("data-id"));
      const target = Number.isFinite(rowIdx) ? sorted[rowIdx] : null;
      if (target) {
        openCompliancePeek(target, rowIdx, btn);
      }
    });
  });

  tbody.querySelectorAll("tr[data-row-index]").forEach((rowEl) => {
    rowEl.addEventListener("click", (event) => {
      if (event.target?.closest?.("button, a")) return;
      const rowIdx = Number(rowEl.getAttribute("data-row-index"));
      const target = Number.isFinite(rowIdx) ? sorted[rowIdx] : null;
      const triggerBtn = rowEl.querySelector(".btn-peek-compliance") || rowEl;
      if (target) {
        openCompliancePeek(target, rowIdx, triggerBtn);
      }
    });
  });
}

// Loads the compliance overview from the gateway.
export async function loadCompliance(userId) {
  state.complianceLoading = true;
  state.complianceError = null;
  try {
    state.compliance = await fetchCompliance(userId);
  } catch (err) {
    state.complianceError = err.message || err;
    state.compliance = null;
  } finally {
    state.complianceLoading = false;
  }
}

// Retry button on the compliance failure state.
export async function retryCompliance() {
  state.complianceLoading = true;
  state.complianceError = null;
  renderCompliance();
  await loadCompliance(state.session.userId);
  renderCompliance();
}
