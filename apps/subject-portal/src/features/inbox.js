// 4. My Inbox: notifications, reminders and their acknowledgement.
import { createSrOnlyActionButton, handoffFocus } from "ui";
import {
  dispatchApi,
  fetchNotifications,
  isAuthenticatedSession,
} from "../api/client.js";
import { state } from "../stores/state.js";
import { isDemoModeActive } from "../utils/demoMode.js";
import { allowUnbackedAction } from "../utils/unbackedActions.js";
import { logAuditRecord } from "./audit.js";
import { openNotificationPeek } from "./peek.js";
import { openReconsentModal } from "./reconsent.js";

function bindKeyboardAndClickActivation(el, callback) {
  el.addEventListener("click", callback);
  el.addEventListener("keydown", (event) => {
    if (event.key === "Enter" || event.key === " ") {
      event.preventDefault();
      callback();
    }
  });
}

function escapeNotificationId(id) {
  if (typeof CSS !== "undefined" && typeof CSS.escape === "function") {
    return CSS.escape(String(id));
  }
  return String(id).replace(/[^a-zA-Z0-9_-]/g, "\\$&");
}

// 4. My Inbox (Notifications & Reminders)
export function renderInbox() {
  const loadingEl = document.getElementById("inbox-loading");
  const failureEl = document.getElementById("inbox-failure");
  const container = document.getElementById("inbox-container");
  const errorMsgEl = document.getElementById("inbox-error-msg");

  if (loadingEl) loadingEl.style.display = "none";
  if (failureEl) failureEl.style.display = "none";
  if (!container) return;
  container.style.display = "none";

  if (state.notificationsLoading) {
    if (loadingEl) loadingEl.style.display = "block";
    return;
  }

  if (state.notificationsError) {
    if (failureEl) {
      failureEl.style.display = "block";
      if (errorMsgEl) errorMsgEl.textContent = state.notificationsError;
    }
    // Populate container with error content so tests can inspect innerHTML,
    // but keep it visually hidden behind the failure overlay.
    if (container) {
      container.style.display = "none";
      container.innerHTML = `
        <div class="card" style="text-align: center; padding: 32px; color: var(--danger);">
          <p style="font-size: 16px; font-weight: 700;">Error loading notifications</p>
        </div>
      `;
    }
    return;
  }

  const unread = state.notifications.filter((n) => !n.is_read);
  state.unreadCount = unread.length;
  const badge = document.getElementById("unread-count");
  if (badge) {
    badge.textContent = unread.length;
    badge.style.display = unread.length > 0 ? "inline-block" : "none";
  }

  container.style.display = "block";

  if (state.notifications.length === 0) {
    const emptyMsg =
      !isAuthenticatedSession() && !isDemoModeActive() && !state.session.userId
        ? "Sign in to view your study notifications."
        : "No notifications found.";
    container.innerHTML = `
      <div class="card" style="text-align: center; padding: 32px; color: var(--text-muted);">
        <p style="font-size: 16px; font-weight: 700;">${emptyMsg}</p>
      </div>
    `;
    return;
  }

  container.innerHTML = state.notifications
    .map((notif) => {
      const isUnread = !notif.is_read;
      const unreadClass = isUnread ? "unread" : "";
      const dueText = notif.due_at
        ? new Date(notif.due_at).toLocaleString()
        : new Date().toLocaleString();
      const isReconsent =
        notif.related_entity_type === "RECONSENT_REQUIRED" ||
        (notif.message_content &&
          notif.message_content
            .toLowerCase()
            .includes("re-consent required")) ||
        (notif.message &&
          notif.message.toLowerCase().includes("re-consent required"));

      const notifLabel =
        notif.message_content ||
        notif.message ||
        `Scheduled trial survey reminder (channel: ${notif.channel})`;

      let actionHtml = `<span class="status-pill completed">Read</span>`;
      if (isUnread) {
        if (isReconsent) {
          actionHtml = createSrOnlyActionButton({
            label: "Review & Sign",
            srContext: notifLabel,
            className: "btn btn-primary btn-reconsent-action",
            dataId: notif.id,
          });
        } else {
          actionHtml = createSrOnlyActionButton({
            label: "Acknowledge & Read",
            srContext: notifLabel,
            className: "btn btn-secondary btn-acknowledge",
            dataId: notif.id,
          });
        }
      }

      const peekBtnHtml = createSrOnlyActionButton({
        id: `btn-peek-notif-${notif.id}`,
        label: "View Details",
        srContext: notifLabel,
        className: "btn btn-secondary btn-peek-notif",
        dataId: notif.id,
      });

      return `
        <div class="inbox-item ${unreadClass}" id="notif-card-${notif.id}" tabindex="-1">
          <div class="inbox-meta">
            <span class="inbox-name">${notifLabel}</span>
            <span class="inbox-due">Timestamp: ${dueText} (Channel: ${notif.channel || "IN_APP"})</span>
          </div>
          <div class="inbox-actions">
            ${peekBtnHtml}
            ${actionHtml}
          </div>
        </div>
      `;
    })
    .join("");

  // Attach peek details click & keyboard (Enter / Space) events
  container.querySelectorAll(".btn-peek-notif").forEach((btn) => {
    bindKeyboardAndClickActivation(btn, () => {
      const id = btn.getAttribute("data-id");
      const notif = state.notifications.find(
        (n) => String(n.id) === String(id)
      );
      if (notif) {
        openNotificationPeek(notif, btn);
      }
    });
  });

  // Attach acknowledgment click & keyboard (Enter / Space) events
  container.querySelectorAll(".btn-acknowledge").forEach((btn) => {
    bindKeyboardAndClickActivation(btn, () => {
      const id = btn.getAttribute("data-id");
      acknowledgeNotification(id);
    });
  });

  // Attach reconsent action click & keyboard (Enter / Space) events
  container.querySelectorAll(".btn-reconsent-action").forEach((btn) => {
    bindKeyboardAndClickActivation(btn, () => {
      const id = btn.getAttribute("data-id");
      acknowledgeNotification(id);
      openReconsentModal();
    });
  });
}

function handoffInboxAfterAcknowledge(notificationId) {
  if (typeof document === "undefined") return;
  if (state.reconsentModalOpen) return;
  if (state.notificationsError) {
    void handoffFocus([
      "#btn-retry-inbox",
      "#inbox-view-heading",
      "#main-content",
    ]);
    return;
  }
  const escapedId = escapeNotificationId(notificationId);
  void handoffFocus([
    () =>
      document.querySelector(
        "#inbox-container .btn-acknowledge, #inbox-container .btn-reconsent-action"
      ),
    `#notif-card-${escapedId}`,
    "#inbox-view-heading",
    "#main-content",
  ]);
}

export async function acknowledgeNotification(notificationId) {
  const notif = state.notifications.find((n) => n.id === notificationId);
  if (!notif) return;

  // Acknowledgment requires a simple audit reason
  const actionReason = `Acknowledge study reminder: "${notif.message || `Scheduled trial survey reminder (channel: ${notif.channel})`}"`;

  if (isAuthenticatedSession()) {
    try {
      const updatedNotif = await dispatchApi(
        `notifications/${notificationId}/acknowledge`,
        {
          method: "POST",
          body: JSON.stringify({ reason_for_change: actionReason }),
          change_reason: actionReason,
        }
      );

      // Update state notification on success
      const idx = state.notifications.findIndex((n) => n.id === notificationId);
      if (idx !== -1) {
        state.notifications[idx] = updatedNotif;
      } else {
        notif.is_read = true;
        notif.read_at = new Date().toISOString();
      }

      // Audit Stamp locally
      await logAuditRecord(
        "ACKNOWLEDGE_NOTIFICATION",
        { notification_id: notificationId, channel: notif.channel },
        actionReason
      );

      // Reset any notification error if successful
      state.notificationsError = null;
      renderInbox();
      handoffInboxAfterAcknowledge(notificationId);
    } catch (err) {
      // Keep the notification unread and render the visible actionable failure state with retry
      state.notificationsError = err.message || err;
      renderInbox();
      handoffInboxAfterAcknowledge(notificationId);
    }
  } else if (allowUnbackedAction("Acknowledge study notification")) {
    // Explicit demo mode optimistic flow
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
      console.info("Acknowledged notification in demo mode.");
    }

    // Refresh inbox & count badges
    renderInbox();
    handoffInboxAfterAcknowledge(notificationId);
  } else {
    state.notificationsError =
      "Cannot acknowledge notification while offline or unauthenticated. Please sign in and retry.";
    renderInbox();
    handoffInboxAfterAcknowledge(notificationId);
  }
}

// Loads the participant's notifications from the gateway.
export async function loadNotifications(userId) {
  state.notificationsLoading = true;
  state.notificationsError = null;
  try {
    state.notifications = await fetchNotifications(userId);
  } catch (err) {
    state.notificationsError = err.message || err;
    state.notifications = [];
  } finally {
    state.notificationsLoading = false;
  }
}

// Retry button on the inbox failure state.
export async function retryInbox() {
  const startedInFailure =
    typeof document !== "undefined" &&
    Boolean(
      document.getElementById("inbox-failure")?.contains(document.activeElement)
    );
  state.notificationsLoading = true;
  state.notificationsError = null;
  renderInbox();
  await loadNotifications(state.session.userId);
  renderInbox();
  if (startedInFailure) {
    if (state.notificationsError) {
      void handoffFocus([
        "#btn-retry-inbox",
        "#inbox-view-heading",
        "#main-content",
      ]);
    } else {
      void handoffFocus([
        () =>
          document.querySelector(
            "#inbox-container button:not([disabled]), #inbox-container a[href], #inbox-container input:not([disabled])"
          ),
        "#inbox-view-heading",
        "#main-content",
      ]);
    }
  }
}
