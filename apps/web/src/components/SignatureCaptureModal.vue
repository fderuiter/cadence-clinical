<template>
  <SignatureCaptureModal
    ref="innerRef"
    v-bind="props"
    @cancel="$emit('cancel', $event)"
    @success="$emit('success', $event)"
    @error="$emit('error', $event)"
  />
</template>

<script setup>
import { ref, computed, provide } from "vue";
import SignatureCaptureModal from "../features/signatures/components/SignatureCaptureModal.vue";
import { etmfService } from "../api/etmf";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  username: {
    type: String,
    default: "",
  },
  signerName: {
    type: String,
    default: "",
  },
  role: {
    type: String,
    default: "Subject",
  },
  actionUrl: {
    type: String,
    default: "",
  },
  onSign: {
    type: Function,
    default: null,
  },
});

// Provide the eTMF-specific service to the decoupled FSD component via Dependency Injection
provide("signatureService", etmfService);

const innerRef = ref(null);

defineEmits(["cancel", "success", "error"]);

// Proxy exposed properties for testing backward compatibility
const password = computed({
  get: () => innerRef.value?.password || "",
  set: (val) => {
    if (innerRef.value) {
      innerRef.value.password = val;
    }
  },
});

const totp = computed({
  get: () => innerRef.value?.totp || "",
  set: (val) => {
    if (innerRef.value) {
      innerRef.value.totp = val;
    }
  },
});

const signerNameVal = computed({
  get: () => innerRef.value?.signerNameVal || "",
  set: (val) => {
    if (innerRef.value) {
      innerRef.value.signerNameVal = val;
    }
  },
});

const signerRole = computed({
  get: () => innerRef.value?.signerRoleVal || "",
  set: (val) => {
    if (innerRef.value) {
      innerRef.value.signerRoleVal = val;
    }
  },
});

const signingReason = computed({
  get: () => innerRef.value?.signingReason || "",
  set: (val) => {
    if (innerRef.value) {
      innerRef.value.signingReason = val;
    }
  },
});

defineExpose({
  password,
  totp,
  signerName: signerNameVal,
  signerRole,
  signingReason,
});
</script>
