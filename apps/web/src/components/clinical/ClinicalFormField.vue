<template>
  <ClinicalRadioGroup
    v-if="field.type === 'radio' || field.type === 'choice_single'"
    :id="field.id"
    :label="field.label"
    :options="field.options"
    :model-value="modelValue"
    :query="query"
    :grid-span="field.gridSpan || 12"
    @update:model-value="$emit('update:modelValue', $event)"
    @change="(val, target) => $emit('change', val, target)"
    @create-query="$emit('create-query', $event)"
    @respond-query="$emit('respond-query', $event)"
    @close-query="$emit('close-query')"
    @reopen-query="$emit('reopen-query')"
  />

  <ClinicalLookupInput
    v-else-if="field.type === 'concept_code'"
    :id="field.id"
    :label="field.label"
    :model-value="modelValue"
    :query="query"
    :grid-span="field.gridSpan || 12"
    :error="error"
    :status="lookupStatus ? lookupStatus.status : 'none'"
    :status-message="lookupStatus ? lookupStatus.message : ''"
    :attributes="customAttributes"
    @update:model-value="$emit('update:modelValue', $event)"
    @input="$emit('input', $event)"
    @change="(val, target) => $emit('change', val, target)"
    @create-query="$emit('create-query', $event)"
    @respond-query="$emit('respond-query', $event)"
    @close-query="$emit('close-query')"
    @reopen-query="$emit('reopen-query')"
  />

  <ClinicalInput
    v-else
    :id="field.id"
    :label="field.label"
    :model-value="modelValue"
    :query="query"
    :grid-span="field.gridSpan || 12"
    :error="error"
    :attributes="customAttributes"
    @update:model-value="$emit('update:modelValue', $event)"
    @change="(val, target) => $emit('change', val, target)"
    @create-query="$emit('create-query', $event)"
    @respond-query="$emit('respond-query', $event)"
    @close-query="$emit('close-query')"
    @reopen-query="$emit('reopen-query')"
  />
</template>

<script setup>
import { computed } from "vue";
import ClinicalInput from "./ClinicalInput.vue";
import ClinicalRadioGroup from "./ClinicalRadioGroup.vue";
import ClinicalLookupInput from "./ClinicalLookupInput.vue";

const props = defineProps({
  field: {
    type: Object,
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
  error: {
    type: String,
    default: null,
  },
  lookupStatus: {
    type: Object,
    default: null,
  },
});

defineEmits([
  "update:modelValue",
  "change",
  "input",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
]);

const customAttributes = computed(() => {
  const attrs = {};
  if (props.field.cdash) {
    attrs["data-cdash"] = props.field.cdash;
  }
  return attrs;
});
</script>
