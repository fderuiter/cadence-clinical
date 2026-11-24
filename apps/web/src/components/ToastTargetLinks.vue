<template>
  <div class="toast-targets">
    <EntityLink
      v-for="(item, idx) in targets"
      :key="`${item.target}-${idx}`"
      :node-ref="item.target"
      :label="item.description || undefined"
      compact
      @peek="$emit('peek', item)"
    />
  </div>
</template>

<script setup>
import { EntityLink } from "ui";

defineProps({
  targets: {
    type: Array,
    default: () => [],
  },
});

defineEmits(["peek"]);
</script>

<style scoped>
.toast-targets {
  display: flex;
  flex-wrap: wrap;
  gap: 6px;
  margin-top: 4px;
}
</style>
