<template>
  <SoAMatrixEditor
    :activities="activities"
    :visits="visits"
    @update:activities="$emit('update:activities', $event)"
    @add-visit="$emit('add-visit', $event)"
  />
</template>

<script setup>
import SoAMatrixEditor from "./SoAMatrixEditor.vue";

defineProps({
  activities: {
    type: Array,
    default: () => [],
  },
  visits: {
    type: Array,
    default: () => [],
  },
});

defineEmits(["update:activities", "add-visit"]);
</script>
