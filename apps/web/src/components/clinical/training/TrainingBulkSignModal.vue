<template>
  <ClinicalModal
    :model-value="modelValue"
    :teleport="false"
    :dismissible="!submittingBulkSign"
    dialog-class="modal-card"
    title="21 CFR Part 11 Bulk Delegation Log Sign-Off"
    @update:model-value="emit('update:modelValue', $event)"
  >
    <p class="modal-description tabular-nums">
      Executing bulk electronic sign-off for
      <strong class="tabular-nums"
        >{{ selectedCount }} pending delegation records</strong
      >. This operation generates an immutable SHA-256 digital seal and routes
      signed records to eTMF Zone 5 (Artifact 05.02.04).
    </p>
    <p v-if="bulkSignError" class="badge badge-error" role="alert">
      {{ bulkSignError }}
    </p>
    <div class="form-group">
      <label for="bulk-signer-name">Signer Printed Name:</label>
      <input
        id="bulk-signer-name"
        :value="signerName"
        type="text"
        class="text-input"
        :disabled="submittingBulkSign"
        placeholder="Your full name"
        @input="emit('update:signerName', $event.target.value)"
      />
    </div>
    <div class="form-group">
      <label for="bulk-signer-role">Clinical Authority Role:</label>
      <input
        id="bulk-signer-role"
        :value="signerRole"
        type="text"
        class="text-input"
        :disabled="submittingBulkSign"
        placeholder="Principal Investigator"
        @input="emit('update:signerRole', $event.target.value)"
      />
    </div>
    <div class="form-group">
      <label for="bulk-password">21 CFR Part 11 Password Credential:</label>
      <input
        id="bulk-password"
        :value="signerCredential"
        type="password"
        class="text-input"
        :disabled="submittingBulkSign"
        placeholder="Enter password to verify identity"
        @input="emit('update:signerCredential', $event.target.value)"
      />
    </div>
    <div class="form-group">
      <span>Legal Attestation Statement:</span>
      <div class="statement-box">
        "I confirm that I have reviewed and approved the delegated duties for
        the selected site staff personnel in accordance with 21 CFR Part 11 and
        ICH GCP E6(R2)."
      </div>
    </div>
    <template #footer>
      <button
        type="button"
        class="btn btn-secondary"
        :disabled="submittingBulkSign"
        @click="emit('update:modelValue', false)"
      >
        Cancel
      </button>
      <button
        type="button"
        class="btn btn-success"
        :disabled="submittingBulkSign || !signerCredential || !signerName"
        @click="emit('submit')"
      >
        <span v-if="submittingBulkSign">Executing Part 11 Signature...</span>
        <span v-else>Confirm &amp; Execute Signature </span>
      </button>
    </template>
  </ClinicalModal>
</template>

<script setup>
import { ClinicalModal } from "ui";

defineProps({
  modelValue: {
    type: Boolean,
    default: false,
  },
  selectedCount: {
    type: Number,
    default: 0,
  },
  signerName: {
    type: String,
    default: "",
  },
  signerRole: {
    type: String,
    default: "",
  },
  signerCredential: {
    type: String,
    default: "",
  },
  bulkSignError: {
    type: String,
    default: "",
  },
  submittingBulkSign: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits([
  "update:modelValue",
  "update:signerName",
  "update:signerRole",
  "update:signerCredential",
  "submit",
]);
</script>

<style scoped src="../protocolTrainingConsole.css"></style>
