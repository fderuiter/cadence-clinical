<template>
  <div class="reconsent-channels">
    <label
      v-for="channel in channels"
      :key="channel.value"
      class="reconsent-channel"
    >
      <input v-model="selected" type="checkbox" :value="channel.value" />
      {{ channel.label }}
    </label>
  </div>
</template>

<script setup>
// Checkbox group for the channels used to notify participants who must re-consent.
const selected = defineModel({ type: Array, default: () => [] });

defineProps({
  channels: {
    type: Array,
    default: () => [
      { value: "SMS", label: "SMS" },
      { value: "Mobile Push", label: "Mobile Push" },
      { value: "Email", label: "Email" },
    ],
  },
});
</script>

<style scoped>
.reconsent-channels {
  display: flex;
  gap: 16px;
  flex-wrap: wrap;
}

.reconsent-channel {
  font-size: 0.8rem;
  cursor: pointer;
  display: flex;
  align-items: center;
  gap: 4px;
}
</style>
