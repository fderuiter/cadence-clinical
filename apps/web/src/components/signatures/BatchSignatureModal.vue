<template>
  <BatchSignatureModal
    ref="innerRef"
    v-bind="$attrs"
    @close="$emit('close', $event)"
    @signed="$emit('signed', $event)"
  />
</template>

<script setup>
import { ref } from "vue";
import BatchSignatureModal from "../../features/signatures/components/BatchSignatureModal.vue";

const innerRef = ref(null);

defineEmits(["close", "signed"]);

defineExpose({
  innerRef,
});
</script>
