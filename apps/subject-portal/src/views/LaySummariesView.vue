<template>
  <section
    id="view-lay-summaries"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-lay-summaries' }"
    :hidden="ui.currentView !== 'view-lay-summaries'"
    :inert="ui.currentView !== 'view-lay-summaries' || undefined"
    :role="hasLaySummariesTab ? 'tabpanel' : 'region'"
    :aria-labelledby="
      hasLaySummariesTab ? 'btn-tab-btn-lay-summaries' : undefined
    "
    :aria-label="
      hasLaySummariesTab ? undefined : 'Plain-Language Clinical Trial Results'
    "
    tabindex="0"
  >
    <div class="view-header">
      <h2 id="lay-summaries-view-heading" tabindex="-1">
        Plain-Language Clinical Trial Results
      </h2>
      <p>
        Easy-to-read summaries of what this study found, including potential
        benefits and side effects.
      </p>
    </div>

    <div
      v-if="!isDemo"
      id="lay-summaries-unavailable"
      class="card"
      role="status"
      aria-live="polite"
      style="margin-bottom: 20px"
    >
      <h3 style="margin-top: 0">Trial Summary Not Yet Available</h3>
      <p
        style="
          font-size: var(--font-size-sm);
          color: var(--text-muted);
          line-height: 1.5;
        "
      >
        Plain-language clinical trial results have not been published for your
        study in this portal yet. Your study team will notify you when a
        plain-language study summary is ready to read.
      </p>
    </div>

    <div v-else class="card" style="margin-bottom: 20px">
      <div style="display: flex; gap: 8px; margin-bottom: 8px">
        <span class="badge badge-gxp">Demo Sample</span>
        <span class="badge badge-regulatory">Phase III Results</span>
      </div>
      <h3 style="margin-top: 0">
        Sample Plain Language Summary: Trial Findings Preview
      </h3>
      <p
        style="
          font-size: var(--font-size-sm);
          color: var(--text-muted);
          line-height: 1.5;
        "
      >
        Synthetic demonstration preview of how plain-language study results will
        appear after trial completion and regulatory approval.
      </p>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";

import { isScreenAvailable } from "../router/availability.js";
import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const isDemo = computed(() => {
  void session.session?.isDemoMode;
  return isDemoModeActive();
});
const hasLaySummariesTab = computed(() => {
  void session.session?.isDemoMode;
  void session.session?.token;
  void session.session?.userId;
  void session.session?.isOfflineMode;
  return isDemo.value || isScreenAvailable("lay-summaries");
});
</script>
