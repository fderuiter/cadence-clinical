<template>
  <div id="vue-app-root">
    <!-- Skip Link (WCAG 2.4.1 Bypass Blocks) -->
    <a
      href="#main-content"
      class="skip-link"
    >Skip to main content</a>

    <!-- Header -->
    <header class="app-header">
      <div class="header-left">
        <div class="brand-logo-mark">
          ⚡
        </div>
        <div class="header-title-area">
          <h1 class="brand-title">
            {{ brandNameFirst }}
            <span class="brand-title-rest">{{ brandNameRest }}</span>
          </h1>
          <p class="brand-subtitle">
            Automated Digital Data Flow & Regulatory eClinical Platform
          </p>
        </div>
        <button
          id="btn-mobile-menu-toggle"
          class="mobile-menu-btn"
          type="button"
          :aria-expanded="isMobileMenuOpen"
          aria-controls="mobile-nav-menu"
          aria-label="Toggle navigation menu"
          @click="isMobileMenuOpen = !isMobileMenuOpen"
        >
          <span
            class="hamburger-icon"
            aria-hidden="true"
          >{{
            isMobileMenuOpen ? "✕" : "☰"
          }}</span>
        </button>
      </div>

      <div
        id="mobile-nav-menu"
        class="header-nav-collapse"
        :class="{ 'is-open': isMobileMenuOpen }"
      >
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

          <!-- The Clinical Canvas: the study-centric workspace the screens below move into (#4833). -->
          <router-link
            id="btn-open-study-canvas"
            :to="studyCanvasPath"
            class="cmd-palette-trigger"
            title="Open the study workspace"
          >
            <span class="search-placeholder">Studies</span>
          </router-link>

          <!-- Contextual Help Trigger Button -->
          <button
            id="btn-contextual-help-trigger"
            class="cmd-palette-trigger btn-help-trigger"
            title="Open In-Page Contextual Guidance & SOPs (F1 or ?)"
            @click="knowledgeStore.toggleDrawer()"
          >
            <span class="search-icon">📖</span>
            <span class="search-placeholder">Help &amp; SOPs</span>
            <kbd class="shortcut-badge">?</kbd>
          </button>
        </div>

        <div class="header-right">
          <!-- Regulatory Badges -->
          <div class="header-badges">
            <span class="badge gxp">21 CFR Part 11</span>
            <span class="badge">GAMP 5</span>
            <span class="badge">IEC 62304</span>
          </div>

          <!-- Workspace view switcher: only personas the token's roles allow;
               switching never changes the user's roles. -->
          <div
            v-if="authStore.availablePersonas.length > 1"
            class="persona-switcher-box"
          >
            <label
              for="persona-select"
              class="sr-only"
            >Switch workspace view</label>
            <select
              id="persona-select"
              v-model="activePersonaKey"
              class="persona-select"
              title="Switch workspace view"
              @change="onPersonaSelect"
            >
              <option
                v-for="p in authStore.availablePersonas"
                :key="p.key"
                :value="p.key"
              >
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
                {{ authStore.identity?.username || "Not signed in" }}
              </div>
              <div class="user-role-label">
                {{ activePersonaLabel }}
              </div>
            </div>
            <button
              v-if="authStore.isAuthenticated"
              id="btn-logout"
              class="btn btn-logout-action"
              title="Log out of session"
              @click="authStore.logout()"
            >
              Logout
            </button>
            <button
              v-else
              id="btn-login"
              class="btn btn-login-action"
              title="Log in to session"
              @click="authStore.login()"
            >
              Login
            </button>
          </div>
        </div>
      </div>
    </header>

    <div class="app-container">
      <!-- Sidebar -->
      <aside class="app-sidebar">
        <div class="sidebar-nav-scroll">
          <!-- Section 1: Study Design & MDR -->
          <div
            v-if="hasAvailableScreen(DESIGN_SCREENS)"
            class="nav-category"
          >
            <div class="nav-category-title">
              Design &amp; Metadata
            </div>
            <ul class="nav-menu">
              <li
                v-if="
                  isScreenAvailable('digitization') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/digitization"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">⚡</span>
                    <span class="nav-label">Protocol AI Digitizer</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('mdr') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/mdr"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📋</span>
                    <span class="nav-label">Protocol &amp; SoA Designer</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('mdr') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/mdr?tab=canvas"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🎨</span>
                    <span class="nav-label">eCRF Visual Canvas</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('icf-builder') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/icf-builder"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📝</span>
                    <span class="nav-label">eConsent &amp; ICF Builder</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('rules') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/rules"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">⚙️</span>
                    <span class="nav-label">Study Rules &amp; Queries</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('amendment-diff') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/amendment-diff"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🔀</span>
                    <span class="nav-label">Amendments &amp; Migration</span>
                  </button>
                </router-link>
              </li>
            </ul>
          </div>

          <!-- Section 2: Study Execution & EDC -->
          <div class="nav-category">
            <div class="nav-category-title">
              Execution &amp; EDC
            </div>
            <ul class="nav-menu">
              <li
                v-if="
                  isScreenAvailable('ecrf') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/ecrf"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🩺</span>
                    <span class="nav-label">eCRF Data Capture</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('imaging') &&
                    canAccess([
                      'site_investigator',
                      'crc',
                      'data_manager',
                      'sponsor_admin',
                      'cra',
                      'monitor',
                      'sponsor_designer',
                    ])
                "
                id="tab-btn-imaging"
                class="nav-item"
                :class="{ active: $route.name === 'imaging' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/imaging"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🔬</span>
                    <span class="nav-label">DICOM &amp; RECIST Viewer</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('devices') &&
                    canAccess([
                      'site_investigator',
                      'crc',
                      'data_manager',
                      'sponsor_admin',
                      'cra',
                      'monitor',
                      'sponsor_designer',
                      'auditor',
                    ])
                "
                id="tab-btn-devices"
                class="nav-item"
                :class="{
                  active:
                    $route.name === 'devices' ||
                    $route.name === 'device-investigation',
                }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/devices"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🩺</span>
                    <span class="nav-label">Device &amp; IVD Suite</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('sensors')"
                id="tab-btn-sensors"
                class="nav-item"
                :class="{ active: $route.name === 'sensors' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/sensors"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">⌚</span>
                    <span class="nav-label">Connected Sensors &amp; IoT</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('dct')"
                id="tab-btn-dct"
                class="nav-item"
                :class="{ active: $route.name === 'dct' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/dct"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📹</span>
                    <span class="nav-label">Telehealth &amp; DTP Logistics</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('coding') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/coding"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🏷️</span>
                    <span class="nav-label">Medical Coding</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('data-lock') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/data-lock"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🔐</span>
                    <span class="nav-label">Data Lock Console</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('ctms') &&
                    canAccess(['cra', 'monitor', 'sponsor_admin'])
                "
                id="tab-btn-ctms"
                class="nav-item"
                :class="{ active: $route.name === 'ctms' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/ctms"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📊</span>
                    <span class="nav-label">CTMS Operations</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('participant-wallet')"
                id="tab-btn-participant-wallet"
                class="nav-item"
                :class="{ active: $route.name === 'participant-wallet' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/participant-wallet"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">💳</span>
                    <span class="nav-label">Participant Financial Wallet</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('lay-summaries')"
                id="tab-btn-lay-summaries"
                class="nav-item"
                :class="{ active: $route.name === 'lay-summaries' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/lay-summaries"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📜</span>
                    <span class="nav-label">EU CTR Lay Summaries</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('etmf') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/etmf"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📁</span>
                    <span class="nav-label">eTMF Document Vault</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('documents')"
                id="tab-btn-documents"
                class="nav-item"
                :class="{ active: $route.name === 'documents' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/documents"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🗂️</span>
                    <span class="nav-label">Documents Hub</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('safety')"
                id="tab-btn-safety"
                class="nav-item"
                :class="{ active: $route.name === 'safety' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/safety"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🛡️</span>
                    <span class="nav-label">Safety &amp; DSUR Compiler</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('quality')"
                id="tab-btn-quality"
                class="nav-item"
                :class="{ active: $route.name === 'quality' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/quality"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🎯</span>
                    <span class="nav-label">Quality &amp; RBQM Cockpit</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('tickets')"
                id="tab-btn-tickets"
                class="nav-item"
                :class="{ active: $route.name === 'tickets' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/tickets"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🎫</span>
                    <span class="nav-label">Clinical Issues &amp; Tickets</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('knowledge')"
                id="tab-btn-knowledge"
                class="nav-item"
                :class="{ active: $route.name === 'knowledge' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/knowledge"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📚</span>
                    <span class="nav-label">Knowledge &amp; SOP Hub</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('training')"
                id="tab-btn-training"
                class="nav-item"
                :class="{ active: $route.name === 'training' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/training"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🎓</span>
                    <span class="nav-label">Study Training &amp; Gating</span>
                  </button>
                </router-link>
              </li>

              <li
                v-if="
                  isScreenAvailable('audit') &&
                    canAccess(['auditor', 'tmf_auditor', 'sponsor_admin'])
                "
                id="tab-btn-audit"
                class="nav-item"
                :class="{
                  active: $route.name === 'audit' || $route.name === 'auditor',
                }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/audit"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🔒</span>
                    <span class="nav-label">Part 11 Audit Ledger</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="
                  isScreenAvailable('exports') &&
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
                <router-link
                  v-slot="{ navigate }"
                  to="/exports"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">📤</span>
                    <span class="nav-label">Data Export Wizard</span>
                  </button>
                </router-link>
              </li>
              <li
                v-if="isScreenAvailable('notifications')"
                id="tab-btn-notifications"
                class="nav-item"
                :class="{ active: $route.name === 'notifications' }"
              >
                <router-link
                  v-slot="{ navigate }"
                  to="/notifications"
                  custom
                >
                  <button
                    type="button"
                    @click="navigate"
                  >
                    <span class="nav-icon">🔔</span>
                    <span class="nav-label">Notifications</span>
                  </button>
                </router-link>
              </li>
            </ul>
          </div>

          <!-- Section 3: Decentralized Patient Portals -->
          <div class="nav-category">
            <div class="nav-category-title">
              Decentralized &amp; ePRO
            </div>
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
          <div class="status-indicator-dot" />
          <div class="status-text">
            <strong>Engine:</strong> Active Sandbox Mode
          </div>
        </div>
      </aside>

      <!-- Main Workspace -->
      <main
        id="main-content"
        tabindex="-1"
      >
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
        <span style="color: var(--accent-light); font-weight: 600">Active Sandbox Mode</span>
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
          <div
            v-if="ev.details"
            style="color: #475569; font-size: 0.7rem"
          >
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
import { useNotificationsStore } from "../stores/notifications";
import { useClinicalStore } from "../stores/clinical";
import { useOnboardingStore } from "../stores/onboarding";
import { useKnowledgeStore } from "../stores/knowledge";
import { hasRequiredRole } from "../router";
import { isScreenAvailable } from "../router/availability";
import CommandPaletteOverlay from "./CommandPaletteOverlay.vue";

const authStore = useAuthStore();
const notificationsStore = useNotificationsStore();
const clinicalStore = useClinicalStore();
const studyCanvasPath = computed(() =>
  clinicalStore.activeStudyId
    ? `/studies/${encodeURIComponent(clinicalStore.activeStudyId)}`
    : "/studies"
);
const onboardingStore = useOnboardingStore();
const knowledgeStore = useKnowledgeStore();

const DESIGN_SCREENS = [
  "digitization",
  "mdr",
  "icf-builder",
  "rules",
  "amendment-diff",
];

function hasAvailableScreen(routeNames) {
  return routeNames.some(isScreenAvailable);
}

// No .NET service serves the user's notifications yet (#4828).
function refreshNotifications() {
  if (isScreenAvailable("notifications")) {
    notificationsStore.fetchNotifications();
  }
}

function canAccess(requiredRoles) {
  if (!requiredRoles || requiredRoles.length === 0) return true;
  return hasRequiredRole(authStore.normalizedRoles, requiredRoles);
}

const brandName = import.meta.env.VITE_BRAND_NAME || "Cadence Clinical";
const parts = brandName.split(" ");
const brandNameFirst = parts[0] || "";
const brandNameRest = parts.slice(1).join(" ") || "";

const activePersonaKey = ref(authStore.currentPersona);

const activePersonaLabel = computed(() => {
  const p = PERSONA_PRESETS.find(
    (preset) => preset.key === activePersonaKey.value
  );
  return p ? p.label.replace(/^.+?\s/, "") : "";
});

function onPersonaSelect() {
  if (!authStore.switchPersona(activePersonaKey.value)) {
    activePersonaKey.value = authStore.currentPersona;
    return;
  }
  onboardingStore.loadPersonaState(activePersonaKey.value);
  clinicalStore.loadPersonaState(activePersonaKey.value);
  refreshNotifications();
  screenReaderAnnouncement.value = `Switched workspace view to ${activePersonaLabel.value}.`;
}

watch(
  () => authStore.currentPersona,
  (newVal) => {
    if (newVal) {
      if (newVal !== activePersonaKey.value) {
        activePersonaKey.value = newVal;
      }
      onboardingStore.loadPersonaState(newVal);
      clinicalStore.loadPersonaState(newVal);
      refreshNotifications();
    }
  },
  { immediate: true }
);

const screenReaderAnnouncement = ref("");

const isMobileMenuOpen = ref(false);
const isCommandPaletteOpen = ref(false);
const showDiagnosticPanel = ref(false);

const handleGlobalKeydown = (e) => {
  if ((e.metaKey || e.ctrlKey) && e.key.toLowerCase() === "k") {
    e.preventDefault();
    isCommandPaletteOpen.value = !isCommandPaletteOpen.value;
  }
};

// Announce sign-in state changes to screen readers
watch(
  () => authStore.isAuthenticated,
  (newVal) => {
    screenReaderAnnouncement.value = newVal
      ? "System status: Signed in."
      : "System status: Signed out.";
  }
);

onMounted(() => {
  document.addEventListener("keydown", handleGlobalKeydown);
});

onUnmounted(() => {
  document.removeEventListener("keydown", handleGlobalKeydown);
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
