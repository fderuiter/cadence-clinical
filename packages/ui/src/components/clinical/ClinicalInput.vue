<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :attributes="attributes"
    :can-manage-queries="canManageQueries"
    :query-label="queryLabel"
    tag="div"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <input
        :id="slotId"
        type="text"
        class="clinical-input-control"
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
import { clinicalFieldBaseProps } from "./clinicalFieldProps.js";

defineProps({
  ...clinicalFieldBaseProps,
  modelValue: {
    type: [String, Number],
    default: "",
  },
});

defineEmits(["update:modelValue", "change"]);
</script>
