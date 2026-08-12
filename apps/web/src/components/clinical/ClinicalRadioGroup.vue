<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    tag="fieldset"
  >
    <template #default="{ id: slotId }">
      <div class="radio-options">
        <div
          v-for="(opt, idx) in normalizedOptions"
          :key="idx"
          class="radio-option"
        >
          <input
            :id="`${slotId}_option_${idx}`"
            type="radio"
            :name="slotId"
            :value="opt.value"
            :checked="modelValue === opt.value"
            @change="
              $emit('update:modelValue', opt.value);
              $emit('change', opt.value, $event.target);
            "
          />
          <label :for="`${slotId}_option_${idx}`">{{ opt.label }}</label>
        </div>
      </div>
    </template>
  </ClinicalFieldLayout>
</template>

<script setup>
import { computed } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
  label: {
    type: String,
    required: true,
  },
  options: {
    type: Array,
    default: () => [],
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
});

defineEmits(["update:modelValue", "change"]);

const normalizedOptions = computed(() => {
  return props.options.map((opt) => {
    if (typeof opt === "string") {
      return { value: opt, label: opt };
    }
    return {
      value: opt.value !== undefined ? opt.value : opt.label,
      label: opt.label !== undefined ? opt.label : opt.value,
    };
  });
});
</script>
