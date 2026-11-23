<template>
  <div id="app">
    <!-- Accessible Skip to Content Link -->
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <!-- Persistent Demo Mode Synthetic Data Banner -->
    <DemoBanner />

    <!-- Portal Header with the persistent sync status button -->
    <PortalHeader />

    <!-- Global Floating Sync Drawer -->
    <SyncDrawer />

    <!-- Main Layout -->
    <div class="portal-container">
      <!-- Patient Navigation Tabs -->
      <nav class="portal-navigation" aria-label="Participant navigation">
        <TabBar />

        <button
          id="btn-mobile-session-toggle"
          type="button"
          class="btn btn-secondary mobile-session-toggle"
          :aria-expanded="isMobileSessionExpanded ? 'true' : 'false'"
          aria-controls="participant-secondary-details"
          @click="isMobileSessionExpanded = !isMobileSessionExpanded"
        >
          <span>Participant Session</span>
          <span aria-hidden="true">{{
            isMobileSessionExpanded ? "Hide ▴" : "Details ▾"
          }}</span>
        </button>

        <!-- Active Participant Card -->
        <ParticipantCard :mobile-expanded="isMobileSessionExpanded" />
      </nav>

      <!-- Main Viewport -->
      <main id="main-content" class="portal-main" tabindex="-1">
        <!-- Urgent Re-Consent Notification Banner -->
        <ReconsentBanner />

        <!-- Version mismatch banner -->
        <SyncSuspendedBanner />

        <!--
          Every view stays mounted (the feature modules render into their
          elements); the ui store's currentView, which the router follows,
          picks the visible one.
        -->
        <component
          :is="route.component"
          v-for="route in viewRoutes"
          :key="route.name"
        />
        <UnavailableView />
      </main>
    </div>

    <!-- Modals host -->
    <SignatureModal />
    <ReconsentModal />
    <PinSetupModal />
    <PinUnlockModal />

    <!-- Activity log at the bottom for patient transparency & 21 CFR compliance review -->
    <LedgerFooter />
  </div>
</template>

<script setup>
import { ref } from "vue";

import DemoBanner from "./components/DemoBanner.vue";
import LedgerFooter from "./components/LedgerFooter.vue";
import ParticipantCard from "./components/ParticipantCard.vue";
import PinSetupModal from "./components/PinSetupModal.vue";
import PinUnlockModal from "./components/PinUnlockModal.vue";
import PortalHeader from "./components/PortalHeader.vue";
import ReconsentBanner from "./components/ReconsentBanner.vue";
import ReconsentModal from "./components/ReconsentModal.vue";
import SignatureModal from "./components/SignatureModal.vue";
import SyncDrawer from "./components/SyncDrawer.vue";
import SyncSuspendedBanner from "./components/SyncSuspendedBanner.vue";
import TabBar from "./components/TabBar.vue";
import { viewRoutes } from "./router/routes.js";
import UnavailableView from "./views/UnavailableView.vue";

const isMobileSessionExpanded = ref(false);
</script>
