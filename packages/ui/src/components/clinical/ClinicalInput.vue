<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :attributes="attributes"
    :can-manage-queries="canManageQueries"
    :show-query-flag="showQueryFlag"
    :query-label="queryLabel"
    tag="div"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <input
        :id="slotId"
        :type="inputType"
        class="clinical-input-control"
        :name="slotId"
        :value="modelValue"
        v-bind="inputAttributes"
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
import { clinicalFieldBaseProps } from "./sharedProps.js";

defineProps({
  ...clinicalFieldBaseProps,
  modelValue: {
    type: [String, Number],
    default: "",
  },
  inputType: {
    type: String,
    default: "text",
  },
  inputAttributes: {
    type: Object,
    default: () => ({}),
  },
});

defineEmits(["update:modelValue", "change"]);
</script>
