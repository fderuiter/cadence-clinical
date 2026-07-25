<template>
  <div id="vue-app-root">
    <!-- Header -->
    <header>
      <div class="header-title-area">
        <h1>Cadence <span>Clinical</span></h1>
        <p>Interactive Web Demo & Regulatory Validation Sandbox</p>
      </div>
      <div class="header-badges">
        <span class="badge gxp">21 CFR Part 11</span>
        <span class="badge">GAMP 5</span>
        <span class="badge">IEC 62304</span>
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
import "./style.css";

const store = useClinicalStore();

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
