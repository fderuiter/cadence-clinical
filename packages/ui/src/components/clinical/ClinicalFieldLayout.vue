<template>
  <component
    :is="tag"
    :id="`field-container-${id}`"
    :class="[
      tag === 'fieldset' ? 'clinical-radio-grid' : 'clinical-input',
      {
        'clinical-input--compact': density === 'compact',
        'has-error': showError,
        [`grid-span-${effectiveGridSpan}`]: Boolean(effectiveGridSpan),
      },
      extraClass,
    ]"
    :style="
      effectiveGridSpan ? `grid-column: span ${effectiveGridSpan};` : undefined
    "
    v-bind="outerDataAttributes"
  >
    <component
      :is="tag === 'fieldset' ? 'legend' : 'label'"
      :for="tag === 'fieldset' ? undefined : id"
      :class="[hideLabel ? 'sr-only' : null, labelClass || null]"
    >
      {{ label
      }}<span
        v-if="required"
        :class="[
          'clinical-field-required',
          labelClass === 'form-label' ? 'text-danger' : null,
        ]"
        aria-hidden="true"
        >*</span
      >
    </component>

    <div
      :class="tag === 'fieldset' ? 'radio-options-wrapper' : 'input-wrapper'"
    >
      <slot
        :id="id"
        :error="error"
        :error-id="`validation-error-${id}`"
        :hint-id="hint ? `field-hint-${id}` : undefined"
        :unit-id="unit ? `field-unit-${id}` : undefined"
        :range-id="range ? `field-range-${id}` : undefined"
        :described-by="describedBy"
        :has-error="showError"
        :required="Boolean(required)"
      />

      <!-- Optional Unit -->
      <span v-if="unit" :id="`field-unit-${id}`" class="clinical-field-unit">
        {{ unit }}
      </span>

      <!-- Query Flag -->
      <ClinicalQueryFlag
        v-if="showQueryFlag"
        :id="id"
        :query="query"
        :is-open="isQueryOpen"
        :field-label="label"
        @click="isQueryOpen = !isQueryOpen"
      />
    </div>

    <!-- Additional markup like lookup status indicators -->
    <slot name="after-input" />

    <!-- Optional Range -->
    <span v-if="range" :id="`field-range-${id}`" class="clinical-field-range">
      {{ range }}
    </span>

    <!-- Optional Hint -->
    <p v-if="hint" :id="`field-hint-${id}`" class="clinical-field-hint">
      {{ hint }}
    </p>

    <!-- Validation Error -->
    <div
      v-if="showError"
      :id="`validation-error-${id}`"
      class="validation-error-msg"
      role="alert"
      aria-live="polite"
    >
      {{ error }}
    </div>

    <!-- Query Panel -->
    <ClinicalQueryPanel
      v-if="showQueryFlag && isQueryOpen"
      :id="id"
      :query="query"
      :can-manage-queries="canManageQueries"
      :query-label="queryLabel"
      @close-panel="isQueryOpen = false"
      @create-query="$emit('create-query', $event)"
      @respond-query="$emit('respond-query', $event)"
      @close-query="$emit('close-query')"
      @reopen-query="$emit('reopen-query')"
    />
  </component>
</template>

<script setup>
import { ref, computed } from "vue";
import ClinicalQueryFlag from "./ClinicalQueryFlag.vue";
import ClinicalQueryPanel from "./ClinicalQueryPanel.vue";
import {
  clinicalFieldBaseProps,
  pickDataOnlyAttributes,
  mergeDescribedByIds,
} from "./sharedProps.js";

const props = defineProps({
  ...clinicalFieldBaseProps,
  tag: {
    type: String,
    default: "div",
  },
  extraClass: {
    type: String,
    default: "",
  },
  labelClass: {
    type: [String, Array, Object],
    default: "",
  },
});

defineEmits(["create-query", "respond-query", "close-query", "reopen-query"]);

const isQueryOpen = ref(false);

const showError = computed(() => {
  return !!props.error;
});

const effectiveGridSpan = computed(() => {
  if (props.gridSpan !== null && props.gridSpan !== undefined) {
    return props.gridSpan;
  }
  return props.density === "compact" ? 0 : 12;
});

const outerDataAttributes = computed(() =>
  pickDataOnlyAttributes(props.attributes)
);

const describedBy = computed(() =>
  mergeDescribedByIds(
    props.unit ? `field-unit-${props.id}` : "",
    props.range ? `field-range-${props.id}` : "",
    props.hint ? `field-hint-${props.id}` : "",
    showError.value ? `validation-error-${props.id}` : ""
  )
);
</script>
