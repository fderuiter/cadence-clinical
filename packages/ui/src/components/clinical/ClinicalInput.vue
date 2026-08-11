<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :attributes="attributes"
    tag="div"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <input
        :id="slotId"
        type="text"
        :name="slotId"
        :value="modelValue"
        :aria-describedby="hasError ? errorId : undefined"
        :aria-invalid="hasError ? 'true' : undefined"
        @input="$emit('update:modelValue', $event.target.value)"
        @change="$emit('change', $event.target.value, $event.target)"
      />
    </template>
  </ClinicalFieldLayout>
</template>

<script setup>
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";

defineProps({
  id: {
    type: String,
    required: true,
  },
  label: {
    type: String,
    required: true,
  },
  modelValue: {
    type: [String, Number],
    default: "",
  },
  query: {
    type: Object,
    default: null,
  },
  gridSpan: {
    type: [Number, String],
    default: 12,
  },
  error: {
    type: String,
    default: null,
  },
  attributes: {
    type: Object,
    default: () => ({}),
  },
});

defineEmits(["update:modelValue", "change"]);
</script>
