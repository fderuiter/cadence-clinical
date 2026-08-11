<template>
  <div ref="elRef" :style="wrapperStyle" class="clinical-form-field-wrapper">
    <template v-if="shouldRender">
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
  </div>
</template>

<script setup>
import { computed, ref, onMounted, onUnmounted } from "vue";
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

const elRef = ref(null);
const isIntersecting = ref(true); // Default to true so initial render compiles inside the DOM, then gets intersected
const measuredHeight = ref(0);
const hasEnteredViewport = ref(false);

const shouldRender = computed(() => true);

let io = null;
let ro = null;

const wrapperStyle = computed(() => {
  const styles = {
    minHeight: "44px",
    width: "100%",
    boxSizing: "border-box",
    contentVisibility: "auto",
    containIntrinsicSize: `auto ${measuredHeight.value ? measuredHeight.value + 'px' : '44px'}`,
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
