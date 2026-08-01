<template>
  <div
    :id="`field-container-${id}`"
    class="clinical-input"
    :class="{ 'has-error': showError, [`grid-span-${gridSpan}`]: true }"
    :style="`grid-column: span ${gridSpan};`"
    v-bind="attributes"
  >
    <label :for="id">{{ label }}</label>
    <div class="input-wrapper">
      <input
        :id="id"
        type="text"
        :name="id"
        :value="modelValue"
        @input="$emit('update:modelValue', $event.target.value)"
        @change="$emit('change', $event.target.value, $event.target)"
      >

      <!-- Query Flag -->
      <ClinicalQueryFlag
        :id="id"
        :query="query"
        :is-open="isQueryOpen"
        @click="isQueryOpen = !isQueryOpen"
      />
    </div>

    <!-- Validation Error -->
    <div
      v-if="showError"
      class="validation-error-msg"
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
  </div>
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
  modelValue: {
    type: [String, Number],
    default: "",
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
});

defineEmits([
  "update:modelValue",
  "change",
  "create-query",
  "respond-query",
  "close-query",
  "reopen-query",
]);

const isQueryOpen = ref(false);

const showError = computed(() => {
  return props.error && props.modelValue !== "";
});
</script>
