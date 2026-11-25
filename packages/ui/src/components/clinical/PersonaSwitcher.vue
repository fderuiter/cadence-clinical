<script setup>
import { computed, ref } from "vue";
import ClinicalIcon from "./ClinicalIcon.vue";
import ClinicalPopover from "./ClinicalPopover.vue";

const props = defineProps({
  modelValue: {
    type: String,
    default: "super_admin",
  },
  personas: {
    type: Array,
    default: () => [
      {
        id: "super_admin",
        label: "Super Administrator",
        role: "admin",
        badge: "Admin",
      },
      {
        id: "sponsor_designer",
        label: "Protocol Author",
        role: "sponsor_designer",
        badge: "Designer",
      },
      {
        id: "site_crc",
        label: "Site Coordinator (CRC)",
        role: "site_crc",
        badge: "Site",
      },
      {
        id: "cra_monitor",
        label: "CRA Monitor",
        role: "cra_monitor",
        badge: "Monitor",
      },
      {
        id: "data_manager",
        label: "Clinical Data Manager",
        role: "data_manager",
        badge: "DM",
      },
      {
        id: "auditor",
        label: "Independent Auditor",
        role: "auditor",
        badge: "Auditor",
      },
    ],
  },
});

const emit = defineEmits(["update:modelValue", "change"]);

const isOpen = ref(false);
const toggleBtnRef = ref(null);

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
      ref="toggleBtnRef"
      type="button"
      class="persona-toggle-btn"
      :aria-expanded="isOpen"
      aria-haspopup="true"
      @click="isOpen = !isOpen"
    >
      <span class="persona-indicator"></span>
      <div class="persona-details">
        <span class="persona-label">{{ activePersona.label }}</span>
        <span class="persona-badge">{{ activePersona.badge }}</span>
      </div>
      <span class="sr-only"> — Switch User Persona or Role</span>
      <span class="dropdown-arrow" aria-hidden="true">
        <ClinicalIcon
          :name="isOpen ? 'chevron-up' : 'chevron-down'"
          :size="14"
        />
      </span>
    </button>

    <ClinicalPopover
      v-model="isOpen"
      :anchor="toggleBtnRef"
      align="end"
      :offset="4"
      :viewport-margin="12"
      :preferred-width="280"
      :teleport="false"
      class="persona-menu"
      role="menu"
      aria-label="Available Personas"
    >
      <div class="menu-header">Active Persona &amp; Role Scope</div>
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
          <span class="item-role">Role: {{ persona.role }}</span>
        </div>
        <span class="item-badge">{{ persona.badge }}</span>
      </button>
    </ClinicalPopover>
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
  font-size: var(--font-size-2xs);
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-muted);
  font-weight: 600;
}

.dropdown-arrow {
  display: inline-flex;
  align-items: center;
  color: var(--semantic-color-text-muted);
}

.persona-menu {
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: 8px;
  box-shadow: var(--shadow-lg);
  z-index: 1000;
  overflow-x: hidden;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.menu-header {
  padding: 8px 12px;
  font-size: var(--font-size-2xs);
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
  align-items: center;
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
}

.item-title {
  font-size: 13px;
  font-weight: 500;
  color: var(--semantic-color-text);
}

.item-role {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-muted);
}

.item-badge {
  font-size: var(--font-size-2xs);
  padding: 2px 6px;
  border-radius: 4px;
  background: var(--semantic-color-surface-header);
  color: var(--semantic-color-text-muted);
}
</style>
