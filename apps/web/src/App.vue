<template>
  <div id="vue-app-root">
    <!-- Header -->
    <header>
      <div class="header-title-area">
        <h1>Cadence <span>Clinical</span></h1>
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
              {{ authStore.normalizedRoles.join(", ") }}
            </div>
          </div>
          <button
            v-if="authStore.isAuthenticated && !authStore.isDemoMode"
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
        <router-view />
      </main>
    </div>
  </div>
</template>

<script setup>
import { onMounted } from "vue";
import { useClinicalStore } from "./stores/clinical";
import { useAuthStore } from "./stores/auth";
import "./style.css";

const store = useClinicalStore();
const authStore = useAuthStore();

onMounted(async () => {
  if (store.ledgerBlocks.length === 0) {
    await store.addLedgerBlock(
      "GENESIS",
      {
        platform: "Cadence Clinical",
        environment: "Interactive Web Sandbox",
        compliantStandards: [
          "21 CFR Part 11",
          "GAMP 5 (Category 4/5)",
          "IEC 62304",
          "ISO 14155:2020",
        ],
      },
      "System boot and cryptographic ledger initialized successfully."
    );
  }
  await store.startSyncTimer();
});
</script>

<style>
/* Any additional global or fallback styles if needed */
</style>
