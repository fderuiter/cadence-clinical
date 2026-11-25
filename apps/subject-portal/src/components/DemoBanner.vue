<template>
  <div
    v-if="showBanner"
    id="demo-mode-banner"
    class="demo-mode-banner"
    role="region"
    aria-label="Synthetic study data notice"
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
</template>

<script setup>
import { computed } from "vue";

import { useSessionStore } from "../stores/session.js";
import { isDemoModeActive } from "../utils/demoMode.js";

const sessionStore = useSessionStore();

const showBanner = computed(() => {
  void sessionStore.session?.isDemoMode;
  return isDemoModeActive();
});
</script>

<style scoped>
.demo-mode-banner {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: space-between;
  gap: 4px 16px;
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
  border-bottom: 1px solid var(--semantic-color-warning);
  padding: 4px 16px;
  font-size: var(--font-size-xs);
  font-weight: 600;
  line-height: 1.35;
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

@media (max-width: 767px) {
  .demo-mode-banner {
    padding: 6px 12px;
    font-size: var(--font-size-2xs);
    line-height: 1.35;
  }
}
</style>
