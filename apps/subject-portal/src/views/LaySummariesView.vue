<template>
  <section
    id="view-lay-summaries"
    class="portal-view"
    :class="{ active: ui.currentView === 'view-lay-summaries' }"
    role="tabpanel"
    aria-labelledby="btn-tab-btn-lay-summaries"
    tabindex="0"
  >
    <div class="view-header">
      <h2>Plain-Language Clinical Trial Results</h2>
      <p>
        EU CTR Regulation No 536/2014 Article 37 summary of trial findings,
        clinical benefits, and safety profiles.
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
      <p style="font-size: 14px; color: var(--text-muted); line-height: 1.5">
        Plain-language clinical trial results have not been published for your
        study in this portal yet. Your study team will notify you when an
        IRB-approved lay summary is ready for review.
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
      <p style="font-size: 14px; color: var(--text-muted); line-height: 1.5">
        Synthetic demonstration preview of how plain-language study results will
        appear after trial completion and regulatory approval.
      </p>
    </div>
  </section>
</template>

<script setup>
import { computed } from "vue";

import { useSessionStore } from "../stores/session.js";
import { useUiStore } from "../stores/ui.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const ui = useUiStore();
const session = useSessionStore();
const isDemo = computed(() => {
  void session.session?.isDemoMode;
  return isDemoModeActive();
});
</script>
