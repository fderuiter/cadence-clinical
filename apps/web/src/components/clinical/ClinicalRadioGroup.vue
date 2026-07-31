<template>
  <fieldset
    :id="`field-container-${id}`"
    class="clinical-radio-grid"
    :class="`grid-span-${gridSpan}`"
    :style="`grid-column: span ${gridSpan};`"
  >
    <legend>{{ label }}</legend>
    <div class="radio-options-wrapper">
      <div class="radio-options">
        <div
          v-for="(opt, idx) in normalizedOptions"
          :key="idx"
          class="radio-option"
        >
          <input
            :id="`${id}_option_${idx}`"
            type="radio"
            :name="id"
            :value="opt.value"
            :checked="modelValue === opt.value"
            @change="
              $emit('update:modelValue', opt.value);
              $emit('change', opt.value, $event.target);
            "
          >
          <label :for="`${id}_option_${idx}`">{{ opt.label }}</label>
        </div>
      </div>

      <!-- Query Flag -->
      <ClinicalQueryFlag
        :id="id"
        :query="query"
        :is-open="isQueryOpen"
        @click="isQueryOpen = !isQueryOpen"
      />
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
  </fieldset>
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
  options: {
    type: Array,
    default: () => [],
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

const normalizedOptions = computed(() => {
  return props.options.map((opt) => {
    if (typeof opt === "string") {
      return { value: opt, label: opt };
    }
    return {
      value: opt.value !== undefined ? opt.value : opt.label,
      label: opt.label !== undefined ? opt.label : opt.value,
    };
  });
});
</script>
