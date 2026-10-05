<template>
  <div id="sop-audit-viewer" class="reader-body-card">
    <div class="tab-section-header">
      <h4>21 CFR Part 11 Electronic Audit Ledger</h4>
      <p class="section-desc">
        Append-only tamper-evident transaction log with SHA-256 cryptographic
        hashes.
      </p>
    </div>

    <div v-if="auditLoading" class="loading-box">
      <div class="spinner" />
      <p>Fetching immutable audit trail...</p>
    </div>

    <div
      v-else-if="!auditLogList || auditLogList.length === 0"
      class="empty-box"
    >
      <p>No audit trail logs recorded yet.</p>
    </div>

    <div v-else class="audit-table-wrap">
      <table class="data-table">
        <thead>
          <tr>
            <th>Event Type</th>
            <th>Target State</th>
            <th>Performed By</th>
            <th>Reason / Justification</th>
            <th>SHA-256 Checksum Hash</th>
            <th>Timestamp</th>
          </tr>
        </thead>
        <tbody>
          <tr v-for="log in auditLogList" :key="log.id">
            <td>
              <strong>{{ log.event_type }}</strong>
            </td>
            <td>
              <span
                v-if="log.target_status"
                class="status-pill"
                :class="
                  getStatusPillClass
                    ? getStatusPillClass(log.target_status)
                    : ''
                "
              >
                {{ log.target_status }}
              </span>
              <span v-else class="text-muted">-</span>
            </td>
            <td>{{ log.performed_by_user_id || "system" }}</td>
            <td>{{ log.reason_for_change || "Document lifecycle update" }}</td>
            <td>
              <code class="hash-code" :title="log.sha256_hash">
                {{ (log.sha256_hash || "sha256:e3b0c442").substring(0, 12) }}...
              </code>
            </td>
            <td>
              {{ formatDate ? formatDate(log.created_at) : log.created_at }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>
</template>

<script setup>
defineProps({
  auditLogList: { type: Array, default: () => [] },
  auditLoading: { type: Boolean, default: false },
  formatDate: { type: Function, default: (d) => String(d || "") },
  getStatusPillClass: { type: Function, default: () => "" },
});
</script>
