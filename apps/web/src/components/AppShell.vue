<template>
  <div id="vue-app-root">
    <!-- Header -->
    <header>
      <div class="header-title-area">
        <h1>
          {{ brandNameFirst }} <span>{{ brandNameRest }}</span>
        </h1>
        <p>Interactive Web Demo & Regulatory Validation Sandbox</p>
      </div>
      <div
        class="header-user-profile"
        style="display: flex; align-items: center; gap: 16px"
      >
        <div class="header-badges" style="display: flex; gap: 8px">
          <span class="badge gxp">21 CFR Part 11</span>
          <span class="badge">GAMP 5</span>
          <span class="badge">IEC 62304</span>
          <!-- deid: ignore -->
        </div>
        <div
          class="user-status-card"
          style="
            background-color: var(--primary-light);
            padding: 6px 12px;
            border-radius: 6px;
            display: flex;
            align-items: center;
            gap: 12px;
            font-size: 0.85rem;
            border: 1px solid rgba(255, 255, 255, 0.15);
          "
        >
          <div class="user-info" style="color: white; text-align: right">
            <div style="font-weight: 700; color: var(--accent-light)">
              {{ authStore.identity?.username }}
            </div>
            <div style="font-size: 0.75rem; color: #cbd5e1">
              {{ (authStore.normalizedRoles || []).join(", ") }}
            </div>
          </div>
          <button
            v-if="authStore.isAuthenticated && !authStore.isDemoMode"
            id="btn-logout"
            class="btn btn-logout"
            style="
              padding: 4px 8px;
              font-size: 0.75rem;
              background-color: #ef4444;
              border: none;
              color: white;
              border-radius: 4px;
              cursor: pointer;
              font-weight: 600;
            "
            @click="authStore.logout()"
          >
            Logout
          </button>
          <button
            v-else-if="!authStore.isAuthenticated && !authStore.isDemoMode"
            id="btn-login"
            class="btn btn-login"
            style="
              padding: 4px 8px;
              font-size: 0.75rem;
              background-color: var(--accent);
              border: none;
              color: var(--primary);
              border-radius: 4px;
              cursor: pointer;
              font-weight: 600;
            "
            @click="authStore.login()"
          >
            Login
          </button>
          <div
            v-else
            style="font-size: 0.75rem; color: #cbd5e1; font-style: italic"
          >
            Demo Mode
          </div>
        </div>
      </div>
    </header>

    <div class="app-container">
      <!-- Sidebar -->
      <aside>
        <div>
          <div class="nav-title">Showcase Modules</div>
          <ul class="nav-menu">
            <li
              v-if="canViewMdr"
              id="tab-btn-mdr"
              class="nav-item"
              :class="{ active: $route.name === 'mdr' }"
            >
              <router-link v-slot="{ navigate }" to="/mdr" custom>
                <button type="button" @click="navigate">
                  <span>📋</span> MDR Protocol Designer
                </button>
              </router-link>
            </li>
            <li
              v-if="canViewEcrf"
              id="tab-btn-ecrf"
              class="nav-item"
              :class="{ active: $route.name === 'ecrf' }"
            >
              <router-link v-slot="{ navigate }" to="/ecrf" custom>
                <button type="button" @click="navigate">
                  <span>🩺</span> eCRF Form Engine
                </button>
              </router-link>
            </li>
            <li
              v-if="canViewCtms"
              id="tab-btn-ctms"
              class="nav-item"
              :class="{ active: $route.name === 'ctms' }"
            >
              <router-link v-slot="{ navigate }" to="/ctms" custom>
                <button type="button" @click="navigate">
                  <span>📊</span> CTMS Dashboard
                </button>
              </router-link>
            </li>
            <li
              v-if="canViewRules"
              id="tab-btn-rules"
              class="nav-item"
              :class="{ active: $route.name === 'rules' }"
            >
              <router-link v-slot="{ navigate }" to="/rules" custom>
                <button type="button" @click="navigate">
                  <span>⚙️</span> Rules Designer
                </button>
              </router-link>
            </li>
            <li
              v-if="canViewAudit"
              id="tab-btn-audit"
              class="nav-item"
              :class="{ active: $route.name === 'audit' }"
            >
              <router-link v-slot="{ navigate }" to="/audit" custom>
                <button type="button" @click="navigate">
                  <span>🔒</span> Cryptographic Ledger
                </button>
              </router-link>
            </li>
            <li
              v-if="canViewEtmf"
              id="tab-btn-etmf"
              class="nav-item"
              :class="{ active: $route.name === 'etmf' }"
            >
              <router-link v-slot="{ navigate }" to="/etmf" custom>
                <button type="button" @click="navigate">
                  <span>📁</span> eTMF Document Manager
                </button>
              </router-link>
            </li>
            <li
              id="tab-btn-notifications"
              class="nav-item"
              :class="{ active: $route.name === 'notifications' }"
            >
              <router-link v-slot="{ navigate }" to="/notifications" custom>
                <button type="button" @click="navigate">
                  <span>🔔</span> Notifications
                </button>
              </router-link>
            </li>
          </ul>
        </div>
        <div
          style="
            margin-top: auto;
            padding: 12px;
            font-size: 0.8rem;
            color: #64748b;
            background-color: #f8fafc;
            border-radius: 8px;
          "
        >
          <strong>Offline Engine:</strong> All operations run securely inside
          your browser's VM using in-memory mock handlers and cryptographic
          APIs.
        </div>
      </aside>

      <!-- Main Workspace -->
      <main>
        <slot />
      </main>
    </div>

    <!-- Quiet Screen Reader Status Alert Region -->
    <div
      class="sr-only-status-alerts"
      role="status"
      aria-live="polite"
      aria-atomic="true"
      style="
        position: absolute;
        width: 1px;
        height: 1px;
        padding: 0;
        margin: -1px;
        overflow: hidden;
        clip: rect(0, 0, 0, 0);
        border: 0;
      "
    >
      {{ screenReaderAnnouncement }}
    </div>

    <!-- Global Searchable Command Palette Overlay -->
    <CommandPaletteOverlay
      v-if="isCommandPaletteOpen"
      :is-open="isCommandPaletteOpen"
      @close="isCommandPaletteOpen = false"
    />

    <!-- Localized Shell Footer & Onboarding Telemetry Diagnostic Panel -->
    <footer
      style="
        background-color: var(--primary);
        color: white;
        padding: 8px 16px;
        display: flex;
        justify-content: space-between;
        align-items: center;
        border-top: 1px solid var(--border);
        font-size: 0.8rem;
        z-index: 100;
      "
    >
      <div>
        <span>System Status: </span>
        <span style="color: var(--accent-light); font-weight: 600"
          >Active Sandbox Mode</span
        >
      </div>
      <div>
        <button
          id="toggle-diagnostic-panel"
          class="btn"
          style="
            background-color: var(--neutral-dark);
            color: white;
            border: 1px solid rgba(255, 255, 255, 0.2);
            padding: 4px 10px;
            font-size: 0.75rem;
            border-radius: 4px;
            cursor: pointer;
            transition: all 0.2s;
          "
          @click="showDiagnosticPanel = !showDiagnosticPanel"
        >
          🔍
          {{
            showDiagnosticPanel
              ? "Hide Onboarding Telemetry"
              : "Show Onboarding Telemetry"
          }}
        </button>
      </div>
    </footer>

    <!-- Diagnostic Panel Overlay -->
    <div
      v-if="showDiagnosticPanel"
      class="diagnostic-panel"
      style="
        position: fixed;
        right: 0;
        bottom: 40px;
        width: 360px;
        max-height: 450px;
        background-color: white;
        border-left: 2px solid var(--border);
        border-top: 2px solid var(--border);
        box-shadow: -4px 0 12px rgba(0, 0, 0, 0.15);
        display: flex;
        flex-direction: column;
        z-index: 999;
        font-family: var(--font);
        color: var(--primary);
        border-top-left-radius: 8px;
      "
    >
      <div
        style="
          background-color: #f8fafc;
          padding: 12px;
          border-bottom: 1px solid var(--border);
          display: flex;
          justify-content: space-between;
          align-items: center;
          font-weight: bold;
          font-size: 0.9rem;
          border-top-left-radius: 6px;
        "
      >
        <span>Telemetry Diagnostic Log</span>
        <div style="display: flex; gap: 8px">
          <button
            class="btn btn-clear-events"
            style="
              padding: 2px 6px;
              font-size: 0.7rem;
              background-color: #ef4444;
              color: white;
              border: none;
              border-radius: 4px;
              cursor: pointer;
            "
            @click="onboardingStore.clearEvents()"
          >
            Clear
          </button>
          <button
            class="btn btn-reset-tour"
            style="
              padding: 2px 6px;
              font-size: 0.7rem;
              background-color: var(--accent);
              color: white;
              border: none;
              border-radius: 4px;
              cursor: pointer;
            "
            @click="onboardingStore.resetTour()"
          >
            Reset Tour
          </button>
          <button
            style="
              background: none;
              border: none;
              cursor: pointer;
              font-size: 1rem;
            "
            @click="showDiagnosticPanel = false"
          >
            ✕
          </button>
        </div>
      </div>
      <div
        style="
          flex: 1;
          overflow-y: auto;
          padding: 12px;
          display: flex;
          flex-direction: column;
          gap: 8px;
          max-height: 380px;
        "
      >
        <div
          v-if="onboardingStore.events.length === 0"
          style="
            color: #64748b;
            font-size: 0.8rem;
            font-style: italic;
            text-align: center;
            margin-top: 20px;
          "
        >
          No telemetry events recorded yet.
        </div>
        <div
          v-for="ev in [...onboardingStore.events].reverse()"
          :key="ev.id"
          class="telemetry-event-row"
          style="
            border-bottom: 1px solid #f1f5f9;
            padding-bottom: 6px;
            font-size: 0.75rem;
          "
        >
          <div
            style="
              display: flex;
              justify-content: space-between;
              color: #64748b;
              font-size: 0.7rem;
            "
          >
            <span>{{ ev.type.toUpperCase() }}</span>
            <span>{{ new Date(ev.timestamp).toLocaleTimeString() }}</span>
          </div>
          <div
            class="telemetry-target"
            style="font-weight: 600; margin-top: 2px"
          >
            {{ ev.target }}
          </div>
          <div v-if="ev.details" style="color: #475569; font-size: 0.7rem">
            {{ ev.details }}
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script setup>
import { ref, computed, watch, onMounted, onUnmounted } from "vue";
import { useAuthStore } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { useOnboardingStore } from "../stores/onboarding";
import { hasRequiredRole } from "../router";
import CommandPaletteOverlay from "./CommandPaletteOverlay.vue";

const authStore = useAuthStore();
const clinicalStore = useClinicalStore();
const onboardingStore = useOnboardingStore();

const brandName = import.meta.env.VITE_BRAND_NAME || "Cadence Clinical";
const parts = brandName.split(" ");
const brandNameFirst = parts[0] || "";
const brandNameRest = parts.slice(1).join(" ") || "";

const screenReaderAnnouncement = ref("");
let refreshTimer = null;

const isCommandPaletteOpen = ref(false);
const showDiagnosticPanel = ref(false);

const handleGlobalKeydown = (e) => {
  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    isCommandPaletteOpen.value = !isCommandPaletteOpen.value;
  }
};

// Watch background session authentication silent refresh events
watch(
  () => authStore.isAuthenticated,
  (newVal) => {
    if (newVal) {
      screenReaderAnnouncement.value =
        "System status: Session silently re-authenticated and Keycloak tokens refreshed successfully.";
    } else {
      screenReaderAnnouncement.value = "System status: Session logged out.";
    }
  }
);

// Emulate quiet background token refresh events periodically in demo/sandbox modes
onMounted(() => {
  document.addEventListener("keydown", handleGlobalKeydown);
  refreshTimer = setInterval(() => {
    if (authStore.isAuthenticated) {
      screenReaderAnnouncement.value =
        "System status: Quiet background token refresh and security re-authentication completed successfully.";
    }
  }, 45000); // Trigger every 45s to avoid flood but remain active
});

onUnmounted(() => {
  document.removeEventListener("keydown", handleGlobalKeydown);
  if (refreshTimer) {
    clearInterval(refreshTimer);
  }
});

// Watch offline syncing / ledger transaction commits
watch(
  () => clinicalStore.ledgerBlocks.length,
  (newVal, oldVal) => {
    if (newVal > oldVal) {
      screenReaderAnnouncement.value = `System status: Regulatory ledger block synchronized. Total blocks: ${newVal}.`;
    }
  }
);

const canViewMdr = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "sponsor_designer",
    "data_manager",
    "sponsor_admin",
  ]);
});

const canViewEcrf = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "site_investigator",
    "crc",
    "data_manager",
    "sponsor_admin",
  ]);
});

const canViewCtms = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "cra",
    "monitor",
    "sponsor_admin",
  ]);
});

const canViewRules = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "sponsor_designer",
    "data_manager",
    "sponsor_admin",
  ]);
});

const canViewAudit = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "auditor",
    "tmf_auditor",
    "sponsor_admin",
  ]);
});

const canViewEtmf = computed(() => {
  return hasRequiredRole(authStore.normalizedRoles, [
    "cra",
    "monitor",
    "auditor",
    "tmf_auditor",
    "sponsor_admin",
  ]);
});
</script>
