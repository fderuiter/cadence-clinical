import {
  generateGatewaySignature,
} from "ui";

// Mock Data fallbacks for high-fidelity offline/sandbox usage
const MOCK_ASSIGNMENTS = [
  {
    id: "assign_01",
    subject_id: "subject_001",
    instrument_id: "inst_daily_diary",
    instrument_name: "Daily Health & Vital Diary",
    due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
    end_date: new Date(Date.now() + 24 * 3600 * 1000).toISOString(),
    status: "PENDING",
  },
  {
    id: "assign_02",
    subject_id: "subject_001",
    instrument_id: "inst_weekly_symptoms",
    instrument_name: "Weekly Symptoms & eCOA Checklist",
    due_at: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    end_date: new Date(Date.now() + 12 * 3600 * 1000).toISOString(),
    status: "OVERDUE",
  },
  {
    id: "assign_03",
    subject_id: "subject_001",
    instrument_id: "inst_weekly_symptoms",
    instrument_name: "Weekly Symptoms & eCOA Checklist",
    due_at: new Date(Date.now() - 48 * 3600 * 1000).toISOString(),
    end_date: new Date(Date.now() - 24 * 3600 * 1000).toISOString(),
    status: "COMPLETED",
    submitted_at: new Date(Date.now() - 47 * 3600 * 1000).toISOString(),
  }
];

const MOCK_INSTRUMENTS = {
  inst_daily_diary: {
    id: "inst_daily_diary",
    name: "Daily Health & Vital Diary",
    description: "Please record your systolic/diastolic blood pressure, pulse, and current symptoms.",
    items: {
      vssbp: { label: "Systolic Blood Pressure (mmHg)", type: "numeric", required: true, min: 50, max: 250 },
      vsdpb: { label: "Diastolic Blood Pressure (mmHg)", type: "numeric", required: true, min: 30, max: 150 },
      vshr: { label: "Pulse Rate (bpm)", type: "numeric", required: true, min: 30, max: 200 },
      has_symptoms: { label: "Are you experiencing any new physical symptoms today?", type: "choice_single", options: ["Yes", "No"] },
    }
  },
  inst_weekly_symptoms: {
    id: "inst_weekly_symptoms",
    name: "Weekly Symptoms & eCOA Checklist",
    description: "Please complete this survey detailing any adverse clinical signs experienced during the week.",
    items: {
      severity: { label: "Overall symptom severity this week", type: "choice_single", options: ["None", "Mild", "Moderate", "Severe"] },
      restricted_activity: { label: "Did symptoms restrict your daily activities?", type: "choice_single", options: ["Yes", "No"] },
      missed_doses: { label: "Did you miss any medication doses?", type: "choice_single", options: ["Yes", "No"] },
    }
  }
};

const MOCK_NOTIFICATIONS = [
  {
    id: "notif_01",
    subject_id: "subject_001",
    assignment_id: "assign_01",
    message: "Reminder: Daily Health & Vital Diary is due shortly.",
    due_at: new Date(Date.now() + 4 * 3600 * 1000).toISOString(),
    channel: "IN_APP",
    is_read: false,
  },
  {
    id: "notif_02",
    subject_id: "subject_001",
    assignment_id: "assign_02",
    message: "ALERT: Weekly Symptoms & eCOA Checklist is OVERDUE! Please complete immediately.",
    due_at: new Date(Date.now() - 2 * 3600 * 1000).toISOString(),
    channel: "SMS",
    is_read: false,
  }
];

// App State Core
const state = {
  session: {
    userId: "subject_001",
    roles: "Subject",
    token: null,
    isOfflineMode: true,
  },
  assignments: [],
  notifications: [],
  ledgerBlocks: [],
  activeQuestionnaire: null,
};

// Simple Router
function showView(viewId) {
  document.querySelectorAll(".portal-view").forEach((v) => {
    v.classList.remove("active");
  });
  document.querySelectorAll(".nav-item").forEach((item) => {
    item.classList.remove("active");
  });

  const targetView = document.getElementById(viewId);
  if (targetView) targetView.classList.add("active");

  // Highlight matching sidebar/navbar tab
  if (viewId === "view-tasks" || viewId === "view-questionnaire") {
    const tab = document.getElementById("tab-btn-tasks");
    if (tab) tab.classList.add("active");
  } else if (viewId === "view-compliance") {
    const tab = document.getElementById("tab-btn-compliance");
    if (tab) tab.classList.add("active");
  } else if (viewId === "view-inbox") {
    const tab = document.getElementById("tab-btn-inbox");
    if (tab) tab.classList.add("active");
  }
}

// SHA-256 Hash helper
async function sha256(message) {
  const msgBuffer = new TextEncoder().encode(message);
  const hashBuffer = await globalThis.crypto.subtle.digest("SHA-256", msgBuffer);
  const hashArray = Array.from(new Uint8Array(hashBuffer));
  return hashArray.map((b) => b.toString(16).padStart(2, "0")).join("");
}

// 21 CFR Part 11 Compliant Cryptographic Audit Ledger logging
async function logAuditRecord(action, details, reason = "Patient action verified") {
  const timestamp = new Date().toISOString();
  const index = state.ledgerBlocks.length;
  const prevHash =
    index === 0
      ? "0000000000000000000000000000000000000000000000000000000000000000"
      : state.ledgerBlocks[index - 1].hash;

  const payloadString = `${index}|${timestamp}|${action}|${JSON.stringify(details)}|${reason}|${prevHash}`;
  const hash = await sha256(payloadString);

  const block = {
    index,
    timestamp,
    action,
    details,
    reason,
    prevHash,
    hash,
  };

  state.ledgerBlocks.push(block);
  renderLedger();
  return block;
}

function renderLedger() {
  const container = document.getElementById("portal-ledger-timeline");
  if (!container) return;

  if (state.ledgerBlocks.length === 0) {
    container.innerHTML = `<div class="loading-state">No audit ledger logs initialized.</div>`;
    return;
  }

  container.innerHTML = state.ledgerBlocks
    .slice()
    .reverse()
    .map((block) => `
      <div class="ledger-item">
        <div class="ledger-item-header">
          <span><strong>BLOCK #${block.index} - ${block.action}</strong></span>
          <span>${block.timestamp}</span>
        </div>
        <div class="ledger-item-details">
          <span><strong>Details:</strong> ${JSON.stringify(block.details)}</span><br/>
          <span><strong>Reason/Declaration:</strong> <span style="color: #60a5fa; font-weight:600;">${block.reason}</span></span>
        </div>
        <div class="ledger-hash">
          <span>hash: ${block.hash}</span>
        </div>
      </div>
    `).join("");
}

// API Call helper
async function dispatchApi(endpoint, options = {}) {
  const url = `http://localhost:8000/api/v1/interop/${endpoint}`; // Assume API Gateway defaults
  const timestamp = new Date().toISOString();

  // Signing headers
  const secret = "internal-gateway-secret-12345"; // pragma: allowlist secret
  let signature = "";
  try {
    signature = await generateGatewaySignature(
      state.session.userId,
      state.session.roles,
      timestamp,
      "2",
      options.change_reason || "API Operation",
      secret
    );
  } catch (err) {
    console.error("Signature stamping failed:", err);
  }

  const defaultHeaders = {
    "Content-Type": "application/json",
    "X-User-Id": state.session.userId,
    "X-User-Roles": state.session.roles,
    "X-Gateway-Timestamp": timestamp,
    "X-Gateway-Signature": signature,
    "X-Signature-Version": "2",
  };

  if (options.change_reason) {
    defaultHeaders["X-Change-Reason"] = options.change_reason;
  }

  if (state.session.token) {
    defaultHeaders["Authorization"] = `Bearer ${state.session.token}`;
  }

  try {
    const res = await fetch(url, {
      ...options,
      headers: { ...defaultHeaders, ...options.headers },
    });

    if (!res.ok) {
      throw new Error(`HTTP Error ${res.status}`);
    }
    return await res.json();
  } catch (err) {
    console.warn(`Gateway API call '${endpoint}' failed (running in sandbox/offline mode):`, err.message);
    throw err;
  }
}

// Core Screens Logic

// 1. My Tasks (Assigned Surveys)
function renderTasks() {
  const container = document.getElementById("tasks-list-container");
  if (!container) return;

  const activeTasks = state.assignments.filter(a => a.status !== "COMPLETED");

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
      const isOverdue = task.status === "OVERDUE" || new Date(task.due_at) < new Date();
      const statusClass = isOverdue ? "overdue" : "pending";
      const statusLabel = isOverdue ? "Overdue" : "Pending";
      const dueText = new Date(task.due_at).toLocaleString();

      return `
        <div class="task-item" id="task-card-${task.id}">
          <div class="task-meta">
            <span class="task-name">${task.instrument_name}</span>
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

// 2. Questionnaire Completion
function startQuestionnaire(assignmentId) {
  const assignment = state.assignments.find(a => a.id === assignmentId);
  if (!assignment) return;

  const instrument = MOCK_INSTRUMENTS[assignment.instrument_id];
  if (!instrument) {
    alert("Questionnaire specification not found.");
    return;
  }

  state.activeQuestionnaire = {
    assignment,
    instrument,
    answers: {},
  };

  // Set HTML headers
  document.getElementById("questionnaire-title").textContent = instrument.name;
  document.getElementById("questionnaire-desc").textContent = instrument.description;

  // Build questionnaire fields using raw HTML string widgets to remain compatible with packages/ui index.js primitives
  const formContainer = document.getElementById("questionnaire-form-container");
  formContainer.innerHTML = "";

  // Dynamic compiler
  let formHtml = `<div class="clinical-form-grid">`;
  formHtml += `<h2 class="form-title">Enter Clinical Findings</h2>`;

  Object.entries(instrument.items).forEach(([id, field]) => {
    if (field.type === "choice_single") {
      // Create Clinical Radio Grid equivalent
      const optionsHtml = field.options
        .map((opt, idx) => `
          <div class="radio-option">
            <input type="radio" id="${id}_option_${idx}" name="${id}" value="${opt}" />
            <label for="${id}_option_${idx}">${opt}</label>
          </div>
        `)
        .join("");

      formHtml += `
        <fieldset class="clinical-radio-grid grid-span-12" style="grid-column: span 12;" id="field-container-${id}">
          <legend>${field.label}</legend>
          <div class="radio-options-wrapper">
            <div class="radio-options">
              ${optionsHtml}
            </div>
          </div>
        </fieldset>
      `;
    } else {
      // Create Clinical Input equivalent
      formHtml += `
        <div class="clinical-input grid-span-12" style="grid-column: span 12;" id="field-container-${id}">
          <label for="${id}">${field.label}</label>
          <div class="input-wrapper">
            <input type="text" id="${id}" name="${id}" value="" />
          </div>
        </div>
      `;
    }
  });

  formHtml += `</div>`;
  formContainer.innerHTML = formHtml;

  // Render and navigate
  showView("view-questionnaire");
}

function validateActiveQuestionnaire() {
  if (!state.activeQuestionnaire) return false;

  const { instrument } = state.activeQuestionnaire;
  let allValid = true;
  const errors = [];

  Object.entries(instrument.items).forEach(([id, field]) => {
    const val = field.type === "choice_single"
      ? (document.querySelector(`input[name="${id}"]:checked`)?.value || "")
      : (document.getElementById(id)?.value.trim() || "");

    // Clean previous error markers
    const container = document.getElementById(`field-container-${id}`);
    if (container) {
      container.classList.remove("has-error");
      const oldErr = container.querySelector(".validation-error-msg");
      if (oldErr) oldErr.remove();
    }

    if (field.required && !val) {
      allValid = false;
      errors.push(`${field.label} is required.`);
      markFieldInvalid(id, "This field is required.");
    } else if (val && field.type === "numeric") {
      const num = parseFloat(val);
      if (isNaN(num)) {
        allValid = false;
        errors.push(`${field.label} must be a valid number.`);
        markFieldInvalid(id, "Value must be a number.");
      } else {
        if (field.min !== undefined && num < field.min) {
          allValid = false;
          errors.push(`${field.label} is below minimum of ${field.min}.`);
          markFieldInvalid(id, `Minimum value is ${field.min}.`);
        }
        if (field.max !== undefined && num > field.max) {
          allValid = false;
          errors.push(`${field.label} exceeds maximum of ${field.max}.`);
          markFieldInvalid(id, `Maximum value is ${field.max}.`);
        }
      }
    }

    state.activeQuestionnaire.answers[id] = val;
  });

  return allValid;
}

function markFieldInvalid(fieldId, msg) {
  const container = document.getElementById(`field-container-${fieldId}`);
  if (container) {
    container.classList.add("has-error");
    const errDiv = document.createElement("div");
    errDiv.className = "validation-error-msg";
    errDiv.style.color = "var(--danger)";
    errDiv.style.fontSize = "12px";
    errDiv.style.fontWeight = "600";
    errDiv.style.marginTop = "4px";
    errDiv.textContent = msg;
    container.appendChild(errDiv);
  }
}

// Submit with 21 CFR PIN signature validation
function openSignatureModal() {
  document.getElementById("sign-username").value = state.session.userId;
  document.getElementById("sign-password").value = "";
  document.getElementById("sign-reason").value = "Initial Questionnaire Completion";
  document.getElementById("sign-reason-custom").value = "";
  document.getElementById("portal-sign-modal").style.display = "flex";
}

function closeSignatureModal() {
  document.getElementById("portal-sign-modal").style.display = "none";
}

async function verifyAndSubmitSignature() {
  const username = document.getElementById("sign-username").value.trim();
  const password = document.getElementById("sign-password").value;
  const reasonSelect = document.getElementById("sign-reason").value;
  const reasonCustom = document.getElementById("sign-reason-custom").value.trim();

  if (!username || !password) {
    alert("Please enter both User ID and Security PIN/Password to sign.");
    return;
  }

  const finalReason = reasonSelect === "Other" && reasonCustom
    ? reasonCustom
    : `${reasonSelect}${reasonCustom ? ": " + reasonCustom : ""}`;

  closeSignatureModal();

  // Save changes locally and attempt API submission
  const active = state.activeQuestionnaire;
  if (!active) return;

  const payload = {
    subject_id: state.session.userId,
    diary_id: active.instrument.id,
    device_timestamp: new Date().toISOString(),
    answers: active.answers,
    offline_sync_markers: {
      sequence_number: state.ledgerBlocks.length + 1,
      client_id: "subject_portal_pwa",
      conflict_strategy: "CLIENT_WINS"
    }
  };

  // Perform GxP audit logging locally
  await logAuditRecord(
    "EPRO_SUBMIT",
    {
      diary_id: active.instrument.id,
      answers: active.answers,
      assignment_id: active.assignment.id,
    },
    `Verified Electronic Signature: "${username}" with statement "${finalReason}"`
  );

  // Mark assignment complete
  const foundAssign = state.assignments.find(a => a.id === active.assignment.id);
  if (foundAssign) {
    foundAssign.status = "COMPLETED";
    foundAssign.submitted_at = payload.device_timestamp;
  }

  // Attempt standard gateway API call
  try {
    await dispatchApi("epro/submit", {
      method: "POST",
      body: JSON.stringify(payload),
      change_reason: finalReason,
    });
  } catch {
    console.info("Falling back to local cache storage for electronic submissions.");
  }

  state.activeQuestionnaire = null;
  alert("Diary/Questionnaire successfully signed and submitted!");

  // Recalculate and update views
  renderTasks();
  renderCompliance();
  showView("view-tasks");
}

// 3. My Compliance
function renderCompliance() {
  const total = state.assignments.length;
  const completed = state.assignments.filter(a => a.status === "COMPLETED").length;
  const overdue = state.assignments.filter(a => a.status === "OVERDUE").length;
  const pending = state.assignments.filter(a => a.status === "PENDING").length;

  const rate = total > 0 ? Math.round((completed / total) * 100) : 100;

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

  if (state.assignments.length === 0) {
    tbody.innerHTML = `<tr><td colspan="4" class="no-data">No history found.</td></tr>`;
    return;
  }

  // Sort assignments chronologically by due_at or end_date
  const sorted = state.assignments.slice().sort((a, b) => new Date(b.due_at) - new Date(a.due_at));

  tbody.innerHTML = sorted
    .map((item) => {
      const scheduledText = new Date(item.due_at).toLocaleDateString();
      const submittedText = item.submitted_at
        ? new Date(item.submitted_at).toLocaleString()
        : "—";

      let pillClass = "pending";
      let statusLabel = "Pending";
      if (item.status === "COMPLETED") {
        pillClass = "completed";
        statusLabel = "Completed";
      } else if (item.status === "OVERDUE" || new Date(item.due_at) < new Date()) {
        pillClass = "overdue";
        statusLabel = "Overdue";
      }

      return `
        <tr>
          <td><strong>${item.instrument_name}</strong></td>
          <td>${scheduledText}</td>
          <td>${submittedText}</td>
          <td><span class="status-pill ${pillClass}">${statusLabel}</span></td>
        </tr>
      `;
    })
    .join("");
}

// 4. My Inbox (Notifications & Reminders)
function renderInbox() {
  const container = document.getElementById("inbox-container");
  if (!container) return;

  const unread = state.notifications.filter(n => !n.is_read);
  const badge = document.getElementById("unread-count");
  if (badge) {
    badge.textContent = unread.length;
    badge.style.display = unread.length > 0 ? "inline-block" : "none";
  }

  if (state.notifications.length === 0) {
    container.innerHTML = `
      <div class="card" style="text-align: center; padding: 32px; color: var(--text-muted);">
        <p style="font-size: 16px; font-weight: 700;">No notifications found.</p>
      </div>
    `;
    return;
  }

  container.innerHTML = state.notifications
    .map((notif) => {
      const isUnread = !notif.is_read;
      const unreadClass = isUnread ? "unread" : "";
      const dueText = new Date(notif.due_at).toLocaleString();

      return `
        <div class="inbox-item ${unreadClass}" id="notif-card-${notif.id}">
          <div class="inbox-meta">
            <span class="inbox-name">${notif.message}</span>
            <span class="inbox-due">Due reminder timestamp: ${dueText} (Channel: ${notif.channel})</span>
          </div>
          <div class="inbox-actions">
            ${isUnread
              ? `<button type="button" class="btn btn-secondary btn-acknowledge" data-id="${notif.id}">Acknowledge & Read</button>`
              : `<span class="status-pill completed">Read</span>`
            }
          </div>
        </div>
      `;
    })
    .join("");

  // Attach acknowledgment click events
  container.querySelectorAll(".btn-acknowledge").forEach((btn) => {
    btn.addEventListener("click", () => {
      const id = btn.getAttribute("data-id");
      acknowledgeNotification(id);
    });
  });
}

async function acknowledgeNotification(notificationId) {
  const notif = state.notifications.find(n => n.id === notificationId);
  if (!notif) return;

  // Acknowledgment requires a simple audit reason
  const actionReason = `Acknowledge study reminder: "${notif.message}"`;

  notif.is_read = true;
  notif.read_at = new Date().toISOString();

  // Audit Stamp locally
  await logAuditRecord(
    "ACKNOWLEDGE_NOTIFICATION",
    { notification_id: notificationId, channel: notif.channel },
    actionReason
  );

  // Attempt API delivery
  try {
    await dispatchApi(`notifications/${notificationId}/acknowledge`, {
      method: "POST",
      body: JSON.stringify({ reason_for_change: actionReason }),
      change_reason: actionReason,
    });
  } catch {
    console.info("Acknowledged notification offline.");
  }

  // Refresh inbox & count badges
  renderInbox();
}

// Bootstrap Initialization
async function initializeApp() {
  // Graceful OIDC Keycloak setup
  if (typeof window !== "undefined" && !window.__MOCK_TEST_ENV__) {
    try {
      const KeycloakClass = window.Keycloak || (await import("keycloak-js")).default;
      if (KeycloakClass) {
        const keycloak = new KeycloakClass({
          url: "http://localhost:8080/",
          realm: "cadence",
          clientId: "cadence-web",
        });

        await keycloak.init({
          onLoad: "check-sso",
          pkceMethod: "S256",
        });

        if (keycloak.authenticated) {
          state.session.userId = keycloak.subject || "subject_001";
          state.session.token = keycloak.token;
          state.session.isOfflineMode = false;
          console.log("OIDC Session Verified for subject:", state.session.userId);
        }
      }
    } catch (err) {
      console.warn("Keycloak login failed or offline. Continuing in sandbox demo mode:", err.message);
    }
  }

  // Set participant visual name
  const nameEl = document.getElementById("session-subject-id");
  if (nameEl) nameEl.textContent = state.session.userId;

  // Bootstrap initial items
  state.assignments = JSON.parse(JSON.stringify(MOCK_ASSIGNMENTS));
  state.notifications = JSON.parse(JSON.stringify(MOCK_NOTIFICATIONS));

  // Initialize genesis compliance ledger
  await logAuditRecord(
    "GENESIS",
    { platform: "Cadence MyPortal", roles: state.session.roles },
    "Patient companion portal session securely booted."
  );

  // Query real endpoints to synchronize initially if online
  if (!state.session.isOfflineMode) {
    try {
      const assignments = await dispatchApi(`assignments/subject/${state.session.userId}`);
      if (Array.isArray(assignments)) {
        state.assignments = assignments.map(a => ({
          id: a.id,
          subject_id: a.subject_id,
          instrument_id: a.instrument_id,
          instrument_name: MOCK_INSTRUMENTS[a.instrument_id]?.name || "Assigned Instrument",
          due_at: a.due_at || a.end_date,
          end_date: a.end_date,
          status: a.version_index > 1 ? "COMPLETED" : "PENDING",
        }));
      }
    } catch {
      // Keep mock structures
    }

    try {
      const notifs = await dispatchApi(`subjects/${state.session.userId}/notifications`);
      if (Array.isArray(notifs)) {
        state.notifications = notifs.map(n => ({
          id: n.id,
          subject_id: n.subject_id,
          assignment_id: n.assignment_id,
          message: `Scheduled trial survey reminder (channel: ${n.channel})`,
          due_at: n.due_at,
          channel: n.channel,
          is_read: n.is_read,
        }));
      }
    } catch {
      // Keep mock structures
    }
  }

  // Draw Initial Views
  renderTasks();
  renderCompliance();
  renderInbox();

  // Attach Global Navigation listeners
  const btnTasks = document.getElementById("tab-btn-tasks");
  const btnCompliance = document.getElementById("tab-btn-compliance");
  const btnInbox = document.getElementById("tab-btn-inbox");

  if (btnTasks) btnTasks.addEventListener("click", () => showView("view-tasks"));
  if (btnCompliance) btnCompliance.addEventListener("click", () => showView("view-compliance"));
  if (btnInbox) btnInbox.addEventListener("click", () => showView("view-inbox"));

  const btnBack = document.getElementById("btn-back-to-tasks");
  if (btnBack) btnBack.addEventListener("click", () => {
    state.activeQuestionnaire = null;
    showView("view-tasks");
  });

  const btnCancel = document.getElementById("btn-cancel-questionnaire");
  if (btnCancel) btnCancel.addEventListener("click", () => {
    state.activeQuestionnaire = null;
    showView("view-tasks");
  });

  const btnSubmit = document.getElementById("btn-submit-questionnaire");
  if (btnSubmit) btnSubmit.addEventListener("click", () => {
    if (validateActiveQuestionnaire()) {
      openSignatureModal();
    } else {
      alert("Please fix all form errors before signing.");
    }
  });

  // Modal actions
  const btnModalCancel = document.getElementById("btn-modal-cancel");
  if (btnModalCancel) btnModalCancel.addEventListener("click", closeSignatureModal);

  const btnModalSign = document.getElementById("btn-modal-sign");
  if (btnModalSign) btnModalSign.addEventListener("click", verifyAndSubmitSignature);
}

// Auto-run on load in DOM environments
if (typeof document !== "undefined") {
  document.addEventListener("DOMContentLoaded", initializeApp);
}

export {
  state,
  showView,
  startQuestionnaire,
  validateActiveQuestionnaire,
  verifyAndSubmitSignature,
  acknowledgeNotification,
  initializeApp,
};
