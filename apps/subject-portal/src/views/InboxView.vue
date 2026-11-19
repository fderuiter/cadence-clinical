<template>
  <section
    id="view-inbox"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-inbox' }"
    role="tabpanel"
    aria-labelledby="btn-tab-btn-inbox"
    tabindex="0"
  >
    <div class="view-header">
      <h2>My Notification Inbox</h2>
      <p>
        Receive reminders, alerts, and system notifications regarding your trial
        schedule.
      </p>
    </div>

    <!-- Inbox Loading Placeholder -->
    <div id="inbox-loading" class="loading-state" style="display: none">
      Loading notifications...
    </div>

    <!-- Inbox Failure Container -->
    <div
      id="inbox-failure"
      class="card error-state"
      style="
        display: none;
        text-align: center;
        padding: 32px;
        border: 1px solid var(--danger);
        margin-bottom: 16px;
      "
    >
      <p style="color: var(--danger); font-weight: bold; font-size: 16px">
        Failed to load notifications.
      </p>
      <p id="inbox-error-msg" style="font-size: 14px; margin-bottom: 12px">
        Unknown error
      </p>
      <button id="btn-retry-inbox" type="button" class="btn btn-primary">
        Retry
      </button>
    </div>

    <div id="inbox-container" class="inbox-list">
      <div class="loading-state">Loading notifications...</div>
    </div>
  </section>
</template>

<script setup>
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();
</script>
