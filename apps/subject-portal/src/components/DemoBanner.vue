<template>
  <div
    v-if="showBanner"
    id="demo-mode-banner"
    class="demo-mode-banner"
    role="region"
    aria-label="Synthetic study data notice"
  >
    <p class="demo-mode-banner-copy">
      Demo Mode: Showing synthetic study data. Entries stay in this browser
      only.
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
  gap: 4px 12px;
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
  border-bottom: 1px solid var(--semantic-color-warning);
  padding: 4px 16px;
  font-size: 13px;
  font-weight: 600;
  line-height: 1.3;
}

.demo-mode-banner-copy {
  flex: 1 1 240px;
  min-width: 0;
  margin: 0;
  text-align: left;
}

.demo-mode-banner-links {
  display: inline-flex;
  flex-wrap: wrap;
  min-width: 0;
  max-width: 100%;
  align-items: center;
  gap: 4px;
}

.demo-mode-banner-link {
  display: inline-flex;
  align-items: center;
  min-height: var(--semantic-control-target-min, 24px);
  padding: 0 6px;
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
    padding: 4px 12px;
    gap: 2px 10px;
    font-size: 0.75rem;
    line-height: 1.25;
  }
}
</style>
