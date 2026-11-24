<script setup>
import { computed, ref } from "vue";
import { PERSONA_CATALOG } from "../../catalog/personas.js";

const props = defineProps({
  modelValue: {
    type: String,
    default: "super_admin",
  },
  personas: {
    type: Array,
    default: () => PERSONA_CATALOG,
  },
});

const emit = defineEmits(["update:modelValue", "change"]);

const isOpen = ref(false);

const activePersona = computed(() => {
  return (
    props.personas.find((p) => p.id === props.modelValue) || props.personas[0]
  );
});

const selectPersona = (persona) => {
  emit("update:modelValue", persona.id);
  emit("change", persona);
  isOpen.value = false;
};
</script>

<template>
  <div class="persona-switcher-container">
    <button
      type="button"
      class="persona-toggle-btn"
      :aria-expanded="isOpen"
      aria-haspopup="true"
      :title="activePersona?.title || activePersona?.label"
      @click="isOpen = !isOpen"
    >
      <span class="persona-indicator"></span>
      <div class="persona-details">
        <span class="persona-label">{{ activePersona.label }}</span>
        <span class="persona-badge">{{ activePersona.badge }}</span>
      </div>
      <span class="sr-only"> — Switch User Persona or Role</span>
      <span class="dropdown-arrow" aria-hidden="true">{{
        isOpen ? "▲" : "▼"
      }}</span>
    </button>

    <div
      v-if="isOpen"
      class="persona-menu"
      role="menu"
      aria-label="Available Personas"
    >
      <div class="menu-header">Active Persona & Role Scope</div>
      <button
        v-for="persona in personas"
        :key="persona.id"
        type="button"
        :class="['menu-item', { active: persona.id === modelValue }]"
        role="menuitem"
        @click="selectPersona(persona)"
      >
        <div class="item-content">
          <span class="item-title">{{ persona.label }}</span>
          <span
            v-if="persona.title && persona.title !== persona.label"
            class="item-workspace-title"
          >
            {{ persona.title }}
          </span>
          <span v-if="persona.description" class="item-description">
            {{ persona.description }}
          </span>
          <span class="item-role">Role: {{ persona.role }}</span>
        </div>
        <span class="item-badge">{{ persona.badge }}</span>
      </button>
    </div>
  </div>
</template>

<style scoped>
.persona-switcher-container {
  position: relative;
  display: inline-block;
}

.persona-toggle-btn {
  display: flex;
  align-items: center;
  gap: 8px;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 6px;
  padding: 6px 12px;
  cursor: pointer;
  box-shadow: var(--shadow-sm);
  transition: all 0.15s ease;
}

.persona-toggle-btn:hover:not(:disabled) {
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border-control);
}

.persona-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--semantic-color-success);
}

.persona-details {
  display: flex;
  align-items: center;
  gap: 6px;
}

.persona-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--semantic-color-text);
}

.persona-badge {
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-muted);
  font-weight: 600;
}

.dropdown-arrow {
  font-size: 10px;
  color: var(--semantic-color-text-muted);
}

.persona-menu {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
  width: min(340px, calc(100vw - 32px));
  max-height: min(440px, calc(100vh - 96px));
  overflow-y: auto;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  box-shadow: var(--shadow-lg);
  z-index: 1000;
}

.menu-header {
  padding: 8px 12px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--semantic-color-text-muted);
  background: var(--semantic-color-surface-muted);
  border-bottom: 1px solid var(--semantic-color-border);
}

.menu-item {
  display: flex;
  justify-content: space-between;
  align-items: flex-start;
  gap: 8px;
  width: 100%;
  padding: 10px 12px;
  border: none;
  background: transparent;
  text-align: left;
  cursor: pointer;
  border-bottom: 1px solid var(--semantic-color-surface-header);
  transition: background-color 0.15s ease;
}

.menu-item:last-child {
  border-bottom: none;
}

.menu-item:hover {
  background: var(--semantic-color-surface-muted);
}

.menu-item.active {
  background: var(--semantic-color-primary-light);
}

.item-content {
  display: flex;
  flex-direction: column;
  gap: 2px;
  min-width: 0;
}

.item-title {
  font-size: 13px;
  font-weight: 600;
  color: var(--semantic-color-text);
}

.item-workspace-title {
  font-size: 11px;
  font-weight: 500;
  color: var(--semantic-color-text-secondary);
}

.item-description {
  font-size: 11px;
  line-height: 1.35;
  color: var(--semantic-color-text-muted);
}

.item-role {
  font-size: 11px;
  color: var(--semantic-color-text-muted);
}

.item-badge {
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-muted);
  flex-shrink: 0;
}
</style>
