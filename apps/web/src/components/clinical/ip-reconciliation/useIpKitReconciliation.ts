import { computed, nextTick, onMounted, ref, watch, type Ref } from "vue";

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

export const BATCH_RECONCILIATION_COLUMNS = [
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

export const resolveKitRowClass = (item: ScannedKitBufferItem) => [
  "kit-row",
  {
    "row-quarantine":
      item.disposition === "QUARANTINED_FOR_DESTRUCTION" ||
      item.dispositionReason === "DAMAGED_SEAL" ||
      item.hasLotMismatch,
    "row-lot-mismatch": item.hasLotMismatch,
  },
];

export function formatReconciliationDate(iso: string) {
  if (!iso) return "";
  return new Date(iso).toLocaleString();
}

export function useIpKitReconciliation(
  props: {
    isOpen: boolean;
    studyId: string;
    siteId: string;
    pharmacistName: string;
    defaultUserId: string;
  },
  emit: {
    (e: "close"): void;
    (e: "reconciled", batchResult: any): void;
  },
  clinicalDrawerRef: Ref<any>
) {
  const barcodeInputValue = ref("");
  const scannedItems = ref<ScannedKitBufferItem[]>([]);
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

  function focusScannerInput() {
    nextTick(() => {
      const el =
        clinicalDrawerRef.value?.drawerRef?.querySelector?.(
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
    const existing = scannedItems.value.find(
      (item) => item.kitNumber.toUpperCase() === normalizedKit
    );
    if (existing) {
      warningMessage.value = `Duplicate barcode scan: Kit '${normalizedKit}' is already in the current reconciliation batch buffer. Double counting prevented.`;
      barcodeInputValue.value = "";
      focusScannerInput();
      return;
    }

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
    disp:
      "RETURNED_TO_SITE" | "QUARANTINED_FOR_DESTRUCTION" | "RETURNED_TO_DEPOT"
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

  onMounted(() => {
    if (props.isOpen) {
      loadInventory();
      focusScannerInput();
    }
  });

  return {
    barcodeInputValue,
    scannedItems,
    warningMessage,
    isLoadingInventory,
    isSubmitting,
    signerUserId,
    reasonForChange,
    batchNotes,
    showCertificateModal,
    certificateData,
    returnedToSiteCount,
    quarantinedCount,
    returnedToDepotCount,
    lotMismatchCount,
    loadInventory,
    handleBarcodeScan,
    removeItem,
    clearAllScanned,
    bulkSetDisposition,
    submitBatchReconciliation,
    printCertificate,
    closeDrawerAndReset,
    closeDrawer,
  };
}
