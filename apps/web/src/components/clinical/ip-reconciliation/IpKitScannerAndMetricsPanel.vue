<template>
  <div class="intro-meta-block">
    <div class="badge-row">
      <span class="badge badge-gxp">21 CFR Part 11</span>
      <span class="badge badge-rtsm">RTSM Supply Chain</span>
      <span class="badge badge-atomic">Atomic Transaction</span>
    </div>
    <p class="drawer-subtitle">
      Continuous optical barcode scanning, real-time lot discrepancy validation,
      residual volume recording, and atomic Part 11 inventory status commitment.
    </p>
  </div>

  <!-- Context Bar -->
  <div class="context-bar">
    <div class="context-item">
      <span class="context-label">Study:</span>
      <span class="context-val">{{ studyId }}</span>
    </div>
    <div class="context-item">
      <span class="context-label">Site:</span>
      <span class="context-val">{{ siteId }}</span>
    </div>
    <div class="context-item">
      <span class="context-label">Pharmacist / Investigator:</span>
      <span class="context-val">{{ pharmacistName }}</span>
    </div>
    <div class="context-actions">
      <button
        type="button"
        class="btn btn-sm btn-secondary"
        :disabled="isLoadingInventory"
        @click="emit('refresh-inventory')"
      >
        <RefreshCw
          :size="16"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
        <span>Refresh Inventory</span>
      </button>
    </div>
  </div>

  <!-- Optical Barcode Scanner Input Zone -->
  <section class="scanner-section">
    <div class="scanner-card">
      <div class="scanner-header">
        <label for="barcode-scanner-input" class="scanner-label">
          <ScanBarcode
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Multi-Barcode Scanner Buffer Input</span>
        </label>
        <span class="focus-indicator badge badge-active"
          >Auto-Focus Active</span
        >
      </div>
      <div class="scanner-input-row">
        <ClinicalInput
          id="barcode-scanner-input"
          :model-value="barcodeInputValue"
          label="Multi-Barcode Scanner Buffer Input"
          hide-label
          density="compact"
          :show-query-flag="false"
          extra-class="scanner-field"
          control-class="barcode-input"
          placeholder="Scan or enter kit barcode (e.g. KIT-1001 or KIT-1001|LOT-A100)..."
          autocomplete="off"
          @update:model-value="
            emit('update:barcodeInputValue', String($event ?? ''))
          "
          @keydown.enter.prevent="emit('scan')"
        />
        <button
          type="button"
          class="btn btn-primary btn-scan-add"
          @click="emit('scan')"
        >
          + Add to Buffer
        </button>
      </div>
      <p class="scanner-help">
        Format supported: <code>KIT_NUMBER</code> or
        <code>KIT_NUMBER|LOT_NUMBER</code>. Press <strong>Enter</strong> (or
        optical scanner carriage return) to append immediately.
      </p>
    </div>

    <!-- Warning / Duplicate / Discrepancy Banner -->
    <div v-if="warningMessage" class="alert-banner alert-warning" role="alert">
      <AlertTriangle
        :size="16"
        class="alert-icon"
        aria-hidden="true"
        :stroke-width="1.75"
        focusable="false"
      />
      <span class="alert-text">{{ warningMessage }}</span>
      <button
        type="button"
        class="btn-alert-dismiss"
        aria-label="Dismiss warning"
        @click="emit('dismiss-warning')"
      >
        <X
          :size="16"
          aria-hidden="true"
          :stroke-width="1.75"
          focusable="false"
        />
      </button>
    </div>
  </section>

  <!-- Live Batch Summary Metrics -->
  <section class="metrics-grid">
    <div class="metric-card">
      <span class="metric-num tabular-nums">{{ totalScannedCount }}</span>
      <span class="metric-label">Total Scanned Kits</span>
    </div>
    <div class="metric-card metric-returned">
      <span class="metric-num tabular-nums">{{ returnedToSiteCount }}</span>
      <span class="metric-label">Returned to Site</span>
    </div>
    <div class="metric-card metric-quarantined">
      <span class="metric-num tabular-nums">{{ quarantinedCount }}</span>
      <span class="metric-label">Quarantined (Destruction)</span>
    </div>
    <div class="metric-card metric-depot">
      <span class="metric-num tabular-nums">{{ returnedToDepotCount }}</span>
      <span class="metric-label">Returned to Depot</span>
    </div>
    <div class="metric-card metric-discrepancy">
      <span class="metric-num tabular-nums">{{ lotMismatchCount }}</span>
      <span class="metric-label">Lot Discrepancies</span>
    </div>
  </section>

  <!-- Bulk Action Toolbar -->
  <div v-if="totalScannedCount > 0" class="toolbar">
    <div class="bulk-actions">
      <span class="toolbar-label">Bulk Set Disposition:</span>
      <button
        type="button"
        class="btn btn-xs btn-outline-success"
        @click="emit('bulk-disposition', 'RETURNED_TO_SITE')"
      >
        All Returned to Site
      </button>
      <button
        type="button"
        class="btn btn-xs btn-outline-warning"
        @click="emit('bulk-disposition', 'QUARANTINED_FOR_DESTRUCTION')"
      >
        All Quarantine
      </button>
      <button
        type="button"
        class="btn btn-xs btn-outline-primary"
        @click="emit('bulk-disposition', 'RETURNED_TO_DEPOT')"
      >
        All Return to Depot
      </button>
    </div>
    <button
      type="button"
      class="btn btn-xs btn-ghost-danger tabular-nums"
      @click="emit('clear-all')"
    >
      Clear All ({{ totalScannedCount }})
    </button>
  </div>
</template>

<script setup lang="ts">
import { ClinicalInput } from "ui";
import { AlertTriangle, RefreshCw, ScanBarcode, X } from "lucide-vue-next";

defineProps<{
  studyId: string;
  siteId: string;
  pharmacistName: string;
  isLoadingInventory: boolean;
  barcodeInputValue: string;
  warningMessage: string;
  totalScannedCount: number;
  returnedToSiteCount: number;
  quarantinedCount: number;
  returnedToDepotCount: number;
  lotMismatchCount: number;
}>();

const emit = defineEmits<{
  (e: "refresh-inventory"): void;
  (e: "update:barcodeInputValue", value: string): void;
  (e: "scan"): void;
  (e: "dismiss-warning"): void;
  (
    e: "bulk-disposition",
    disposition:
      "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT"
  ): void;
  (e: "clear-all"): void;
}>();
</script>

<style scoped src="../bulkIpKitReconciliation.css"></style>
