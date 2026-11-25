<template>
  <ClinicalDrawer
    ref="clinicalDrawerRef"
    :model-value="isOpen"
    :teleport="false"
    size="xl"
    :dismissible="!isSubmitting"
    initial-focus="#barcode-scanner-input"
    overlay-class="drawer-backdrop"
    panel-class="bulk-ip-reconciliation-drawer"
    aria-labelledby="drawer-heading"
    @close="closeDrawer"
  >
    <template #header>
      <div class="drawer-header">
        <div class="header-title-wrap">
          <ScanBarcode :size="18" class="header-icon" aria-hidden="true" />
          <h2 id="drawer-heading" class="drawer-title">
            Bulk IP Kit Reconciliation &amp; Batch Disposition Drawer
          </h2>
        </div>

        <button
          id="btn-close-bulk-ip-drawer"
          type="button"
          class="btn-close"
          aria-label="Close Bulk IP Kit Reconciliation Drawer"
          :disabled="isSubmitting"
          @click="closeDrawer"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </div>
    </template>

    <div class="drawer-body">
      <div class="intro-meta-block">
        <div class="badge-row">
          <span class="badge badge-gxp">21 CFR Part 11</span>
          <span class="badge badge-rtsm">RTSM Supply Chain</span>
          <span class="badge badge-atomic">Atomic Transaction</span>
        </div>
        <p class="drawer-subtitle">
          Continuous optical barcode scanning, real-time lot discrepancy
          validation, residual volume recording, and atomic Part 11 inventory
          status commitment.
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
            @click="loadInventory"
          >
            <RefreshCw :size="14" aria-hidden="true" />
            <span>Refresh Inventory</span>
          </button>
        </div>
      </div>

      <!-- Optical Barcode Scanner Input Zone -->
      <section class="scanner-section">
        <div class="scanner-card">
          <div class="scanner-header">
            <label for="barcode-scanner-input" class="scanner-label">
              <ScanBarcode :size="16" aria-hidden="true" />
              <span>Multi-Barcode Scanner Buffer Input</span>
            </label>
            <span class="focus-indicator badge badge-active"
              >Auto-Focus Active</span
            >
          </div>
          <div class="scanner-input-row">
            <ClinicalInput
              id="barcode-scanner-input"
              v-model="barcodeInputValue"
              label="Multi-Barcode Scanner Buffer Input"
              hide-label
              density="compact"
              :show-query-flag="false"
              extra-class="scanner-field"
              control-class="barcode-input"
              placeholder="Scan or enter kit barcode (e.g. KIT-1001 or KIT-1001|LOT-A100)..."
              autocomplete="off"
              @keydown.enter.prevent="handleBarcodeScan"
            />
            <button
              type="button"
              class="btn btn-primary btn-scan-add"
              @click="handleBarcodeScan"
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
        <div
          v-if="warningMessage"
          class="alert-banner alert-warning"
          role="alert"
        >
          <AlertTriangle :size="16" class="alert-icon" aria-hidden="true" />
          <span class="alert-text">{{ warningMessage }}</span>
          <button
            type="button"
            class="btn-alert-dismiss"
            aria-label="Dismiss warning"
            @click="warningMessage = ''"
          >
            <X :size="16" aria-hidden="true" />
          </button>
        </div>
      </section>

      <!-- Live Batch Summary Metrics -->
      <section class="metrics-grid">
        <div class="metric-card">
          <span class="metric-num">{{ scannedItems.length }}</span>
          <span class="metric-label">Total Scanned Kits</span>
        </div>
        <div class="metric-card metric-returned">
          <span class="metric-num">{{ returnedToSiteCount }}</span>
          <span class="metric-label">Returned to Site</span>
        </div>
        <div class="metric-card metric-quarantined">
          <span class="metric-num">{{ quarantinedCount }}</span>
          <span class="metric-label">Quarantined (Destruction)</span>
        </div>
        <div class="metric-card metric-depot">
          <span class="metric-num">{{ returnedToDepotCount }}</span>
          <span class="metric-label">Returned to Depot</span>
        </div>
        <div class="metric-card metric-discrepancy">
          <span class="metric-num">{{ lotMismatchCount }}</span>
          <span class="metric-label">Lot Discrepancies</span>
        </div>
      </section>

      <!-- Bulk Action Toolbar -->
      <div v-if="scannedItems.length > 0" class="toolbar">
        <div class="bulk-actions">
          <span class="toolbar-label">Bulk Set Disposition:</span>
          <button
            type="button"
            class="btn btn-xs btn-outline-success"
            @click="bulkSetDisposition('RETURNED_TO_SITE')"
          >
            All Returned to Site
          </button>
          <button
            type="button"
            class="btn btn-xs btn-outline-warning"
            @click="bulkSetDisposition('QUARANTINED_FOR_DESTRUCTION')"
          >
            All Quarantine
          </button>
          <button
            type="button"
            class="btn btn-xs btn-outline-primary"
            @click="bulkSetDisposition('RETURNED_TO_DEPOT')"
          >
            All Return to Depot
          </button>
        </div>
        <button
          type="button"
          class="btn btn-xs btn-ghost-danger"
          @click="clearAllScanned"
        >
          Clear All ({{ scannedItems.length }})
        </button>
      </div>

      <!-- Scanned Kits Buffer Table / Mobile Stacked Record Cards -->
      <div v-if="scannedItems.length === 0" class="empty-state-box">
        <div class="empty-icon">
          <Package :size="36" aria-hidden="true" />
        </div>
        <h3>Scan Buffer Empty</h3>
        <p>
          Scan kit barcodes with an optical scanner or enter kit numbers
          manually above to populate the reconciliation batch table.
        </p>
      </div>

      <ClinicalDataTable
        v-else
        title="Scanned Kits Reconciliation Buffer"
        hide-header
        plain
        :columns="batchColumns"
        :data="scannedItems"
        row-key="kitNumber"
        :page-size="Math.max(scannedItems.length, 50)"
        wrapper-class="table-container"
        table-class="batch-table"
        :row-class="resolveKitRowClass"
      >
        <template #cell-index="{ index }">
          {{ index + 1 }}
        </template>
        <template #cell-kitNumber="{ row }">
          {{ row.kitNumber }}
        </template>
        <template #cell-scannedLotNumber="{ row }">
          {{ row.scannedLotNumber }}
        </template>
        <template #cell-expectedLotNumber="{ row }">
          <span>{{ row.expectedLotNumber }}</span>
          <span v-if="row.hasLotMismatch" class="badge badge-alert-lot"
            >LOT MISMATCH</span
          >
        </template>
        <template #cell-volume="{ row, index }">
          <div class="volume-inputs">
            <ClinicalInput
              :id="`kit-residual-ml-${index}`"
              :model-value="row.residualVolumeMl ?? ''"
              type="number"
              min="0"
              step="0.1"
              label="Residual volume in mL"
              hide-label
              density="compact"
              :show-query-flag="false"
              control-class="input-sm volume-input"
              placeholder="mL"
              :attributes="{ 'aria-label': 'Residual volume in mL' }"
              @update:model-value="
                (val) =>
                  (row.residualVolumeMl =
                    val === '' || val === null ? null : Number(val))
              "
            />
            <ClinicalInput
              :id="`kit-residual-pct-${index}`"
              :model-value="row.residualVolumePercentage ?? ''"
              type="number"
              min="0"
              max="100"
              step="1"
              label="Residual percentage"
              hide-label
              density="compact"
              :show-query-flag="false"
              control-class="input-sm volume-input"
              placeholder="%"
              :attributes="{ 'aria-label': 'Residual percentage' }"
              @update:model-value="
                (val) =>
                  (row.residualVolumePercentage =
                    val === '' || val === null ? null : Number(val))
              "
            />
          </div>
        </template>
        <template #cell-disposition="{ row, index }">
          <ClinicalInput
            :id="`kit-disposition-${index}`"
            v-model="row.disposition"
            as="select"
            label="Disposition Action"
            hide-label
            density="compact"
            :show-query-flag="false"
            :control-class="[
              'select-sm',
              'disposition-select',
              `disp-${row.disposition.toLowerCase()}`,
            ]"
            :attributes="{ 'aria-label': 'Disposition Action' }"
          >
            <option value="RETURNED_TO_SITE">Returned to Site</option>
            <option value="QUARANTINED_FOR_DESTRUCTION">
              Quarantined for Destruction
            </option>
            <option value="RETURNED_TO_DEPOT">Returned to Depot</option>
          </ClinicalInput>
        </template>
        <template #cell-dispositionReason="{ row, index }">
          <ClinicalInput
            :id="`kit-reason-${index}`"
            v-model="row.dispositionReason"
            as="select"
            label="Disposition Reason"
            hide-label
            density="compact"
            :show-query-flag="false"
            control-class="select-sm reason-select"
            :attributes="{ 'aria-label': 'Disposition Reason' }"
          >
            <option value="NORMAL_RETURN">Normal Return</option>
            <option value="PATIENT_RETURN">Patient Return / Adherence</option>
            <option value="DAMAGED_SEAL">Damaged Seal / Tampered</option>
            <option value="EXPIRED">Expired Lot</option>
            <option value="LOT_MISMATCH">Lot Mismatch</option>
            <option value="OTHER">Other / Notes</option>
          </ClinicalInput>
        </template>
        <template #cell-flags="{ row }">
          <span v-if="row.hasLotMismatch" class="status-flag flag-warning">
            <AlertTriangle :size="13" aria-hidden="true" />
            <span>Lot Mismatch</span>
          </span>
          <span
            v-else-if="row.disposition === 'QUARANTINED_FOR_DESTRUCTION'"
            class="status-flag flag-quarantine"
          >
            <Lock :size="13" aria-hidden="true" />
            <span>Quarantined</span>
          </span>
          <span v-else class="status-flag flag-valid">
            <Check :size="13" aria-hidden="true" />
            <span>Valid</span>
          </span>
        </template>
        <template #cell-action="{ row, index }">
          <ClinicalIconButton
            class="btn-icon btn-remove"
            :label="`Remove kit ${row.kitNumber || index + 1} from reconciliation`"
            title="Remove item"
            @click="removeItem(index)"
          >
            <X :size="16" aria-hidden="true" />
          </ClinicalIconButton>
        </template>
      </ClinicalDataTable>

      <!-- 21 CFR Part 11 Electronic Signature / Change Rationale -->
      <section v-if="scannedItems.length > 0" class="esign-section">
        <div class="esign-card">
          <h4 class="esign-title">
            <FileSignature :size="16" aria-hidden="true" />
            <span
              >21 CFR Part 11 Electronic Signature &amp; Audit Rationale</span
            >
          </h4>
          <div class="esign-form-grid">
            <div class="form-group">
              <ClinicalInput
                id="pharmacist-signature-id"
                v-model="signerUserId"
                label="Signer Identity / User ID:"
                required
                density="compact"
                :show-query-flag="false"
                label-class="form-label"
                control-class="input-text"
                placeholder="e.g. site_pharmacist_01"
              />
            </div>

            <div class="form-group">
              <ClinicalInput
                id="reason-for-change-input"
                v-model="reasonForChange"
                label="Audit Change Rationale (X-Change-Reason):"
                required
                density="compact"
                :show-query-flag="false"
                label-class="form-label"
                control-class="input-text"
                placeholder="e.g. Shipment return carton batch reconciliation intake"
              />
            </div>

            <div class="form-group form-group-full">
              <ClinicalInput
                id="batch-notes-input"
                v-model="batchNotes"
                as="textarea"
                :rows="2"
                label="Pharmacist Notes / Shipment Reference:"
                density="compact"
                :show-query-flag="false"
                label-class="form-label"
                control-class="textarea-input"
                placeholder="Additional observations, courier tracking numbers, or carton condition..."
              />
            </div>
          </div>
        </div>
      </section>
    </div>

    <template #footer>
      <div class="drawer-footer">
        <div class="footer-summary">
          <span v-if="scannedItems.length > 0" class="summary-text">
            Ready to reconcile <strong>{{ scannedItems.length }}</strong> kit(s)
            with digital accountability certificate.
          </span>
        </div>

        <div class="footer-buttons">
          <button
            type="button"
            class="btn btn-secondary"
            :disabled="isSubmitting"
            @click="closeDrawer"
          >
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-success btn-submit-batch"
            :disabled="
              scannedItems.length === 0 ||
              isSubmitting ||
              !signerUserId.trim() ||
              !reasonForChange.trim()
            "
            @click="submitBatchReconciliation"
          >
            <Loader2
              v-if="isSubmitting"
              :size="16"
              class="spin-icon"
              aria-hidden="true"
            />
            <template v-else>
              <Lock :size="15" aria-hidden="true" />
              <span
                >Commit Batch Reconciliation ({{ scannedItems.length }})</span
              >
            </template>
          </button>
        </div>
      </div>
    </template>
  </ClinicalDrawer>

  <!-- Sibling Certificate Modal View Upon Successful Commitment -->
  <ClinicalModal
    :model-value="Boolean(showCertificateModal && certificateData)"
    :teleport="false"
    size="lg"
    :show-close="false"
    backdrop-class="modal-backdrop"
    dialog-class="modal-card modal-lg cert-modal"
    aria-labelledby="cert-modal-title"
    @close="showCertificateModal = false"
  >
    <template #header>
      <header class="cert-modal-header">
        <div class="cert-header-titles">
          <div class="cert-badge">
            <Award :size="14" aria-hidden="true" />
            <span>21 CFR Part 11 Digital Certificate</span>
          </div>
          <h3 id="cert-modal-title" class="cert-modal-title">
            Digital IP Kit Reconciliation Certificate
          </h3>
        </div>
        <button
          type="button"
          class="btn-close"
          aria-label="Close certificate dialog"
          @click="showCertificateModal = false"
        >
          <X :size="18" aria-hidden="true" />
        </button>
      </header>
    </template>

    <div v-if="certificateData" class="cert-body">
      <div class="cert-header-grid">
        <div class="cert-meta-item">
          <span class="lbl">Certificate ID:</span>
          <span class="val mono fw-bold">{{
            certificateData.certificate_id
          }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Batch Number:</span>
          <span class="val mono">{{ certificateData.batch_number }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Study / Site:</span>
          <span class="val"
            >{{ certificateData.study_id }} /
            {{ certificateData.site_id }}</span
          >
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Issued Timestamp:</span>
          <span class="val">{{ formatDate(certificateData.issued_at) }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Issued By:</span>
          <span class="val">{{ certificateData.issued_by }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Total Reconciled Kits:</span>
          <span class="val fw-bold">{{
            certificateData.kit_numbers?.length || 0
          }}</span>
        </div>
      </div>

      <div class="cert-section">
        <h4>Reconciled Kit Catalog</h4>
        <div class="cert-kit-tags">
          <span
            v-for="kit in certificateData.kit_numbers"
            :key="kit"
            class="cert-kit-chip mono"
          >
            <Package :size="13" aria-hidden="true" />
            <span>{{ kit }}</span>
          </span>
        </div>
      </div>

      <div class="cert-signature-box">
        <span class="sig-label">Cryptographic Seal (SHA-256):</span>
        <code class="sig-hash">{{ certificateData.digital_signature }}</code>
      </div>
    </div>

    <template #footer>
      <footer class="cert-modal-footer">
        <button
          type="button"
          class="btn btn-secondary"
          @click="printCertificate"
        >
          <Printer :size="15" aria-hidden="true" />
          <span>Print Certificate</span>
        </button>
        <button
          type="button"
          class="btn btn-primary"
          @click="closeDrawerAndReset"
        >
          Done &amp; Close
        </button>
      </footer>
    </template>
  </ClinicalModal>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import {
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalModal,
} from "ui";
import {
  AlertTriangle,
  Award,
  Check,
  FileSignature,
  Loader2,
  Lock,
  Package,
  Printer,
  RefreshCw,
  ScanBarcode,
  X,
} from "lucide-vue-next";

export interface ScannedKitBufferItem {
  kitNumber: string;
  scannedLotNumber: string;
  expectedLotNumber: string;
  hasLotMismatch: boolean;
  residualVolumeMl: number | null;
  residualVolumePercentage: number | null;
  disposition:
    "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT";
  dispositionReason: string;
}

export interface InventoryKitItem {
  id: string;
  study_id: string;
  site_id: string;
  kit_number: string;
  lot_number: string;
  expiration_date: string;
  status: string;
  residual_volume_ml?: number | null;
  residual_volume_percentage?: number | null;
  reconciliation_count: number;
}

const props = withDefaults(
  defineProps<{
    isOpen: boolean;
    studyId?: string;
    siteId?: string;
    pharmacistName?: string;
    defaultUserId?: string;
  }>(),
  {
    studyId: "STUDY-ONC-2026",
    siteId: "SITE-101",
    pharmacistName: "Dr. Elena Rostova, PharmD",
    defaultUserId: "pharmacist_site101",
  }
);

const emit = defineEmits<{
  (e: "close"): void;
  (e: "reconciled", batchResult: any): void;
}>();

const clinicalDrawerRef = ref<InstanceType<typeof ClinicalDrawer> | null>(null);
const barcodeInputValue = ref("");
const scannedItems = ref<ScannedKitBufferItem[]>([]);

const batchColumns = [
  { key: "index", label: "#", cellClass: "col-index" },
  {
    key: "kitNumber",
    label: "Kit Barcode",
    cellClass: "col-kit mono fw-bold",
  },
  { key: "scannedLotNumber", label: "Scanned Lot", cellClass: "col-lot mono" },
  {
    key: "expectedLotNumber",
    label: "Expected Inventory Lot",
    cellClass: "col-expected-lot mono",
  },
  { key: "volume", label: "Residual Vol (mL / %)", cellClass: "col-volume" },
  {
    key: "disposition",
    label: "Disposition Action",
    cellClass: "col-disposition",
  },
  {
    key: "dispositionReason",
    label: "Disposition Reason",
    cellClass: "col-reason",
  },
  { key: "flags", label: "Validation Flags", cellClass: "col-flags" },
  { key: "action", label: "Action", cellClass: "col-action" },
];

const resolveKitRowClass = (item: ScannedKitBufferItem) => [
  "kit-row",
  {
    "row-quarantine":
      item.disposition === "QUARANTINED_FOR_DESTRUCTION" ||
      item.dispositionReason === "DAMAGED_SEAL" ||
      item.hasLotMismatch,
    "row-lot-mismatch": item.hasLotMismatch,
  },
];

const warningMessage = ref("");
const isLoadingInventory = ref(false);
const isSubmitting = ref(false);

const signerUserId = ref(props.defaultUserId);
const reasonForChange = ref(
  "Site pharmacy batch physical inventory return and accountability reconciliation"
);
const batchNotes = ref("");

const inventoryMap = ref<Record<string, InventoryKitItem>>({});
const showCertificateModal = ref(false);
const certificateData = ref<any>(null);

// Computed metrics
const returnedToSiteCount = computed(
  () =>
    scannedItems.value.filter((i) => i.disposition === "RETURNED_TO_SITE")
      .length
);
const quarantinedCount = computed(
  () =>
    scannedItems.value.filter(
      (i) => i.disposition === "QUARANTINED_FOR_DESTRUCTION"
    ).length
);
const returnedToDepotCount = computed(
  () =>
    scannedItems.value.filter((i) => i.disposition === "RETURNED_TO_DEPOT")
      .length
);
const lotMismatchCount = computed(
  () => scannedItems.value.filter((i) => i.hasLotMismatch).length
);

// Auto-focus barcode input when drawer opens
watch(
  () => props.isOpen,
  async (open) => {
    if (open) {
      await loadInventory();
      await nextTick();
      focusScannerInput();
    }
  }
);

function focusScannerInput() {
  nextTick(() => {
    const el =
      clinicalDrawerRef.value?.drawerRef?.querySelector<HTMLInputElement>(
        "#barcode-scanner-input"
      ) ||
      (typeof document !== "undefined"
        ? (document.getElementById(
            "barcode-scanner-input"
          ) as HTMLInputElement | null)
        : null);
    el?.focus();
  });
}

async function loadInventory() {
  isLoadingInventory.value = true;
  try {
    const res = await fetch(
      `/api/v1/execution/rtsm/kits/inventory/${encodeURIComponent(props.studyId)}?site_id=${encodeURIComponent(props.siteId)}`
    );
    if (res.ok) {
      const list: InventoryKitItem[] = await res.json();
      const map: Record<string, InventoryKitItem> = {};
      for (const item of list) {
        map[item.kit_number.toUpperCase()] = item;
      }
      inventoryMap.value = map;
    }
  } catch {
    // Fallback silent in offline/unit tests unless explicitly mocked
  } finally {
    isLoadingInventory.value = false;
  }
}

function handleBarcodeScan() {
  const raw = barcodeInputValue.value.trim();
  if (!raw) return;

  warningMessage.value = "";

  // Parse barcode: supports "KIT-1001" or "KIT-1001|LOT-99" or "KIT-1001:LOT-99"
  let kitNum = raw;
  let scannedLot = "";

  if (raw.includes("|")) {
    const parts = raw.split("|");
    kitNum = parts[0].trim();
    scannedLot = (parts[1] || "").trim();
  } else if (raw.includes(":")) {
    const parts = raw.split(":");
    kitNum = parts[0].trim();
    scannedLot = (parts[1] || "").trim();
  }

  const normalizedKit = kitNum.toUpperCase();

  // Check duplicate in current scan buffer
  const existing = scannedItems.value.find(
    (item) => item.kitNumber.toUpperCase() === normalizedKit
  );
  if (existing) {
    warningMessage.value = `Duplicate barcode scan: Kit '${normalizedKit}' is already in the current reconciliation batch buffer. Double counting prevented.`;
    barcodeInputValue.value = "";
    focusScannerInput();
    return;
  }

  // Lookup expected inventory record
  const invRecord = inventoryMap.value[normalizedKit];
  const expectedLot = invRecord ? invRecord.lot_number : "LOT-STD-2026";
  const resolvedScannedLot = scannedLot || expectedLot;
  const hasMismatch = Boolean(
    invRecord &&
    scannedLot &&
    scannedLot.toUpperCase() !== invRecord.lot_number.toUpperCase()
  );

  if (
    invRecord &&
    ["RECONCILED_RETURNED", "QUARANTINED", "RETURNED_TO_DEPOT"].includes(
      invRecord.status
    )
  ) {
    warningMessage.value = `Kit '${normalizedKit}' has already been reconciled in inventory (Status: ${invRecord.status}).`;
    barcodeInputValue.value = "";
    focusScannerInput();
    return;
  }

  const newItem: ScannedKitBufferItem = {
    kitNumber: invRecord ? invRecord.kit_number : normalizedKit,
    scannedLotNumber: resolvedScannedLot,
    expectedLotNumber: expectedLot,
    hasLotMismatch: hasMismatch,
    residualVolumeMl: invRecord?.residual_volume_ml ?? 0,
    residualVolumePercentage: invRecord?.residual_volume_percentage ?? 0,
    disposition: hasMismatch
      ? "QUARANTINED_FOR_DESTRUCTION"
      : "RETURNED_TO_SITE",
    dispositionReason: hasMismatch ? "LOT_MISMATCH" : "PATIENT_RETURN",
  };

  if (hasMismatch) {
    warningMessage.value = `Discrepancy Alert: Kit '${newItem.kitNumber}' scanned lot '${resolvedScannedLot}' mismatches expected inventory lot '${expectedLot}'. Automatically flagged for Quarantine.`;
  }

  scannedItems.value.unshift(newItem);
  barcodeInputValue.value = "";
  focusScannerInput();
}

function removeItem(index: number) {
  scannedItems.value.splice(index, 1);
  focusScannerInput();
}

function clearAllScanned() {
  scannedItems.value = [];
  warningMessage.value = "";
  focusScannerInput();
}

function bulkSetDisposition(
  disp: "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT"
) {
  for (const item of scannedItems.value) {
    item.disposition = disp;
    if (
      disp === "QUARANTINED_FOR_DESTRUCTION" &&
      item.dispositionReason === "NORMAL_RETURN"
    ) {
      item.dispositionReason = "DAMAGED_SEAL";
    }
  }
}

async function submitBatchReconciliation() {
  if (scannedItems.value.length === 0 || isSubmitting.value) return;

  isSubmitting.value = true;
  warningMessage.value = "";

  try {
    const payload = {
      study_id: props.studyId,
      site_id: props.siteId,
      user_id: signerUserId.value.trim(),
      reason_for_change: reasonForChange.value.trim(),
      notes: batchNotes.value.trim() || null,
      items: scannedItems.value.map((i) => ({
        kit_number: i.kitNumber,
        scanned_lot_number: i.scannedLotNumber,
        residual_volume_ml: i.residualVolumeMl,
        residual_volume_percentage: i.residualVolumePercentage,
        disposition: i.disposition,
        disposition_reason: i.dispositionReason,
      })),
    };

    const res = await fetch("/api/v1/execution/rtsm/kits/reconcile-batch", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason": reasonForChange.value.trim(),
      },
      body: JSON.stringify(payload),
    });

    if (!res.ok) {
      const errBody = await res.json().catch(() => ({}));
      warningMessage.value =
        errBody.detail ||
        `Batch reconciliation failed (HTTP ${res.status}). Transaction rolled back atomically.`;
      return;
    }

    const result = await res.json();
    certificateData.value = result.certificate;
    showCertificateModal.value = true;
    emit("reconciled", result);
  } catch (err: any) {
    warningMessage.value =
      err?.message || "Network error during atomic batch reconciliation.";
  } finally {
    isSubmitting.value = false;
  }
}

function printCertificate() {
  if (typeof window !== "undefined" && window.print) {
    window.print();
  }
}

function closeDrawerAndReset() {
  showCertificateModal.value = false;
  certificateData.value = null;
  scannedItems.value = [];
  warningMessage.value = "";
  emit("close");
}

function closeDrawer() {
  if (isSubmitting.value) return;
  emit("close");
}

function formatDate(iso: string) {
  if (!iso) return "";
  return new Date(iso).toLocaleString();
}

onMounted(() => {
  if (props.isOpen) {
    loadInventory();
    focusScannerInput();
  }
});
</script>

<style scoped src="../shared/drawerHeaderChrome.css"></style>

<style scoped>
:global(
  .clinical-drawer-backdrop .clinical-drawer-panel.bulk-ip-reconciliation-drawer
) {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-left-color: var(--semantic-color-border-inverse);
}

:global(
  .clinical-drawer-backdrop
    .bulk-ip-reconciliation-drawer
    > .clinical-drawer-footer
) {
  background: var(--semantic-color-surface-elevated);
  border-top-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

:global(.clinical-modal-backdrop .clinical-modal-dialog.cert-modal) {
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

:global(.clinical-modal-backdrop .cert-modal > .modal-header) {
  background: var(--semantic-color-surface-elevated);
  border-bottom-color: var(--semantic-color-border-inverse);
}

:global(.clinical-modal-backdrop .cert-modal > .modal-footer) {
  background: var(--semantic-color-surface-elevated);
  border-top-color: var(--semantic-color-border-inverse);
}

.drawer-header {
  background: var(--semantic-color-surface-elevated);
  border-bottom-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

.header-icon {
  color: var(--semantic-color-accent-inverse);
}

.drawer-title {
  color: var(--semantic-color-text-inverse);
}

.btn-close {
  color: var(--semantic-color-text-inverse-muted);
}

.btn-close:hover:not(:disabled) {
  color: var(--semantic-color-text-inverse);
  background: var(--semantic-color-border-inverse);
}

.drawer-body {
  display: flex;
  flex-direction: column;
  gap: 14px;
  min-width: 0;
  font-variant-numeric: tabular-nums;
}

.intro-meta-block {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.badge-row {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
}

.badge {
  padding: 3px 8px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.badge-gxp {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
}
.badge-rtsm {
  background: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
  border: 1px solid var(--semantic-color-purple-border);
}
.badge-atomic {
  background: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}
.badge-active {
  background: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}
.badge-alert-lot {
  background: var(--semantic-color-error-interactive);
  color: var(--semantic-color-text-on-solid);
  font-size: var(--font-size-2xs);
  margin-left: 6px;
}

.drawer-subtitle {
  margin: 0;
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-inverse-muted);
}

.context-bar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 10px 14px;
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  font-size: var(--font-size-xs);
  flex-wrap: wrap;
}

.context-item {
  display: flex;
  gap: 6px;
}

.context-label {
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}

.context-val {
  color: var(--semantic-color-text-inverse);
  font-weight: 600;
}

.mono {
  font-family: var(--semantic-font-mono);
}

.context-actions {
  margin-left: auto;
}

.scanner-section {
  display: flex;
  flex-direction: column;
  gap: 10px;
}

.scanner-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  padding: 14px;
}

.scanner-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
  margin-bottom: 10px;
}

.scanner-label {
  font-weight: 700;
  font-size: var(--font-size-sm);
  color: var(--semantic-color-text-inverse);
  display: flex;
  align-items: center;
  gap: 6px;
}

.scanner-input-row {
  display: flex;
  gap: 10px;
}

:deep(.scanner-field) {
  flex: 1;
  min-width: 0;
}

:deep(.barcode-input) {
  width: 100%;
  min-width: 0;
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--semantic-color-text-inverse);
  font-size: var(--font-size-md);
  font-family: var(--semantic-font-mono);
  box-sizing: border-box;
}

:deep(.barcode-input:focus-visible) {
  border-color: var(--semantic-color-interactive-primary);
}

.scanner-help {
  margin: 8px 0 0 0;
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-inverse-muted);
}

.scanner-help code {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  padding: 1px 4px;
  border-radius: 3px;
}

.alert-banner {
  padding: 10px 14px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: var(--font-size-sm);
}

.alert-warning {
  background: var(--semantic-badge-queried-bg);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-badge-queried-text);
}

.alert-icon {
  flex-shrink: 0;
}

.btn-alert-dismiss {
  margin-left: auto;
  background: transparent;
  border: none;
  color: inherit;
  cursor: pointer;
  min-width: 44px;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(120px, 1fr));
  gap: 10px;
}

.metric-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 10px;
  display: flex;
  flex-direction: column;
  align-items: center;
  text-align: center;
}

.metric-num {
  font-size: var(--font-size-xl);
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
}

.metric-label {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-inverse-muted);
  text-transform: uppercase;
  font-weight: 600;
  margin-top: 2px;
}

.metric-returned .metric-num {
  color: var(--semantic-color-success);
}
.metric-quarantined .metric-num {
  color: var(--semantic-color-warning);
}
.metric-depot .metric-num {
  color: var(--semantic-color-interactive-primary);
}
.metric-discrepancy .metric-num {
  color: var(--semantic-color-error);
}

.toolbar {
  padding: 10px 14px;
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  flex-wrap: wrap;
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 8px;
  flex-wrap: wrap;
}

.toolbar-label {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}

.empty-state-box {
  text-align: center;
  padding: 32px 20px;
  background: var(--semantic-color-surface-elevated);
  border: 2px dashed var(--semantic-color-border-inverse);
  border-radius: 8px;
}

.empty-icon {
  margin-bottom: 10px;
  color: var(--semantic-color-text-inverse-muted);
  display: flex;
  justify-content: center;
}
.empty-state-box h3 {
  margin: 0 0 8px 0;
  color: var(--semantic-color-text-inverse);
}
.empty-state-box p {
  margin: 0 auto;
  color: var(--semantic-color-text-inverse-muted);
  max-width: 500px;
  font-size: var(--font-size-sm);
}

:deep(.table-container) {
  overflow-x: auto;
  max-width: 100%;
}

:deep(.batch-table) {
  width: 100%;
  border-collapse: collapse;
  font-size: var(--font-size-sm);
  background: var(--semantic-color-surface-elevated);
  border-radius: 8px;
  overflow: hidden;
}

:deep(.batch-table th) {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse-muted);
  text-align: left;
  padding: 10px 12px;
  font-weight: 700;
  border-bottom: 2px solid var(--semantic-color-border-inverse);
  font-size: var(--font-size-2xs);
  text-transform: uppercase;
}

:deep(.batch-table td) {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-border-inverse);
  vertical-align: middle;
}

:deep(.batch-table .kit-row:hover) {
  background: var(--semantic-color-surface-elevated);
}

:deep(.batch-table .row-quarantine) {
  background: var(--semantic-color-warning-bg-soft);
}

:deep(.batch-table .row-lot-mismatch) {
  background: var(--semantic-color-error-bg-row);
}

.volume-inputs {
  display: flex;
  gap: 6px;
}

:deep(.input-sm) {
  width: 72px;
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 6px 8px;
  color: var(--semantic-color-text-inverse);
  font-size: var(--font-size-md);
  box-sizing: border-box;
}

:deep(.select-sm) {
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 6px 8px;
  color: var(--semantic-color-text-inverse);
  font-size: var(--font-size-md);
  max-width: 100%;
  box-sizing: border-box;
}

:deep(.disp-returned_to_site) {
  color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
}
:deep(.disp-quarantined_for_destruction) {
  color: var(--semantic-color-warning);
  border-color: var(--semantic-color-warning-border-strong);
}
:deep(.disp-returned_to_depot) {
  color: var(--semantic-color-interactive-primary);
  border-color: var(--semantic-color-interactive-primary);
}

.status-flag {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  padding: 3px 6px;
  border-radius: 4px;
}

.flag-valid {
  color: var(--semantic-color-emerald-inverse-text);
  background: var(--semantic-color-emerald-inverse-bg);
}
.flag-warning {
  color: var(--semantic-badge-error-text);
  background: var(--semantic-badge-error-bg);
}
.flag-quarantine {
  color: var(--semantic-badge-queried-text);
  background: var(--semantic-badge-queried-bg);
}

.btn-icon {
  background: transparent;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  cursor: pointer;
  min-width: 44px;
  min-height: 44px;
  padding: 0;
  border-radius: 6px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
}

.btn-icon:hover:not(:disabled) {
  color: var(--semantic-badge-error-text);
  background: var(--semantic-badge-error-bg);
}

.esign-section {
  margin-top: 6px;
}

.esign-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-interactive-primary);
  border-radius: 8px;
  padding: 16px;
}

.esign-title {
  margin: 0 0 12px 0;
  font-size: var(--font-size-base);
  color: var(--semantic-color-text-inverse);
  font-weight: 700;
  display: flex;
  align-items: center;
  gap: 6px;
}

.esign-form-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 14px;
}

.form-group-full {
  grid-column: 1 / -1;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
  min-width: 0;
}

:deep(.form-label) {
  font-size: var(--font-size-xs);
  font-weight: 600;
  color: var(--semantic-color-text-inverse-muted);
}

:deep(.input-text),
:deep(.textarea-input) {
  width: 100%;
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--semantic-color-text-inverse);
  font-size: var(--font-size-md);
  box-sizing: border-box;
}

:deep(.input-text:focus-visible),
:deep(.textarea-input:focus-visible) {
  border-color: var(--semantic-color-interactive-primary);
}

.drawer-footer {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
}

.summary-text {
  font-size: var(--font-size-xs);
  color: var(--semantic-color-text-inverse-muted);
}
.summary-text strong {
  color: var(--semantic-color-text-inverse);
}

.footer-buttons {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.btn {
  min-height: 44px;
  padding: 8px 14px;
  border-radius: 6px;
  font-weight: 600;
  font-size: var(--font-size-sm);
  cursor: pointer;
  border: 1px solid transparent;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  transition: all 0.15s ease;
}

.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-text-on-primary);
}
.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
}
.btn-success {
  background: var(--semantic-color-success-interactive);
  color: var(--semantic-color-text-on-solid);
}
.btn-success:hover:not(:disabled) {
  background: var(--semantic-color-success-accent);
}
.btn-secondary {
  background: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
  border-color: var(--semantic-color-border-control);
}
.btn-secondary:hover:not(:disabled) {
  background: var(--semantic-color-text-secondary);
}

.btn-outline-success {
  background: transparent;
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-success);
}
.btn-outline-success:hover:not(:disabled) {
  background: var(--semantic-color-emerald-inverse-bg);
}
.btn-outline-warning {
  background: transparent;
  border-color: var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning);
}
.btn-outline-warning:hover:not(:disabled) {
  background: var(--semantic-badge-queried-bg);
}
.btn-outline-primary {
  background: transparent;
  border-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-interactive-primary);
}
.btn-outline-primary:hover:not(:disabled) {
  background: var(--semantic-badge-in-progress-bg);
}
.btn-ghost-danger {
  background: transparent;
  color: var(--semantic-color-error);
  border: 1px solid transparent;
}
.btn-ghost-danger:hover:not(:disabled) {
  background: var(--semantic-badge-error-bg);
  border-color: var(--semantic-color-error);
}
.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.spin-icon {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}

.cert-modal-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  gap: 8px;
  width: 100%;
  min-width: 0;
}

.cert-header-titles {
  min-width: 0;
}

.cert-badge {
  display: inline-flex;
  align-items: center;
  gap: 6px;
  font-size: var(--font-size-2xs);
  font-weight: 700;
  color: var(--semantic-color-success);
  margin-bottom: 2px;
}

.cert-modal-title {
  margin: 0;
  font-size: var(--font-size-md);
  color: var(--semantic-color-text-inverse);
}

.cert-header-grid {
  display: grid;
  grid-template-columns: repeat(2, minmax(0, 1fr));
  gap: 12px;
  background: var(--semantic-color-surface-elevated);
  padding: 14px;
  border-radius: 8px;
  margin-bottom: 16px;
  font-size: var(--font-size-sm);
}

.cert-meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.cert-meta-item .lbl {
  color: var(--semantic-color-text-inverse-muted);
  font-size: var(--font-size-2xs);
  font-weight: 600;
}
.cert-meta-item .val {
  color: var(--semantic-color-text-inverse);
}

.cert-section {
  margin-bottom: 16px;
}
.cert-section h4 {
  margin: 0 0 8px 0;
  color: var(--semantic-color-text-inverse-muted);
  font-size: var(--font-size-xs);
  text-transform: uppercase;
}

.cert-kit-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.cert-kit-chip {
  display: inline-flex;
  align-items: center;
  gap: 4px;
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  padding: 4px 8px;
  border-radius: 4px;
  font-size: var(--font-size-xs);
}

.cert-signature-box {
  background: var(--semantic-color-surface-elevated);
  border: 1px dashed var(--semantic-color-success);
  padding: 12px;
  border-radius: 6px;
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.sig-label {
  font-size: var(--font-size-2xs);
  font-weight: 700;
  color: var(--semantic-color-success);
  text-transform: uppercase;
}
.sig-hash {
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  word-break: break-all;
}

.cert-modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
}

.fw-bold {
  font-weight: 700;
}

@media (max-width: 640px) {
  .esign-form-grid,
  .cert-header-grid {
    grid-template-columns: 1fr;
  }

  .scanner-input-row,
  .drawer-footer,
  .footer-buttons,
  .cert-modal-footer {
    flex-direction: column;
    align-items: stretch;
  }

  .btn-scan-add,
  .footer-buttons .btn,
  .cert-modal-footer .btn {
    width: 100%;
  }

  .context-actions {
    margin-left: 0;
    width: 100%;
  }

  .context-actions .btn {
    width: 100%;
  }
}

/* Stacked kit record cards at <= 480px (Req 6) */
@media (max-width: 480px) {
  .metrics-grid {
    grid-template-columns: repeat(2, minmax(0, 1fr));
  }

  :deep(.table-container) {
    overflow-x: visible;
  }

  :deep(.batch-table),
  :deep(.batch-table tbody),
  :deep(.batch-table tr),
  :deep(.batch-table td) {
    display: block;
    width: 100%;
    box-sizing: border-box;
  }

  :deep(.batch-table) {
    background: transparent;
  }

  :deep(.batch-table thead) {
    display: none;
  }

  :deep(.batch-table tr.kit-row) {
    background: var(--semantic-color-surface-elevated);
    border: 1px solid var(--semantic-color-border-inverse);
    border-radius: 8px;
    padding: 12px;
    margin-bottom: 10px;
  }

  :deep(.batch-table td) {
    border-bottom: none;
    padding: 5px 0;
  }

  :deep(.batch-table td::before) {
    content: attr(data-label);
    display: block;
    font-size: var(--font-size-2xs);
    font-weight: 700;
    text-transform: uppercase;
    color: var(--semantic-color-text-inverse-muted);
    margin-bottom: 2px;
  }

  :deep(.select-sm),
  :deep(.input-sm) {
    width: 100%;
  }

  .volume-inputs {
    display: grid;
    grid-template-columns: repeat(2, minmax(0, 1fr));
    gap: 8px;
  }
}

@media (max-width: 340px) {
  .metrics-grid {
    grid-template-columns: 1fr;
  }
}
</style>
