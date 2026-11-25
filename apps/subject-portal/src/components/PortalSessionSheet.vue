<template>
  <ClinicalDrawer
    v-if="!isWideDesktopViewport && ui.sessionDrawerOpen"
    id="participant-session-drawer"
    v-model="ui.sessionDrawerOpen"
    overlay-id="participant-session-overlay"
    :position="isMobileViewport ? 'bottom' : 'right'"
    size="sm"
    mobile-bottom-sheet
    title="Participant Session"
    close-label="Close participant session"
  >
    <ParticipantCard mobile-expanded in-drawer />
  </ClinicalDrawer>
  <div
    v-else-if="!isWideDesktopViewport"
    id="participant-session-drawer"
    hidden
    inert
    aria-hidden="true"
  >
    <ParticipantCard
      mobile-expanded
      in-drawer
      :suppress-inline-alerts="showInlineSessionAlert"
    />
  </div>
</template>

<script setup>
import { ClinicalDrawer } from "ui";
import { watch } from "vue";

import { usePortalSessionChrome } from "../composables/usePortalSessionChrome.js";
import ParticipantCard from "./ParticipantCard.vue";

const { ui, showInlineSessionAlert, isMobileViewport, isWideDesktopViewport } =
  usePortalSessionChrome();

watch(isWideDesktopViewport, (isWide) => {
  if (isWide && ui.sessionDrawerOpen) {
    ui.sessionDrawerOpen = false;
  }
});
</script>
