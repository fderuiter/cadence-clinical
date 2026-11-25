<template>
  <router-link
    v-if="recordLocation"
    id="inspector-open-record"
    class="inspector-link"
    :to="recordLocation"
  >
    Open participant's data
  </router-link>
  <router-link
    v-if="dataEntryLocation"
    id="inspector-open-data-entry"
    class="inspector-link"
    :to="dataEntryLocation"
  >
    Open in data entry
  </router-link>
  <router-link
    v-if="isDemoBuild() && handoffLocation"
    id="inspector-handoff-standalone"
    data-testid="inspector-ecrf-handoff"
    class="inspector-link"
    :to="handoffLocation"
  >
    Open standalone form view
  </router-link>
</template>

<script setup lang="ts">
import type { RouteLocationRaw } from "vue-router";
import { isDemoBuild } from "../../utils/demoMode";

defineProps<{
  recordLocation?: RouteLocationRaw | null;
  dataEntryLocation?: RouteLocationRaw | null;
  handoffLocation?: RouteLocationRaw | null;
}>();
</script>

<style scoped>
.inspector-link {
  align-self: flex-start;
  color: var(--accent);
  max-width: 100%;
  overflow-wrap: anywhere;
}
</style>
