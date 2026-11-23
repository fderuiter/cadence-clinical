<template>
  <div class="empty-state-container" role="region" aria-label="Empty State">
    <div class="empty-state-icon" aria-hidden="true">
      <slot name="icon">
        <ClinicalIcon v-if="isSemanticIcon" :name="icon" :size="20" />
        <span v-else>{{ icon }}</span>
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
import { computed } from "vue";
import { isClinicalIconName } from "../../icons/clinicalCoreIcons.js";
import ClinicalIcon from "../clinical/ClinicalIcon.vue";

const props = defineProps({
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
});

defineEmits(["action"]);

const isSemanticIcon = computed(() => isClinicalIconName(props.icon));
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
