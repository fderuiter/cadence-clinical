<template>
  <!-- Query Panel -->
  <div
    v-if="activeQueryPanels[field.id]"
    :id="`query-panel-${field.id}`"
    class="query-panel"
    role="region"
  >
    <div class="query-panel-header">
      <span class="query-panel-title">Query Manager - {{ field.id }}</span>
      <button
        type="button"
        class="btn-close-panel"
        @click="$emit('toggle-query-panel', field.id)"
      >
        ×
      </button>
    </div>
    <div class="query-panel-body">
      <!-- No Query State -->
      <div
        v-if="getQueryStatus(field.id) === 'NONE'"
        class="query-create-section"
      >
        <p class="query-panel-instruction">Raise a query for this field:</p>
        <div class="form-group">
          <label :for="`query-message-${field.id}`">Discrepancy Message</label>
          <textarea
            :id="`query-message-${field.id}`"
            :value="queryInputs[field.id]"
            @input="$emit('update-query-input', field.id, $event.target.value)"
            placeholder="Enter clinical discrepancy details..."
            required
          />
        </div>
        <button
          type="button"
          class="btn-submit-query"
          @click="$emit('create-query', field.id)"
        >
          Submit Query
        </button>
      </div>

      <!-- Open/Reopened Query State -->
      <div
        v-else-if="
          getQueryStatus(field.id) === 'OPEN' ||
          getQueryStatus(field.id) === 'REOPENED'
        "
        class="query-details"
      >
        <div
          class="query-status-badge"
          :class="`badge-${getQueryStatus(field.id).toLowerCase()}`"
        >
          Status: {{ getQueryStatus(field.id) }}
        </div>
        <p class="query-current-msg">
          <strong>Discrepancy:</strong>
          {{ store.formQueries[field.id].message }}
        </p>
        <p class="query-meta">
          Raised by:
          {{ store.formQueries[field.id].createdBy || "System" }} on
          {{ store.formQueries[field.id].createdAt }}
        </p>
        <div class="query-respond-section" style="margin-top: 12px">
          <div class="form-group">
            <label :for="`query-response-${field.id}`">Your Response</label>
            <textarea
              :id="`query-response-${field.id}`"
              :value="queryResponses[field.id]"
              @input="
                $emit('update-query-response', field.id, $event.target.value)
              "
              placeholder="Enter clinical justification or resolution explanation..."
              required
            />
          </div>
          <button
            type="button"
            class="btn-respond-query"
            @click="$emit('respond-query', field.id)"
          >
            Submit Response
          </button>
        </div>
      </div>

      <!-- Answered Query State -->
      <div
        v-else-if="getQueryStatus(field.id) === 'ANSWERED'"
        class="query-details"
      >
        <div class="query-status-badge badge-answered">Status: ANSWERED</div>
        <p class="query-current-msg">
          <strong>Discrepancy:</strong>
          {{ store.formQueries[field.id].message }}
        </p>
        <p class="query-response-msg">
          <strong>Response:</strong>
          {{ store.formQueries[field.id].response }}
        </p>
        <p class="query-meta">
          Responded by:
          {{ store.formQueries[field.id].respondedBy }} on
          {{ store.formQueries[field.id].respondedAt }}
        </p>
        <div
          class="query-actions-section"
          style="margin-top: 12px; display: flex; gap: 8px"
        >
          <button
            type="button"
            class="btn-close-query"
            @click="$emit('close-query', field.id)"
          >
            Close Query (Resolve)
          </button>
          <button
            type="button"
            class="btn-reopen-query"
            @click="$emit('reopen-query', field.id)"
          >
            Reopen Query
          </button>
        </div>
      </div>

      <!-- Closed Query State -->
      <div
        v-else-if="getQueryStatus(field.id) === 'CLOSED'"
        class="query-details"
      >
        <div class="query-status-badge badge-closed">Status: CLOSED</div>
        <p class="query-current-msg">
          <strong>Discrepancy:</strong>
          {{ store.formQueries[field.id].message }}
        </p>
        <p class="query-response-msg">
          <strong>Response:</strong>
          {{ store.formQueries[field.id].response }}
        </p>
        <p class="query-meta">
          Closed by: {{ store.formQueries[field.id].closedBy }} on
          {{ store.formQueries[field.id].closedAt }}
        </p>
        <p class="query-history-info">
          This query is permanently resolved and closed.
        </p>
      </div>
    </div>
  </div>
</template>

<script setup>
const props = defineProps({
  field: {
    type: Object,
    required: true,
  },
  activeQueryPanels: {
    type: Object,
    required: true,
  },
  queryInputs: {
    type: Object,
    required: true,
  },
  queryResponses: {
    type: Object,
    required: true,
  },
  store: {
    type: Object,
    required: true,
  },
});

defineEmits([
  "toggle-query-panel",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
  "update-query-input",
  "update-query-response",
]);

function getQueryStatus(fieldId) {
  const query = props.store.formQueries[fieldId];
  return query ? query.status : "NONE";
}
</script>
