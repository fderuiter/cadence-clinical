<template>
  <div class="audit-trail-viewer">
    <!-- Filter Panel -->
    <div class="card" style="padding: 16px; margin-bottom: 20px;">
      <h3 style="margin-top: 0; margin-bottom: 12px; font-size: 15px;">Search &amp; Filter Logs</h3>
      <div style="display: flex; gap: 16px; flex-wrap: wrap; align-items: flex-end;">
        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 150px;">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 4px; display: block;">User ID</label>
          <input
            v-model="userIdInput"
            type="text"
            placeholder="Filter by User ID"
            class="filter-user-id"
            @input="onFilterChange"
            style="width: 100%; padding: 8px; border-radius: 4px; border: 1px solid var(--border); background: var(--bg); color: var(--text);"
          />
        </div>

        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 150px;">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 4px; display: block;">Action Type</label>
          <select
            v-model="actionInput"
            class="filter-action-type"
            @change="onFilterChange"
            style="width: 100%; padding: 8px; border-radius: 4px; border: 1px solid var(--border); background: var(--bg); color: var(--text);"
          >
            <option value="">All Actions</option>
            <option value="UPDATE">UPDATE</option>
            <option value="SIGN_OFF">SIGN_OFF</option>
            <option value="QUERY_RAISED">QUERY_RAISED</option>
            <option value="INGEST">INGEST</option>
            <option value="VIEW">VIEW</option>
          </select>
        </div>

        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 150px;">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 4px; display: block;">Start Date</label>
          <input
            v-model="startDateInput"
            type="date"
            class="filter-start-date"
            @change="onDateChange"
            style="width: 100%; padding: 8px; border-radius: 4px; border: 1px solid var(--border); background: var(--bg); color: var(--text);"
          />
        </div>

        <div class="form-group" style="margin-bottom: 0; flex: 1; min-width: 150px;">
          <label style="font-size: 11px; font-weight: 600; margin-bottom: 4px; display: block;">End Date</label>
          <input
            v-model="endDateInput"
            type="date"
            class="filter-end-date"
            @change="onDateChange"
            style="width: 100%; padding: 8px; border-radius: 4px; border: 1px solid var(--border); background: var(--bg); color: var(--text);"
          />
        </div>
      </div>
    </div>

    <!-- Events Table -->
    <div class="card" style="padding: 0; overflow-x: auto;">
      <table class="clinical-table" style="width: 100%; border-collapse: collapse; font-size: 13px;">
        <thead>
          <tr style="background: var(--bg); border-bottom: 1px solid var(--border); text-align: left;">
            <th style="padding: 12px 16px; font-weight: 600;">Timestamp</th>
            <th style="padding: 12px 16px; font-weight: 600;">User</th>
            <th style="padding: 12px 16px; font-weight: 600;">Action</th>
            <th style="padding: 12px 16px; font-weight: 600;">Details</th>
            <th style="padding: 12px 16px; font-weight: 600; width: 220px;">21 CFR Part 11 Audit fields</th>
          </tr>
        </thead>
        <tbody>
          <tr v-if="events.length === 0">
            <td colspan="5" style="padding: 24px; text-align: center; color: var(--text-muted); font-style: italic;">
              No audit trail logs match the specified criteria.
            </td>
          </tr>
          <tr
            v-else
            v-for="event in events"
            :key="event.id || event.timestamp"
            style="border-bottom: 1px solid var(--border);"
          >
            <td style="padding: 12px 16px; font-family: monospace; font-size: 12px; color: var(--text-muted);">
              {{ formatTimestamp(event.timestamp) }}
            </td>
            <td style="padding: 12px 16px; font-weight: 500;">
              <span class="user-id">{{ event.user_id || event.user }}</span>
            </td>
            <td style="padding: 12px 16px;">
              <span class="badge" :class="getActionBadgeClass(event.action)" style="font-size: 10px;">
                {{ event.action }}
              </span>
            </td>
            <td style="padding: 12px 16px; line-height: 1.4;">
              {{ event.details || event.message }}
            </td>
            <td style="padding: 12px 16px;">
              <div v-if="event.reason_for_change || event.reasonForChange" class="part11-reason" style="padding: 6px 8px; background: rgba(0, 123, 255, 0.05); border-left: 3px solid var(--accent); border-radius: 2px; font-size: 11px; margin-bottom: 6px; color: var(--text);">
                <strong>Reason:</strong> {{ event.reason_for_change || event.reasonForChange }}
              </div>
              <div v-if="event.version_index !== undefined || event.versionIndex !== undefined" class="part11-version">
                <span class="badge status-approved" style="font-size: 10px; font-weight: bold;">
                  Version: v{{ event.version_index !== undefined ? event.version_index : event.versionIndex }}
                </span>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup>
import { ref, watch } from "vue";
import { useAuditorStore } from "../../stores/auditor";

defineProps({
  events: {
    type: Array,
    required: true,
  },
});

const auditorStore = useAuditorStore();

const userIdInput = ref(auditorStore.filters.user);
const actionInput = ref(auditorStore.filters.eventType);
const startDateInput = ref(auditorStore.filters.dateRange.start);
const endDateInput = ref(auditorStore.filters.dateRange.end);

// Watch for store filter changes to sync back (e.g. if cleared from modal/elsewhere)
watch(
  () => auditorStore.filters,
  (newFilters) => {
    userIdInput.value = newFilters.user;
    actionInput.value = newFilters.eventType;
    startDateInput.value = newFilters.dateRange.start;
    endDateInput.value = newFilters.dateRange.end;
  },
  { deep: true }
);

function onFilterChange() {
  auditorStore.setFilters({
    user: userIdInput.value,
    eventType: actionInput.value,
  });
  auditorStore.fetchAuditLogs();
}

function onDateChange() {
  auditorStore.setFilters({
    dateRange: {
      start: startDateInput.value,
      end: endDateInput.value,
    },
  });
  auditorStore.fetchAuditLogs();
}

function formatTimestamp(isoString) {
  if (!isoString) return "";
  try {
    const date = new Date(isoString);
    return date.toISOString().replace("T", " ").substring(0, 19) + " UTC";
  } catch {
    return isoString;
  }
}

function getActionBadgeClass(action) {
  const map = {
    SIGN_OFF: "status-approved",
    QUERY_RAISED: "status-review",
    UPDATE: "status-draft",
    INGEST: "status-draft",
    VIEW: "status-review",
  };
  return map[action] || "";
}
</script>

<style scoped>
.clinical-table tr:hover {
  background-color: rgba(0, 0, 0, 0.01);
}
</style>
