<template>
  <ReasonModal v-bind="$props" @confirm="onConfirm" @cancel="onCancel" />
</template>

<script setup>
import ReasonModal from "./ReasonModal.vue";

defineProps({
  show: { type: Boolean, required: true },
  title: { type: String, default: undefined },
  description: { type: String, default: undefined },
  options: {
    type: Array,
    default: () => [
      {
        value: "Typographical Error",
        text: "Correction of typographical error",
      },
      {
        value: "Transcription Error",
        text: "Correction of transcription error",
      },
      {
        value: "Source Data Clarification",
        text: "Source document clarification",
      },
      { value: "Other", text: "Other (specify below)" },
    ],
  },
  defaultOption: { type: String, default: "Typographical Error" },
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
