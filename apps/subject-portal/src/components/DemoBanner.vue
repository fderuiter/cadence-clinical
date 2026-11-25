<template>
  <div
    v-if="showBanner"
    id="demo-mode-banner"
    class="demo-mode-banner"
    :class="{ 'is-expanded': detailsExpanded }"
    role="region"
    aria-label="Synthetic study data notice"
  >
    <div class="demo-mode-banner-compact-row">
      <span class="demo-mode-banner-compact-summary">
        Demo Mode &mdash; Synthetic study data
      </span>
      <button
        id="btn-demo-banner-details"
        type="button"
        class="demo-mode-banner-toggle"
        :aria-expanded="detailsExpanded ? 'true' : 'false'"
        aria-controls="demo-mode-banner-details"
        @click="detailsExpanded = !detailsExpanded"
      >
        {{ detailsExpanded ? "Hide" : "Details" }}
      </button>
    </div>

    <div
      id="demo-mode-banner-details"
      class="demo-mode-banner-details"
      :class="{ 'is-expanded': detailsExpanded }"
    >
      <p class="demo-mode-banner-copy">
        Demo Mode — Displaying synthetic study data. Entries and signatures in
        this sandbox are simulated locally and are not recorded in a clinical
        trial system.
      </p>
      <nav class="demo-mode-banner-links" aria-label="Co-hosted demo surfaces">
        <a
          id="participant-demo-link-docs"
          href="/cadence-clinical/"
          class="demo-mode-banner-link"
        >
          Documentation Portal
        </a>
        <span class="demo-mode-banner-sep" aria-hidden="true">&middot;</span>
        <a
          id="participant-demo-link-staff"
          href="/cadence-clinical/demo/"
          class="demo-mode-banner-link"
        >
          Staff Demo
        </a>
      </nav>
    </div>
  </div>
</template>

<script setup>
import { computed, ref } from "vue";

import { useSessionStore } from "../stores/session.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sessionStore = useSessionStore();
const detailsExpanded = ref(false);

const showBanner = computed(() => {
  void sessionStore.session?.isDemoMode;
  return isDemoModeActive();
});
</script>

<style scoped>
.demo-mode-banner {
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
  border-bottom: 1px solid var(--semantic-color-warning);
  padding: 4px 16px;
  font-size: 13px;
  font-weight: 600;
  line-height: 1.35;
}

.demo-mode-banner-compact-row {
  display: none;
}

.demo-mode-banner-details {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 4px 16px;
}

.demo-mode-banner-copy {
  flex: 1 1 360px;
  margin: 0;
  text-align: left;
}

.demo-mode-banner-links {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 6px;
}

.demo-mode-banner-link {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 2px 6px;
  border-radius: var(--semantic-radius-sm, 6px);
  color: var(--semantic-color-warning);
  font-weight: 700;
  text-decoration: underline;
  text-underline-offset: 2px;
  white-space: nowrap;
}

.demo-mode-banner-link:hover {
  background-color: var(--semantic-color-warning-bg-badge);
}

.demo-mode-banner-sep {
  color: var(--semantic-color-warning);
}

.demo-mode-banner-toggle {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-height: var(--semantic-control-target-min, 24px);
  min-width: var(--semantic-control-target-min, 24px);
  padding: 2px 8px;
  border-radius: var(--semantic-radius-sm, 6px);
  border: 1px solid var(--semantic-color-warning);
  background-color: transparent;
  color: var(--semantic-color-warning);
  font-family: inherit;
  font-size: 0.75rem;
  font-weight: 700;
  line-height: 1.2;
  cursor: pointer;
  flex-shrink: 0;
}

.demo-mode-banner-toggle:hover {
  background-color: var(--semantic-color-warning-bg-badge);
}

@media (max-width: 1023px), (max-height: 480px) {
  .demo-mode-banner {
    padding: 4px 12px;
    font-size: 0.75rem;
  }

  .demo-mode-banner-compact-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
    min-width: 0;
  }

  .demo-mode-banner-compact-summary {
    flex: 1 1 auto;
    min-width: 0;
    overflow: hidden;
    text-overflow: ellipsis;
    white-space: nowrap;
  }

  .demo-mode-banner-details:not(.is-expanded) {
    display: none;
  }

  .demo-mode-banner-details.is-expanded {
    margin-top: 6px;
    padding-top: 6px;
    border-top: 1px solid var(--semantic-color-warning);
  }
}
</style>
