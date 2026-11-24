<template>
  <ClinicalDrawer
    :model-value="isOpen"
    :teleport="false"
    size="xl"
    :dismissible="!isSubmitting"
    :initial-focus="() => barcodeInputRef"
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
            <RefreshCw :size="14" aria-hidden="true" />
            <span>Refresh Inventory</span>
          </button>
        </div>
      </div>

      <!-- Scanner Input Buffer Section -->
      <section class="scanner-section" aria-label="Barcode Scanner Input">
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
            <input
              id="barcode-scanner-input"
              ref="barcodeInputRef"
              v-model="barcodeInputValue"
              type="text"
              class="barcode-input"
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

      <div v-else class="table-container">
        <table
          class="batch-table"
          role="grid"
          aria-label="Scanned Kits Reconciliation Buffer"
        >
          <thead>
            <tr>
              <th>#</th>
              <th>Kit Barcode</th>
              <th>Scanned Lot</th>
              <th>Expected Inventory Lot</th>
              <th>Residual Vol (mL / %)</th>
              <th>Disposition Action</th>
              <th>Disposition Reason</th>
              <th>Validation Flags</th>
              <th>Action</th>
            </tr>
          </thead>
          <tbody>
            <tr
              v-for="(item, index) in scannedItems"
              :key="item.kitNumber"
              class="kit-row"
              :class="{
                'row-quarantine':
                  item.disposition === 'QUARANTINED_FOR_DESTRUCTION' ||
                  item.dispositionReason === 'DAMAGED_SEAL' ||
                  item.hasLotMismatch,
                'row-lot-mismatch': item.hasLotMismatch,
              }"
            >
              <td data-label="Kit #" class="col-index">
                {{ index + 1 }}
              </td>
              <td data-label="Kit Barcode" class="col-kit mono fw-bold">
                {{ item.kitNumber }}
              </td>
              <td data-label="Scanned Lot" class="col-lot mono">
                {{ item.scannedLotNumber }}
              </td>
              <td
                data-label="Expected Inventory Lot"
                class="col-expected-lot mono"
              >
                <span>{{ item.expectedLotNumber }}</span>
                <span v-if="item.hasLotMismatch" class="badge badge-alert-lot"
                  >LOT MISMATCH</span
                >
              </td>
              <td data-label="Residual Vol (mL / %)" class="col-volume">
                <div class="volume-inputs">
                  <input
                    v-model.number="item.residualVolumeMl"
                    type="number"
                    min="0"
                    step="0.1"
                    class="input-sm volume-input"
                    placeholder="mL"
                    aria-label="Residual volume in mL"
                  />
                  <input
                    v-model.number="item.residualVolumePercentage"
                    type="number"
                    min="0"
                    max="100"
                    step="1"
                    class="input-sm volume-input"
                    placeholder="%"
                    aria-label="Residual percentage"
                  />
                </div>
              </td>
              <td data-label="Disposition Action" class="col-disposition">
                <select
                  v-model="item.disposition"
                  class="select-sm disposition-select"
                  :class="`disp-${item.disposition.toLowerCase()}`"
                  aria-label="Disposition Action"
                >
                  <option value="RETURNED_TO_SITE">Returned to Site</option>
                  <option value="QUARANTINED_FOR_DESTRUCTION">
                    Quarantined for Destruction
                  </option>
                  <option value="RETURNED_TO_DEPOT">Returned to Depot</option>
                </select>
              </td>
              <td data-label="Disposition Reason" class="col-reason">
                <select
                  v-model="item.dispositionReason"
                  class="select-sm reason-select"
                  aria-label="Disposition Reason"
                >
                  <option value="NORMAL_RETURN">Normal Return</option>
                  <option value="PATIENT_RETURN">
                    Patient Return / Adherence
                  </option>
                  <option value="DAMAGED_SEAL">Damaged Seal / Tampered</option>
                  <option value="EXPIRED">Expired Lot</option>
                  <option value="LOT_MISMATCH">Lot Mismatch</option>
                  <option value="OTHER">Other / Notes</option>
                </select>
              </td>
              <td data-label="Validation Flags" class="col-flags">
                <span
                  v-if="item.hasLotMismatch"
                  class="status-flag flag-warning"
                >
                  <AlertTriangle :size="13" aria-hidden="true" />
                  <span>Lot Mismatch</span>
                </span>
                <span
                  v-else-if="item.disposition === 'QUARANTINED_FOR_DESTRUCTION'"
                  class="status-flag flag-quarantine"
                >
                  <Lock :size="13" aria-hidden="true" />
                  <span>Quarantined</span>
                </span>
                <span v-else class="status-flag flag-valid">
                  <Check :size="13" aria-hidden="true" />
                  <span>Valid</span>
                </span>
              </td>
              <td data-label="Action" class="col-action">
                <ClinicalIconButton
                  class="btn-icon btn-remove"
                  :label="`Remove kit ${item.kitNumber || index + 1} from reconciliation`"
                  title="Remove item"
                  @click="removeItem(index)"
                >
                  <X :size="16" aria-hidden="true" />
                </ClinicalIconButton>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

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
              <label for="pharmacist-signature-id" class="form-label"
                >Signer Identity / User ID: *</label
              >
              <input
                id="pharmacist-signature-id"
                v-model="signerUserId"
                type="text"
                class="input-text"
                placeholder="e.g. site_pharmacist_01"
              />
            </div>

            <div class="form-group">
              <label for="reason-for-change-input" class="form-label"
                >Audit Change Rationale (X-Change-Reason): *</label
              >
              <input
                id="reason-for-change-input"
                v-model="reasonForChange"
                type="text"
                class="input-text"
                placeholder="e.g. Shipment return carton batch reconciliation intake"
              />
            </div>

            <div class="form-group form-group-full">
              <label for="batch-notes-input" class="form-label"
                >Pharmacist Notes / Shipment Reference:</label
              >
              <textarea
                id="batch-notes-input"
                v-model="batchNotes"
                class="textarea-input"
                rows="2"
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
            (<strong>{{ quarantinedCount }}</strong> marked for quarantine)
          </span>
          <span v-else class="summary-text">
            Scan barcodes to enable batch commitment
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
              !signerUserId.trim() ||
              !reasonForChange.trim() ||
              isSubmitting
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
import { ClinicalDrawer, ClinicalIconButton, ClinicalModal } from "ui";
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

const barcodeInputRef = ref<HTMLInputElement | null>(null);
const barcodeInputValue = ref("");
const scannedItems = ref<ScannedKitBufferItem[]>([]);
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
    if (barcodeInputRef.value) {
      barcodeInputRef.value.focus();
    }
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
:deep(.bulk-ip-reconciliation-drawer) {
  background-color: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse);
  border-left-color: var(--semantic-color-border-inverse);
}

:deep(.bulk-ip-reconciliation-drawer > .clinical-drawer-header) {
  background: var(--semantic-color-surface-elevated);
  border-bottom-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

:deep(.bulk-ip-reconciliation-drawer > .clinical-drawer-footer) {
  background: var(--semantic-color-surface-elevated);
  border-top-color: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

:deep(.cert-modal) {
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
}

:deep(.cert-modal > .clinical-modal-header) {
  background: var(--semantic-color-surface-elevated);
  border-bottom-color: var(--semantic-color-border-inverse);
}

:deep(.cert-modal > .clinical-modal-footer) {
  background: var(--semantic-color-surface-elevated);
  border-top-color: var(--semantic-color-border-inverse);
}

.drawer-title {
  color: var(--semantic-color-text-inverse);
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
  font-size: 0.7rem;
  font-weight: 700;
  text-transform: uppercase;
  letter-spacing: 0.04em;
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

.drawer-subtitle {
  margin: 0;
  font-size: 0.82rem;
  color: var(--semantic-color-text-inverse-muted);
  line-height: 1.4;
}

.context-bar {
  display: flex;
  align-items: center;
  flex-wrap: wrap;
  gap: 12px 20px;
  padding: 10px 14px;
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  font-size: 0.85rem;
}

.context-item {
  display: flex;
  gap: 6px;
  flex-wrap: wrap;
}
.context-label {
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}
.context-value {
  color: var(--semantic-color-text-inverse);
  font-weight: 600;
}
.mono {
  font-family: var(--font-mono, monospace);
}

.context-actions {
  margin-left: auto;
}

.scanner-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-interactive-primary);
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
  font-size: 0.9rem;
  color: var(--semantic-color-accent-inverse);
  display: flex;
  align-items: center;
  gap: 6px;
}

.scanner-input-row {
  display: flex;
  gap: 10px;
  flex-wrap: wrap;
}

.barcode-input {
  flex: 1 1 220px;
  min-width: 0;
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--semantic-color-text-inverse);
  font-size: 1rem;
  font-family: var(--font-mono, monospace);
  box-sizing: border-box;
}

.barcode-input:focus-visible {
  border-color: var(--semantic-color-interactive-primary);
}

.scanner-help {
  margin: 8px 0 0 0;
  font-size: 0.78rem;
  color: var(--semantic-color-text-inverse-muted);
  word-break: break-word;
}

.scanner-help code {
  background: var(--semantic-color-border-inverse);
  color: var(--semantic-color-border);
  padding: 1px 4px;
  border-radius: 3px;
}

.alert-banner {
  margin-top: 10px;
  padding: 10px 12px;
  border-radius: 6px;
  display: flex;
  align-items: center;
  gap: 10px;
  font-size: 0.85rem;
}

.alert-icon {
  flex-shrink: 0;
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
  min-width: 44px;
  min-height: 44px;
  display: inline-flex;
  align-items: center;
  justify-content: center;
  cursor: pointer;
  flex-shrink: 0;
}

.metrics-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(135px, 1fr));
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
  min-width: 0;
}

.metric-num {
  font-size: 1.25rem;
  font-weight: 800;
  color: var(--semantic-color-text-inverse);
}

.metric-label {
  font-size: 0.7rem;
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
  font-size: 0.8rem;
  color: var(--semantic-color-text-inverse-muted);
  font-weight: 600;
}

.empty-state-box {
  text-align: center;
  padding: 32px 16px;
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
  font-size: 0.875rem;
}

.table-container {
  overflow-x: auto;
  max-width: 100%;
}

.batch-table {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
  background: var(--semantic-color-surface-elevated);
  border-radius: 8px;
  overflow: hidden;
}

.batch-table th {
  background: var(--semantic-color-surface-inverse);
  color: var(--semantic-color-text-inverse-muted);
  text-align: left;
  padding: 10px 12px;
  font-weight: 700;
  border-bottom: 2px solid var(--semantic-color-border-inverse);
  font-size: 0.75rem;
  text-transform: uppercase;
}

.batch-table td {
  padding: 10px 12px;
  border-bottom: 1px solid var(--semantic-color-border-inverse);
  vertical-align: middle;
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
  flex-wrap: wrap;
}

.input-sm {
  width: 72px;
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 6px 8px;
  color: var(--semantic-color-text-inverse);
  font-size: 1rem;
  box-sizing: border-box;
}

.select-sm {
  min-height: 44px;
  max-width: 100%;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 6px 8px;
  color: var(--semantic-color-text-inverse);
  font-size: 1rem;
  box-sizing: border-box;
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
  display: inline-flex;
  align-items: center;
  gap: 4px;
  font-size: 0.78rem;
  font-weight: 700;
  padding: 3px 8px;
  border-radius: 4px;
}

.flag-valid {
  color: var(--semantic-color-success);
  background: rgba(16, 185, 129, 0.12);
}
.flag-warning {
  color: var(--semantic-color-error);
  background: rgba(239, 68, 68, 0.16);
}
.flag-quarantine {
  color: var(--semantic-color-warning);
  background: rgba(245, 158, 11, 0.16);
}

.btn-icon {
  background: transparent;
  border: none;
  color: var(--semantic-color-text-inverse-muted);
  cursor: pointer;
  border-radius: 6px;
}

.btn-icon:hover {
  color: var(--semantic-color-error);
  background: rgba(239, 68, 68, 0.12);
}

.esign-card {
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-interactive-primary);
  border-radius: 8px;
  padding: 16px;
}

.esign-title {
  margin: 0 0 12px 0;
  font-size: 0.95rem;
  color: var(--semantic-color-accent-inverse);
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

.form-label {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-inverse-muted);
}

.input-text,
.textarea-input {
  width: 100%;
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 8px 12px;
  color: var(--semantic-color-text-inverse);
  font-size: 1rem;
  box-sizing: border-box;
}

.input-text:focus-visible,
.textarea-input:focus-visible {
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
  font-size: 0.82rem;
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
  font-size: 0.85rem;
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
  color: var(--semantic-color-surface);
}
.btn-success {
  background: var(--semantic-color-success);
  color: var(--semantic-color-surface);
}
.btn-secondary {
  background: var(--semantic-color-border-inverse);
  color: var(--semantic-color-text-inverse);
  border-color: var(--semantic-color-border-control);
}

.btn-outline-success {
  background: transparent;
  border-color: var(--semantic-color-success);
  color: var(--semantic-color-success);
}
.btn-outline-warning {
  background: transparent;
  border-color: var(--semantic-color-warning-border-strong);
  color: var(--semantic-color-warning);
}
.btn-outline-primary {
  background: transparent;
  border-color: var(--semantic-color-interactive-primary);
  color: var(--semantic-color-interactive-primary);
}
.btn-ghost-danger {
  background: transparent;
  color: var(--semantic-color-error);
  border: 1px solid transparent;
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
  font-size: 0.75rem;
  font-weight: 700;
  color: var(--semantic-color-success);
  margin-bottom: 2px;
}

.cert-modal-title {
  margin: 0;
  font-size: 1.05rem;
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
  font-size: 0.85rem;
}

.cert-meta-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
}
.cert-meta-item .lbl {
  color: var(--semantic-color-text-inverse-muted);
  font-size: 0.75rem;
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
  font-size: 0.82rem;
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
  font-family: var(--font-mono, monospace);
  font-size: 0.78rem;
  word-break: break-all;
}

.cert-modal-footer {
  display: flex;
  justify-content: flex-end;
  gap: 10px;
  width: 100%;
  flex-wrap: wrap;
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

  .table-container {
    overflow-x: visible;
  }

  .batch-table,
  .batch-table tbody,
  .batch-table tr,
  .batch-table td {
    display: block;
    width: 100%;
    box-sizing: border-box;
  }

  .batch-table {
    background: transparent;
  }

  .batch-table thead {
    display: none;
  }

  .batch-table tr.kit-row {
    background: var(--semantic-color-surface-elevated);
    border: 1px solid var(--semantic-color-border-inverse);
    border-radius: 8px;
    padding: 12px;
    margin-bottom: 10px;
  }

  .batch-table td {
    border-bottom: none;
    padding: 5px 0;
  }

  .batch-table td::before {
    content: attr(data-label);
    display: block;
    font-size: 0.7rem;
    font-weight: 700;
    text-transform: uppercase;
    color: var(--semantic-color-text-inverse-muted);
    margin-bottom: 2px;
  }

  .select-sm,
  .input-sm {
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
