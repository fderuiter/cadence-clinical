<template>
  <div
    ref="elRef"
    :class="['clinical-form-field-wrapper', attrs.class]"
    :style="[wrapperStyle, attrs.style]"
  >
    <template v-if="shouldRender">
      <ClinicalRadioGroup
        v-if="field.type === 'radio' || field.type === 'choice_single'"
        :id="field.id"
        :label="field.label"
        :options="field.options ?? []"
        :model-value="modelValue"
        :query="query"
        :grid-span="field.gridSpan || 12"
        :error="error ?? undefined"
        :attributes="customAttributes"
        :required="resolvedRequired"
        :disabled="resolvedDisabled"
        :hint="resolvedHint"
        :unit="resolvedUnit"
        :range="resolvedRange"
        :can-manage-queries="canManageQueries"
        :query-label="queryLabel"
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
        :required="resolvedRequired"
        :disabled="resolvedDisabled"
        :min="resolvedMin"
        :max="resolvedMax"
        :step="resolvedStep"
        :hint="resolvedHint"
        :unit="resolvedUnit"
        :range="resolvedRange"
        :can-manage-queries="canManageQueries"
        :query-label="queryLabel"
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
        :type="resolvedInputType"
        :options="field.options ?? undefined"
        :model-value="modelValue"
        :query="query"
        :grid-span="field.gridSpan || 12"
        :error="error ?? undefined"
        :attributes="customAttributes"
        :required="resolvedRequired"
        :disabled="resolvedDisabled"
        :min="resolvedMin"
        :max="resolvedMax"
        :step="resolvedStep"
        :data-tabular-nums="isTabularField ? 'true' : undefined"
        :hint="resolvedHint"
        :unit="resolvedUnit"
        :range="resolvedRange"
        :can-manage-queries="canManageQueries"
        :query-label="queryLabel"
        @update:model-value="$emit('update:modelValue', $event)"
        @change="(val, target) => $emit('change', val, target)"
        @create-query="$emit('create-query', $event)"
        @respond-query="$emit('respond-query', $event)"
        @close-query="$emit('close-query')"
        @reopen-query="$emit('reopen-query')"
      />
    </template>
  </div>
</template>

<script setup lang="ts">
import {
  computed,
  ref,
  useAttrs,
  onMounted,
  onUnmounted,
  type CSSProperties,
} from "vue";
import ClinicalInput from "./ClinicalInput.vue";
import ClinicalRadioGroup from "./ClinicalRadioGroup.vue";
import ClinicalLookupInput from "./ClinicalLookupInput.vue";
import type { CdashField } from "../../types/cdash";

defineOptions({ inheritAttrs: false });

const attrs = useAttrs();

const props = withDefaults(
  defineProps<{
    field: CdashField;
    modelValue?: string | number;
    query?: any;
    error?: string | null;
    lookupStatus?: {
      status: "none" | "loading" | "valid" | "invalid" | "degraded";
      message?: string;
    } | null;
    canManageQueries?: boolean;
    queryLabel?: string;
    disabled?: boolean;
  }>(),
  {
    modelValue: "",
    query: null,
    error: null,
    lookupStatus: null,
    canManageQueries: false,
    queryLabel: "",
    disabled: false,
  }
);

defineEmits<{
  (e: "update:modelValue", value: string | number): void;
  (e: "change", value: string | number, target: any): void;
  (e: "input", value: string | number): void;
  (e: "create-query", event: any): void;
  (e: "respond-query", event: any): void;
  (e: "close-query"): void;
  (e: "reopen-query"): void;
}>();

const TABULAR_FIELD_TYPES = new Set([
  "number",
  "numeric",
  "integer",
  "float",
  "decimal",
  "date",
  "datetime",
  "datetime-local",
  "time",
  "month",
  "week",
  "tel",
]);

const resolvedInputType = computed(() => {
  const raw = String(props.field.type || "text").trim();
  return raw || "text";
});

const resolvedRequired = computed(() =>
  Boolean(props.field.required ?? props.field.validation?.required)
);

const resolvedDisabled = computed(() =>
  Boolean(props.disabled || props.field.disabled)
);

const resolvedMin = computed(
  () => props.field.min ?? props.field.validation?.min ?? undefined
);

const resolvedMax = computed(
  () => props.field.max ?? props.field.validation?.max ?? undefined
);

const resolvedStep = computed(
  () => props.field.step ?? props.field.validation?.step ?? undefined
);

const resolvedHint = computed(() =>
  props.field.hint ? String(props.field.hint) : ""
);

const resolvedUnit = computed(() =>
  props.field.unit ? String(props.field.unit) : ""
);

const resolvedRange = computed(() =>
  props.field.range ? String(props.field.range) : ""
);

const isTabularField = computed(
  () =>
    TABULAR_FIELD_TYPES.has(String(props.field.type || "").toLowerCase()) ||
    props.field.tabularNums === true ||
    props.field["data-tabular-nums"] === true ||
    props.field["data-tabular-nums"] === "true" ||
    resolvedMin.value !== undefined ||
    resolvedMax.value !== undefined ||
    resolvedStep.value !== undefined
);

const customAttributes = computed(() => {
  const merged: Record<string, any> = {};
  for (const [k, v] of Object.entries(attrs)) {
    if (
      k !== "class" &&
      k !== "style" &&
      !(k.startsWith("on") && k.length > 2) &&
      v !== undefined
    ) {
      merged[k] = v;
    }
  }
  if (props.field.attributes && typeof props.field.attributes === "object") {
    Object.assign(merged, props.field.attributes);
  }
  if (props.field.cdash) {
    merged["data-cdash"] = props.field.cdash;
  }
  const pattern = props.field.pattern ?? props.field.validation?.pattern;
  if (pattern) {
    merged.pattern = String(pattern);
  }
  if (isTabularField.value) {
    merged["data-tabular-nums"] = "true";
  }
  return merged;
});

const elRef = ref<HTMLElement | null>(null);
const isIntersecting = ref(true); // Default to true so initial render compiles inside the DOM, then gets intersected
const measuredHeight = ref(0);
const hasEnteredViewport = ref(false);

const shouldRender = computed(() => true);

let io: IntersectionObserver | null = null;
let ro: ResizeObserver | null = null;

const wrapperStyle = computed<CSSProperties>(() => ({
  minHeight: "44px",
  width: "100%",
  boxSizing: "border-box",
  contentVisibility: "auto",
  containIntrinsicSize: `auto ${measuredHeight.value ? measuredHeight.value + "px" : "44px"}`,
}));

onMounted(() => {
  if (typeof IntersectionObserver !== "undefined") {
    io = new IntersectionObserver(
      (entries) => {
        const entry = entries[0];
        isIntersecting.value = entry.isIntersecting;
        if (entry.isIntersecting) {
          hasEnteredViewport.value = true;
        }
      },
      {
        rootMargin: "200px", // Render slightly ahead of viewport to avoid visual pop-in
      }
    );
    if (elRef.value) {
      io.observe(elRef.value);
    }
  } else {
    isIntersecting.value = true;
  }

  if (typeof ResizeObserver !== "undefined") {
    ro = new ResizeObserver((entries) => {
      // Bypass the early-return guard for the very first height registration event
      const isFirstMeasurement = measuredHeight.value === 0;
      if (!isFirstMeasurement && !isIntersecting.value) return;
      const entry = entries[0];
      if (entry) {
        const height =
          entry.borderBoxSize && entry.borderBoxSize[0]
            ? entry.borderBoxSize[0].blockSize
            : entry.target.getBoundingClientRect().height;
        if (height > 0) {
          measuredHeight.value = height;
        }
      }
    });
    if (elRef.value) {
      ro.observe(elRef.value);
    }
  }
});

onUnmounted(() => {
  if (io) {
    io.disconnect();
  }
  if (ro) {
    ro.disconnect();
  }
});
</script>
