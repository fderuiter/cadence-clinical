// 1. My Tasks: the assigned questionnaires list, its loading and retry.
import {
  fetchAssignedInstruments,
  fetchAssignments,
  isAuthenticatedSession,
} from "../api/client.js";
import { state } from "../stores/state.js";
import {
  getAssignmentsFromDB,
  getInstrumentsFromDB,
  saveAssignmentsToDB,
  saveInstrumentsToDB,
} from "../sync-queue.js";
import { startQuestionnaire } from "./questionnaire.js";

// 1. My Tasks (Assigned Surveys)
export function renderTasks() {
  const loadingEl = document.getElementById("tasks-loading");
  const failureEl = document.getElementById("tasks-failure");
  const container = document.getElementById("tasks-list-container");
  const errorMsgEl = document.getElementById("tasks-error-msg");

  if (loadingEl) loadingEl.style.display = "none";
  if (failureEl) failureEl.style.display = "none";
  if (!container) return;
  container.style.display = "none";

  if (state.tasksLoading) {
    if (loadingEl) loadingEl.style.display = "block";
    return;
  }

  if (state.tasksError || state.assignmentsError) {
    if (failureEl) {
      failureEl.style.display = "block";
      if (errorMsgEl)
        errorMsgEl.textContent =
          state.tasksError ||
          "We are unable to connect to the study servers right now.";
    }
    // Populate container with error content so tests can inspect innerHTML,
    // but keep it visually hidden behind the failure overlay.
    if (container) {
      container.style.display = "none";
      container.innerHTML = `
        <div class="card" style="text-align: center; padding: 32px; color: var(--danger);">
          <p style="font-size: 18px; font-weight: 700; margin-bottom: 6px;">⚠️ Error loading tasks</p>
          <p style="font-size: 14px;">We are unable to connect to the study servers right now. Please check your connection and try again.</p>
        </div>
      `;
    }
    return;
  }

  if (container) {
    container.style.display = "block";
  }

  // To support both authenticated and unauthenticated mappings cleanly:
  const activeTasks = state.assignments.filter((task) => {
    const isCompleted = task.status === "COMPLETED" || task.version_index > 1;
    return !isCompleted;
  });

  if (activeTasks.length === 0) {
    container.innerHTML = `
      <div class="card" style="text-align: center; padding: 32px; color: var(--text-muted);">
        <p style="font-size: 18px; font-weight: 700; margin-bottom: 6px;">🎉 All caught up!</p>
        <p style="font-size: 14px;">You have no pending clinical questionnaires due today.</p>
      </div>
    `;
    return;
  }

  container.innerHTML = activeTasks
    .map((task) => {
      const instName =
        (state.instruments && state.instruments[task.instrument_id]?.name) ||
        task.instrument_name ||
        "Assigned Instrument";
      const isCompleted = task.status === "COMPLETED" || task.version_index > 1;
      const isOverdue =
        !isCompleted &&
        (task.status === "OVERDUE" ||
          (task.due_at && new Date(task.due_at) < new Date()));
      const statusClass = isOverdue ? "overdue" : "pending";
      const statusLabel = isOverdue ? "Overdue" : "Pending";
      const dueText = task.due_at
        ? new Date(task.due_at).toLocaleString()
        : "—";

      return `
        <div class="task-item" id="task-card-${task.id}">
          <div class="task-meta">
            <span class="task-name">${instName}</span>
            <span class="task-due">Scheduled Due: <strong>${dueText}</strong></span>
          </div>
          <div style="display: flex; align-items: center; gap: 16px;">
            <span class="status-pill ${statusClass}">${statusLabel}</span>
            <button type="button" class="btn btn-primary btn-start-task" data-id="${task.id}">Start Survey</button>
          </div>
        </div>
      `;
    })
    .join("");

  // Attach events
  container.querySelectorAll(".btn-start-task").forEach((btn) => {
    btn.addEventListener("click", () => {
      const taskId = btn.getAttribute("data-id");
      startQuestionnaire(taskId);
    });
  });
}

// Loads the participant's assignments, falling back to the IndexedDB cache.
export async function loadAssignedTasks(userId) {
  // Fetch Assignments/Tasks
  state.tasksLoading = true;
  state.tasksError = null;
  try {
    state.assignments = await fetchAssignments(userId);
    state.assignmentsError = false;
    try {
      await saveAssignmentsToDB(state.assignments);
    } catch (dbErr) {
      console.warn("Could not cache assignments to IndexedDB:", dbErr);
    }
  } catch (err) {
    state.tasksError = err.message || err;
    state.assignments = [];
    state.assignmentsError = true;
    try {
      const cachedAssignments = await getAssignmentsFromDB();
      if (cachedAssignments && cachedAssignments.length > 0) {
        state.assignments = cachedAssignments;
        state.tasksError = null;
        state.assignmentsError = false;
      }
    } catch (dbErr) {
      console.warn("Could not read assignments from IndexedDB:", dbErr);
    }
  } finally {
    state.tasksLoading = false;
  }
}

// Loads the assigned instrument definitions, falling back to the cache.
export async function loadAssignedInstruments(userId) {
  // Fetch Assigned Instruments
  state.instrumentsLoading = true;
  state.instrumentsError = null;
  try {
    const insts = await fetchAssignedInstruments(userId);
    state.instruments = {};
    insts.forEach((inst) => {
      state.instruments[inst.id] = inst;
    });
    try {
      await saveInstrumentsToDB(insts);
    } catch (dbErr) {
      console.warn("Could not cache instruments to IndexedDB:", dbErr);
    }
  } catch (err) {
    state.instrumentsError = err.message || err;
    state.instruments = {};
    try {
      const cachedInstruments = await getInstrumentsFromDB();
      if (cachedInstruments && cachedInstruments.length > 0) {
        cachedInstruments.forEach((inst) => {
          state.instruments[inst.id] = inst;
        });
        state.instrumentsError = null;
      }
    } catch (dbErr) {
      console.warn("Could not read instruments from IndexedDB:", dbErr);
    }
  } finally {
    state.instrumentsLoading = false;
  }
}

// Retry button on the task list failure state.
export async function retryTasks() {
  state.tasksLoading = true;
  state.tasksError = null;
  state.assignmentsError = false;
  renderTasks();
  try {
    state.assignments = await fetchAssignments(state.session.userId);
    state.assignmentsError = false;
    try {
      await saveAssignmentsToDB(state.assignments);
    } catch (dbErr) {
      console.warn("Could not cache assignments to IndexedDB:", dbErr);
    }
    if (isAuthenticatedSession()) {
      try {
        const insts = await fetchAssignedInstruments(state.session.userId);
        state.instruments = {};
        insts.forEach((inst) => {
          state.instruments[inst.id] = inst;
        });
        try {
          await saveInstrumentsToDB(insts);
        } catch (dbErr) {
          console.warn("Could not cache instruments to IndexedDB:", dbErr);
        }
      } catch (e) {
        console.warn("Could not refetch instruments during retry:", e);
      }
    }
  } catch (err) {
    state.tasksError = err.message || err;
    state.assignmentsError = true;
    state.assignments = [];
    try {
      const cachedAssignments = await getAssignmentsFromDB();
      if (cachedAssignments && cachedAssignments.length > 0) {
        state.assignments = cachedAssignments;
        state.tasksError = null;
        state.assignmentsError = false;
      }
    } catch (dbErr) {
      console.warn("Could not read assignments from IndexedDB:", dbErr);
    }
    try {
      const cachedInstruments = await getInstrumentsFromDB();
      if (cachedInstruments && cachedInstruments.length > 0) {
        cachedInstruments.forEach((inst) => {
          state.instruments[inst.id] = inst;
        });
      }
    } catch (dbErr) {
      console.warn("Could not read instruments from IndexedDB:", dbErr);
    }
  } finally {
    state.tasksLoading = false;
    renderTasks();
  }
}
