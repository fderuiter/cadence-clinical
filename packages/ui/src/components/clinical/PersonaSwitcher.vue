<script setup>
import { computed, ref } from "vue";

const props = defineProps({
  modelValue: {
    type: String,
    default: "super_admin",
  },
  personas: {
    type: Array,
    default: () => [
      { id: "super_admin", label: "Super Administrator", role: "admin", badge: "Admin" },
      { id: "sponsor_designer", label: "Protocol Author", role: "sponsor_designer", badge: "Designer" },
      { id: "site_crc", label: "Site Coordinator (CRC)", role: "site_crc", badge: "Site" },
      { id: "cra_monitor", label: "CRA Monitor", role: "cra_monitor", badge: "Monitor" },
      { id: "data_manager", label: "Clinical Data Manager", role: "data_manager", badge: "DM" },
      { id: "auditor", label: "Independent Auditor", role: "auditor", badge: "Auditor" },
    ],
  },
});

const emit = defineEmits(["update:modelValue", "change"]);

const isOpen = ref(false);

const activePersona = computed(() => {
  return props.personas.find((p) => p.id === props.modelValue) || props.personas[0];
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
      class="persona-toggle-btn"
      :aria-expanded="isOpen"
      aria-haspopup="true"
      aria-label="Switch User Persona / Role"
      @click="isOpen = !isOpen"
    >
      <span class="persona-indicator"></span>
      <div class="persona-details">
        <span class="persona-label">{{ activePersona.label }}</span>
        <span class="persona-badge">{{ activePersona.badge }}</span>
      </div>
      <span class="dropdown-arrow" aria-hidden="true">{{ isOpen ? '▲' : '▼' }}</span>
    </button>

    <div v-if="isOpen" class="persona-menu" role="menu" aria-label="Available Personas">
      <div class="menu-header">Active Persona & Role Scope</div>
      <button
        v-for="persona in personas"
        :key="persona.id"
        :class="['menu-item', { active: persona.id === modelValue }]"
        role="menuitem"
        @click="selectPersona(persona)"
      >
        <div class="item-content">
          <span class="item-title">{{ persona.label }}</span>
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
  background: var(--color-surface, #ffffff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 6px;
  padding: 6px 12px;
  cursor: pointer;
  box-shadow: 0 1px 2px rgba(0, 0, 0, 0.05);
  transition: all 0.15s ease;
}

.persona-toggle-btn:hover {
  background: #f8fafc;
  border-color: #cbd5e1;
}

.persona-indicator {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--color-success, #15803d);
}

.persona-details {
  display: flex;
  align-items: center;
  gap: 6px;
}

.persona-label {
  font-size: 13px;
  font-weight: 500;
  color: var(--color-text, #0f172a);
}

.persona-badge {
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
  background: #f1f5f9;
  color: var(--color-text-muted, #475569);
  font-weight: 600;
}

.dropdown-arrow {
  font-size: 10px;
  color: var(--color-text-muted, #475569);
}

.persona-menu {
  position: absolute;
  top: calc(100% + 4px);
  right: 0;
  width: 280px;
  background: var(--color-surface, #ffffff);
  border: 1px solid var(--color-border, #e2e8f0);
  border-radius: 8px;
  box-shadow: 0 10px 15px -3px rgba(0, 0, 0, 0.1), 0 4px 6px -2px rgba(0, 0, 0, 0.05);
  z-index: 1000;
  overflow: hidden;
}

.menu-header {
  padding: 8px 12px;
  font-size: 11px;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.5px;
  color: var(--color-text-muted, #475569);
  background: var(--color-surface-muted, #f8fafc);
  border-bottom: 1px solid var(--color-border, #e2e8f0);
}

.menu-item {
  display: flex;
  justify-content: space-between;
  align-items: center;
  width: 100%;
  padding: 10px 12px;
  border: none;
  background: transparent;
  text-align: left;
  cursor: pointer;
  border-bottom: 1px solid #f1f5f9;
  transition: background-color 0.15s ease;
}

.menu-item:last-child {
  border-bottom: none;
}

.menu-item:hover {
  background: #f8fafc;
}

.menu-item.active {
  background: var(--color-primary-light, #e0f2fe);
}

.item-content {
  display: flex;
  flex-direction: column;
}

.item-title {
  font-size: 13px;
  font-weight: 500;
  color: var(--color-text, #0f172a);
}

.item-role {
  font-size: 11px;
  color: var(--color-text-muted, #475569);
}

.item-badge {
  font-size: 11px;
  padding: 2px 6px;
  border-radius: 4px;
  background: #f1f5f9;
  color: var(--color-text-muted, #475569);
}
</style>
