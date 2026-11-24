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
import SignatureCaptureModal, {
  SHARED_SIGNATURE_MODAL_PROPS,
} from "../features/signatures/components/SignatureCaptureModal.vue";
import { etmfService } from "../api/etmf";

const props = defineProps({
  ...SHARED_SIGNATURE_MODAL_PROPS,
  isOpen: {
    type: Boolean,
    default: false,
  },
  actionUrl: {
    type: String,
    default: "",
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

const signerNameProxy = computed({
  get: () => innerRef.value?.signerNameVal || "",
  set: (val) => {
    if (innerRef.value) {
      innerRef.value.signerNameVal = val;
    }
  },
});

const signerRoleProxy = computed({
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

const needsPasswordReentry = computed(() =>
  Boolean(innerRef.value?.needsPasswordReentry)
);

defineExpose({
  password,
  totp,
  signerName: signerNameProxy,
  signerRole: signerRoleProxy,
  signingReason,
  needsPasswordReentry,
  confirm: (...args) => innerRef.value?.confirm?.(...args),
  handleRetry: (...args) => innerRef.value?.handleRetry?.(...args),
});
</script>
