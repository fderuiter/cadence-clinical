<template>
  <ReasonModal
    :show="show"
    :title="title"
    :description="description"
    :options="options"
    :default-option="defaultOption"
    :id-prefix="idPrefix"
    :cancel-text="cancelText"
    :confirm-text="confirmText"
    :placeholder="placeholder"
    @confirm="onConfirm"
    @cancel="onCancel"
  />
</template>

<script setup>
import ReasonModal from "./ReasonModal.vue";

const props = defineProps({
  show: {
    type: Boolean,
    required: true,
  },
  title: {
    type: String,
    default: "Reason for Change Required",
  },
  description: {
    type: String,
    default:
      "To comply with 21 CFR Part 11 / EU Annex 11, you must document a reason for this modification.",
  },
  options: {
    type: Array,
    default: undefined, // defaults to ReasonModal internal default options
  },
  defaultOption: {
    type: String,
    default: "",
  },
  idPrefix: {
    type: String,
    default: "",
  },
  cancelText: {
    type: String,
    default: "Cancel Change",
  },
  confirmText: {
    type: String,
    default: "Sign & Save",
  },
  placeholder: {
    type: String,
    default: "Explain the clinical reason for this modification...",
  },
});

const emit = defineEmits(["confirm", "cancel"]);

function onConfirm(reason) {
  emit("confirm", reason);
}

function onCancel() {
  emit("cancel");
}
</script>
