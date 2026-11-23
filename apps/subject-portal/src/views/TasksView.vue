<template>
  <section
    id="view-tasks"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-tasks' }"
    :hidden="ui.currentView !== 'view-tasks'"
    :inert="ui.currentView !== 'view-tasks' || undefined"
    :role="isTasksTabAvailable ? 'tabpanel' : 'region'"
    :aria-labelledby="isTasksTabAvailable ? 'btn-tab-btn-tasks' : undefined"
    :aria-label="isTasksTabAvailable ? undefined : 'My Daily Questionnaires'"
    tabindex="0"
  >
    <div class="view-header">
      <h2>My Daily Questionnaires</h2>
      <p>
        Please complete your scheduled questionnaires on time to help us ensure
        high data quality.
      </p>
    </div>

    <div
      v-if="tasksStore.taskActionError"
      id="tasks-action-error"
      role="alert"
      aria-live="assertive"
      style="
        margin-bottom: 16px;
        padding: 12px 16px;
        border-radius: 8px;
        background: var(--semantic-color-error-bg);
        border: 1px solid var(--semantic-color-error);
        color: var(--semantic-color-error);
        font-size: var(--font-size-sm, 0.875rem);
        font-weight: 600;
      "
    >
      {{ tasksStore.taskActionError }}
    </div>

    <!-- Tasks Loading Placeholder -->
    <div id="tasks-loading" class="loading-state" style="display: none">
      Loading your assigned tasks...
    </div>

    <!-- Tasks Failure Container -->
    <div
      id="tasks-failure"
      class="card error-state"
      style="
        display: none;
        text-align: center;
        padding: 32px;
        border: 1px solid var(--danger);
        margin-bottom: 16px;
      "
    >
      <p
        style="
          color: var(--danger);
          font-weight: bold;
          font-size: var(--font-size-md, 1rem);
        "
      >
        Failed to load assigned tasks.
      </p>
      <p
        id="tasks-error-msg"
        style="font-size: var(--font-size-sm, 0.875rem); margin-bottom: 12px"
      >
        Unknown error
      </p>
      <button id="btn-retry-tasks" type="button" class="btn btn-primary">
        Retry
      </button>
    </div>

    <!-- List of tasks -->
    <div id="tasks-list-container" class="tasks-list">
      <div class="loading-state">Loading your assigned tasks...</div>
    </div>

    <!-- Offline Sync Status Panel -->
    <div
      id="sync-queue-panel"
      class="card sync-queue-panel"
      style="margin-top: 24px"
    >
      <div
        class="panel-header"
        style="
          display: flex;
          flex-wrap: wrap;
          gap: 8px;
          justify-content: space-between;
          align-items: center;
          border-bottom: 1px solid var(--semantic-color-border);
          padding-bottom: 12px;
          margin-bottom: 12px;
        "
      >
        <h3 style="margin: 0; display: flex; align-items: center; gap: 8px">
          <span>🔄</span> Offline Sync Queue
        </h3>
        <button
          id="btn-sync-now"
          type="button"
          class="btn btn-secondary"
          style="padding: 6px 12px; font-size: var(--font-size-xs, 0.8125rem)"
        >
          Sync Now
        </button>
      </div>
      <div
        id="sync-queue-status-text"
        class="text-muted"
        style="font-size: var(--font-size-sm, 0.875rem); margin-bottom: 12px"
        role="status"
        aria-live="polite"
      >
        Checking sync status...
      </div>
      <div
        id="sync-queue-list"
        style="display: flex; flex-direction: column; gap: 8px"
      >
        <!-- Dynamically populated synced & queued submissions go here -->
      </div>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";
import { isScreenAvailable } from "../router/availability.js";
import { useSessionStore } from "../stores/session.js";
import { useTasksStore } from "../stores/tasks.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const tasksStore = useTasksStore();
const isTasksTabAvailable = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemoModeActive() || isScreenAvailable("tasks");
});
</script>
