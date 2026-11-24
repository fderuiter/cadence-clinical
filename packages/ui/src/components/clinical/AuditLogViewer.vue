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
          <option value="ALL">All Actions</option>
          <option value="CREATE">CREATE</option>
          <option value="UPDATE">UPDATE</option>
          <option value="DELETE">DELETE</option>
          <option value="SIGN">SIGN</option>
        </select>
      </div>
    </div>

    <div class="audit-timeline" role="feed" aria-label="Audit Timeline">
      <article
        v-for="entry in filteredLogs"
        :key="entry.id"
        class="timeline-item"
        tabindex="0"
      >
        <div class="timeline-marker"></div>
        <div class="timeline-content">
          <div class="content-header">
            <span class="action-tag" :data-action="entry.action">{{
              entry.action
            }}</span>
            <span class="timestamp">{{ entry.timestamp }}</span>
            <span class="user-id"
              >by <strong>{{ entry.user_id }}</strong></span
            >
          </div>

          <div class="target-info">
            Target: <code>{{ entry.entity_type }} ({{ entry.entity_id }})</code>
          </div>

          <div v-if="entry.reason_for_change" class="change-reason">
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
                <tr v-for="(change, cIdx) in entry.changes" :key="cIdx">
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

      <div v-if="filteredLogs.length === 0" class="empty-state">
        No audit trail events match the filter criteria.
      </div>
    </div>
  </div>
</template>

<style scoped>
.audit-log-viewer {
  display: flex;
  flex-direction: column;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
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
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
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
  color: var(--semantic-color-text);
}

.part11-badge {
  font-size: var(--font-size-2xs);
  font-weight: 600;
  padding: 2px 8px;
  border-radius: 12px;
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}

.audit-controls {
  display: flex;
  gap: 8px;
}

.audit-search,
.audit-select {
  padding: 6px 10px;
  font-size: 13px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
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
  background: var(--semantic-color-primary);
  margin-top: 6px;
  flex-shrink: 0;
}

.timeline-content {
  flex: 1;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
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
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-body);
}

.action-tag[data-action="CREATE"] {
  background: var(--semantic-color-success-bg);
  color: var(--semantic-color-success);
}
.action-tag[data-action="UPDATE"] {
  background: var(--semantic-color-primary-light);
  color: var(--semantic-color-primary);
}
.action-tag[data-action="DELETE"] {
  background: var(--semantic-color-error-bg);
  color: var(--semantic-color-error);
}
.action-tag[data-action="SIGN"] {
  background: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
}

.timestamp {
  color: var(--semantic-color-text-muted);
  font-size: 12px;
}

.user-id {
  color: var(--semantic-color-text);
}

.target-info {
  font-size: 13px;
  color: var(--semantic-color-text-muted);
  margin-bottom: 6px;
}

.change-reason {
  font-size: 13px;
  color: var(--semantic-color-text);
  background: var(--semantic-color-surface);
  padding: 6px 10px;
  border-radius: 4px;
  border-left: 3px solid var(--semantic-color-primary);
  margin-bottom: 8px;
}

.reason-label {
  font-weight: 600;
}

.diff-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 12px;
  font-variant-numeric: tabular-nums;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
}

.diff-table th,
.diff-table td {
  padding: 6px 10px;
  border: 1px solid var(--semantic-color-border);
}

.diff-old {
  color: var(--semantic-color-error);
  background: var(--semantic-color-error-bg-row);
}

.diff-new {
  color: var(--semantic-color-success);
  background: var(--semantic-color-success-bg-soft);
}

.empty-state {
  text-align: center;
  padding: 32px;
  color: var(--semantic-color-text-muted);
  font-style: italic;
}
</style>
