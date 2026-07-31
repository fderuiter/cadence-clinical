<template>
  <div
    :id="`query-panel-${id}`"
    class="query-panel"
    role="region"
    :aria-labelledby="`query-flag-${id}`"
  >
    <div class="query-panel-header">
      <span class="query-panel-title">Query Manager - {{ id }}</span>
      <button
        type="button"
        class="btn-close-panel"
        aria-label="Close query panel"
        @click="$emit('close-panel')"
      >
        ×
      </button>
    </div>
    <div class="query-panel-body">
      <!-- NONE state -->
      <div v-if="status === 'NONE'" class="query-create-section">
        <p class="query-panel-instruction">Raise a query for this field:</p>
        <div class="form-group">
          <label :for="`query-message-${id}`">Discrepancy Message</label>
          <textarea
            :id="`query-message-${id}`"
            v-model="messageInput"
            placeholder="Enter clinical discrepancy details..."
            required
          ></textarea>
        </div>
        <button
          type="button"
          class="btn-submit-query"
          :data-field-id="id"
          data-action="create-query"
          @click="handleSubmitQuery"
        >
          Submit Query
        </button>
      </div>

      <!-- OPEN / REOPENED state -->
      <div
        v-else-if="status === 'OPEN' || status === 'REOPENED'"
        class="query-details"
      >
        <div
          class="query-status-badge"
          :class="`badge-${status.toLowerCase()}`"
        >
          Status: {{ status }}
        </div>
        <p class="query-current-msg">
          <strong>Discrepancy:</strong> {{ query.message }}
        </p>
        <p class="query-meta">
          Raised by: {{ query.createdBy || "System" }} on
          {{ query.createdAt || "N/A" }}
        </p>
        <div class="query-respond-section" style="margin-top: 12px">
          <div class="form-group">
            <label :for="`query-response-${id}`">Your Response</label>
            <textarea
              :id="`query-response-${id}`"
              v-model="responseInput"
              placeholder="Enter clinical justification or resolution explanation..."
              required
            ></textarea>
          </div>
          <button
            type="button"
            class="btn-respond-query"
            :data-field-id="id"
            data-action="respond-query"
            @click="handleRespondQuery"
          >
            Submit Response
          </button>
        </div>
      </div>

      <!-- ANSWERED state -->
      <div v-else-if="status === 'ANSWERED'" class="query-details">
        <div class="query-status-badge badge-answered">Status: ANSWERED</div>
        <p class="query-current-msg">
          <strong>Discrepancy:</strong> {{ query.message }}
        </p>
        <p class="query-response-msg">
          <strong>Response:</strong>
          {{ query.response || "No response provided" }}
        </p>
        <p class="query-meta">
          Responded by: {{ query.respondedBy || "Investigator" }} on
          {{ query.respondedAt || "N/A" }}
        </p>
        <div
          class="query-actions-section"
          style="margin-top: 12px; display: flex; gap: 8px"
        >
          <button
            type="button"
            class="btn-close-query"
            :data-field-id="id"
            data-action="close-query"
            @click="$emit('close-query')"
          >
            Close Query (Resolve)
          </button>
          <button
            type="button"
            class="btn-reopen-query"
            :data-field-id="id"
            data-action="reopen-query"
            @click="$emit('reopen-query')"
          >
            Reopen Query
          </button>
        </div>
      </div>

      <!-- CLOSED state -->
      <div v-else-if="status === 'CLOSED'" class="query-details">
        <div class="query-status-badge badge-closed">Status: CLOSED</div>
        <p class="query-current-msg">
          <strong>Discrepancy:</strong> {{ query.message }}
        </p>
        <p class="query-response-msg">
          <strong>Response:</strong> {{ query.response || "N/A" }}
        </p>
        <p class="query-meta">
          Closed by: {{ query.closedBy || "CRA/DM" }} on
          {{ query.closedAt || "N/A" }}
        </p>
        <p class="query-history-info">
          This query is permanently resolved and closed.
        </p>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch } from "vue";

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
  query: {
    type: Object,
    default: null,
  },
});

const emit = defineEmits([
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
  "close-panel",
]);

const messageInput = ref("");
const responseInput = ref("");

const status = computed(() => {
  return props.query && props.query.status
    ? props.query.status.toUpperCase()
    : "NONE";
});

// Clear inputs when status changes
watch(status, () => {
  messageInput.value = "";
  responseInput.value = "";
});

function handleSubmitQuery() {
  if (messageInput.value.trim()) {
    emit("create-query", messageInput.value.trim());
    messageInput.value = "";
  }
}

function handleRespondQuery() {
  if (responseInput.value.trim()) {
    emit("respond-query", responseInput.value.trim());
    responseInput.value = "";
  }
}
</script>
