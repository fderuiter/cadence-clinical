<template>
  <ReasonModal
    v-bind="$props"
    @confirm="onConfirm"
    @cancel="onCancel"
  />
</template>

<script setup>
import ReasonModal from "./ReasonModal.vue";

defineProps({
  show: { type: Boolean, required: true },
  title: String,
  description: String,
  options: Array,
  defaultOption: String,
  idPrefix: String,
  cancelText: String,
  confirmText: String,
  placeholder: String,
});

const emit = defineEmits(["confirm", "cancel"]);

function onConfirm(reason) {
  emit("confirm", reason);
}

function onCancel() {
  emit("cancel");
}
</script>
