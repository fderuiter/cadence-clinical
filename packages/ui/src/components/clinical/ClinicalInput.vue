<template>
  <ClinicalFieldLayout
    :id="id"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :attributes="containerAttributes"
    :can-manage-queries="canManageQueries"
    :query-label="queryLabel"
    :density="density"
    :required="required"
    :hint="hint"
    :hide-label="hideLabel"
    :show-query-flag="showQueryFlag"
    :extra-class="extraClass"
    :label-class="labelClass"
    tag="div"
    @create-query="$emit('create-query', $event)"
    @respond-query="$emit('respond-query', $event)"
    @close-query="$emit('close-query')"
    @reopen-query="$emit('reopen-query')"
  >
    <template #default="{ id: slotId, describedBy, hasError }">
      <select
        v-if="isSelect"
        v-bind="resolvedControlAttributes"
        :id="slotId"
        :class="['clinical-input-control', controlClass]"
        :name="name || slotId"
        :value="modelValue"
        :disabled="effectiveDisabled || undefined"
        :required="required || undefined"
        :data-tabular-nums="resolvedTabularNums"
        :aria-describedby="mergeDescribedBy(describedBy)"
        :aria-invalid="hasError ? 'true' : undefined"
        @change="onSelectChange"
        @blur="$emit('blur', $event)"
      >
        <option v-if="effectivePlaceholder" value="" :disabled="required">
          {{ effectivePlaceholder }}
        </option>
        <template v-if="options && options.length">
          <option
            v-for="(opt, idx) in normalizedOptions"
            :key="`${opt.value}-${idx}`"
            :value="opt.value"
            :disabled="opt.disabled || undefined"
          >
            {{ opt.label }}
          </option>
        </template>
        <slot />
      </select>

      <textarea
        v-else-if="isTextarea"
        v-bind="resolvedControlAttributes"
        :id="slotId"
        :class="['clinical-input-control', controlClass]"
        :name="name || slotId"
        :value="modelValue"
        :rows="rows"
        :placeholder="effectivePlaceholder || undefined"
        :disabled="effectiveDisabled || undefined"
        :readonly="effectiveReadonly || undefined"
        :required="required || undefined"
        :maxlength="maxlength ?? undefined"
        :data-tabular-nums="resolvedTabularNums"
        :aria-describedby="mergeDescribedBy(describedBy)"
        :aria-invalid="hasError ? 'true' : undefined"
        @input="onInput"
        @change="$emit('change', $event.target.value, $event.target)"
        @blur="$emit('blur', $event)"
        @keydown="$emit('keydown', $event)"
        @keyup="$emit('keyup', $event)"
      ></textarea>

      <input
        v-else
        v-bind="resolvedControlAttributes"
        :id="slotId"
        :type="resolvedType"
        :class="['clinical-input-control', controlClass]"
        :name="name || slotId"
        :value="modelValue"
        :placeholder="effectivePlaceholder || undefined"
        :disabled="effectiveDisabled || undefined"
        :readonly="effectiveReadonly || undefined"
        :required="required || undefined"
        :min="min ?? undefined"
        :max="max ?? undefined"
        :step="step ?? undefined"
        :maxlength="maxlength ?? undefined"
        :autocomplete="autocomplete || undefined"
        :inputmode="resolvedInputMode"
        :data-tabular-nums="resolvedTabularNums"
        :list="list || undefined"
        :aria-describedby="mergeDescribedBy(describedBy)"
        :aria-invalid="hasError ? 'true' : undefined"
        @input="onInput"
        @change="$emit('change', $event.target.value, $event.target)"
        @blur="$emit('blur', $event)"
        @keydown="$emit('keydown', $event)"
        @keyup="$emit('keyup', $event)"
      />
    </template>
    <template v-if="$slots['after-input']" #after-input>
      <slot name="after-input" />
    </template>
  </ClinicalFieldLayout>
</template>

<script setup>
import { computed } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import { clinicalFieldBaseProps } from "./sharedProps.js";

const CONTAINER_ONLY_ATTR_KEYS = new Set(["data-cdash"]);
const CONTROL_STRIPPED_ATTR_KEYS = new Set([
  "type",
  "inputmode",
  "data-tabular-nums",
  "placeholder",
  "disabled",
  "readonly",
]);

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
  as: {
    type: String,
    default: "",
  },
  options: {
    type: Array,
    default: null,
  },
  placeholder: {
    type: String,
    default: "",
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  readonly: {
    type: Boolean,
    default: false,
  },
  rows: {
    type: [Number, String],
    default: 3,
  },
  min: {
    type: [Number, String],
    default: null,
  },
  max: {
    type: [Number, String],
    default: null,
  },
  step: {
    type: [Number, String],
    default: null,
  },
  maxlength: {
    type: [Number, String],
    default: null,
  },
  autocomplete: {
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
  list: {
    type: String,
    default: "",
  },
  name: {
    type: String,
    default: "",
  },
  ariaLabel: {
    type: String,
    default: "",
  },
  ariaDescribedby: {
    type: String,
    default: "",
  },
  extraClass: {
    type: String,
    default: "",
  },
  labelClass: {
    type: [String, Array, Object],
    default: "",
  },
  controlClass: {
    type: [String, Array, Object],
    default: "",
  },
  controlAttrs: {
    type: Object,
    default: () => ({}),
  },
});

const emit = defineEmits([
  "update:modelValue",
  "input",
  "change",
  "blur",
  "keydown",
  "keyup",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
]);

const isSelect = computed(
  () =>
    props.as === "select" ||
    props.type === "select" ||
    props.attributes?.type === "select"
);
const isTextarea = computed(
  () =>
    props.as === "textarea" ||
    props.type === "textarea" ||
    props.attributes?.type === "textarea"
);

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
    !isTextarea.value &&
    (NUMERIC_OR_DATE_INPUT_TYPES.has(resolvedType.value) ||
      NUMERIC_INPUT_MODES.has(resolvedInputMode.value || ""))
  ) {
    return "true";
  }
  return undefined;
});

const containerAttributes = computed(() => {
  const out = {};
  if (props.attributes && typeof props.attributes === "object") {
    for (const [key, value] of Object.entries(props.attributes)) {
      if (CONTAINER_ONLY_ATTR_KEYS.has(key)) {
        out[key] = value;
      }
    }
  }
  return out;
});

const resolvedControlAttributes = computed(() => {
  const out = {};
  if (props.attributes && typeof props.attributes === "object") {
    for (const [key, value] of Object.entries(props.attributes)) {
      if (
        !CONTAINER_ONLY_ATTR_KEYS.has(key) &&
        !CONTROL_STRIPPED_ATTR_KEYS.has(key)
      ) {
        out[key] = value;
      }
    }
  }
  if (props.controlAttrs && typeof props.controlAttrs === "object") {
    for (const [key, value] of Object.entries(props.controlAttrs)) {
      if (!CONTROL_STRIPPED_ATTR_KEYS.has(key)) {
        out[key] = value;
      }
    }
  }
  if (props.ariaLabel) {
    out["aria-label"] = props.ariaLabel;
  }
  return out;
});

const effectivePlaceholder = computed(
  () =>
    props.placeholder ||
    props.controlAttrs?.placeholder ||
    props.attributes?.placeholder ||
    ""
);

const effectiveDisabled = computed(() =>
  Boolean(
    props.disabled || props.controlAttrs?.disabled || props.attributes?.disabled
  )
);

const effectiveReadonly = computed(() =>
  Boolean(
    props.readonly || props.controlAttrs?.readonly || props.attributes?.readonly
  )
);

const normalizedOptions = computed(() => {
  if (!Array.isArray(props.options)) return [];
  return props.options.map((item) => {
    if (item && typeof item === "object") {
      const value = item.value ?? item.code ?? item.id ?? "";
      return {
        value,
        label: item.label ?? String(value),
        disabled: Boolean(item.disabled),
      };
    }
    return {
      value: item,
      label: String(item),
      disabled: false,
    };
  });
});

function mergeDescribedBy(layoutDescribedBy) {
  const parts = [props.ariaDescribedby, layoutDescribedBy].filter(Boolean);
  return parts.length > 0 ? parts.join(" ") : undefined;
}

function onInput(event) {
  const val = event.target.value;
  emit("update:modelValue", val);
  emit("input", val, event);
}

function onSelectChange(event) {
  const val = event.target.value;
  emit("update:modelValue", val);
  emit("change", val, event.target);
}
</script>
