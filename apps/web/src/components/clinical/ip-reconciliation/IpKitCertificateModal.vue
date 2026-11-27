<template>
  <ClinicalModal
    :model-value="Boolean(isOpen && certificateData)"
    :teleport="false"
    size="lg"
    :show-close="false"
    backdrop-class="modal-backdrop"
    dialog-class="modal-card modal-lg cert-modal"
    aria-labelledby="cert-modal-title"
    @close="emit('close')"
  >
    <template #header>
      <header class="cert-modal-header">
        <div class="cert-header-titles">
          <div class="cert-badge">
            <Award
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
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
          @click="emit('close')"
        >
          <X
            :size="20"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
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
          <span class="val tabular-nums">{{
            formatReconciliationDate(certificateData.issued_at)
          }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Issued By:</span>
          <span class="val">{{ certificateData.issued_by }}</span>
        </div>
        <div class="cert-meta-item">
          <span class="lbl">Total Reconciled Kits:</span>
          <span class="val fw-bold tabular-nums">{{
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
            <Package
              :size="16"
              aria-hidden="true"
              :stroke-width="1.75"
              focusable="false"
            />
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
        <button type="button" class="btn btn-secondary" @click="emit('print')">
          <Printer
            :size="16"
            aria-hidden="true"
            :stroke-width="1.75"
            focusable="false"
          />
          <span>Print Certificate</span>
        </button>
        <button type="button" class="btn btn-primary" @click="emit('done')">
          Done &amp; Close
        </button>
      </footer>
    </template>
  </ClinicalModal>
</template>

<script setup lang="ts">
import { ClinicalModal } from "ui";
import { Award, Package, Printer, X } from "lucide-vue-next";
import { formatReconciliationDate } from "./useIpKitReconciliation";

defineProps<{
  isOpen: boolean;
  certificateData: any;
}>();

const emit = defineEmits<{
  (e: "close"): void;
  (e: "print"): void;
  (e: "done"): void;
}>();
</script>

<style scoped src="../bulkIpKitReconciliation.css"></style>
