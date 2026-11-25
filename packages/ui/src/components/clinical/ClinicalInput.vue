<template>
  <ClinicalFieldLayout
    :id="id"
    :class="attrs.class"
    :style="attrs.style"
    :label="label"
    :query="query"
    :grid-span="gridSpan"
    :error="error"
    :attributes="containerAttributes"
    :can-manage-queries="canManageQueries"
    :query-label="queryLabel"
    :density="density"
    :required="effectiveRequired"
    :hint="hint"
    :unit="unit"
    :range="range"
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
    <template
      #default="{ id: slotId, describedBy, hasError, required: slotRequired }"
    >
      <select
        v-if="isSelect"
        v-bind="buildSharedAttrs(slotId, describedBy, hasError, slotRequired)"
        :class="['clinical-input-control', controlClass]"
        :value="modelValue"
        :aria-label="effectiveAriaLabel"
        :data-tabular-nums="resolvedTabularNums"
        @change="onSelectChange"
        @blur="$emit('blur', $event)"
      >
        <option
          v-if="effectivePlaceholder"
          value=""
          :disabled="effectiveRequired"
        >
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
        v-bind="buildSharedAttrs(slotId, describedBy, hasError, slotRequired)"
        :class="['clinical-input-control', controlClass]"
        :value="modelValue"
        :rows="rows"
        :aria-label="effectiveAriaLabel"
        :placeholder="effectivePlaceholder || undefined"
        :readonly="effectiveReadonly || undefined"
        :maxlength="effectiveMaxlength"
        @input="onInput"
        @change="$emit('change', $event.target.value, $event.target)"
        @blur="$emit('blur', $event)"
        @keydown="$emit('keydown', $event)"
        @keyup="$emit('keyup', $event)"
      ></textarea>

      <input
        v-else
        v-bind="buildSharedAttrs(slotId, describedBy, hasError, slotRequired)"
        :type="resolvedType"
        :class="['clinical-input-control', controlClass]"
        :value="modelValue"
        :aria-label="effectiveAriaLabel"
        :data-tabular-nums="resolvedTabularNums"
        :placeholder="effectivePlaceholder || undefined"
        :readonly="effectiveReadonly || undefined"
        :min="effectiveMin"
        :max="effectiveMax"
        :step="effectiveStep"
        :maxlength="effectiveMaxlength"
        :autocomplete="effectiveAutocomplete"
        :inputmode="resolvedInputMode"
        :list="list || undefined"
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
import { computed, useAttrs } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import {
  clinicalFieldBaseProps,
  splitContainerAndControlAttributes,
  firstDefinedAttr,
  resolveBooleanAttr,
  mergeDescribedByIds,
  resolveAriaInvalidValue,
  resolveTabularNumsAttr,
} from "./sharedProps.js";

defineOptions({ inheritAttrs: false });

const attrs = useAttrs();

const STRIPPED_KEYS = new Set(
  "class style type inputmode data-tabular-nums placeholder disabled readonly required aria-required min max step maxlength autocomplete aria-label aria-describedby aria-invalid".split(
    " "
  )
);

const props = defineProps({
  ...clinicalFieldBaseProps,
  modelValue: { type: [String, Number], default: "" },
  type: String,
  as: String,
  options: Array,
  placeholder: String,
  disabled: Boolean,
  readonly: Boolean,
  rows: { type: [Number, String], default: 3 },
  min: [Number, String],
  max: [Number, String],
  step: [Number, String],
  maxlength: [Number, String],
  autocomplete: String,
  inputmode: String,
  tabularNums: { type: Boolean, default: undefined },
  list: String,
  name: String,
  ariaLabel: String,
  ariaDescribedby: String,
  extraClass: String,
  labelClass: [String, Array, Object],
  controlClass: [String, Array, Object],
  controlAttrs: Object,
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

const splitAttrs = computed(() =>
  splitContainerAndControlAttributes(props.attributes, attrs, STRIPPED_KEYS)
);
const containerAttributes = computed(() => splitAttrs.value.container);

const rawTagType = computed(
  () => props.as || props.type || attrs.type || props.attributes?.type
);
const isSelect = computed(() => rawTagType.value === "select");
const isTextarea = computed(() => rawTagType.value === "textarea");

const resolvedType = computed(() => {
  const candidate = firstDefinedAttr(
    props.type,
    attrs.type,
    props.attributes?.type
  );
  if (!candidate || candidate === "select" || candidate === "textarea")
    return "text";
  return candidate === "numeric" ? "number" : String(candidate);
});

const pickAttr = (key, propVal) =>
  firstDefinedAttr(
    propVal,
    props.controlAttrs?.[key],
    attrs[key],
    props.attributes?.[key]
  );

const pickBool = (key, propVal) =>
  resolveBooleanAttr(
    propVal ? true : undefined,
    props.controlAttrs?.[key],
    attrs[key],
    props.attributes?.[key]
  );

const resolvedInputMode = computed(() => {
  const rawMode = pickAttr("inputmode", props.inputmode);
  if (rawMode) return String(rawMode);
  const rawType = firstDefinedAttr(
    props.type,
    attrs.type,
    props.attributes?.type
  );
  return rawType === "numeric" ? "numeric" : undefined;
});
const effectivePlaceholder = computed(
  () => pickAttr("placeholder", props.placeholder) || ""
);
const effectiveDisabled = computed(() => pickBool("disabled", props.disabled));
const effectiveReadonly = computed(() => pickBool("readonly", props.readonly));
const effectiveRequired = computed(
  () => pickBool("required", props.required) || pickBool("aria-required", false)
);
const effectiveAriaRequired = computed(() => {
  const raw = pickAttr("aria-required");
  return raw === true || raw === "true"
    ? "true"
    : raw === "false"
      ? "false"
      : undefined;
});
const effectiveMin = computed(() => pickAttr("min", props.min));
const effectiveMax = computed(() => pickAttr("max", props.max));
const effectiveStep = computed(() => pickAttr("step", props.step));
const effectiveMaxlength = computed(() =>
  pickAttr("maxlength", props.maxlength)
);
const effectiveAutocomplete = computed(() =>
  pickAttr("autocomplete", props.autocomplete)
);
const effectiveAriaLabel = computed(() =>
  pickAttr("aria-label", props.ariaLabel)
);

const resolvedTabularNums = computed(() => {
  const explicitAttr =
    props.controlAttrs?.["data-tabular-nums"] ??
    attrs["data-tabular-nums"] ??
    props.attributes?.["data-tabular-nums"];
  if (
    props.tabularNums === false ||
    explicitAttr === false ||
    explicitAttr === "false"
  ) {
    return undefined;
  }
  if (props.tabularNums === true) {
    return "true";
  }
  if (explicitAttr !== undefined && explicitAttr !== null) {
    return explicitAttr === "" || explicitAttr === true
      ? "true"
      : String(explicitAttr);
  }
  if (isTextarea.value) {
    return undefined;
  }
  return resolveTabularNumsAttr(
    undefined,
    isSelect.value ? "" : resolvedType.value,
    resolvedInputMode.value || "",
    effectiveMin.value,
    effectiveMax.value,
    effectiveStep.value
  );
});

const resolvedControlAttributes = computed(() => {
  const base = { ...splitAttrs.value.control };
  if (props.controlAttrs && typeof props.controlAttrs === "object") {
    for (const [key, value] of Object.entries(props.controlAttrs)) {
      if (!STRIPPED_KEYS.has(key) && value !== undefined && value !== null) {
        base[key] = value;
      }
    }
  }
  return base;
});

function buildSharedAttrs(slotId, describedBy, hasError, slotRequired) {
  return {
    ...resolvedControlAttributes.value,
    id: slotId,
    name: props.name || slotId,
    disabled: effectiveDisabled.value || undefined,
    required: slotRequired || undefined,
    "aria-required": slotRequired ? "true" : effectiveAriaRequired.value,
    "aria-describedby": mergeDescribedBy(describedBy),
    "aria-invalid": resolveAriaInvalid(hasError),
  };
}

const normalizedOptions = computed(() =>
  (props.options || []).map((opt) =>
    typeof opt === "object" && opt !== null
      ? {
          value: opt.value ?? opt.code ?? opt.id ?? "",
          label: opt.label ?? String(opt.value ?? opt.code ?? opt.id ?? ""),
          disabled: Boolean(opt.disabled),
        }
      : { value: opt, label: String(opt), disabled: false }
  )
);

function mergeDescribedBy(slotDescribedBy) {
  return mergeDescribedByIds(
    slotDescribedBy,
    props.ariaDescribedby,
    attrs["aria-describedby"],
    props.attributes?.["aria-describedby"]
  );
}

function resolveAriaInvalid(hasError) {
  return resolveAriaInvalidValue(
    hasError,
    attrs["aria-invalid"],
    props.attributes?.["aria-invalid"]
  );
}

function onInput(event) {
  const val = event?.target ? event.target.value : "";
  emit("update:modelValue", val);
  emit("input", val, event);
}

function onSelectChange(event) {
  const target = event?.target;
  const val = target ? target.value : "";
  emit("update:modelValue", val);
  emit("change", val, target);
}
</script>
