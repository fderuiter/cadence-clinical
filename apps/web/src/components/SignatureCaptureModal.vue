<template>
  <SignatureCaptureModal
    ref="innerRef"
    v-bind="$attrs"
    @cancel="$emit('cancel', $event)"
    @success="$emit('success', $event)"
    @error="$emit('error', $event)"
  />
</template>

<script setup>
import { ref, computed, provide } from "vue";
import SignatureCaptureModal from "../features/signatures/components/SignatureCaptureModal.vue";
import { etmfService } from "../api/etmf";

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

defineExpose({
  password,
  totp,
});
</script>
