<template>
  <ReasonModal v-bind="$props" @confirm="onConfirm" @cancel="onCancel" />
</template>

<script setup>
import ReasonModal from "./ReasonModal.vue";

defineProps({
  show: { type: Boolean, required: true },
  title: { type: String, default: undefined },
  description: { type: String, default: undefined },
  options: { type: Array, default: undefined },
  defaultOption: { type: String, default: undefined },
  idPrefix: { type: String, default: undefined },
  cancelText: { type: String, default: undefined },
  confirmText: { type: String, default: undefined },
  placeholder: { type: String, default: undefined },
});

const emit = defineEmits(["confirm", "cancel"]);

function onConfirm(reason) {
  emit("confirm", reason);
}

function onCancel() {
  emit("cancel");
}
</script>
