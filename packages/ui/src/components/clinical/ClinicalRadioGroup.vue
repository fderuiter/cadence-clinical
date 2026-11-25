<template>
  <ClinicalFieldLayout
    v-bind="layoutProps"
    tag="fieldset"
    @create-query="$emit('create-query', $event)"
    @respond-query="$emit('respond-query', $event)"
    @close-query="$emit('close-query')"
    @reopen-query="$emit('reopen-query')"
  >
    <template
      #default="{ id: slotId, describedBy, hasError, required: slotRequired }"
    >
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
            :disabled="disabled || opt.disabled || undefined"
            :required="slotRequired || undefined"
            :aria-required="slotRequired ? 'true' : effectiveAriaRequired"
            :aria-invalid="resolveAriaInvalid(hasError)"
            :aria-describedby="mergeDescribedBy(describedBy)"
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
import { computed, useAttrs } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import {
  buildFieldLayoutProps,
  clinicalFieldBaseProps,
  mergeDescribedByIds,
  resolveAriaInvalidValue,
  resolveEffectiveRequiredAndAria,
} from "./sharedProps.js";

defineOptions({ inheritAttrs: false });

const attrs = useAttrs();

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
  disabled: {
    type: Boolean,
    default: false,
  },
});

defineEmits([
  "update:modelValue",
  "change",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
]);

const containerAttributes = computed(() => {
  const out = {};
  for (const source of [attrs, props.attributes]) {
    if (source && typeof source === "object") {
      for (const [key, value] of Object.entries(source)) {
        if (key.startsWith("data-") && key !== "data-tabular-nums") {
          out[key] = value;
        }
      }
    }
  }
  return out;
});

const reqState = computed(() => resolveEffectiveRequiredAndAria(props, attrs));
const effectiveRequired = computed(() => reqState.value.required);
const effectiveAriaRequired = computed(() => reqState.value.ariaRequired);
const layoutProps = computed(() =>
  buildFieldLayoutProps(
    props,
    attrs,
    containerAttributes.value,
    effectiveRequired.value
  )
);

function resolveAriaInvalid(hasError) {
  return resolveAriaInvalidValue(
    hasError,
    attrs["aria-invalid"],
    props.attributes?.["aria-invalid"]
  );
}

function mergeDescribedBy(layoutDescribedBy) {
  return mergeDescribedByIds(
    props.attributes?.["aria-describedby"],
    attrs["aria-describedby"],
    layoutDescribedBy
  );
}

const normalizedOptions = computed(() => {
  return props.options.map((opt) => {
    if (typeof opt === "string") {
      return { value: opt, label: opt, disabled: false };
    }
    return {
      value: opt.value !== undefined ? opt.value : opt.label,
      label: opt.label !== undefined ? opt.label : opt.value,
      disabled: Boolean(opt.disabled),
    };
  });
});
</script>
