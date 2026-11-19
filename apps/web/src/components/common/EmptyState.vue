<template>
  <UiEmptyState v-bind="props" @action="emit('action')">
    <template v-if="$slots.icon" #icon>
      <slot name="icon" />
    </template>
    <template v-if="$slots.action" #action>
      <slot name="action" />
    </template>
    <template v-if="$slots.default" #default>
      <slot />
    </template>
  </UiEmptyState>
</template>

<script setup lang="ts">
import { EmptyState as UiEmptyState } from "ui";

const props = withDefaults(
  defineProps<{
    icon?: string;
    title?: string;
    message?: string;
    actionText?: string;
  }>(),
  {
    icon: "📋",
    title: "No Data Available",
    message: "",
    actionText: "",
  }
);

const emit = defineEmits<{ (e: "action"): void }>();
</script>
