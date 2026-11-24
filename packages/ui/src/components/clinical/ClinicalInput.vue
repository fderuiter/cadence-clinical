<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :attributes="layoutAttributes"
    :can-manage-queries="canManageQueries"
    :query-label="queryLabel"
    tag="div"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <input
        :id="slotId"
        :type="resolvedType"
        class="clinical-input-control"
        :name="slotId"
        :value="modelValue"
        :inputmode="resolvedInputMode"
        :data-tabular-nums="resolvedTabularNums"
        :aria-describedby="hasError ? errorId : undefined"
        :aria-invalid="hasError ? 'true' : undefined"
        @input="$emit('update:modelValue', $event.target.value)"
        @change="$emit('change', $event.target.value, $event.target)"
      />
    </template>
  </ClinicalFieldLayout>
</template>

<script setup>
import { computed } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import { clinicalFieldBaseProps } from "./sharedProps.js";

const NUMERIC_OR_DATE_INPUT_TYPES = new Set([
  "number",
  "date",
  "datetime-local",
  "time",
  "month",
  "week",
  "tel",
  "range",
]);

const NUMERIC_INPUT_MODES = new Set(["numeric", "decimal"]);

const props = defineProps({
  ...clinicalFieldBaseProps,
  modelValue: {
    type: [String, Number],
    default: "",
  },
  type: {
    type: String,
    default: "",
  },
  inputmode: {
    type: String,
    default: "",
  },
  tabularNums: {
    type: Boolean,
    default: undefined,
  },
});

const resolvedType = computed(() => {
  const rawType = props.type || props.attributes?.type || "text";
  return rawType === "numeric" ? "number" : rawType;
});

const resolvedInputMode = computed(() => {
  const rawMode = props.inputmode || props.attributes?.inputmode || "";
  if (rawMode) return rawMode;
  const rawType = props.type || props.attributes?.type || "";
  return rawType === "numeric" ? "numeric" : undefined;
});

const resolvedTabularNums = computed(() => {
  const attrVal = props.attributes?.["data-tabular-nums"];
  if (props.tabularNums === false || attrVal === false || attrVal === "false") {
    return undefined;
  }
  if (props.tabularNums === true) {
    return "true";
  }
  if (attrVal !== undefined && attrVal !== null) {
    return attrVal === "" ? "true" : String(attrVal);
  }
  if (
    NUMERIC_OR_DATE_INPUT_TYPES.has(resolvedType.value) ||
    NUMERIC_INPUT_MODES.has(resolvedInputMode.value || "")
  ) {
    return "true";
  }
  return undefined;
});

const layoutAttributes = computed(() => {
  if (!props.attributes) return {};
  const next = {};
  for (const [key, value] of Object.entries(props.attributes)) {
    if (key !== "type" && key !== "inputmode") {
      next[key] = value;
    }
  }
  return next;
});

defineEmits(["update:modelValue", "change"]);
</script>
