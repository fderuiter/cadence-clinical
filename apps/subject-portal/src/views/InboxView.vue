<template>
  <section
    id="view-inbox"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-inbox' }"
    :hidden="ui.currentView !== 'view-inbox'"
    :inert="ui.currentView !== 'view-inbox' || undefined"
    :role="hasInboxTab ? 'tabpanel' : 'region'"
    :aria-labelledby="hasInboxTab ? 'btn-tab-btn-inbox' : undefined"
    :aria-label="hasInboxTab ? undefined : 'My Notification Inbox'"
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

    <ClinicalLiveRegion
      id="inbox-live-announcer"
      persistent-id
      variant="sr-only"
      :polite-message="inboxPoliteStatus"
      :assertive-message="inboxAssertiveAlert"
    />

    <!-- Inbox Loading Placeholder -->
    <div
      id="inbox-loading"
      class="loading-state"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      style="display: none"
    >
      Loading notifications...
    </div>

    <!-- Inbox Failure Container -->
    <div
      id="inbox-failure"
      class="card error-state inbox-failure-card"
      style="display: none"
    >
      <ClinicalLiveRegion
        id="inbox-failure-alert"
        persistent-id
        variant="inline"
        politeness="assertive"
        tone="error"
        :active="true"
        container-class="portal-failure-region"
      >
        <span class="portal-failure-copy">
          <span class="inbox-failure-title">Failed to load notifications.</span>
          <span id="inbox-error-msg" class="inbox-failure-msg"
            >Unknown error</span
          >
        </span>
        <template #actions>
          <button
            id="btn-retry-inbox"
            type="button"
            class="btn btn-primary portal-retry-btn"
          >
            Retry
          </button>
        </template>
      </ClinicalLiveRegion>
    </div>

    <div id="inbox-container" class="inbox-list">
      <div class="loading-state">Loading notifications...</div>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";
import { ClinicalLiveRegion, useStatusAnnouncer } from "ui";
import SiteContactLinks from "../components/SiteContactLinks.vue";
import { isScreenAvailable } from "../router/availability.js";
import { useInboxStore } from "../stores/inbox.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const inboxStore = useInboxStore();

const inboxStatusSummary = computed(() => {
  if (inboxStore.notificationsLoading) {
    return "Loading notifications...";
  }
  if (inboxStore.notificationsError) {
    return "";
  }
  const total = Array.isArray(inboxStore.notifications)
    ? inboxStore.notifications.length
    : 0;
  if (total === 0) {
    return "No notifications found.";
  }
  const unread = inboxStore.notifications.filter((n) => !n.is_read).length;
  return `${total} ${total === 1 ? "notification" : "notifications"} loaded, ${unread} unread.`;
});

const inboxErrorSummary = computed(() => {
  if (!inboxStore.notificationsError) return "";
  return `Failed to load notifications. ${inboxStore.notificationsError}`;
});

const {
  politeMessage: inboxPoliteStatus,
  assertiveMessage: inboxAssertiveAlert,
} = useStatusAnnouncer({
  debounceMs: 80,
  statusSource: () => inboxStatusSummary.value,
  errorSource: () => inboxErrorSummary.value,
});

const hasInboxTab = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemoModeActive() || isScreenAvailable("inbox");
});
</script>

<style scoped>
.portal-failure-region {
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  gap: 12px;
  width: 100%;
}

.portal-failure-copy {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.inbox-failure-title {
  font-weight: 700;
  font-size: 16px;
}

.inbox-failure-msg {
  font-size: 14px;
}

.portal-retry-btn {
  min-height: 48px;
  min-width: 120px;
}

.portal-failure-region :deep(.clinical-live-region__actions) {
  margin-left: 0;
  justify-content: center;
}
</style>
