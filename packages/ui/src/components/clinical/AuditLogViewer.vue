<script setup>
import { computed, ref } from "vue";

const props = defineProps({
  logs: {
    type: Array,
    required: true,
    // [{ id, timestamp, user_id, action, entity_type, entity_id, reason_for_change, changes: [{ field, old_val, new_val }] }]
  },
  title: {
    type: String,
    default: "21 CFR Part 11 Audit Trail",
  },
});

const searchQuery = ref("");
const selectedFilter = ref("ALL");

const filteredLogs = computed(() => {
  return props.logs.filter((log) => {
    const matchesFilter =
      selectedFilter.value === "ALL" || log.action === selectedFilter.value;
    if (!matchesFilter) return false;
    if (!searchQuery.value) return true;
    const q = searchQuery.value.toLowerCase();
    return (
      (log.user_id && log.user_id.toLowerCase().includes(q)) ||
      (log.action && log.action.toLowerCase().includes(q)) ||
      (log.entity_type && log.entity_type.toLowerCase().includes(q)) ||
      (log.reason_for_change && log.reason_for_change.toLowerCase().includes(q))
    );
  });
});
</script>

<template>
  <div class="audit-log-viewer">
    <div class="audit-header">
      <div class="header-titles">
        <h3 class="audit-title">
          {{ title }}
        </h3>
        <span class="part11-badge">21 CFR Part 11 GxP Immutable</span>
      </div>
      <div class="audit-controls">
        <input
          v-model="searchQuery"
          type="text"
          class="audit-search"
          placeholder="Filter by user, action, reason..."
          aria-label="Filter audit entries"
        />
        <select
          v-model="selectedFilter"
          class="audit-select"
          aria-label="Filter by action"
        >
          <option value="ALL">
            All Actions
          </option>
          <option value="CREATE">
            CREATE
          </option>
          <option value="UPDATE">
            UPDATE
          </option>
          <option value="DELETE">
            DELETE
          </option>
          <option value="SIGN">
            SIGN
          </option>
        </select>
      </div>
    </div>

    <div
      class="audit-timeline"
      role="feed"
      aria-label="Audit Timeline"
    >
      <article
        v-for="entry in filteredLogs"
        :key="entry.id"
        class="timeline-item"
        tabindex="0"
      >
        <div class="timeline-marker"></div>
        <div class="timeline-content">
          <div class="content-header">
            <span
              class="action-tag"
              :data-action="entry.action"
            >{{
              entry.action
            }}</span>
            <span class="timestamp">{{ entry.timestamp }}</span>
            <span class="user-id">by <strong>{{ entry.user_id }}</strong></span>
          </div>

          <div class="target-info">
            Target: <code>{{ entry.entity_type }} ({{ entry.entity_id }})</code>
          </div>

          <div
            v-if="entry.reason_for_change"
            class="change-reason"
          >
            <span class="reason-label">Reason for Change:</span>
            {{ entry.reason_for_change }}
          </div>

          <div
            v-if="entry.changes && entry.changes.length > 0"
            class="field-diffs"
          >
            <table class="diff-table">
              <thead>
                <tr>
                  <th>Field</th>
                  <th>Previous Value</th>
                  <th>New Value</th>
                </tr>
              </thead>
              <tbody>
                <tr
                  v-for="(change, cIdx) in entry.changes"
                  :key="cIdx"
                >
                  <td>
                    <code>{{ change.field }}</code>
                  </td>
                  <td class="diff-old">
                    {{ change.old_val ?? "null" }}
                  </td>
                  <td class="diff-new">
                    {{ change.new_val ?? "null" }}
                  </td>
                </tr>
              </tbody>
            </table>
          </div>
        </div>
      </article>

      <div
        v-if="filteredLogs.length === 0"
        class="empty-state"
      >
        No audit trail events match the filter criteria.
      </div>
    </div>
  </div>
</template>

<style scoped>
.audit-log-viewer {
  display: flex;
  flex-direction: column;
  background: var(--color-surface, #ffffff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 8px;
  overflow: hidden;
}

.audit-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px;
  padding: 12px 16px;
  background: var(--color-surface-muted, #f8fafc);
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.header-titles {
  display: flex;
  align-items: center;
  gap: 10px;
}

.audit-title {
  margin: 0;
  font-size: 15px;
  font-weight: 600;
  color: var(--color-text, #0f172a);
}

.part11-badge {
  font-size: 11px;
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  background: var(--color-success-bg, #dcfce7);
  color: var(--color-success, #15803d);
}

.audit-controls {
  display: flex;
  gap: 8px;
}

.audit-search,
.audit-select {
  padding: 6px 10px;
  font-size: 13px;
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 4px;
  background: var(--color-surface, #ffffff);
}

.audit-timeline {
  padding: 16px;
  display: flex;
  flex-direction: column;
  gap: 16px;
  max-height: 600px;
  overflow-y: auto;
}

.timeline-item {
  position: relative;
  display: flex;
  gap: 16px;
  padding-left: 8px;
}

.timeline-marker {
  width: 10px;
  height: 10px;
  border-radius: 50%;
  background: var(--color-primary, #026597);
  margin-top: 6px;
  flex-shrink: 0;
}

.timeline-content {
  flex: 1;
  background: #f8fafc;
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 6px;
  padding: 12px;
}

.content-header {
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 13px;
  margin-bottom: 6px;
}

.action-tag {
  font-size: 11px;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: #e2e8f0;
  color: #334155;
}

.action-tag[data-action="CREATE"] {
  background: #dcfce7;
  color: #15803d;
}
.action-tag[data-action="UPDATE"] {
  background: #e0f2fe;
  color: #0369a1;
}
.action-tag[data-action="DELETE"] {
  background: #fee2e2;
  color: #b91c1c;
}
.action-tag[data-action="SIGN"] {
  background: #fef9c3;
  color: #854d0e;
}

.timestamp {
  color: var(--color-text-muted, #475569);
  font-size: 12px;
}

.user-id {
  color: var(--color-text, #0f172a);
}

.target-info {
  font-size: 13px;
  color: var(--color-text-muted, #475569);
  margin-bottom: 6px;
}

.change-reason {
  font-size: 13px;
  color: var(--color-text, #0f172a);
  background: #ffffff;
  padding: 6px 10px;
  border-radius: 4px;
  border-left: 3px solid var(--color-primary, #026597);
  margin-bottom: 8px;
}

.reason-label {
  font-weight: 600;
}

.diff-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  background: #ffffff;
  border: 1px solid var(--color-border, #e2e8f0);
}

.diff-table th,
.diff-table td {
  padding: 6px 10px;
  border: 1px solid var(--color-border, #e2e8f0);
}

.diff-old {
  color: #b91c1c;
  background: #fff1f2;
}

.diff-new {
  color: #15803d;
  background: #f0fdf4;
}

.empty-state {
  text-align: center;
  padding: 32px;
  color: var(--color-text-muted, #475569);
  font-style: italic;
}
</style>
