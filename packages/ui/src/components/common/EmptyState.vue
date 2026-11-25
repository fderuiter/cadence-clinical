<template>
  <div class="empty-state-container" role="region" aria-label="Empty State">
    <div class="empty-state-icon">
      <slot name="icon">
        <span>{{ icon }}</span>
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
defineProps({
  icon: {
    type: String,
    default: "📋",
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
  font-size: var(--font-size-4xl);
  line-height: 1;
  margin-bottom: 12px;
}

.empty-state-title {
  font-size: var(--font-size-lg);
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  margin: 0 0 6px 0;
}

.empty-state-message {
  font-size: var(--font-size-sm);
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
