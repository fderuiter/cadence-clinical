<template>
  <Transition name="slide-up">
    <div
      v-if="selectedCount > 0"
      id="batch-ticket-action-bar"
      class="batch-action-bar-floating"
      role="region"
      aria-label="Batch Ticket Actions"
    >
      <div class="batch-bar-content">
        <div class="selection-info">
          <span class="selection-badge">{{ selectedCount }}</span>
          <span class="selection-text">
            {{ selectedCount === 1 ? '1 issue selected' : `${selectedCount} issues selected` }}
          </span>
        </div>

        <div class="action-buttons">
          <button
            type="button"
            class="btn btn-sm btn-success btn-action-resolve"
            title="Bulk Resolve selected tickets with Part 11 electronic signature"
            @click="$emit('resolve')"
          >
            <span class="btn-icon">✍️</span> Resolve (Bulk)
          </button>

          <button
            type="button"
            class="btn btn-sm btn-warning btn-action-escalate"
            title="Bulk Escalate selected tickets to Lead Monitor / PI"
            @click="$emit('escalate')"
          >
            <span class="btn-icon">🚨</span> Escalate
          </button>

          <button
            type="button"
            class="btn btn-sm btn-info btn-action-pause-sla"
            title="Bulk Pause SLA timers for selected tickets"
            @click="$emit('pause-sla')"
          >
            <span class="btn-icon">⏸</span> Pause SLA
          </button>

          <button
            type="button"
            class="btn btn-sm btn-outline-secondary btn-action-clear"
            title="Clear ticket selection"
            @click="$emit('clear')"
          >
            ✕ Clear
          </button>
        </div>
      </div>
    </div>
  </Transition>
</template>

<script setup>
defineProps({
  selectedCount: {
    type: Number,
    default: 0,
  },
  selectedTickets: {
    type: Array,
    default: () => [],
  },
});

defineEmits(["resolve", "escalate", "pause-sla", "clear"]);
</script>

<style scoped>
.batch-action-bar-floating {
  position: fixed;
  bottom: 24px;
  left: 50%;
  transform: translateX(-50%);
  z-index: 1050;
  background: #0f172a;
  color: #ffffff;
  border-radius: 12px;
  padding: 12px 20px;
  box-shadow: 0 20px 25px -5px rgba(0, 0, 0, 0.3), 0 8px 10px -6px rgba(0, 0, 0, 0.2);
  border: 1px solid #334155;
  min-width: 480px;
  max-width: 90vw;
}

.batch-bar-content {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 16px;
}

.selection-info {
  display: flex;
  align-items: center;
  gap: 10px;
}

.selection-badge {
  background: #3b82f6;
  color: #ffffff;
  font-weight: 700;
  font-size: 0.85rem;
  padding: 2px 10px;
  border-radius: 9999px;
}

.selection-text {
  font-size: 0.9rem;
  font-weight: 500;
  color: #f1f5f9;
}

.action-buttons {
  display: flex;
  align-items: center;
  gap: 8px;
}

.btn-success {
  background-color: #059669;
  border-color: #059669;
  color: #ffffff;
}

.btn-success:hover {
  background-color: #047857;
}

.btn-warning {
  background-color: #d97706;
  border-color: #d97706;
  color: #ffffff;
}

.btn-warning:hover {
  background-color: #b45309;
}

.btn-info {
  background-color: #0284c7;
  border-color: #0284c7;
  color: #ffffff;
}

.btn-info:hover {
  background-color: #0369a1;
}

.btn-outline-secondary {
  border: 1px solid #64748b;
  color: #cbd5e1;
  background: transparent;
}

.btn-outline-secondary:hover {
  background: #334155;
  color: #ffffff;
}

.slide-up-enter-active,
.slide-up-leave-active {
  transition: all 0.25s ease-out;
}

.slide-up-enter-from,
.slide-up-leave-to {
  transform: translate(-50%, 100%);
  opacity: 0;
}
</style>
