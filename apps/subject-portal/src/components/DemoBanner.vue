<template>
  <div
    v-if="showBanner"
    id="demo-mode-banner"
    class="demo-mode-banner"
    role="region"
    aria-label="Synthetic study data notice"
  >
    Demo Mode — Displaying synthetic study data. Entries and signatures in this
    sandbox are simulated locally and are not recorded in a clinical trial
    system.
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
  background-color: var(--semantic-color-warning-bg);
  color: var(--semantic-color-warning);
  border-bottom: 1px solid var(--semantic-color-warning);
  padding: 8px 16px;
  font-size: var(--font-size-xs, 0.8125rem);
  font-weight: 600;
  text-align: center;
}

@media (max-width: 767px) {
  .demo-mode-banner {
    padding: 4px 12px;
    font-size: var(--font-size-2xs, 0.6875rem);
    line-height: 1.25;
  }
}
</style>
