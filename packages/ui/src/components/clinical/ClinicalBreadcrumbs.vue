<template>
  <nav
    v-if="normalizedItems.length > 0"
    :aria-label="ariaLabel"
    class="clinical-breadcrumbs"
  >
    <ol class="clinical-breadcrumbs-list">
      <li
        v-for="(item, index) in normalizedItems"
        :key="item.id || `${index}-${item.label}`"
        class="clinical-breadcrumbs-item"
      >
        <template v-if="!item.isCurrent">
          <a
            v-if="item.href"
            :href="item.href"
            class="clinical-breadcrumb-link"
            @click="onItemClick(item, index, $event)"
          >
            {{ item.label }}
          </a>
          <button
            v-else
            type="button"
            class="clinical-breadcrumb-button"
            @click="onItemClick(item, index, $event)"
          >
            {{ item.label }}
          </button>
          <svg
            class="clinical-breadcrumb-separator"
            width="14"
            height="14"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
          >
            <polyline points="9 18 15 12 9 6" />
          </svg>
        </template>
        <span v-else aria-current="page" class="clinical-breadcrumb-current">
          {{ item.label }}
        </span>
      </li>
    </ol>
  </nav>
</template>

<script setup>
import { computed } from "vue";

const props = defineProps({
  items: {
    type: Array,
    default: () => [],
  },
  ariaLabel: {
    type: String,
    default: "Breadcrumb",
  },
});

const emit = defineEmits(["navigate"]);

const normalizedItems = computed(() => {
  const valid = (Array.isArray(props.items) ? props.items : []).filter(
    (entry) =>
      entry && typeof entry.label === "string" && entry.label.trim().length > 0
  );
  return valid.map((entry, idx) => ({
    ...entry,
    label: entry.label.trim(),
    isCurrent:
      typeof entry.current === "boolean"
        ? entry.current
        : idx === valid.length - 1,
  }));
});

function onItemClick(item, index, event) {
  if (typeof item.onSelect === "function") {
    event.preventDefault();
    item.onSelect(item, event);
    emit("navigate", { item, index, event });
    return;
  }
  emit("navigate", { item, index, event });
}
</script>

<style scoped>
.clinical-breadcrumbs {
  margin: 0 0 var(--spacing-sm, 12px) 0;
}

.clinical-breadcrumbs-list {
  list-style: none;
  margin: 0;
  padding: 0;
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-2xs, 4px);
  font-size: var(--font-size-sm);
  line-height: 1.4;
}

.clinical-breadcrumbs-item {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-2xs, 4px);
  min-width: 0;
}

.clinical-breadcrumb-link,
.clinical-breadcrumb-button {
  display: inline-flex;
  align-items: center;
  min-height: 36px;
  padding: 4px 6px;
  border-radius: var(--radius-xs, 4px);
  border: none;
  background: transparent;
  color: var(--semantic-color-primary);
  font-family: inherit;
  font-size: inherit;
  font-weight: 600;
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;
}

.clinical-breadcrumb-link:hover,
.clinical-breadcrumb-button:hover {
  color: var(--semantic-color-primary-dark);
  background: var(--semantic-color-surface-muted);
}

.clinical-breadcrumb-separator {
  color: var(--semantic-color-text-muted);
  flex-shrink: 0;
}

.clinical-breadcrumb-current {
  display: inline-flex;
  align-items: center;
  padding: 4px 6px;
  color: var(--semantic-color-text);
  font-weight: 700;
}

@media (max-width: 640px) {
  .clinical-breadcrumb-link,
  .clinical-breadcrumb-button {
    min-height: var(--touch-target-min, 48px);
    padding: 6px 8px;
  }
}
</style>
