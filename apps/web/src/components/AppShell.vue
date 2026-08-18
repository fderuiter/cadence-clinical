<template>
  <div id="vue-app-root">
    <!-- Skip Link (WCAG 2.4.1 Bypass Blocks) -->
    <a href="#main-content" class="skip-link">Skip to main content</a>

    <!-- Header -->
    <header class="app-header">
      <div class="header-left">
        <div class="brand-logo-mark">⚡</div>
        <div class="header-title-area">
          <h1 class="brand-title">
            {{ brandNameFirst }}
            <span class="brand-title-rest">{{ brandNameRest }}</span>
          </h1>
          <p class="brand-subtitle">
            Automated Digital Data Flow & Regulatory eClinical Platform
          </p>
        </div>
      </div>

      <div class="header-center">
        <!-- Quick Command Palette Trigger -->
        <button
          class="cmd-palette-trigger"
          title="Search anything (Cmd+K)"
          @click="isCommandPaletteOpen = true"
        >
          <span class="search-icon">🔍</span>
          <span class="search-placeholder">Quick Search...</span>
          <kbd class="shortcut-badge">⌘K</kbd>
        </button>
      </div>

      <div class="header-right">
        <!-- Regulatory Badges -->
        <div class="header-badges">
          <span class="badge gxp">21 CFR Part 11</span>
          <span class="badge">GAMP 5</span>
          <span class="badge">IEC 62304</span>
        </div>

        <!-- Role / Persona Switcher for Instant Testing -->
        <div class="persona-switcher-box">
          <label for="persona-select" class="sr-only">Switch Active Role</label>
          <select
            id="persona-select"
            v-model="activePersonaKey"
            class="persona-select"
            title="Switch active user role & permissions"
            @change="onPersonaSelect"
          >
            <option v-for="p in PERSONA_PRESETS" :key="p.key" :value="p.key">
              {{ p.label }}
            </option>
          </select>
        </div>

        <!-- User Identity Card -->
        <div class="user-status-card">
          <div class="user-avatar">
            {{
              (authStore.identity?.username || "U")
                .substring(0, 2)
                .toUpperCase()
            }}
          </div>
          <div class="user-info">
            <div class="user-name">
              {{ authStore.identity?.username || "Demo User" }}
            </div>
            <div class="user-role-label">
              {{ activePersonaLabel }}
            </div>
          </div>
          <button
            v-if="authStore.isAuthenticated && !authStore.isDemoMode"
            id="btn-logout"
            class="btn btn-logout-action"
            title="Log out of session"
            @click="authStore.logout()"
          >
            Logout
          </button>
          <button
            v-else-if="!authStore.isAuthenticated && !authStore.isDemoMode"
            id="btn-login"
            class="btn btn-login-action"
            title="Log in to session"
            @click="authStore.login()"
          >
            Login
          </button>
        </div>
      </div>
    </header>

    <div class="app-container">
      <!-- Sidebar -->
      <aside class="app-sidebar">
        <div class="sidebar-nav-scroll">
          <!-- Section 1: Study Design & MDR -->
          <div class="nav-category">
            <div class="nav-category-title">Design &amp; Metadata</div>
            <ul class="nav-menu">
              <li
                v-if="
                  canAccess([
                    'sponsor_designer',
                    'data_manager',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-digitization"
                class="nav-item"
                :class="{ active: $route.name === 'digitization' }"
              >
                <router-link v-slot="{ navigate }" to="/digitization" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">⚡</span>
                    <span class="nav-label">Protocol AI Digitizer</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'sponsor_designer',
                    'data_manager',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-mdr"
                class="nav-item"
                :class="{
                  active:
                    $route.name === 'mdr' && $route.query.tab !== 'canvas',
                }"
              >
                <router-link v-slot="{ navigate }" to="/mdr" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">📋</span>
                    <span class="nav-label">Protocol &amp; SoA Designer</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'sponsor_designer',
                    'data_manager',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-crf-designer"
                class="nav-item"
                :class="{
                  active:
                    $route.name === 'mdr' && $route.query.tab === 'canvas',
                }"
              >
                <router-link v-slot="{ navigate }" to="/mdr?tab=canvas" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🎨</span>
                    <span class="nav-label">eCRF Visual Canvas</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'sponsor_designer',
                    'data_manager',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-icf-builder"
                class="nav-item"
                :class="{
                  active:
                    $route.name === 'icf-builder' ||
                    $route.name === 'econsent-authoring',
                }"
              >
                <router-link v-slot="{ navigate }" to="/icf-builder" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">📝</span>
                    <span class="nav-label">eConsent &amp; ICF Builder</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'sponsor_designer',
                    'data_manager',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-rules"
                class="nav-item"
                :class="{ active: $route.name === 'rules' }"
              >
                <router-link v-slot="{ navigate }" to="/rules" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">⚙️</span>
                    <span class="nav-label">Study Rules &amp; Queries</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'sponsor_designer',
                    'data_manager',
                    'sponsor_admin',
                    'cra',
                    'monitor',
                    'site_investigator',
                    'crc',
                  ])
                "
                id="tab-btn-amendment-diff"
                class="nav-item"
                :class="{ active: $route.name === 'amendment-diff' }"
              >
                <router-link v-slot="{ navigate }" to="/amendment-diff" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🔀</span>
                    <span class="nav-label">Amendments &amp; Migration</span>
                  </button>
                </router-link>
              </li>
            </ul>
          </div>

          <!-- Section 2: Study Execution & EDC -->
          <div class="nav-category">
            <div class="nav-category-title">Execution &amp; EDC</div>
            <ul class="nav-menu">
              <li
                v-if="
                  canAccess([
                    'site_investigator',
                    'crc',
                    'data_manager',
                    'sponsor_admin',
                    'cra',
                    'monitor',
                  ])
                "
                id="tab-btn-ecrf"
                class="nav-item"
                :class="{ active: $route.name === 'ecrf' }"
              >
                <router-link v-slot="{ navigate }" to="/ecrf" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🩺</span>
                    <span class="nav-label">eCRF Data Capture</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'data_manager',
                    'sponsor_designer',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-coding"
                class="nav-item"
                :class="{ active: $route.name === 'coding' }"
              >
                <router-link v-slot="{ navigate }" to="/coding" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🏷️</span>
                    <span class="nav-label">Medical Coding</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'data_manager',
                    'sponsor_designer',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-data-lock"
                class="nav-item"
                :class="{ active: $route.name === 'data-lock' }"
              >
                <router-link v-slot="{ navigate }" to="/data-lock" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🔐</span>
                    <span class="nav-label">Data Lock Console</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="canAccess(['cra', 'monitor', 'sponsor_admin'])"
                id="tab-btn-ctms"
                class="nav-item"
                :class="{ active: $route.name === 'ctms' }"
              >
                <router-link v-slot="{ navigate }" to="/ctms" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">📊</span>
                    <span class="nav-label">CTMS Operations</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'cra',
                    'monitor',
                    'auditor',
                    'tmf_auditor',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-etmf"
                class="nav-item"
                :class="{ active: $route.name === 'etmf' }"
              >
                <router-link v-slot="{ navigate }" to="/etmf" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">📁</span>
                    <span class="nav-label">eTMF Document Vault</span>
                  </button>
                </router-link>
              </li>
              <li
                id="tab-btn-quality"
                class="nav-item"
                :class="{ active: $route.name === 'quality' }"
              >
                <router-link v-slot="{ navigate }" to="/quality" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🎯</span>
                    <span class="nav-label">Quality &amp; RBQM Cockpit</span>
                  </button>
                </router-link>
              </li>
              <li
                id="tab-btn-tickets"
                class="nav-item"
                :class="{ active: $route.name === 'tickets' }"
              >
                <router-link v-slot="{ navigate }" to="/tickets" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🎫</span>
                    <span class="nav-label">Clinical Issues &amp; Tickets</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="canAccess(['auditor', 'tmf_auditor', 'sponsor_admin'])"
                id="tab-btn-audit"
                class="nav-item"
                :class="{
                  active: $route.name === 'audit' || $route.name === 'auditor',
                }"
              >
                <router-link v-slot="{ navigate }" to="/audit" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">🔒</span>
                    <span class="nav-label">Part 11 Audit Ledger</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  canAccess([
                    'data_manager',
                    'sponsor_designer',
                    'sponsor_admin',
                  ])
                "
                id="tab-btn-exports"
                class="nav-item"
                :class="{ active: $route.name === 'exports' }"
              >
                <router-link v-slot="{ navigate }" to="/exports" custom>
                  <button type="button" @click="navigate">
                    <span class="nav-icon">📤</span>
                    <span class="nav-label">Data Export Wizard</span>
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
                    <span class="nav-icon">🔔</span>
                    <span class="nav-label">Notifications</span>
                  </button>
                </router-link>
              </li>
            </ul>
          </div>

          <!-- Section 3: Decentralized Patient Portals -->
          <div class="nav-category">
            <div class="nav-category-title">Decentralized &amp; ePRO</div>
            <ul class="nav-menu">
              <li class="nav-item portal-nav-item">
                <a
                  href="http://localhost:5174/subject-portal/"
                  target="_blank"
                  rel="noopener noreferrer"
                  class="portal-link-btn"
                >
                  <span class="nav-icon">📱</span>
                  <span class="nav-label">Patient Subject Portal</span>
                  <span class="external-pill">Open ↗</span>
                </a>
              </li>
            </ul>
          </div>
        </div>

        <!-- Sidebar Footer Status -->
        <div class="sidebar-bottom-status">
          <div class="status-indicator-dot"></div>
          <div class="status-text">
            <strong>Engine:</strong> Active Sandbox Mode
          </div>
        </div>
      </aside>

      <!-- Main Workspace -->
      <main id="main-content" tabindex="-1">
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
import { useAuthStore, PERSONA_PRESETS } from "../stores/auth";
import { useClinicalStore } from "../stores/clinical";
import { useOnboardingStore } from "../stores/onboarding";
import { hasRequiredRole } from "../router";
import CommandPaletteOverlay from "./CommandPaletteOverlay.vue";

const authStore = useAuthStore();
const clinicalStore = useClinicalStore();
const onboardingStore = useOnboardingStore();

function canAccess(requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return hasRequiredRole(authStore.normalizedRoles, requiredRoles);
}

const brandName = import.meta.env.VITE_BRAND_NAME || "Cadence Clinical";
const parts = brandName.split(" ");
const brandNameFirst = parts[0] || "";
const brandNameRest = parts.slice(1).join(" ") || "";

const activePersonaKey = ref(authStore.currentPersona || "super_admin");

const activePersonaLabel = computed(() => {
  const p = PERSONA_PRESETS.find(
    (preset) => preset.key === activePersonaKey.value
  );
  return p ? p.label.replace(/^.+?\s/, "") : "Super Admin";
});

function onPersonaSelect() {
  authStore.switchPersona(activePersonaKey.value);
  screenReaderAnnouncement.value = `Switched persona to ${activePersonaLabel.value}. Permissions updated.`;
}

watch(
  () => authStore.currentPersona,
  (newVal) => {
    if (newVal && newVal !== activePersonaKey.value) {
      activePersonaKey.value = newVal;
    }
  }
);

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
</script>
