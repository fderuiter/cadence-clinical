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
    v-bind="attributes"
  >
    <legend
      v-if="tag === 'fieldset'"
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
    </legend>
    <label
      v-else
      :for="id"
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
    </label>

    <div
      :class="tag === 'fieldset' ? 'radio-options-wrapper' : 'input-wrapper'"
    >
      <slot
        :id="id"
        :error="error"
        :error-id="`validation-error-${id}`"
        :hint-id="hint ? `field-hint-${id}` : undefined"
        :described-by="describedBy"
        :has-error="showError"
      />

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

    <!-- Optional Hint -->
    <p v-if="hint" :id="`field-hint-${id}`" class="clinical-field-hint">
      {{ hint }}
    </p>

    <!-- Validation Error -->
    <div
      :id="`validation-error-${id}`"
      :class="showError ? 'validation-error-msg' : 'sr-only'"
      role="alert"
    >
      {{ showError ? error : "" }}
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
import { clinicalFieldBaseProps } from "./sharedProps.js";

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

const describedBy = computed(() => {
  const ids = [];
  if (props.hint) ids.push(`field-hint-${props.id}`);
  if (showError.value) ids.push(`validation-error-${props.id}`);
  return ids.length > 0 ? ids.join(" ") : undefined;
});
</script>
