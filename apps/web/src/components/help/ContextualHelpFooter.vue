<template>
  <div class="drawer-footer">
    <div class="drawer-footer-primary">
      <button
        id="btn-escalate-support-ticket"
        type="button"
        class="btn btn-escalate"
        :disabled="isReadOnlyAuditor"
        :aria-disabled="isReadOnlyAuditor ? 'true' : undefined"
        :aria-describedby="
          isReadOnlyAuditor ? 'auditor-escalation-readonly-note' : undefined
        "
        :title="
          isReadOnlyAuditor
            ? 'Ticket escalation is disabled for read-only auditor roles'
            : 'Log Support Ticket pre-populated with current route context'
        "
        @click="emit('escalate')"
      >
        <Lock
          v-if="isReadOnlyAuditor"
          class="btn-icon"
          :size="16"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
        <Ticket
          v-else
          class="btn-icon"
          :size="16"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
        <span>Escalate to Support Ticket</span>
      </button>

      <p
        v-if="isReadOnlyAuditor"
        id="auditor-escalation-readonly-note"
        class="auditor-readonly-note"
      >
        Read-only auditor access: creating support tickets is disabled to
        preserve inspection independence. Use the Support Tickets tab to inspect
        scoped records.
      </p>

      <div class="drawer-hub-links">
        <button
          id="btn-open-knowledge-hub"
          type="button"
          class="btn-hub-link"
          @click="emit('open-hub', '/knowledge')"
        >
          <Library
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Browse SOP Hub</span>
        </button>
        <span class="hub-link-sep" aria-hidden="true">·</span>
        <button
          id="btn-open-tickets-hub"
          type="button"
          class="btn-hub-link"
          @click="emit('open-hub', '/tickets')"
        >
          <Ticket
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Support Queue</span>
        </button>
      </div>
    </div>
    <button type="button" class="btn btn-secondary" @click="emit('close')">
      Close
    </button>
  </div>
</template>

<script setup>
import { Library, Lock, Ticket } from "lucide-vue-next";

defineProps({
  isReadOnlyAuditor: { type: Boolean, default: false },
});

const emit = defineEmits(["escalate", "open-hub", "close"]);
</script>

<style scoped>
.drawer-footer {
  width: 100%;
  padding: 10px 16px;
  border-top: 1px solid var(--border);
  background-color: var(--semantic-color-surface-muted);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  flex-wrap: wrap;
  gap: 8px;
  box-sizing: border-box;
}

.drawer-footer-primary {
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 6px;
  flex: 1 1 200px;
  min-width: 0;
}

.auditor-readonly-note {
  margin: 0;
  font-size: 0.76rem;
  line-height: 1.4;
  color: var(--semantic-color-text-secondary);
  background-color: var(--semantic-color-surface-header);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 6px 8px;
}

.drawer-hub-links {
  display: flex;
  align-items: center;
  gap: 6px;
  flex-wrap: wrap;
}

.btn-hub-link {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  min-height: var(--semantic-control-target-min, 24px);
  background: none;
  border: none;
  padding: 2px 4px;
  color: var(--semantic-color-interactive-primary);
  font-size: 0.78rem;
  font-weight: 600;
  cursor: pointer;
}

.btn-hub-link:hover:not(:disabled) {
  text-decoration: underline;
}

.hub-link-sep {
  color: var(--semantic-color-text-secondary);
  font-size: 0.78rem;
}

.drawer-footer > .btn,
.drawer-footer-primary > .btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  box-sizing: border-box;
  gap: 6px;
  min-height: 44px;
  padding: 8px 14px;
  border-radius: 6px;
  font-size: 0.84rem;
  font-weight: 600;
  cursor: pointer;
  transition: all 0.15s;
}

.btn-escalate {
  border: 1px solid var(--primary);
  background-color: var(--text-main);
  color: var(--semantic-color-text-on-primary);
}

.btn-escalate:hover:not(:disabled) {
  background-color: var(--primary-dark);
  border-color: var(--primary-dark);
}

.btn-escalate:disabled {
  opacity: 0.55;
  cursor: not-allowed;
}

.drawer-footer > .btn-secondary {
  border: 1px solid var(--semantic-color-border-control);
  background-color: var(--semantic-color-surface);
  color: var(--semantic-color-text-body);
}

.drawer-footer > .btn-secondary:hover:not(:disabled) {
  background-color: var(--semantic-color-surface-header);
}

.btn-icon {
  flex-shrink: 0;
}
</style>
