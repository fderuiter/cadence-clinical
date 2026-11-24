<template>
  <div
    v-if="isOpen"
    class="drawer-backdrop"
    role="presentation"
    @click.self="closeDrawer"
  >
    <aside
      ref="drawerRef"
      class="bulk-ip-reconciliation-drawer"
      role="dialog"
      aria-modal="true"
      aria-labelledby="drawer-heading"
      tabindex="-1"
    >
      <!-- Drawer Header -->
      <header class="drawer-header">
        <div class="header-titles">
          <div class="badge-row">
            <span class="badge badge-gxp">21 CFR Part 11</span>
            <span class="badge badge-rtsm">RTSM Supply Chain</span>
            <span class="badge badge-atomic">Atomic Transaction</span>
          </div>
          <h2 id="drawer-heading" class="drawer-title">
            Bulk IP Kit Reconciliation &amp; Batch Disposition Drawer
          </h2>
          <p class="drawer-subtitle">
            Continuous optical barcode scanning, real-time lot discrepancy
            validation, residual volume recording, and atomic Part 11 inventory
            status commitment.
          </p>
        </div>

        <button
          type="button"
          class="btn-close"
          aria-label="Close Reconciliation Drawer"
          @click="closeDrawer"
        >
          ✕
        </button>
      </header>

      <!-- Context Bar -->
      <div class="context-bar">
        <div class="context-item">
          <span class="context-label">Study:</span>
          <span class="context-value mono">{{ studyId || "STUDY-101" }}</span>
        </div>
        <div class="context-item">
          <span class="context-label">Site:</span>
          <span class="context-value mono">{{ siteId || "SITE-01" }}</span>
        </div>
        <div class="context-item">
          <span class="context-label">Pharmacist:</span>
          <span class="context-value">{{
            pharmacistName || "Site Pharmacist"
          }}</span>
        </div>
        <div class="context-actions">
          <button
            type="button"
            class="btn btn-sm btn-secondary"
            :disabled="isLoadingInventory"
            @click="loadInventory"
          >
            🔄 Refresh Inventory
          </button>
        </div>
      </div>

      <!-- Scanner Input Buffer Section -->
      <section class="scanner-section" aria-label="Barcode Scanner Input">
        <div class="scanner-card">
          <div class="scanner-header">
            <label for="barcode-scanner-input" class="scanner-label">
              <span class="scanner-icon">📷</span> Multi-Barcode Scanner Buffer
              Input
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
              Add Scan
            </button>
          </div>
          <p class="scanner-help">
            Optical scanner inputs automatically refocus here between scans.
            Format: <code>KIT-NUMBER</code> or
            <code>KIT-NUMBER|LOT-NUMBER</code>.
          </p>
        </div>

        <!-- Warning Alert Banner for Duplicate Scans or Lot Errors -->
        <div
          v-if="warningMessage"
          class="alert-banner alert-warning"
          role="alert"
        >
          <span class="alert-icon">⚠️</span>
          <span class="alert-text">{{ warningMessage }}</span>
          <button
            type="button"
            class="btn-alert-dismiss"
            aria-label="Dismiss warning"
            @click="warningMessage = ''"
          >
            ✕
          </button>
        </div>
      </section>

      <!-- Batch Statistics Summary -->
      <div class="metrics-grid">
        <div class="metric-card">
          <span class="metric-num">{{ scannedItems.length }}</span>
          <span class="metric-label">Total Scanned Kits</span>
        </div>
        <div class="metric-card metric-returned">
          <span class="metric-num">{{ returnedCount }}</span>
          <span class="metric-label">Returned to Site</span>
        </div>
        <div class="metric-card metric-quarantined">
          <span class="metric-num">{{ quarantinedCount }}</span>
          <span class="metric-label">Quarantined / Damaged</span>
        </div>
        <div class="metric-card metric-depot">
          <span class="metric-num">{{ depotCount }}</span>
          <span class="metric-label">Returned to Depot</span>
        </div>
        <div class="metric-card metric-discrepancy">
          <span class="metric-num">{{ lotMismatchCount }}</span>
          <span class="metric-label">Lot Discrepancies</span>
        </div>
      </div>

      <!-- Quick Bulk Action Toolbar -->
      <div v-if="scannedItems.length > 0" class="toolbar">
        <div class="bulk-actions">
          <span class="toolbar-label">Bulk Disposition:</span>
          <button
            type="button"
            class="btn btn-sm btn-outline-success"
            @click="bulkSetDisposition('RETURNED_TO_SITE')"
          >
            Mark All Returned
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-warning"
            @click="bulkSetDisposition('QUARANTINED_FOR_DESTRUCTION')"
          >
            Mark All Quarantined
          </button>
          <button
            type="button"
            class="btn btn-sm btn-outline-primary"
            @click="bulkSetDisposition('RETURNED_TO_DEPOT')"
          >
            Mark All Depot Return
          </button>
        </div>

        <button
          type="button"
          class="btn btn-sm btn-ghost-danger btn-clear-buffer"
          @click="clearScanBuffer"
        >
          Clear Scan Buffer
        </button>
      </div>

      <!-- Main Drawer Body: Scanned Kits Buffer Table -->
      <div class="drawer-body">
        <div v-if="scannedItems.length === 0" class="empty-state-box">
          <div class="empty-icon">📦</div>
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
            {{ row.expectedLotNumber }}
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
            <span v-if="row.hasLotMismatch" class="status-flag flag-warning"
              >Lot Mismatch</span
            >
            <span
              v-else-if="row.disposition === 'QUARANTINED_FOR_DESTRUCTION'"
              class="status-flag flag-quarantine"
              >Quarantined</span
            >
            <span v-else class="status-flag flag-valid">Valid</span>
          </template>
          <template #cell-action="{ row, index }">
            <ClinicalIconButton
              class="btn-icon btn-remove"
              :label="`Remove kit ${row.kitNumber || index + 1} from reconciliation`"
              title="Remove item"
              @click="removeItem(index)"
            >
              ×
            </ClinicalIconButton>
          </template>
        </ClinicalDataTable>

        <!-- 21 CFR Part 11 Electronic Signature / Change Rationale -->
        <section v-if="scannedItems.length > 0" class="esign-section">
          <div class="esign-card">
            <h4 class="esign-title">
              21 CFR Part 11 Electronic Signature &amp; Audit Rationale
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
                  control-class="textarea-input"
                  placeholder="Additional observations, courier tracking numbers, or carton condition..."
                />
              </div>
            </div>
          </div>
        </section>
      </div>

      <!-- Drawer Footer Action Bar -->
      <footer class="drawer-footer">
        <div class="footer-summary">
          <span v-if="scannedItems.length > 0" class="summary-text">
            Ready to reconcile <strong>{{ scannedItems.length }}</strong> kit(s)
            (<strong>{{ quarantinedCount }}</strong> marked for quarantine)
          </span>
          <span v-else class="summary-text">
            Scan barcodes to enable batch commitment
          </span>
        </div>

        <div class="footer-buttons">
          <button type="button" class="btn btn-secondary" @click="closeDrawer">
            Cancel
          </button>
          <button
            type="button"
            class="btn btn-success btn-submit-batch"
            :disabled="
              scannedItems.length === 0 ||
              !signerUserId.trim() ||
              !reasonForChange.trim() ||
              isSubmitting
            "
            @click="submitBatchReconciliation"
          >
            <span v-if="isSubmitting" class="spinner">⏳</span>
            <span v-else
              >🔒 Commit Batch Reconciliation ({{ scannedItems.length }})</span
            >
          </button>
        </div>
      </footer>

      <!-- Certificate Modal View Upon Successful Commitment -->
      <div
        v-if="showCertificateModal && certificateData"
        class="modal-backdrop"
        role="presentation"
        @click.self="showCertificateModal = false"
      >
        <div
          ref="certificateModalRef"
          class="modal-card modal-lg cert-modal"
          role="dialog"
          aria-modal="true"
          aria-labelledby="cert-modal-title"
          tabindex="-1"
        >
          <header class="cert-modal-header">
            <div class="cert-badge">📜 21 CFR Part 11 Digital Certificate</div>
            <h3 id="cert-modal-title" class="cert-modal-title">
              Digital IP Kit Reconciliation Certificate
            </h3>
            <button
              type="button"
              class="btn-close"
              aria-label="Close certificate dialog"
              @click="showCertificateModal = false"
            >
              ✕
            </button>
          </header>

          <div class="cert-body">
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
                <span class="val">{{
                  formatDate(certificateData.issued_at)
                }}</span>
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
                  📦 {{ kit }}
                </span>
              </div>
            </div>

            <div class="cert-signature-box">
              <span class="sig-label">Cryptographic Seal (SHA-256):</span>
              <code class="sig-hash">{{
                certificateData.digital_signature
              }}</code>
            </div>
          </div>

          <footer class="cert-modal-footer">
            <button
              type="button"
              class="btn btn-secondary"
              @click="printCertificate"
            >
              🖨️ Print Certificate
            </button>
            <button
              type="button"
              class="btn btn-primary"
              @click="closeDrawerAndReset"
            >
              Done &amp; Close
            </button>
          </footer>
        </div>
      </div>
    </aside>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from "vue";
import {
  ClinicalDataTable,
  ClinicalIconButton,
  ClinicalInput,
  useFocusTrap,
} from "ui";

export interface ScannedKitBufferItem {
  kitNumber: string;
  scannedLotNumber: string;
  expectedLotNumber: string;
  hasLotMismatch: boolean;
  residualVolumeMl?: number | null;
  residualVolumePercentage?: number | null;
  disposition:
    "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT";
  dispositionReason:
    | "NORMAL_RETURN"
    | "PATIENT_RETURN"
    | "DAMAGED_SEAL"
    | "EXPIRED"
    | "LOT_MISMATCH"
    | "OTHER";
}

export interface SiteInventoryItem {
  kit_number: string;
  study_id: string;
  site_id: string;
  lot_number: string;
  expiration_date: string;
  status: string;
}

const props = defineProps<{
  isOpen: boolean;
  studyId?: string;
  siteId?: string;
  pharmacistName?: string;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "reconciled", batchResult: any): void;
}>();

const drawerRef = ref<HTMLElement | null>(null);
const certificateModalRef = ref<HTMLElement | null>(null);
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

const siteInventory = ref<Map<string, SiteInventoryItem>>(new Map());

const signerUserId = ref(props.pharmacistName || "site_pharmacist_01");
const reasonForChange = ref(
  "Shipment return carton batch reconciliation intake"
);
const batchNotes = ref("");

const showCertificateModal = ref(false);
const certificateData = ref<any>(null);

const returnedCount = computed(
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
const depotCount = computed(
  () =>
    scannedItems.value.filter((i) => i.disposition === "RETURNED_TO_DEPOT")
      .length
);
const lotMismatchCount = computed(
  () => scannedItems.value.filter((i) => i.hasLotMismatch).length
);

watch(
  () => props.isOpen,
  (newVal) => {
    if (newVal) {
      loadInventory();
      focusScannerInput();
    }
  }
);

function focusScannerInput() {
  nextTick(() => {
    const el = drawerRef.value?.querySelector<HTMLInputElement>(
      "#barcode-scanner-input"
    );
    el?.focus();
  });
}

async function loadInventory() {
  const currentStudy = props.studyId || "STUDY-101";
  const currentSite = props.siteId || "SITE-01";
  isLoadingInventory.value = true;
  try {
    const res = await fetch(
      `/api/v1/execution/rtsm/kits/inventory/${currentStudy}/${currentSite}`
    );
    if (res.ok) {
      const data: SiteInventoryItem[] = await res.json();
      const map = new Map<string, SiteInventoryItem>();
      data.forEach((item) => map.set(item.kit_number, item));
      siteInventory.value = map;
    }
  } catch (err) {
    console.warn(
      "Could not load live inventory, using ad-hoc validation mode",
      err
    );
  } finally {
    isLoadingInventory.value = false;
  }
}

function handleBarcodeScan() {
  const rawInput = barcodeInputValue.value.trim();
  if (!rawInput) return;

  warningMessage.value = "";

  // Parse barcode format: "KIT-NUMBER" or "KIT-NUMBER|LOT-NUMBER"
  let parsedKit = rawInput;
  let parsedLot = "";
  if (rawInput.includes("|")) {
    const parts = rawInput.split("|");
    parsedKit = parts[0].trim();
    parsedLot = parts[1].trim();
  }

  // Duplicate Check in current batch session
  const isDuplicate = scannedItems.value.some(
    (item) => item.kitNumber.toUpperCase() === parsedKit.toUpperCase()
  );

  if (isDuplicate) {
    warningMessage.value = `Duplicate barcode scan: Kit '${parsedKit}' is already in the reconciliation buffer. Double counting prevented.`;
    barcodeInputValue.value = "";
    focusScannerInput();
    return;
  }

  // Validate against known inventory
  const inventoryMatch = siteInventory.value.get(parsedKit);
  const expectedLot = inventoryMatch
    ? inventoryMatch.lot_number
    : parsedLot || `LOT-EST-${Math.abs(hashString(parsedKit)) % 1000}`;
  const scannedLot = parsedLot || expectedLot;
  const hasLotMismatch =
    parsedLot !== "" && parsedLot.toUpperCase() !== expectedLot.toUpperCase();

  let initialDisposition:
    "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT" =
    "RETURNED_TO_SITE";
  let initialReason:
    | "NORMAL_RETURN"
    | "PATIENT_RETURN"
    | "DAMAGED_SEAL"
    | "EXPIRED"
    | "LOT_MISMATCH"
    | "OTHER" = "NORMAL_RETURN";

  if (hasLotMismatch) {
    initialDisposition = "QUARANTINED_FOR_DESTRUCTION";
    initialReason = "LOT_MISMATCH";
  }

  scannedItems.value.unshift({
    kitNumber: parsedKit,
    scannedLotNumber: scannedLot,
    expectedLotNumber: expectedLot,
    hasLotMismatch: hasLotMismatch,
    residualVolumeMl: 100.0,
    residualVolumePercentage: 100,
    disposition: initialDisposition,
    dispositionReason: initialReason,
  });

  barcodeInputValue.value = "";
  focusScannerInput();
}

function hashString(str: string): number {
  let hash = 0;
  for (let i = 0; i < str.length; i++) {
    hash = (hash << 5) - hash + str.charCodeAt(i);
    hash |= 0;
  }
  return hash;
}

function removeItem(index: number) {
  scannedItems.value.splice(index, 1);
  focusScannerInput();
}

function clearScanBuffer() {
  scannedItems.value = [];
  warningMessage.value = "";
  focusScannerInput();
}

function bulkSetDisposition(
  disposition:
    "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT"
) {
  scannedItems.value.forEach((item) => {
    item.disposition = disposition;
  });
}

async function submitBatchReconciliation() {
  if (scannedItems.value.length === 0) return;
  if (!signerUserId.value.trim() || !reasonForChange.value.trim()) return;

  isSubmitting.value = true;
  warningMessage.value = "";

  const payload = {
    study_id: props.studyId || "STUDY-101",
    site_id: props.siteId || "SITE-01",
    processed_by: signerUserId.value.trim(),
    notes: batchNotes.value.trim() || undefined,
    reason_for_change: reasonForChange.value.trim(),
    items: scannedItems.value.map((i) => ({
      kit_number: i.kitNumber,
      lot_number: i.scannedLotNumber,
      expected_lot_number: i.expectedLotNumber,
      residual_volume_ml: i.residualVolumeMl,
      residual_volume_percentage: i.residualVolumePercentage,
      disposition: i.disposition,
      disposition_reason: i.dispositionReason,
    })),
  };

  try {
    const res = await fetch("/api/v1/execution/rtsm/kits/reconcile-batch", {
      method: "POST",
      headers: {
        "Content-Type": "application/json",
        "X-Change-Reason": reasonForChange.value.trim(),
      },
      body: JSON.stringify(payload),
    });

    if (!res.ok) {
      const err = await res.json();
      throw new Error(
        err.detail || "Server failed to process batch reconciliation."
      );
    }

    const batchResult = await res.json();
    certificateData.value = batchResult.certificate;
    showCertificateModal.value = true;
    emit("reconciled", batchResult);
  } catch (err: any) {
    warningMessage.value = `Batch submission error: ${err.message || err}`;
  } finally {
    isSubmitting.value = false;
  }
}

function printCertificate() {
  window.print();
}

function closeDrawerAndReset() {
  showCertificateModal.value = false;
  scannedItems.value = [];
  emit("close");
}

function closeDrawer() {
  emit("close");
}

useFocusTrap(drawerRef, {
  isOpen: () => props.isOpen,
  initialFocus: "#barcode-scanner-input",
  onEscape: closeDrawer,
});

useFocusTrap(certificateModalRef, {
  isOpen: () => Boolean(showCertificateModal.value && certificateData.value),
  onEscape: () => {
    showCertificateModal.value = false;
  },
});

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

<style scoped>
.drawer-backdrop {
  position: fixed;
  inset: 0;
  background-color: rgba(15, 23, 42, 0.65);
  backdrop-filter: blur(4px);
  z-index: 1000;
  display: flex;
  justify-content: flex-end;
}

.bulk-ip-reconciliation-drawer {
  width: 100%;
  max-width: 1100px;
  height: 100vh;
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  display: flex;
  flex-direction: column;
  box-shadow: -8px 0 32px rgba(0, 0, 0, 0.5);
  overflow: hidden;
  font-family:
    system-ui,
    -apple-system,
    BlinkMacSystemFont,
    "Segoe UI",
    Roboto,
    sans-serif;
}

.drawer-header {
  padding: 20px 24px;
  background: var(--semantic-color-surface-elevated);
  border-bottom: 1px solid var(--semantic-color-border-inverse);
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
}

.badge-row {
  display: flex;
  gap: 8px;
  margin-bottom: 8px;
}

.badge {
  padding: 3px 8px;
  border-radius: 4px;
  font-size: 0.72rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.05em;
}

.badge-gxp {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
}
.badge-rtsm {
  background: var(--semantic-color-purple-bg);
  color: var(--semantic-color-purple-text);
  border: 1px solid var(--semantic-color-purple-border);
}
.badge-atomic {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}
.badge-active {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}
.badge-alert-lot {
  background: var(--semantic-color-error);
  color: var(--semantic-color-surface);
  font-size: 0.65rem;
  margin-left: 6px;
}

.drawer-title {
  margin: 0 0 6px 0;
  font-size: 1.4rem;
  font-weight: 700;
  color: var(--semantic-color-text-inverse);
}

.drawer-subtitle {
  margin: 0;
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
  max-width: 800px;
}

.btn-close {
  background: transparent;
  border: none;
  color: var(--semantic-color-text-secondary);
  font-size: 1.5rem;
  cursor: pointer;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-close:hover:not(:disabled) {
  color: var(--semantic-color-text-inverse);
  background: var(--semantic-color-border-inverse);
}

.context-bar {
  display: flex;
  align-items: center;
  gap: 24px;
  padding: 12px 24px;
  background: var(--semantic-color-surface-inverse);
  border-bottom: 1px solid var(--semantic-color-text-dark);
  font-size: 0.88rem;
}

.context-item {
  display: flex;
  gap: 6px;
}
.context-label {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}
.context-value {
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}
.mono {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.context-actions {
  margin-left: auto;
}

.scanner-section {
  padding: 16px 24px;
  background: var(--semantic-color-surface-elevated);
  border-bottom: 1px solid var(--semantic-color-border-inverse);
}

.scanner-card {
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-interactive-primary);
  border-radius: 8px;
  padding: 14px 18px;
}

.scanner-header {
  display: flex;
  justify-content: space-between;
  align-items: center;
  margin-bottom: 10px;
}

.scanner-label {
  font-weight: 700;
  font-size: 0.95rem;
  color: var(--semantic-color-primary);
  display: flex;
  align-items: center;
  gap: 6px;
}

.scanner-input-row {
  display: flex;
  gap: 12px;
}

.scanner-field {
  flex: 1;
}

.barcode-input {
  flex: 1;
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 10px 14px;
  color: var(--semantic-color-text-inverse);
  font-size: 1.05rem;
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
}

.barcode-input:focus-visible {
  border-color: var(--semantic-color-interactive-primary);
}

.scanner-help {
  margin: 8px 0 0 0;
  font-size: 0.8rem;
  color: var(--semantic-color-text-secondary);
}

.scanner-help code {
  background: var(--semantic-color-border-inverse);
  color: var(--semantic-color-border);
  padding: 1px 4px;
  border-radius: 3px;
}

.alert-banner {
  margin-top: 12px;
  padding: 10px 14px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 0.88rem;
}

.alert-warning {
  background: rgba(245, 158, 11, 0.15);
  border: 1px solid var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning);
}

.btn-alert-dismiss {
  margin-left: auto;
  background: transparent;
  border: none;
  color: inherit;
  cursor: pointer;
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(5, 1fr);
  gap: 12px;
  padding: 16px 24px;
  background: var(--semantic-color-surface-inverse);
}

.metric-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 10px 12px;
  display: flex;
  flex-direction: column;
  align-items: center;
}

.metric-num {
  font-size: 1.4rem;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
}

.metric-label {
  font-size: 0.72rem;
  color: var(--semantic-color-text-secondary);
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
  padding: 10px 24px;
  background: var(--semantic-color-surface-elevated);
  border-top: 1px solid var(--semantic-color-border-inverse);
  border-bottom: 1px solid var(--semantic-color-border-inverse);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.bulk-actions {
  display: flex;
  align-items: center;
  gap: 10px;
}

.toolbar-label {
  font-size: 0.82rem;
  color: var(--semantic-color-text-secondary);
  font-weight: 600;
}

.drawer-body {
  flex: 1;
  padding: 20px 24px;
  overflow-y: auto;
}

.empty-state-box {
  text-align: center;
  padding: 48px 24px;
  background: var(--semantic-color-surface-elevated);
  border: 2px dashed var(--semantic-color-border-inverse);
  border-radius: 8px;
}

.empty-icon {
  font-size: 3rem;
  margin-bottom: 12px;
}
.empty-state-box h3 {
  margin: 0 0 8px 0;
  color: var(--semantic-color-text-inverse);
}
.empty-state-box p {
  margin: 0;
  color: var(--semantic-color-text-secondary);
  max-width: 500px;
  margin: 0 auto;
  font-size: 0.9rem;
}

.table-container {
  overflow-x: auto;
}

.batch-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.88rem;
  background: var(--semantic-color-surface-elevated);
  border-radius: 8px;
  overflow: hidden;
}

.batch-table th {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse-muted);
  text-align: left;
  padding: 12px 14px;
  font-weight: 700;
  border-bottom: 2px solid var(--semantic-color-border-inverse);
  font-size: 0.8rem;
  text-transform: uppercase;
}

.batch-table td {
  padding: 12px 14px;
  border-bottom: 1px solid var(--semantic-color-text-body);
  vertical-align: middle;
}

.kit-row:hover {
  background: var(--semantic-color-surface-elevated);
}

.row-quarantine {
  background: rgba(245, 158, 11, 0.08);
}

.row-lot-mismatch {
  background: rgba(239, 68, 68, 0.12);
}

.volume-inputs {
  display: flex;
  gap: 6px;
}

.input-sm {
  width: 64px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 4px 6px;
  color: var(--semantic-color-text-inverse);
  font-size: 0.82rem;
}

.select-sm {
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 4px;
  padding: 6px 8px;
  color: var(--semantic-color-text-inverse);
  font-size: 0.82rem;
}

.disp-returned_to_site {
  color: var(--semantic-color-success);
  border-color: var(--semantic-color-success);
}
.disp-quarantined_for_destruction {
  color: var(--semantic-color-warning);
  border-color: var(--semantic-color-warning-border-strong);
}
.disp-returned_to_depot {
  color: var(--semantic-color-interactive-primary);
  border-color: var(--semantic-color-interactive-primary);
}

.status-flag {
  font-size: 0.78rem;
  font-weight: 700;
  padding: 2px 6px;
  border-radius: 4px;
}

.flag-valid {
  color: var(--semantic-color-success);
  background: rgba(16, 185, 129, 0.1);
}
.flag-warning {
  color: var(--semantic-color-error);
  background: rgba(239, 68, 68, 0.15);
}
.flag-quarantine {
  color: var(--semantic-color-warning);
  background: rgba(245, 158, 11, 0.15);
}

.btn-icon {
  background: transparent;
  border: none;
  color: var(--semantic-color-text-secondary);
  cursor: pointer;
  font-size: 1rem;
  padding: 4px 8px;
  border-radius: 4px;
}

.btn-icon:hover:not(:disabled) {
  color: var(--semantic-color-error);
  background: rgba(239, 68, 68, 0.1);
}

.esign-section {
  margin-top: 24px;
}

.esign-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-interactive-primary);
  border-radius: 8px;
  padding: 18px 20px;
}

.esign-title {
  margin: 0 0 14px 0;
  font-size: 0.98rem;
  color: var(--semantic-color-primary);
  font-weight: 700;
}

.esign-form-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 16px;
}

.form-group-full {
  grid-column: span 2;
}

.form-group {
  display: flex;
  flex-direction: column;
  gap: 6px;
}

.form-label {
  font-size: 0.82rem;
  font-weight: 600;
  color: var(--semantic-color-text-secondary);
}

.input-text,
.textarea-input {
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--semantic-color-text-inverse);
  font-size: 0.88rem;
}

.input-text:focus-visible,
.textarea-input:focus-visible {
  border-color: var(--semantic-color-interactive-primary);
}

.drawer-footer {
  padding: 16px 24px;
  background: var(--semantic-color-surface-elevated);
  border-top: 1px solid var(--semantic-color-border-inverse);
  display: flex;
  justify-content: space-between;
  align-items: center;
}

.summary-text {
  font-size: 0.88rem;
  color: var(--semantic-color-text-secondary);
}
.summary-text strong {
  color: var(--semantic-color-text-inverse);
}

.footer-buttons {
  display: flex;
  gap: 12px;
}

.btn {
  padding: 8px 16px;
  border-radius: 6px;
  font-weight: 600;
  font-size: 0.88rem;
  cursor: pointer;
  border: 1px solid transparent;
  transition: all 0.15s ease;
}

.btn-primary {
  background: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-surface);
}
.btn-primary:hover:not(:disabled) {
  background: var(--semantic-color-interactive-primary);
}
.btn-success {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}
.btn-success:hover:not(:disabled) {
  background: var(--semantic-color-success);
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
  background: rgba(16, 185, 129, 0.1);
}
.btn-outline-warning {
  background: transparent;
  border-color: var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning);
}
.btn-outline-warning:hover:not(:disabled) {
  background: rgba(245, 158, 11, 0.1);
}
.btn-outline-primary {
  background: transparent;
  border-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-interactive-primary);
}
.btn-outline-primary:hover:not(:disabled) {
  background: rgba(59, 130, 246, 0.1);
}

.btn-ghost-danger {
  background: transparent;
  color: var(--semantic-color-error);
  border: 1px solid transparent;
}
.btn-ghost-danger:hover:not(:disabled) {
  background: rgba(239, 68, 68, 0.1);
  border-color: var(--semantic-color-error);
}

.btn:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}

.cert-modal {
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 12px;
  max-width: 700px;
  width: 90%;
  padding: 24px;
}

.cert-modal-header {
  border-bottom: 1px solid var(--semantic-color-text-body);
  padding-bottom: 12px;
  margin-bottom: 18px;
}

.cert-badge {
  font-size: 0.8rem;
  font-weight: 700;
  color: var(--semantic-color-success);
  margin-bottom: 4px;
}
.cert-modal-title {
  margin: 0;
  font-size: 1.3rem;
  color: var(--semantic-color-text-inverse);
}

.cert-header-grid {
  display: grid;
  grid-template-columns: 1fr 1fr;
  gap: 12px;
  background: var(--semantic-color-surface-elevated);
  padding: 14px;
  border-radius: 8px;
  margin-bottom: 18px;
  font-size: 0.88rem;
}

.cert-meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.cert-meta-item .lbl {
  color: var(--semantic-color-text-secondary);
  font-size: 0.75rem;
  font-weight: 600;
}
.cert-meta-item .val {
  color: var(--semantic-color-text-inverse);
}

.cert-section {
  margin-bottom: 18px;
}
.cert-section h4 {
  margin: 0 0 8px 0;
  color: var(--semantic-color-text-secondary);
  font-size: 0.88rem;
  text-transform: uppercase;
}

.cert-kit-tags {
  display: flex;
  flex-wrap: wrap;
  gap: 8px;
}

.cert-kit-chip {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  padding: 4px 8px;
  border-radius: 4px;
  font-size: 0.82rem;
  color: var(--semantic-color-accent-inverse);
}

.cert-signature-box {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
  padding: 12px 16px;
  border-radius: 8px;
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.sig-label {
  font-size: 0.75rem;
  font-weight: 700;
  text-transform: uppercase;
}
.sig-hash {
  font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
  font-size: 0.78rem;
  word-break: break-all;
}

.cert-modal-footer {
  margin-top: 20px;
  display: flex;
  justify-content: flex-end;
  gap: 12px;
}
</style>
