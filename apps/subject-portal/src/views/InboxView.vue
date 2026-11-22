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
      <h2 id="inbox-view-heading" tabindex="-1">My Notification Inbox</h2>
      <p>
        Receive reminders, alerts, and system notifications regarding your trial
        schedule.
      </p>
    </div>

    <SiteContactLinks
      id="inbox-site-contacts"
      class="inbox-support-banner"
      heading="Study Site Coordinator Support"
      region-label="Inbox study site coordinator contacts"
    />

    <!-- Inbox Loading Placeholder -->
    <div id="inbox-loading" class="loading-state" style="display: none">
      Loading notifications...
    </div>

    <!-- Inbox Failure Container -->
    <div
      id="inbox-failure"
      class="card error-state inbox-failure-card"
      style="display: none"
    >
      <p class="inbox-failure-title">Failed to load notifications.</p>
      <p id="inbox-error-msg" class="inbox-failure-msg">Unknown error</p>
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
import SiteContactLinks from "../components/SiteContactLinks.vue";
import { useUiStore } from "../stores/ui.js";

const ui = useUiStore();
</script>
