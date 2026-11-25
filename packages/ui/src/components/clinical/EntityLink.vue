<template>
  <span
    v-if="isStringNodeRef && parsed.masked"
    class="entity-link entity-link--masked"
    :data-node-kind="parsed.kind"
    :data-mask-reason="parsed.maskReason"
  >
    <EyeOff
      :size="16"
      :stroke-width="1.75"
      aria-hidden="true"
      class="entity-link__icon"
    />
    <span class="entity-link__label">{{ label || parsed.displayLabel }}</span>
  </span>
  <a
    v-else-if="href"
    ref="triggerRef"
    :href="href"
    :class="[
      'clinical-entity-link',
      {
        'clinical-entity-link-tabular': tabular,
        'entity-link': isStringNodeRef,
        'entity-link--interactive': isStringNodeRef,
        'entity-link--compact': compact,
      },
    ]"
    :data-node-ref="isStringNodeRef ? parsed.raw : undefined"
    :data-node-kind="isStringNodeRef ? parsed.kind : undefined"
    :data-entity-type="resolvedObjectNodeRef.type"
    :data-entity-id="resolvedObjectNodeRef.id"
    @click="handleClick"
  >
    <ExternalLink
      v-if="isStringNodeRef && !$slots.default"
      :size="16"
      :stroke-width="1.75"
      aria-hidden="true"
      class="entity-link__icon"
    />
    <slot>
      <span class="entity-link__label">{{ resolvedLabel }}</span>
      <span v-if="showRefCode" class="entity-link__code">{{
        parsed.shortRef
      }}</span>
    </slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </a>
  <button
    v-else
    ref="triggerRef"
    type="button"
    :class="[
      'clinical-entity-link',
      {
        'clinical-entity-link-tabular': tabular,
        'entity-link': isStringNodeRef,
        'entity-link--interactive': isStringNodeRef,
        'entity-link--compact': compact,
      },
    ]"
    :data-node-ref="isStringNodeRef ? parsed.raw : undefined"
    :data-node-kind="isStringNodeRef ? parsed.kind : undefined"
    :data-entity-type="resolvedObjectNodeRef.type"
    :data-entity-id="resolvedObjectNodeRef.id"
    @click="handleClick"
  >
    <ExternalLink
      v-if="isStringNodeRef && !$slots.default"
      :size="16"
      :stroke-width="1.75"
      aria-hidden="true"
      class="entity-link__icon"
    />
    <slot>
      <span class="entity-link__label">{{ resolvedLabel }}</span>
      <span v-if="showRefCode" class="entity-link__code">{{
        parsed.shortRef
      }}</span>
    </slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </button>
</template>

<script setup>
import { computed, ref } from "vue";
import { ExternalLink, EyeOff } from "lucide-vue-next";
import {
  openEntityPeek,
  parseNodeRef,
} from "../../utils/entityPeekRegistry.js";

const props = defineProps({
  nodeRef: {
    type: [String, Object],
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
  compact: {
    type: Boolean,
    default: false,
  },
  showCode: {
    type: Boolean,
    default: true,
  },
});

const emit = defineEmits(["peek", "select"]);
const triggerRef = ref(null);

const isStringNodeRef = computed(() => typeof props.nodeRef === "string");

const parsed = computed(() =>
  parseNodeRef(isStringNodeRef.value ? props.nodeRef : "")
);

const resolvedObjectNodeRef = computed(() => {
  if (isStringNodeRef.value) {
    return {
      type: parsed.value.kind || props.entityType || "record",
      id: String(parsed.value.id ?? ""),
      label: props.label || parsed.value.displayLabel,
      studyId: null,
      siteId: null,
    };
  }
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

const resolvedLabel = computed(() => {
  if (props.label) {
    return props.label;
  }
  if (isStringNodeRef.value) {
    return parsed.value.displayLabel;
  }
  return resolvedObjectNodeRef.value.label || resolvedObjectNodeRef.value.id;
});

const showRefCode = computed(
  () =>
    isStringNodeRef.value &&
    props.showCode &&
    Boolean(parsed.value.shortRef) &&
    parsed.value.shortRef !== resolvedLabel.value
);

function handleClick(event) {
  if (isStringNodeRef.value && parsed.value.masked) {
    return;
  }
  if (props.href && props.preventNavigation) {
    event.preventDefault();
  }
  const triggerEl = triggerRef.value || event?.currentTarget || null;
  if (isStringNodeRef.value) {
    const payload = {
      nodeRef: parsed.value.raw,
      label: resolvedLabel.value,
      parsed: parsed.value,
      triggerEl,
      event,
      originalEvent: event,
    };
    openEntityPeek(parsed.value.raw, {
      label: resolvedLabel.value,
      triggerEl,
    });
    emit("peek", payload);
    emit("select", parsed.value.raw);
    return;
  }
  const payload = {
    nodeRef: resolvedObjectNodeRef.value,
    triggerEl,
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

.entity-link {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  min-height: 2.75rem;
  padding: 0.25rem 0.625rem;
  border-radius: 0.375rem;
  border: 1px solid var(--color-border);
  background: transparent;
  color: var(--color-text);
  font-family: var(--font-sans);
  font-size: var(--font-size-xs);
  font-weight: 500;
  line-height: 1.25;
  text-align: left;
  text-decoration: none;
  vertical-align: middle;
}

.entity-link--compact {
  min-height: 2.75rem;
  padding: 0.25rem 0.5rem;
}

.entity-link--interactive {
  border-color: var(--color-border-control);
  cursor: pointer;
  transition: border-color 120ms ease;
}

.entity-link--interactive:hover,
.entity-link--interactive:focus-visible {
  color: var(--color-text);
  background: transparent;
  border-color: var(--color-primary);
}

.entity-link--masked {
  color: var(--text-muted);
  cursor: default;
}

.entity-link__icon {
  flex-shrink: 0;
}

.entity-link__label {
  font-weight: 500;
}

.entity-link__code {
  font-family: var(--font-mono);
  font-size: var(--font-size-2xs);
  color: var(--text-muted);
}

@media (max-width: 640px) {
  .clinical-entity-link {
    min-height: var(--touch-target-min, 48px);
    min-width: var(--touch-target-min, 48px);
    padding: 8px 10px;
  }
}
</style>
