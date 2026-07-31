<template>
  <div id="section-notifications" class="dashboard-section active">
    <div class="section-header">
      <h2>Notifications & Action-Items</h2>
      <p>
        Authorized notification and task management queue with 21 CFR Part 11
        compliant state transitions.
      </p>
    </div>

    <!-- Filters Row -->
    <div class="card" style="margin-bottom: 24px">
      <div
        style="display: flex; gap: 16px; align-items: flex-end; flex-wrap: wrap"
      >
        <div class="form-group" style="flex: 1; min-width: 150px">
          <label
            for="filter-category"
            style="margin-bottom: 6px; font-weight: 600; font-size: 0.85rem"
            >Category</label
          >
          <select
            id="filter-category"
            v-model="store.filters.category"
            @change="onFilterChange"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 6px;
            "
          >
            <option value="">All Categories</option>
            <option value="ALERTS">ALERTS</option>
            <option value="SYSTEM">SYSTEM</option>
            <option value="ACTION_ITEMS">ACTION_ITEMS</option>
          </select>
        </div>
        <div class="form-group" style="flex: 1; min-width: 150px">
          <label
            for="filter-priority"
            style="margin-bottom: 6px; font-weight: 600; font-size: 0.85rem"
            >Priority</label
          >
          <select
            id="filter-priority"
            v-model="store.filters.priority"
            @change="onFilterChange"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 6px;
            "
          >
            <option value="">All Priorities</option>
            <option value="LOW">LOW</option>
            <option value="MEDIUM">MEDIUM</option>
            <option value="HIGH">HIGH</option>
            <option value="CRITICAL">CRITICAL</option>
          </select>
        </div>
        <div class="form-group" style="flex: 1; min-width: 150px">
          <label
            for="filter-status"
            style="margin-bottom: 6px; font-weight: 600; font-size: 0.85rem"
            >Status</label
          >
          <select
            id="filter-status"
            v-model="store.filters.status"
            @change="onFilterChange"
            style="
              width: 100%;
              padding: 8px;
              border: 1px solid var(--border);
              border-radius: 6px;
            "
          >
            <option value="">All Statuses</option>
            <option value="OPEN">OPEN</option>
            <option value="ACKNOWLEDGED">ACKNOWLEDGED</option>
            <option value="RESOLVED">RESOLVED</option>
          </select>
        </div>
        <button
          id="btn-reset-filters"
          class="btn"
          @click="resetFilters"
          style="padding: 8px 16px"
        >
          Reset Filters
        </button>
      </div>
    </div>

    <!-- Status Banners -->
    <!-- Loading State -->
    <div
      v-if="store.loading"
      id="notifications-loading"
      style="text-align: center; padding: 48px"
    >
      <span style="font-size: 1.2rem; color: #64748b"
        >Loading notifications...</span
      >
    </div>

    <!-- 403 Forbidden State -->
    <div
      v-else-if="store.errorStatus === 403"
      id="notifications-error-403"
      class="card"
      style="
        border-left: 4px solid var(--error);
        background-color: var(--error-bg);
        padding: 24px;
      "
    >
      <div style="display: flex; gap: 16px; align-items: flex-start">
        <span style="font-size: 2rem">🚫</span>
        <div>
          <h3
            style="color: var(--error); font-weight: bold; margin-bottom: 8px"
          >
            Access Denied (403 Forbidden)
          </h3>
          <p style="color: var(--neutral-dark); font-size: 0.95rem">
            You do not have the required permissions or context to access these
            notification resources.
          </p>
        </div>
      </div>
    </div>

    <!-- Generic Error State -->
    <div
      v-else-if="store.error"
      id="notifications-error-generic"
      class="card"
      style="
        border-left: 4px solid var(--warning);
        background-color: var(--warning-bg);
        padding: 24px;
      "
    >
      <div style="display: flex; gap: 16px; align-items: flex-start">
        <span style="font-size: 2rem">⚠️</span>
        <div>
          <h3
            style="color: var(--warning); font-weight: bold; margin-bottom: 8px"
          >
            Failed to Load Notifications
          </h3>
          <p style="color: var(--neutral-dark); font-size: 0.95rem">
            {{ store.error }}
          </p>
        </div>
      </div>
    </div>

    <!-- Empty State -->
    <div
      v-else-if="store.notifications.length === 0"
      id="notifications-empty"
      class="card"
      style="
        text-align: center;
        padding: 48px;
        color: #64748b;
        font-style: italic;
      "
    >
      No notifications found matching the active filters.
    </div>

    <!-- Notifications List -->
    <div
      v-else
      id="notifications-list"
      style="display: flex; flex-direction: column; gap: 16px"
    >
      <div
        v-for="notif in store.notifications"
        :key="notif.id"
        class="card notification-card"
        style="
          display: flex;
          flex-direction: column;
          gap: 12px;
          border-left: 5px solid var(--accent);
        "
      >
        <div
          style="
            display: flex;
            justify-content: space-between;
            align-items: flex-start;
            flex-wrap: wrap;
            gap: 12px;
          "
        >
          <div style="display: flex; gap: 8px; flex-wrap: wrap">
            <span
              class="badge category-badge"
              style="background-color: var(--primary-light); color: white"
              >{{ notif.category }}</span
            >
            <span
              class="badge priority-badge"
              :style="getPriorityStyle(notif.priority)"
              >{{ notif.priority }}</span
            >
            <span
              class="badge status-badge"
              :style="getStatusStyle(notif.status)"
              >{{ notif.status }}</span
            >
            <span
              class="badge delivery-badge"
              style="
                background-color: #f1f5f9;
                color: #475569;
                border-color: #cbd5e1;
              "
              >{{ notif.delivery_state }}</span
            >
          </div>
          <span class="timestamp" style="font-size: 0.8rem; color: #94a3b8">{{
            formatTimestamp(notif.created_at)
          }}</span>
        </div>

        <div
          class="message-content"
          style="
            font-size: 1.05rem;
            font-weight: 500;
            color: var(--primary);
            word-break: break-word;
          "
        >
          {{ notif.message_content }}
        </div>

        <div
          style="
            font-size: 0.85rem;
            color: #64748b;
            display: flex;
            flex-direction: column;
            gap: 4px;
          "
        >
          <div class="target-context">
            <strong>Recipient Context:</strong>
            <span v-if="notif.recipient_user_id || notif.recipient_role">
              <span v-if="notif.recipient_user_id" style="margin-right: 12px"
                >User: <code>{{ notif.recipient_user_id }}</code></span
              >
              <span v-if="notif.recipient_role"
                >Role: <code>{{ notif.recipient_role }}</code></span
              >
            </span>
            <span
              v-else
              class="global-indicator"
              style="font-style: italic; color: #94a3b8"
              >Global / Broadcast</span
            >
          </div>
          <div v-if="notif.related_entity_id" class="linkage">
            <strong>Related:</strong>
            <code
              >{{ notif.related_entity_type }} ({{
                notif.related_entity_id
              }})</code
            >
          </div>
        </div>

        <!-- Action Controls -->
        <div
          v-if="notif.status !== 'RESOLVED'"
          style="
            display: flex;
            justify-content: flex-end;
            gap: 12px;
            margin-top: 8px;
            border-top: 1px dashed var(--border);
            padding-top: 12px;
          "
        >
          <button
            v-if="notif.status === 'OPEN'"
            class="btn btn-acknowledge"
            style="padding: 6px 12px; font-size: 0.8rem"
            @click="openActionModal(notif, 'acknowledge')"
          >
            Acknowledge
          </button>
          <button
            v-if="notif.status === 'OPEN' || notif.status === 'ACKNOWLEDGED'"
            class="btn btn-primary btn-resolve"
            style="padding: 6px 12px; font-size: 0.8rem"
            @click="openActionModal(notif, 'resolve')"
          >
            Resolve
          </button>
        </div>
      </div>
    </div>

    <!-- Justification Modal -->
    <div
      v-if="showModal"
      id="justification-modal"
      class="modal-overlay"
      style="
        display: flex;
        position: fixed;
        top: 0;
        left: 0;
        right: 0;
        bottom: 0;
        background: rgba(0, 0, 0, 0.5);
        justify-content: center;
        align-items: center;
        z-index: 1000;
      "
    >
      <div
        class="modal"
        style="
          background: white;
          border-radius: 12px;
          max-width: 500px;
          width: 100%;
          padding: 24px;
          box-shadow: 0 10px 25px rgba(0, 0, 0, 0.25);
        "
      >
        <div
          class="modal-header"
          style="
            font-size: 1.2rem;
            font-weight: bold;
            color: var(--primary);
            margin-bottom: 12px;
            border-bottom: 1px solid var(--border);
            padding-bottom: 8px;
            background: none;
          "
        >
          21 CFR Part 11 Justification Reason Required
        </div>
        <div class="modal-body" style="padding: 0; margin-bottom: 16px">
          <p style="font-size: 0.9rem; color: #475569; margin-bottom: 12px">
            To complete this transition, you must provide a justification change
            reason:
          </p>

          <!-- Inline Modal Error Banner -->
          <div
            v-if="modalError"
            id="modal-error"
            style="
              background-color: var(--error-bg);
              border: 1px solid var(--error);
              color: var(--error);
              padding: 8px;
              border-radius: 4px;
              margin-bottom: 12px;
              font-size: 0.85rem;
            "
          >
            {{ modalError }}
          </div>

          <div class="form-group" style="margin-bottom: 12px">
            <label
              for="modal-reason-select"
              style="display: block; margin-bottom: 4px; font-weight: 600"
              >Standard Reason</label
            >
            <select
              id="modal-reason-select"
              v-model="selectedReason"
              style="
                width: 100%;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 6px;
              "
            >
              <option value="Action completed successfully">
                Action completed successfully
              </option>
              <option value="Notification reviewed and logged">
                Notification reviewed and logged
              </option>
              <option value="Clinical event acknowledged">
                Clinical event acknowledged
              </option>
              <option value="Corrective action documented">
                Corrective action documented
              </option>
              <option value="Other">Other (specify custom explanation)</option>
            </select>
          </div>

          <div class="form-group">
            <label
              for="modal-custom-reason"
              style="display: block; margin-bottom: 4px; font-weight: 600"
              >Custom Explanation</label
            >
            <textarea
              id="modal-custom-reason"
              v-model="customReason"
              placeholder="Provide details if required or if 'Other' is selected..."
              style="
                width: 100%;
                height: 80px;
                padding: 8px;
                border: 1px solid var(--border);
                border-radius: 6px;
                resize: none;
              "
            ></textarea>
          </div>
        </div>
        <div
          class="modal-footer"
          style="
            padding: 12px 0 0 0;
            background: none;
            border: none;
            display: flex;
            justify-content: flex-end;
            gap: 12px;
          "
        >
          <button id="btn-cancel-modal" class="btn" @click="closeModal">
            Cancel
          </button>
          <button
            id="btn-submit-modal"
            class="btn btn-primary"
            :disabled="!isReasonValid"
            @click="submitAction"
          >
            Confirm
          </button>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { onMounted, ref, computed } from "vue";
import { useNotificationsStore } from "../stores/notifications";

const store = useNotificationsStore();

// Modal properties
const showModal = ref(false);
const selectedReason = ref("Action completed successfully");
const customReason = ref("");
const modalError = ref(null);
const currentNotif = ref(null);
const actionType = ref(null); // "acknowledge" or "resolve"

const onFilterChange = async () => {
  await store.fetchNotifications();
};

const resetFilters = async () => {
  store.filters.category = "";
  store.filters.priority = "";
  store.filters.status = "";
  await store.fetchNotifications();
};

// Style mapping helpers
const getPriorityStyle = (priority) => {
  switch (priority) {
    case "CRITICAL":
      return "background-color: #fee2e2; border-color: #fca5a5; color: #b91c1c;";
    case "HIGH":
      return "background-color: #ffedd5; border-color: #fed7aa; color: #c2410c;";
    case "MEDIUM":
      return "background-color: #fef9c3; border-color: #fde047; color: #a16207;";
    case "LOW":
    default:
      return "background-color: #f1f5f9; border-color: #cbd5e1; color: #475569;";
  }
};

const getStatusStyle = (status) => {
  switch (status) {
    case "RESOLVED":
      return "background-color: #e2e8f0; border-color: #cbd5e1; color: #475569;";
    case "ACKNOWLEDGED":
      return "background-color: #dcfce7; border-color: #86efac; color: #166534;";
    case "OPEN":
    default:
      return "background-color: #dbeafe; border-color: #93c5fd; color: #1e40af;";
  }
};

const formatTimestamp = (created_at) => {
  if (!created_at) return "";
  try {
    const d = new Date(created_at);
    return d.toLocaleString();
  } catch {
    return created_at;
  }
};

// Modal action triggers
const openActionModal = (notif, action) => {
  currentNotif.value = notif;
  actionType.value = action;
  selectedReason.value = "Action completed successfully";
  customReason.value = "";
  modalError.value = null;
  showModal.value = true;
};

const closeModal = () => {
  showModal.value = false;
  currentNotif.value = null;
  actionType.value = null;
  modalError.value = null;
};

// Computed validation of change reason
const isReasonValid = computed(() => {
  if (selectedReason.value === "Other") {
    return customReason.value.trim().length > 0;
  }
  return selectedReason.value.trim().length > 0;
});

const submitAction = async () => {
  if (!isReasonValid.value) return;

  const reason =
    selectedReason.value === "Other"
      ? customReason.value.trim()
      : selectedReason.value.trim();
  modalError.value = null;

  try {
    if (actionType.value === "acknowledge") {
      await store.acknowledge(currentNotif.value.id, reason);
    } else if (actionType.value === "resolve") {
      await store.resolve(currentNotif.value.id, reason);
    }
    closeModal();
  } catch (err) {
    modalError.value =
      err.message ||
      "Action failed due to server transition validation constraints.";
  }
};

onMounted(async () => {
  await store.fetchNotifications();
});
</script>

<style scoped>
.notification-card {
  transition: all 0.2s;
}
.notification-card:hover {
  box-shadow: 0 4px 6px -1px rgb(0 0 0 / 0.1);
  border-color: var(--accent-light) !important;
}
</style>
