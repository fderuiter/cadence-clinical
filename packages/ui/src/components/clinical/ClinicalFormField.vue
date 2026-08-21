<template>
  <div
    ref="elRef"
    :style="wrapperStyle"
    class="clinical-form-field-wrapper"
  >
    <template v-if="shouldRender">
      <ClinicalRadioGroup
        v-if="field.type === 'radio' || field.type === 'choice_single'"
        :id="field.id"
        :label="field.label"
        :options="field.options"
        :model-value="modelValue"
        :query="query"
        :grid-span="field.gridSpan || 12"
        :error="error"
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
        :model-value="modelValue"
        :query="query"
        :grid-span="field.gridSpan || 12"
        :error="error"
        :attributes="customAttributes"
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
import { computed, ref, onMounted, onUnmounted } from "vue";
import ClinicalInput from "./ClinicalInput.vue";
import ClinicalRadioGroup from "./ClinicalRadioGroup.vue";
import ClinicalLookupInput from "./ClinicalLookupInput.vue";
import type { CdashField } from "../../types/cdash";

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
  }>(),
  {
    modelValue: "",
    query: null,
    error: null,
    lookupStatus: null,
    canManageQueries: false,
    queryLabel: "",
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

const customAttributes = computed(() => {
  const attrs: Record<string, string> = {};
  if (props.field.cdash) {
    attrs["data-cdash"] = props.field.cdash;
  }
  return attrs;
});

const elRef = ref<HTMLElement | null>(null);
const isIntersecting = ref(true); // Default to true so initial render compiles inside the DOM, then gets intersected
const measuredHeight = ref(0);
const hasEnteredViewport = ref(false);

const shouldRender = computed(() => true);

let io: IntersectionObserver | null = null;
let ro: ResizeObserver | null = null;

const wrapperStyle = computed(() => {
  const styles = {
    minHeight: "44px",
    width: "100%",
    boxSizing: "border-box",
    contentVisibility: "auto",
    containIntrinsicSize:
      `auto ${measuredHeight.value ? measuredHeight.value + "px" : "44px"}` as any,
  };
  return styles;
});

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
