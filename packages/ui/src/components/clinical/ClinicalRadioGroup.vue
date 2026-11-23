<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :can-manage-queries="canManageQueries"
    :show-query-flag="showQueryFlag"
    :query-label="queryLabel"
    tag="fieldset"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <div class="radio-options">
        <label
          v-for="(opt, idx) in normalizedOptions"
          :key="idx"
          class="radio-option"
          :for="`${slotId}_option_${idx}`"
        >
          <input
            :id="`${slotId}_option_${idx}`"
            type="radio"
            :name="slotId"
            :value="opt.value"
            :checked="modelValue === opt.value"
            :aria-describedby="hasError ? errorId : undefined"
            :aria-invalid="hasError ? 'true' : undefined"
            @change="
              $emit('update:modelValue', opt.value);
              $emit('change', opt.value, $event.target);
            "
          />
          <span class="radio-option-label">{{ opt.label }}</span>
        </label>
      </div>
    </template>
  </ClinicalFieldLayout>
</template>

<script setup>
import { computed } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import { clinicalFieldBaseProps } from "./sharedProps.js";

const props = defineProps({
  ...clinicalFieldBaseProps,
  options: {
    type: Array,
    default: () => [],
  },
  modelValue: {
    type: [String, Number],
    default: "",
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
