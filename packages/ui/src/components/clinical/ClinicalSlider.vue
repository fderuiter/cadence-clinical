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
    extra-class="clinical-slider"
    tag="div"
  >
    <template #default="{ id: slotId, errorId, hasError }">
      <div class="vas-slider-control-stack">
        <div class="vas-slider-status-row">
          <span
            :id="`${slotId}_display`"
            ref="badgeRef"
            class="vas-value-badge"
            :data-state="isTouched ? 'touched' : 'untouched'"
            aria-live="polite"
          >
            {{ displayText }}
          </span>
        </div>
        <input
          :id="slotId"
          ref="inputRef"
          type="range"
          class="vas-slider-input"
          :name="slotId"
          :min="min"
          :max="max"
          :step="step"
          :value="resolvedNumericValue"
          :data-touched="isTouched ? 'true' : 'false'"
          :aria-valuetext="isTouched ? undefined : untouchedLabel"
          :aria-describedby="hasError ? errorId : undefined"
          :aria-invalid="hasError ? 'true' : undefined"
          @pointerdown="handlePointerDown"
          @keydown="handleKeyDown"
          @input="handleInput"
          @change="handleChange"
        />
        <div class="vas-anchor-labels">
          <span>{{ resolvedMinLabel }}</span>
          <span>{{ resolvedMaxLabel }}</span>
        </div>
      </div>
    </template>
  </ClinicalFieldLayout>
</template>

<script setup>
import { computed, ref, watch } from "vue";
import ClinicalFieldLayout from "./ClinicalFieldLayout.vue";
import { clinicalFieldBaseProps } from "./sharedProps.js";

const SLIDER_INTERACTION_KEYS = new Set([
  "ArrowLeft",
  "ArrowRight",
  "ArrowUp",
  "ArrowDown",
  "Home",
  "End",
  "PageUp",
  "PageDown",
]);

const props = defineProps({
  ...clinicalFieldBaseProps,
  modelValue: {
    type: [String, Number],
    default: "",
  },
  min: {
    type: Number,
    default: 0,
  },
  max: {
    type: Number,
    default: 100,
  },
  step: {
    type: Number,
    default: 1,
  },
  minLabel: {
    type: String,
    default: "",
  },
  maxLabel: {
    type: String,
    default: "",
  },
  touched: {
    type: Boolean,
    default: undefined,
  },
  untouchedLabel: {
    type: String,
    default: "Not answered",
  },
  valuePrefix: {
    type: String,
    default: "Value: ",
  },
});

const emit = defineEmits(["update:modelValue", "update:touched", "change"]);

function hasProvidedValue(val) {
  return (
    val !== "" &&
    val !== null &&
    val !== undefined &&
    !Number.isNaN(Number(val))
  );
}

const inputRef = ref(null);
const badgeRef = ref(null);
const internalTouched = ref(
  props.touched !== undefined
    ? props.touched
    : hasProvidedValue(props.modelValue)
);
const internalValue = ref(
  hasProvidedValue(props.modelValue) ? Number(props.modelValue) : props.min
);

watch(
  () => props.touched,
  (next) => {
    if (next !== undefined) {
      internalTouched.value = Boolean(next);
    }
  }
);

watch(
  () => props.modelValue,
  (next) => {
    if (hasProvidedValue(next)) {
      internalValue.value = Number(next);
      if (props.touched === undefined) {
        internalTouched.value = true;
      }
    } else if (props.touched === undefined) {
      internalTouched.value = false;
      internalValue.value = props.min;
    }
  }
);

const isTouched = computed(() =>
  props.touched !== undefined ? props.touched : internalTouched.value
);

const resolvedNumericValue = computed(() => {
  if (hasProvidedValue(props.modelValue)) {
    return Number(props.modelValue);
  }
  return internalValue.value;
});

const displayText = computed(() =>
  isTouched.value
    ? `${props.valuePrefix}${resolvedNumericValue.value}`
    : props.untouchedLabel
);

const resolvedMinLabel = computed(() =>
  props.minLabel ? props.minLabel : String(props.min)
);

const resolvedMaxLabel = computed(() =>
  props.maxLabel ? props.maxLabel : String(props.max)
);

function syncSliderDomImmediate(targetEl, numericValue) {
  const el = targetEl || inputRef.value;
  if (el) {
    el.value = String(numericValue);
    el.setAttribute("data-touched", "true");
    el.removeAttribute("aria-valuetext");
  }
  if (badgeRef.value) {
    badgeRef.value.setAttribute("data-state", "touched");
    badgeRef.value.textContent = `${props.valuePrefix}${numericValue}`;
  }
}

function commitValue(rawVal, targetEl, emitChangeEvent = false) {
  const parsed = Number(rawVal);
  const clamped = Number.isFinite(parsed)
    ? Math.min(props.max, Math.max(props.min, parsed))
    : props.min;
  internalTouched.value = true;
  internalValue.value = clamped;
  syncSliderDomImmediate(targetEl, clamped);
  emit("update:touched", true);
  emit("update:modelValue", clamped);
  if (emitChangeEvent) {
    emit("change", clamped, targetEl || inputRef.value);
  }
}

function handlePointerDown(event) {
  const el = event?.target || inputRef.value;
  commitValue(el?.value ?? resolvedNumericValue.value, el, false);
}

function handleKeyDown(event) {
  if (!event || !SLIDER_INTERACTION_KEYS.has(event.key)) {
    return;
  }
  const el = event.target || inputRef.value;
  let nextVal = Number(el?.value ?? resolvedNumericValue.value);
  if (event.key === "Home") {
    nextVal = props.min;
  } else if (event.key === "End") {
    nextVal = props.max;
  }
  commitValue(nextVal, el, false);
}

function handleInput(event) {
  const el = event?.target || inputRef.value;
  commitValue(el?.value ?? resolvedNumericValue.value, el, false);
}

function handleChange(event) {
  const el = event?.target || inputRef.value;
  commitValue(el?.value ?? resolvedNumericValue.value, el, true);
}
</script>
