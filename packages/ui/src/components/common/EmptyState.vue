<template>
  <div
    class="empty-state-container"
    :role="live ? 'status' : 'region'"
    :aria-live="live ? 'polite' : undefined"
    :aria-atomic="live ? 'true' : undefined"
    :aria-label="live ? undefined : ariaLabel"
  >
    <div class="empty-state-icon" aria-hidden="true">
      <slot name="icon">
        <ClinicalIcon v-if="isClinicalIconName(icon)" :name="icon" :size="20" />
        <span v-else-if="icon">{{ icon }}</span>
      </slot>
    </div>
    <h3 class="empty-state-title">
      {{ title }}
    </h3>
    <p v-if="message" class="empty-state-message">
      {{ message }}
    </p>

    <div
      v-if="$slots.action || $slots.default || actionText"
      class="empty-state-actions"
    >
      <slot name="action">
        <slot>
          <button
            v-if="actionText"
            type="button"
            class="btn btn-primary btn-sm empty-state-btn"
            @click="$emit('action')"
          >
            {{ actionText }}
          </button>
        </slot>
      </slot>
    </div>
  </div>
</template>

<script setup>
import ClinicalIcon from "../clinical/ClinicalIcon.vue";
import { isClinicalIconName } from "../../icons/iconNames.js";

defineProps({
  icon: {
    type: String,
    default: "clipboard-list",
  },
  title: {
    type: String,
    default: "No Data Available",
  },
  message: {
    type: String,
    default: "",
  },
  actionText: {
    type: String,
    default: "",
  },
  live: {
    type: Boolean,
    default: false,
  },
  ariaLabel: {
    type: String,
    default: "Empty State",
  },
});

defineEmits(["action"]);
</script>

<style scoped>
.empty-state-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  text-align: center;
  padding: 32px 24px;
  background: var(--semantic-color-surface-muted);
  border: 1px dashed var(--semantic-color-border-control);
  border-radius: var(--semantic-radius-md);
  margin: 16px 0;
  width: 100%;
}

.empty-state-icon {
  font-size: 2.25rem;
  line-height: 1;
  margin-bottom: 12px;
}

.empty-state-title {
  font-size: 1.1rem;
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  margin: 0 0 6px 0;
}

.empty-state-message {
  font-size: 0.875rem;
  color: var(--semantic-color-text-secondary);
  max-width: 480px;
  margin: 0 0 16px 0;
  line-height: 1.5;
}

.empty-state-actions {
  display: flex;
  flex-wrap: wrap;
  gap: 12px;
  align-items: center;
  justify-content: center;
}
</style>
