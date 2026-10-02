<template>
  <div id="sop-versions-viewer" class="reader-body-card">
    <div class="tab-section-header">
      <h4>Immutable Snapshot Versions</h4>
      <p class="section-desc">
        Controlled snapshot archive of all historical versions under 21 CFR Part 11.
      </p>
    </div>

    <div v-if="versionsLoading" class="loading-box">
      <div class="spinner" />
      <p>Loading version snapshots...</p>
    </div>

    <div v-else-if="!versionsList || versionsList.length === 0" class="empty-box">
      <p>No snapshot versions recorded for this document.</p>
    </div>

    <div v-else class="versions-table-wrap">
      <table class="data-table">
        <thead>
          <tr>
            <th>Version</th>
            <th>Index</th>
            <th>Status at Snapshot</th>
            <th>Author / Editor</th>
            <th>Reason for Change</th>
            <th>Timestamp</th>
            <th>Locked</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="ver in versionsList" :key="ver.id">
            <td>
              <span class="badge badge-version">v{{ ver.version_label }}</span>
            </td>
            <td>#{{ ver.version_index }}</td>
            <td>
              <span
                class="status-pill"
                :class="getStatusPillClass ? getStatusPillClass(ver.status_at_snapshot) : ''"
              >
                {{ ver.status_at_snapshot }}
              </span>
            </td>
            <td>{{ ver.created_by || "system" }}</td>
            <td>{{ ver.reason_for_change || "Baseline initial version" }}</td>
            <td>{{ formatDate ? formatDate(ver.created_at) : ver.created_at }}</td>
            <td>
              <span
                class="badge"
                :class="ver.is_locked ? 'badge-success' : 'badge-neutral'"
              >
                {{ ver.is_locked ? "🔒 Locked" : "📝 Draft" }}
              </span>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup>
defineProps({
  versionsList: { type: Array, default: () => [] },
  versionsLoading: { type: Boolean, default: false },
  formatDate: { type: Function, default: (d) => String(d || "") },
  getStatusPillClass: { type: Function, default: () => "" },
});
</script>
