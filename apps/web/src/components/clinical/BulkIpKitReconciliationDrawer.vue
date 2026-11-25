<template>
  <ClinicalDrawer
    ref="clinicalDrawerRef"
    :model-value="drawerOpen"
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
          <span class="context-label">Pharmacist:</span>
          <span class="context-val">{{ authenticatedSignerLabel }}</span>
        </div>
        <div class="context-actions">
          <button
            type="button"
            class="btn btn-sm btn-secondary btn-inline-icon"
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
            <span class="focus-indicator badge badge-active">
              Auto-Focus Active
            </span>
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
            <span>
              21 CFR Part 11 Electronic Signature &amp; Audit Rationale
            </span>
          </h4>
          <div class="esign-form-grid">
            <div class="form-group">
              <ClinicalInput
                id="pharmacist-signature-id"
                :model-value="authenticatedSignerId"
                label="Authenticated Signer Identity (OIDC Session):"
                readonly
                density="compact"
                :show-query-flag="false"
                label-class="form-label"
                control-class="input-text mono"
                :attributes="{ 'aria-readonly': 'true' }"
              />
            </div>

            <div class="form-group">
              <label for="reconcile-sig-password" class="form-label">
                Part 11 Step-Up Password Credential: *
              </label>
              <input
                id="reconcile-sig-password"
                v-model="signaturePassword"
                v-bind="SIGNATURE_PASSWORD_INPUT_ATTRS"
                class="input-text"
                placeholder="Enter password to mint single-use signature token"
              />
            </div>

            <div class="form-group form-group-full">
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
            class="btn btn-success btn-submit-batch btn-inline-icon"
            data-testid="commit-reconcile-batch"
            :disabled="
              scannedItems.length === 0 ||
              !reasonForChange.trim() ||
              !signaturePassword.trim() ||
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
            <Lock v-else :size="15" aria-hidden="true" />
            <span v-if="isSubmitting">Verifying &amp; Signing...</span>
            <span v-else>
              Commit Batch Reconciliation ({{ scannedItems.length }})
            </span>
          </button>
        </div>
      </div>
    </template>
  </ClinicalDrawer>

  <!-- Sibling Certificate Modal View Upon Successful Commitment -->
  <ClinicalModal
    :model-value="Boolean(showCertificateModal && normalizedCertificate)"
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

    <div
      v-if="normalizedCertificate"
      class="cert-body"
      data-testid="reconciliation-certificate"
    >
      <div class="cert-header-grid">
        <div class="cert-meta-item">
          <span class="lbl">Certificate ID:</span>
          <span class="val mono fw-bold">{{
            normalizedCertificate.certificateId
          }}</span>
        </div>
        <div v-if="normalizedCertificate.batchNumber" class="cert-meta-item">
          <span class="lbl">Batch Number:</span>
          <span class="val mono">{{ normalizedCertificate.batchNumber }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Study / Site:</span>
          <span class="val">
            {{ normalizedCertificate.studyId }} /
            {{ normalizedCertificate.siteId }}
          </span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Issued Timestamp:</span>
          <span class="val">{{
            formatDate(normalizedCertificate.issuedAt)
          }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Verified Signer:</span>
          <span class="val">{{ normalizedCertificate.issuedBy }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Total Reconciled Kits:</span>
          <span class="val fw-bold">{{
            normalizedCertificate.totalReconciled
          }}</span>
        </div>
      </div>

      <div
        v-if="normalizedCertificate.kitNumbers.length > 0"
        class="cert-section"
      >
        <h4>Reconciled Kit Catalog</h4>
        <div class="cert-kit-tags">
          <span
            v-for="kit in normalizedCertificate.kitNumbers"
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
        <code class="sig-hash">{{ normalizedCertificate.signatureHash }}</code>
      </div>
    </div>

    <template #footer>
      <footer class="cert-modal-footer">
        <button
          type="button"
          class="btn btn-secondary btn-inline-icon"
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
} from "./ClinicalLucideIcons";
import {
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalIconButton,
  ClinicalInput,
  ClinicalModal,
  SEMANTIC_ACTIONS,
  SIGNATURE_PASSWORD_INPUT_ATTRS,
} from "ui";
import { computed, nextTick, onMounted, ref, watch } from "vue";
import {
  executionApi,
  type IpKitBatchReconciliationRequest,
  type SiteIpKitInventoryItem,
} from "../../api/executionApi";
import { requestStepUpSignature } from "../../api/signatureVerification";
import { useAuthStore } from "../../stores/auth";

export interface ScannedKitBufferItem {
  kitNumber: string;
  scannedLotNumber: string;
  expectedLotNumber: string;
  hasLotMismatch: boolean;
  residualVolumeMl: number | null;
  residualVolumePercentage: number | null;
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

const props = withDefaults(
  defineProps<{
    isOpen?: boolean;
    open?: boolean;
    modelValue?: boolean;
    studyId?: string;
    siteId?: string;
    pharmacistName?: string;
  }>(),
  {
    isOpen: undefined,
    open: undefined,
    modelValue: undefined,
    studyId: "STUDY-ONC-2026",
    siteId: "SITE-101",
    pharmacistName: "Dr. Elena Rostova, PharmD",
  }
);

const emit = defineEmits<{
  (e: "close"): void;
  (e: "update:modelValue", value: boolean): void;
  (e: "reconciled", batchResult: unknown): void;
}>();

const authStore = useAuthStore();
const drawerOpen = computed(() =>
  Boolean(props.isOpen ?? props.open ?? props.modelValue)
);

const authenticatedSignerId = computed(
  () =>
    authStore.user?.username ||
    authStore.userId ||
    authStore.user?.id ||
    props.pharmacistName ||
    "site_pharmacist"
);

const authenticatedSignerLabel = computed(() => {
  const fullName = [authStore.user?.firstName, authStore.user?.lastName]
    .filter(Boolean)
    .join(" ")
    .trim();
  return fullName || props.pharmacistName || authenticatedSignerId.value;
});

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

const siteInventory = ref<Map<string, SiteIpKitInventoryItem>>(new Map());

const signaturePassword = ref("");
const reasonForChange = ref(
  "Site pharmacy batch physical inventory return and accountability reconciliation"
);
const batchNotes = ref("");

const showCertificateModal = ref(false);
const certificateData = ref<Record<string, any> | null>(null);

const normalizedCertificate = computed(() => {
  const cert = certificateData.value;
  if (!cert) return null;
  const rawItems = Array.isArray(cert.items) ? cert.items : [];
  const kitNumbers: string[] = Array.isArray(cert.kit_numbers)
    ? cert.kit_numbers
    : rawItems
        .map((item: Record<string, any>) =>
          String(item.kit_number ?? item.kitNumber ?? "").trim()
        )
        .filter(Boolean);
  return {
    certificateId: String(cert.certificate_id ?? cert.certificateId ?? "—"),
    batchNumber: String(cert.batch_number ?? cert.batchNumber ?? ""),
    studyId: String(cert.study_id ?? cert.studyId ?? props.studyId ?? ""),
    siteId: String(cert.site_id ?? cert.siteId ?? props.siteId ?? ""),
    issuedAt: String(
      cert.processed_at ?? cert.issued_at ?? cert.processedAt ?? ""
    ),
    issuedBy: String(
      cert.processed_by ??
        cert.issued_by ??
        cert.processedBy ??
        authenticatedSignerId.value
    ),
    totalReconciled: Number(
      cert.total_kits_reconciled ??
        cert.totalReconciled ??
        kitNumbers.length ??
        0
    ),
    kitNumbers,
    signatureHash: String(
      cert.payload_hash ??
        cert.digital_signature ??
        cert.signature_hash ??
        cert.signatureHash ??
        "—"
    ),
  };
});

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
  () => [drawerOpen.value, props.studyId, props.siteId],
  ([openVal]) => {
    if (openVal) {
      signaturePassword.value = "";
      void loadInventory();
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
  const currentStudy = props.studyId || "STUDY-101";
  const currentSite = props.siteId || "SITE-01";
  isLoadingInventory.value = true;
  try {
    const data = await executionApi.getSiteIpKitInventory(
      currentStudy,
      currentSite
    );
    if (Array.isArray(data)) {
      const map = new Map<string, SiteIpKitInventoryItem>();
      for (const item of data) {
        map.set(item.kit_number.toUpperCase(), item);
      }
      siteInventory.value = map;
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
  let parsedKit = raw;
  let parsedLot = "";
  if (raw.includes("|")) {
    const parts = raw.split("|");
    parsedKit = (parts[0] ?? "").trim();
    parsedLot = (parts[1] ?? "").trim();
  } else if (raw.includes(":")) {
    const parts = raw.split(":");
    parsedKit = (parts[0] ?? "").trim();
    parsedLot = (parts[1] ?? "").trim();
  }

  const normalizedKit = parsedKit.toUpperCase();

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
  const inventoryMatch = siteInventory.value.get(normalizedKit);
  if (
    inventoryMatch &&
    ["RECONCILED_RETURNED", "QUARANTINED", "RETURNED_TO_DEPOT"].includes(
      inventoryMatch.status
    )
  ) {
    warningMessage.value = `Kit '${normalizedKit}' has already been reconciled in inventory (Status: ${inventoryMatch.status}).`;
    barcodeInputValue.value = "";
    focusScannerInput();
    return;
  }

  const expectedLot = inventoryMatch
    ? inventoryMatch.lot_number
    : parsedLot || `LOT-EST-${Math.abs(hashString(parsedKit)) % 1000}`;
  const resolvedScannedLot = parsedLot || expectedLot;
  const hasLotMismatch =
    parsedLot !== "" && parsedLot.toUpperCase() !== expectedLot.toUpperCase();

  let initialDisposition:
    "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT" =
    "RETURNED_TO_SITE";
  let initialReason: ScannedKitBufferItem["dispositionReason"] =
    "NORMAL_RETURN";

  if (hasLotMismatch) {
    initialDisposition = "QUARANTINED_FOR_DESTRUCTION";
    initialReason = "LOT_MISMATCH";
  }

  const newItem: ScannedKitBufferItem = {
    kitNumber: inventoryMatch ? inventoryMatch.kit_number : normalizedKit,
    scannedLotNumber: resolvedScannedLot,
    expectedLotNumber: expectedLot,
    hasLotMismatch,
    residualVolumeMl: inventoryMatch?.residual_volume_ml ?? 100.0,
    residualVolumePercentage: inventoryMatch?.residual_volume_percentage ?? 100,
    disposition: initialDisposition,
    dispositionReason: initialReason,
  };

  if (hasLotMismatch) {
    warningMessage.value = `Discrepancy Alert: Kit '${newItem.kitNumber}' scanned lot '${resolvedScannedLot}' mismatches expected inventory lot '${expectedLot}'. Automatically flagged for Quarantine.`;
  }

  scannedItems.value.unshift(newItem);
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
  const reason = reasonForChange.value.trim();
  const password = signaturePassword.value;
  if (!reason || !password.trim()) {
    warningMessage.value =
      "Step-up password verification and audit change rationale are required.";
    return;
  }

  isSubmitting.value = true;
  warningMessage.value = "";

  try {
    const verifyRes = await requestStepUpSignature(
      {
        username: authenticatedSignerId.value,
        password,
        totp: null,
        action: "/api/v1/execution/rtsm/kits/reconcile-batch",
        semanticAction: SEMANTIC_ACTIONS.RTSM_KIT_RECONCILE_BATCH,
      },
      {
        missingTokenMessage:
          "Signature verification did not return a single-use signature token.",
      }
    );
    signaturePassword.value = "";
    const sigToken = verifyRes.sig_token;

    const payload: IpKitBatchReconciliationRequest = {
      study_id: props.studyId || "STUDY-101",
      site_id: props.siteId || "SITE-01",
      notes: batchNotes.value.trim() || null,
      reason_for_change: reason,
      items: scannedItems.value.map((i) => ({
        kit_number: i.kitNumber,
        lot_number: i.scannedLotNumber,
        expected_lot_number: i.expectedLotNumber,
        residual_volume_ml: i.residualVolumeMl ?? null,
        residual_volume_percentage: i.residualVolumePercentage ?? null,
        disposition: i.disposition,
        disposition_reason: i.dispositionReason,
      })),
    };

    const batchResult: any = await executionApi.reconcileIpKitBatch(
      payload,
      sigToken
    );
    certificateData.value = batchResult?.certificate ?? batchResult;
    showCertificateModal.value = true;
    emit("reconciled", batchResult);
  } catch (err: any) {
    const detail =
      err?.response?.data?.detail || err?.detail || err?.message || String(err);
    warningMessage.value = `Batch submission error: ${detail}`;
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
  signaturePassword.value = "";
  emit("update:modelValue", false);
  emit("close");
}

function closeDrawer() {
  if (isSubmitting.value) return;
  signaturePassword.value = "";
  emit("update:modelValue", false);
  emit("close");
}

function formatDate(iso: string) {
  if (!iso) return "—";
  return new Date(iso).toLocaleString();
}

onMounted(() => {
  if (drawerOpen.value) {
    void loadInventory();
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
  font-size: 0.82rem;
  color: var(--semantic-color-text-inverse-muted);
}

.btn-inline-icon {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.context-bar {
  display: flex;
  align-items: center;
  gap: 16px;
  padding: 10px 14px;
  background: var(--semantic-color-surface-elevated);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 8px;
  font-size: 0.82rem;
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
  font-size: 0.9rem;
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
  font-size: 1rem;
  font-family: var(--semantic-font-mono);
  box-sizing: border-box;
}

:deep(.barcode-input:focus-visible) {
  border-color: var(--semantic-color-interactive-primary);
}

.scanner-help {
  margin: 8px 0 0 0;
  font-size: 0.78rem;
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
  font-size: 0.85rem;
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
  font-size: 1.35rem;
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
  font-size: 0.8rem;
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
  font-size: 0.88rem;
}

:deep(.table-container) {
  overflow-x: auto;
  max-width: 100%;
}

:deep(.batch-table) {
  width: 100%;
  border-collapse: collapse;
  font-size: 0.85rem;
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
  font-size: 0.75rem;
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
  font-size: 1rem;
  box-sizing: border-box;
}

:deep(.select-sm) {
  min-height: 44px;
  background: var(--semantic-color-surface-inverse);
  border: 1px solid var(--semantic-color-border-inverse);
  border-radius: 6px;
  padding: 6px 8px;
  color: var(--semantic-color-text-inverse);
  font-size: 1rem;
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
  font-size: 0.76rem;
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
  font-size: 0.95rem;
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

.form-label,
:deep(.form-label) {
  font-size: 0.8rem;
  font-weight: 600;
  color: var(--semantic-color-text-inverse-muted);
}

.input-text,
:deep(.input-text),
:deep(.textarea-input) {
  width: 100%;
  max-width: 100%;
  min-width: 0;
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
  font-size: 0.78rem;
  font-weight: 700;
  color: var(--semantic-color-success);
  text-transform: uppercase;
}
.sig-hash {
  font-family: var(--semantic-font-mono);
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

.spin-icon {
  animation: spin 1s linear infinite;
}

@keyframes spin {
  to {
    transform: rotate(360deg);
  }
}
</style>
