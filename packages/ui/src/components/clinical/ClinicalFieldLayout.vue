<template>
  <component
    :is="tag"
    :id="`field-container-${id}`"
    :class="[
      tag === 'fieldset' ? 'clinical-radio-grid' : 'clinical-input',
      { 'has-error': showError, [`grid-span-${gridSpan}`]: true },
      extraClass,
    ]"
    :style="`grid-column: span ${gridSpan};`"
    v-bind="attributes"
  >
    <legend v-if="tag === 'fieldset'">
      {{ label }}
    </legend>
    <label v-else :for="id">{{ label }}</label>

    <div
      :class="tag === 'fieldset' ? 'radio-options-wrapper' : 'input-wrapper'"
    >
      <slot
        :id="id"
        :error="error"
        :errorId="`validation-error-${id}`"
        :error-id="`validation-error-${id}`"
        :hasError="showError"
        :has-error="showError"
      ></slot>

      <!-- Query Flag -->
      <ClinicalQueryFlag
        :id="id"
        :query="query"
        :is-open="isQueryOpen"
        @click="isQueryOpen = !isQueryOpen"
      />
    </div>

    <!-- Additional markup like lookup status indicators -->
    <slot name="after-input"></slot>

    <!-- Validation Error -->
    <div
      v-if="showError"
      :id="`validation-error-${id}`"
      class="validation-error-msg"
      role="status"
      aria-live="polite"
    >
      {{ error }}
    </div>

    <!-- Query Panel -->
    <ClinicalQueryPanel
      v-if="isQueryOpen"
      :id="id"
      :query="query"
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

const props = defineProps({
  id: {
    type: String,
    required: true,
  },
  label: {
    type: String,
    required: true,
  },
  query: {
    type: Object,
    default: null,
  },
  gridSpan: {
    type: [Number, String],
    default: 12,
  },
  error: {
    type: String,
    default: null,
  },
  attributes: {
    type: Object,
    default: () => ({}),
  },
  tag: {
    type: String,
    default: "div",
  },
  extraClass: {
    type: String,
    default: "",
  },
});

defineEmits(["create-query", "respond-query", "close-query", "reopen-query"]);

const isQueryOpen = ref(false);

const showError = computed(() => {
  return !!props.error;
});
</script>
