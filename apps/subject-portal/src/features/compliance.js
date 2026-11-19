// 3. My Compliance: rate, counts and survey history.
import { fetchCompliance, isAuthenticatedSession } from "../api/client.js";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";

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
    if (loadingEl) loadingEl.style.display = "block";
    return;
  }

  if (state.complianceError) {
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
    overdue = state.assignments.filter((a) => {
      const isCompleted = a.status === "COMPLETED" || a.version_index > 1;
      const isPendingSync = a.status === "PENDING_SYNC";
      return (
        !isCompleted &&
        !isPendingSync &&
        (a.status === "OVERDUE" ||
          (a.due_at && new Date(a.due_at) < new Date()))
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
          (item.due_at && new Date(item.due_at) < new Date()));
      const status = isCompleted
        ? "COMPLETED"
        : isPendingSync
          ? "PENDING_SYNC"
          : isOverdue
            ? "OVERDUE"
            : "PENDING";
      return {
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

  // Update History Table
  const tbody = document.getElementById("compliance-history-tbody");
  if (!tbody) return;

  if (historyList.length === 0) {
    tbody.innerHTML = `<tr><td colspan="4" class="no-data">No history found.</td></tr>`;
    return;
  }

  // Sort assignments chronologically by due_at or end_date
  const sorted = historyList
    .slice()
    .sort((a, b) => new Date(b.due_at) - new Date(a.due_at));

  tbody.innerHTML = sorted
    .map((item) => {
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
        (item.due_at && new Date(item.due_at) < new Date())
      ) {
        pillClass = "overdue";
        statusLabel = "Overdue";
      }

      const instName =
        item.instrument_name ||
        (state.instruments && state.instruments[item.instrument_id]?.name) ||
        "Assigned Instrument";

      return `
        <tr>
          <td><strong>${instName}</strong></td>
          <td>${scheduledText}</td>
          <td>${submittedText}</td>
          <td><span class="status-pill ${pillClass}">${statusLabel}</span></td>
        </tr>
      `;
    })
    .join("");
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
