<template>
  <a
    v-if="href"
    ref="triggerRef"
    :href="href"
    :class="[
      'clinical-entity-link',
      { 'clinical-entity-link-tabular': tabular },
    ]"
    :data-entity-type="resolvedNodeRef.type"
    :data-entity-id="resolvedNodeRef.id"
    @click="handleClick"
  >
    <slot>{{ resolvedLabel }}</slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </a>
  <button
    v-else
    ref="triggerRef"
    type="button"
    :class="[
      'clinical-entity-link',
      { 'clinical-entity-link-tabular': tabular },
    ]"
    :data-entity-type="resolvedNodeRef.type"
    :data-entity-id="resolvedNodeRef.id"
    @click="handleClick"
  >
    <slot>{{ resolvedLabel }}</slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </button>
</template>

<script setup>
import { computed, ref } from "vue";

const props = defineProps({
  nodeRef: {
    type: Object,
    default: null,
  },
  entityType: {
    type: String,
    default: "record",
  },
  entityId: {
    type: String,
    default: "",
  },
  label: {
    type: String,
    default: "",
  },
  srContext: {
    type: String,
    default: "",
  },
  href: {
    type: String,
    default: "",
  },
  preventNavigation: {
    type: Boolean,
    default: true,
  },
  tabular: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["peek", "select"]);
const triggerRef = ref(null);

const resolvedNodeRef = computed(() => {
  const base =
    props.nodeRef && typeof props.nodeRef === "object" ? props.nodeRef : {};
  return {
    type: base.type || props.entityType || "record",
    id: String(base.id ?? props.entityId ?? ""),
    label: base.label || props.label || String(base.id ?? props.entityId ?? ""),
    studyId: base.studyId ?? null,
    siteId: base.siteId ?? null,
  };
});

const resolvedLabel = computed(
  () => props.label || resolvedNodeRef.value.label || resolvedNodeRef.value.id
);

function handleClick(event) {
  if (props.href && props.preventNavigation) {
    event.preventDefault();
  }
  const payload = {
    nodeRef: resolvedNodeRef.value,
    triggerEl: triggerRef.value || event.currentTarget || null,
    event,
  };
  emit("peek", payload);
  emit("select", payload);
}
</script>

<style scoped>
.clinical-entity-link {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-2xs, 4px);
  padding: 4px 8px;
  border-radius: var(--radius-sm, 6px);
  border: 1px solid transparent;
  background: transparent;
  color: var(--semantic-color-primary);
  font-family: inherit;
  font-size: inherit;
  font-weight: 600;
  line-height: 1.35;
  text-align: left;
  text-decoration: underline;
  text-underline-offset: 2px;
  cursor: pointer;
}

.clinical-entity-link:hover {
  color: var(--semantic-color-primary-dark);
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
}

.clinical-entity-link-tabular {
  font-variant-numeric: tabular-nums;
}

@media (max-width: 640px) {
  .clinical-entity-link {
    min-height: var(--touch-target-min, 48px);
    min-width: var(--touch-target-min, 48px);
    padding: 8px 10px;
  }
}
</style>
