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
          <ScanBarcode
            :size="20"
            class="header-icon"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <h2 id="drawer-heading" class="drawer-title">
            Bulk IP Kit Reconciliation &amp; Batch Disposition Drawer
          </h2>
        </div>

        <ClinicalIconButton
          id="btn-close-bulk-ip-drawer"
          class="btn-close"
          label="Close Bulk IP Kit Reconciliation Drawer"
          icon="x"
          size="lg"
          :disabled="isSubmitting"
          @click="closeDrawer"
        />
      </div>
    </template>

    <div class="drawer-body">
      <IpKitScannerAndMetricsPanel
        v-model:barcode-input-value="barcodeInputValue"
        :study-id="studyId"
        :site-id="siteId"
        :pharmacist-name="pharmacistName"
        :is-loading-inventory="isLoadingInventory"
        :warning-message="warningMessage"
        :total-scanned-count="scannedItems.length"
        :returned-to-site-count="returnedToSiteCount"
        :quarantined-count="quarantinedCount"
        :returned-to-depot-count="returnedToDepotCount"
        :lot-mismatch-count="lotMismatchCount"
        @refresh-inventory="loadInventory"
        @scan="handleBarcodeScan"
        @dismiss-warning="warningMessage = ''"
        @bulk-disposition="bulkSetDisposition"
        @clear-all="clearAllScanned"
      />

      <!-- Scanned Kits Buffer Table / Mobile Stacked Record Cards -->
      <div v-if="scannedItems.length === 0" class="empty-state-box">
        <div class="empty-icon">
          <Package
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
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
        mobile-mode="table"
        :columns="BATCH_RECONCILIATION_COLUMNS"
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
            <AlertTriangle
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>Lot Mismatch</span>
          </span>
          <span
            v-else-if="row.disposition === 'QUARANTINED_FOR_DESTRUCTION'"
            class="status-flag flag-quarantine"
          >
            <Lock
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
            <span>Quarantined</span>
          </span>
          <span v-else class="status-flag flag-valid">
            <Check
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
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
            <X
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
          </ClinicalIconButton>
        </template>
      </ClinicalDataTable>

      <!-- 21 CFR Part 11 Electronic Signature / Change Rationale -->
      <section v-if="scannedItems.length > 0" class="esign-section">
        <div class="esign-card">
          <h4 class="esign-title">
            <FileSignature
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
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
          <span
            v-if="scannedItems.length > 0"
            class="summary-text tabular-nums"
          >
            Ready to reconcile
            <strong class="tabular-nums">{{ scannedItems.length }}</strong>
            kit(s) with digital accountability certificate.
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
            class="btn btn-success btn-submit-batch tabular-nums"
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
              :stroke-width="1.75"
              focusable="false"
            />
            <template v-else>
              <Lock
                :size="16"
                aria-hidden="true"
                :stroke-width="1.75"
                focusable="false"
              />
              <span class="tabular-nums"
                >Commit Batch Reconciliation ({{ scannedItems.length }})</span
              >
            </template>
          </button>
        </div>
      </div>
    </template>
  </ClinicalDrawer>

  <IpKitCertificateModal
    :is-open="showCertificateModal"
    :certificate-data="certificateData"
    @close="showCertificateModal = false"
    @print="printCertificate"
    @done="closeDrawerAndReset"
  />
</template>

<script setup lang="ts">
import { ref } from "vue";
import {
  ClinicalDataTable,
  ClinicalDrawer,
  ClinicalIconButton,
  ClinicalInput,
} from "ui";
import {
  AlertTriangle,
  Check,
  FileSignature,
  Loader2,
  Lock,
  Package,
  ScanBarcode,
  X,
} from "lucide-vue-next";
import IpKitCertificateModal from "./ip-reconciliation/IpKitCertificateModal.vue";
import IpKitScannerAndMetricsPanel from "./ip-reconciliation/IpKitScannerAndMetricsPanel.vue";
import {
  BATCH_RECONCILIATION_COLUMNS,
  resolveKitRowClass,
  useIpKitReconciliation,
} from "./ip-reconciliation/useIpKitReconciliation";

export type {
  InventoryKitItem,
  ScannedKitBufferItem,
} from "./ip-reconciliation/useIpKitReconciliation";

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

const {
  barcodeInputValue,
  batchNotes,
  bulkSetDisposition,
  certificateData,
  clearAllScanned,
  closeDrawer,
  closeDrawerAndReset,
  handleBarcodeScan,
  isLoadingInventory,
  isSubmitting,
  loadInventory,
  lotMismatchCount,
  printCertificate,
  quarantinedCount,
  reasonForChange,
  removeItem,
  returnedToDepotCount,
  returnedToSiteCount,
  scannedItems,
  showCertificateModal,
  signerUserId,
  submitBatchReconciliation,
  warningMessage,
} = useIpKitReconciliation(props, emit, clinicalDrawerRef);
</script>

<style scoped src="../shared/drawerHeaderChrome.css"></style>
<style scoped src="./bulkIpKitReconciliation.css"></style>

<style scoped>
/* Stacked kit record cards at <= 480px (Req 6) */
@media (max-width: 480px) {
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
</style>
