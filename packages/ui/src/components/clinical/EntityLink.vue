<script>
import {
  ENTITY_LINK_RESOLVER_KEY,
  provideEntityLinkResolver,
} from "./entityLinkContext.js";

export { ENTITY_LINK_RESOLVER_KEY, provideEntityLinkResolver };
</script>

<script setup>
import {
  computed,
  getCurrentInstance,
  inject,
  ref,
  useAttrs,
  watch,
} from "vue";

const KIND_DISPLAY_NAMES = {
  tenant: "Tenant",
  organization: "Organization",
  person: "Person",
  tenant_membership: "Membership",
  role: "Role",
  grant: "Grant",
  qualification_requirement: "Qualification req.",
  qualification: "Qualification",
  study: "Study",
  site: "Site",
  study_site: "Study site",
  participant: "Participant",
  enrollment: "Participant",
  subject: "Participant",
  protocol_version: "Protocol",
  protocol_element: "Schedule element",
  effectivity: "Effectivity",
  concept: "Concept",
  code_system: "Code system",
  code_system_version: "Code system",
  value_set: "Value set",
  standard_version: "Standard",
  library_item: "Library item",
  mapping_spec: "Mapping",
  validation_run: "Validation run",
  validation_finding: "Finding",
  form_query: "Query",
  attestation: "Signature",
  audit_entry: "Audit event",
  artifact: "Document",
  artifact_version: "Document version",
  work_queue_definition: "Work queue",
  proposal: "Proposal",
  agent_task_config: "AI task",
  evaluation_run: "AI evaluation",
  observation: "Observation",
  assertion: "Assertion",
  collection_instance: "Form",
  subscription: "Subscription",
  product_entitlement: "Entitlement",
  ticket: "Ticket",
};

const props = defineProps({
  nodeRef: {
    type: [String, Object],
    default: null,
  },
  entityType: {
    type: String,
    default: null,
  },
  entityId: {
    type: [String, Number],
    default: null,
  },
  label: {
    type: String,
    default: null,
  },
  kindLabel: {
    type: String,
    default: null,
  },
  srContext: {
    type: String,
    default: "",
  },
  href: {
    type: String,
    default: null,
  },
  preventNavigation: {
    type: Boolean,
    default: true,
  },
  tabular: {
    type: Boolean,
    default: false,
  },
  disabled: {
    type: Boolean,
    default: false,
  },
  masked: {
    type: Boolean,
    default: false,
  },
  maskReason: {
    type: String,
    default: null,
  },
  mono: {
    type: Boolean,
    default: false,
  },
  size: {
    type: String,
    default: "sm",
  },
});

const emit = defineEmits(["peek", "navigate", "select"]);
const attrs = useAttrs();
const instance = getCurrentInstance();
const resolver = inject(ENTITY_LINK_RESOLVER_KEY, null);
const triggerRef = ref(null);
const asyncResolvedLabel = ref(null);

const isObjectNodeRef = computed(() =>
  Boolean(props.nodeRef && typeof props.nodeRef === "object")
);

const hasPeekListener = computed(() =>
  Boolean(instance?.vnode?.props?.onPeek || attrs.onPeek)
);

const isPortalStyleLink = computed(
  () =>
    isObjectNodeRef.value ||
    (hasPeekListener.value && !stringNodeRef.value && !props.entityType)
);

const stringNodeRef = computed(() =>
  typeof props.nodeRef === "string" && props.nodeRef.trim()
    ? props.nodeRef.trim()
    : null
);

const objectNodeRef = computed(() => {
  if (!isObjectNodeRef.value) return null;
  const base = props.nodeRef;
  return {
    type: base.type || props.entityType || "record",
    id: String(base.id ?? props.entityId ?? ""),
    label: base.label || props.label || String(base.id ?? props.entityId ?? ""),
    studyId: base.studyId ?? null,
    siteId: base.siteId ?? null,
  };
});

function parseRawNodeRef(value) {
  if (!value || typeof value !== "string") return null;
  const trimmed = value.trim();
  const atIndex = trimmed.indexOf("@");
  const base = atIndex >= 0 ? trimmed.slice(0, atIndex) : trimmed;
  const version =
    atIndex >= 0 ? Number.parseInt(trimmed.slice(atIndex + 1), 10) : null;
  const colonIndex = base.indexOf(":");
  if (colonIndex <= 0 || colonIndex === base.length - 1) return null;
  return {
    raw: trimmed,
    kind: base.slice(0, colonIndex),
    id: base.slice(colonIndex + 1),
    version: Number.isFinite(version) ? version : null,
  };
}

const UUID_TEXT_PATTERN =
  /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

function isUuidText(value) {
  return Boolean(value && UUID_TEXT_PATTERN.test(String(value).trim()));
}

const parsedRef = computed(() => parseRawNodeRef(stringNodeRef.value));

const effectiveKind = computed(() => {
  if (objectNodeRef.value?.type) {
    return String(objectNodeRef.value.type).trim().toLowerCase();
  }
  if (parsedRef.value?.kind) return parsedRef.value.kind;
  if (props.entityType) return String(props.entityType).trim().toLowerCase();
  return null;
});

const effectiveEntityType = computed(() => {
  if (objectNodeRef.value?.type) return objectNodeRef.value.type;
  if (props.entityType) return props.entityType;
  if (parsedRef.value?.kind) return parsedRef.value.kind;
  return null;
});

const effectiveEntityId = computed(() => {
  if (objectNodeRef.value) return objectNodeRef.value.id;
  if (props.entityId !== null && props.entityId !== undefined) {
    return String(props.entityId);
  }
  if (parsedRef.value?.id) return parsedRef.value.id;
  return null;
});

const resolvedDescriptor = computed(() => {
  if (isObjectNodeRef.value) return null;
  if (!resolver || typeof resolver.resolve !== "function") return null;
  return resolver.resolve({
    nodeRef: stringNodeRef.value,
    entityType: props.entityType,
    entityId:
      props.entityId !== null && props.entityId !== undefined
        ? String(props.entityId)
        : null,
    label: props.label,
  });
});

watch(
  () => [stringNodeRef.value, props.entityType, props.entityId, props.label],
  async ([nextNodeRef], _prev, onCleanup) => {
    asyncResolvedLabel.value = null;
    if (
      props.label ||
      !nextNodeRef ||
      !resolver ||
      typeof resolver.lookupLabel !== "function"
    ) {
      return;
    }
    let cancelled = false;
    onCleanup(() => {
      cancelled = true;
    });
    try {
      const found = await resolver.lookupLabel(nextNodeRef);
      if (!cancelled && found) {
        asyncResolvedLabel.value = found;
      }
    } catch {
      // Keep fallback short label when node lookup fails or is unauthorized.
    }
  },
  { immediate: true }
);

const isMasked = computed(() => Boolean(props.masked || props.maskReason));

const computedKindBadge = computed(() => {
  if (props.kindLabel !== null && props.kindLabel !== undefined) {
    return props.kindLabel;
  }
  if (isPortalStyleLink.value) {
    return "";
  }
  if (resolvedDescriptor.value?.kindLabel) {
    return resolvedDescriptor.value.kindLabel;
  }
  const kind = effectiveKind.value;
  if (!kind) return "";
  return (
    KIND_DISPLAY_NAMES[kind] ||
    kind.replace(/[_-]+/g, " ").replace(/\b\w/g, (char) => char.toUpperCase())
  );
});

const computedLabel = computed(() => {
  if (isMasked.value) {
    return props.maskReason === "Blinded" ? "Blinded" : "Restricted";
  }
  if (props.label && String(props.label).trim()) {
    return String(props.label).trim();
  }
  if (objectNodeRef.value) {
    return objectNodeRef.value.label || objectNodeRef.value.id || "Record";
  }
  if (asyncResolvedLabel.value) {
    return asyncResolvedLabel.value;
  }
  if (resolvedDescriptor.value?.label) {
    return resolvedDescriptor.value.label;
  }
  if (parsedRef.value) {
    const versionSuffix =
      parsedRef.value.version !== null ? ` v${parsedRef.value.version}` : "";
    if (!isUuidText(parsedRef.value.id)) {
      return `${parsedRef.value.id}${versionSuffix}`;
    }
    return `${computedKindBadge.value || "Record"}${versionSuffix}`;
  }
  if (
    props.entityId !== null &&
    props.entityId !== undefined &&
    String(props.entityId).trim()
  ) {
    const rawId = String(props.entityId).trim();
    if (!isUuidText(rawId) && !parseRawNodeRef(rawId)) {
      return rawId;
    }
  }
  return computedKindBadge.value || "Record";
});

const showKindBadge = computed(() => {
  const badge = computedKindBadge.value;
  if (!badge) return false;
  const labelText = computedLabel.value || "";
  return (
    labelText.toLowerCase() !== badge.toLowerCase() &&
    !labelText.toLowerCase().startsWith(`${badge.toLowerCase()} v`)
  );
});

const computedHref = computed(() => {
  if (isMasked.value || props.disabled) return null;
  if (props.href) return props.href;
  return resolvedDescriptor.value?.href || null;
});

const hasExplicitClickListener = computed(() => {
  const vnodeProps = instance?.vnode?.props;
  return Boolean(
    vnodeProps?.onPeek ||
    vnodeProps?.onNavigate ||
    vnodeProps?.onSelect ||
    attrs.onPeek ||
    attrs.onNavigate ||
    attrs.onSelect
  );
});

const isInteractive = computed(() => {
  if (isMasked.value || props.disabled) return false;
  if (isPortalStyleLink.value) return true;
  if (computedHref.value) return true;
  if (resolvedDescriptor.value?.navigable) return true;
  if (hasExplicitClickListener.value) return true;
  return Boolean(
    resolver &&
    typeof resolver.navigate === "function" &&
    (stringNodeRef.value || (props.entityType && props.entityId))
  );
});

async function handleActivate(event) {
  if (!isInteractive.value) return;
  const triggerEl = triggerRef.value || event?.currentTarget || null;

  if (isPortalStyleLink.value) {
    if (props.href && props.preventNavigation && event?.preventDefault) {
      event.preventDefault();
    }
    const peekNodeRef = objectNodeRef.value || {
      type: props.entityType || "record",
      id: String(props.entityId ?? ""),
      label: props.label || String(props.entityId ?? ""),
      studyId: null,
      siteId: null,
    };
    const peekPayload = {
      nodeRef: peekNodeRef,
      triggerEl,
      event,
    };
    emit("peek", peekPayload);
    emit("select", peekPayload);
    return;
  }

  if (
    event &&
    (event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey ||
      event.button === 1)
  ) {
    return;
  }
  if (event && typeof event.preventDefault === "function") {
    event.preventDefault();
  }
  if (event && typeof event.stopPropagation === "function") {
    event.stopPropagation();
  }
  const payload = {
    nodeRef: stringNodeRef.value || resolvedDescriptor.value?.nodeRef || null,
    entityType: props.entityType || effectiveKind.value || null,
    entityId: effectiveEntityId.value,
    label: computedLabel.value,
    href: computedHref.value,
    triggerEl,
    event,
  };
  emit("peek", payload);
  emit("select", payload);
  emit("navigate", payload);
  if (resolver && typeof resolver.navigate === "function") {
    await resolver.navigate(payload);
  }
}
</script>

<template>
  <span
    v-if="isMasked"
    :class="['entity-link', `entity-link--${size}`, 'entity-link--masked']"
    :data-entity-kind="effectiveKind || undefined"
    :data-entity-type="effectiveEntityType || undefined"
    :title="
      maskReason === 'Blinded'
        ? 'Masked under K-AUTH-4 blinding protection'
        : 'Masked outside caller scope'
    "
  >
    <span v-if="showKindBadge" class="entity-link__kind">{{
      computedKindBadge
    }}</span>
    <span class="entity-link__label">{{ computedLabel }}</span>
  </span>

  <a
    v-else-if="isInteractive && computedHref"
    ref="triggerRef"
    :href="computedHref"
    :class="[
      'clinical-entity-link',
      'entity-link',
      `entity-link--${size}`,
      'entity-link--interactive',
      {
        'clinical-entity-link-tabular': tabular,
        'entity-link--mono': mono,
        'entity-link--portal': isPortalStyleLink,
      },
    ]"
    :data-node-ref="stringNodeRef || resolvedDescriptor?.nodeRef || undefined"
    :data-entity-kind="effectiveKind || undefined"
    :data-entity-type="effectiveEntityType || undefined"
    :data-entity-id="effectiveEntityId ?? undefined"
    @click="handleActivate"
  >
    <slot>
      <span v-if="showKindBadge" class="entity-link__kind">{{
        computedKindBadge
      }}</span>
      <span class="entity-link__label">{{ computedLabel }}</span>
    </slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </a>

  <button
    v-else-if="isInteractive"
    ref="triggerRef"
    type="button"
    :class="[
      'clinical-entity-link',
      'entity-link',
      `entity-link--${size}`,
      'entity-link--interactive',
      {
        'clinical-entity-link-tabular': tabular,
        'entity-link--mono': mono,
        'entity-link--portal': isPortalStyleLink,
      },
    ]"
    :data-node-ref="stringNodeRef || resolvedDescriptor?.nodeRef || undefined"
    :data-entity-kind="effectiveKind || undefined"
    :data-entity-type="effectiveEntityType || undefined"
    :data-entity-id="effectiveEntityId ?? undefined"
    @click="handleActivate"
  >
    <slot>
      <span v-if="showKindBadge" class="entity-link__kind">{{
        computedKindBadge
      }}</span>
      <span class="entity-link__label">{{ computedLabel }}</span>
    </slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </button>

  <span
    v-else
    :class="[
      'entity-link',
      `entity-link--${size}`,
      'entity-link--static',
      { 'clinical-entity-link-tabular': tabular, 'entity-link--mono': mono },
    ]"
    :data-node-ref="stringNodeRef || undefined"
    :data-entity-kind="effectiveKind || undefined"
    :data-entity-type="effectiveEntityType || undefined"
    :data-entity-id="effectiveEntityId ?? undefined"
  >
    <slot>
      <span v-if="showKindBadge" class="entity-link__kind">{{
        computedKindBadge
      }}</span>
      <span class="entity-link__label">{{ computedLabel }}</span>
    </slot>
    <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
  </span>
</template>

<style scoped>
.entity-link {
  display: inline-flex;
  align-items: center;
  gap: 5px;
  max-width: 100%;
  padding: 2px 8px;
  border-radius: 999px;
  border: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: inherit;
  font-size: 0.75rem;
  font-weight: 500;
  line-height: 1.35;
  text-decoration: none;
  vertical-align: middle;
}

.entity-link--portal {
  padding: 4px 8px;
  border-radius: var(--radius-sm, 6px);
  border-color: transparent;
  background: transparent;
  color: var(--semantic-color-primary);
  font-size: inherit;
  font-weight: 600;
  text-align: left;
  text-decoration: underline;
  text-underline-offset: 2px;
}

.entity-link--md {
  padding: 3px 10px;
  font-size: 0.8125rem;
}

.entity-link--interactive {
  cursor: pointer;
  border-color: var(--semantic-color-border-control);
  transition:
    background-color 0.14s ease,
    border-color 0.14s ease,
    color 0.14s ease;
}

.entity-link--portal.entity-link--interactive {
  border-color: transparent;
}

.entity-link--interactive:hover {
  background: var(--semantic-color-primary-light);
  border-color: var(--semantic-color-primary);
  color: var(--semantic-color-primary);
}

.entity-link--portal.entity-link--interactive:hover {
  color: var(--semantic-color-primary-dark);
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
}

.entity-link--interactive:focus-visible {
  outline: 2px solid var(--semantic-color-accent, var(--semantic-color-primary));
  outline-offset: 2px;
}

.entity-link--masked {
  background: var(--semantic-color-surface-muted);
  border-style: dashed;
  color: var(--semantic-color-text-muted);
}

.entity-link__kind {
  font-size: 0.6875rem;
  font-weight: 600;
  text-transform: uppercase;
  letter-spacing: 0.02em;
  color: var(--semantic-color-text-muted);
}

.entity-link--interactive:hover .entity-link__kind {
  color: var(--semantic-color-primary);
}

.entity-link__label {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}

.entity-link--mono .entity-link__label {
  font-family: var(--font-mono, ui-monospace, SFMono-Regular, Menlo, monospace);
}

.clinical-entity-link-tabular {
  font-variant-numeric: tabular-nums;
}

@media (max-width: 640px) {
  .entity-link--portal {
    min-height: var(--touch-target-min, 48px);
    min-width: var(--touch-target-min, 48px);
    padding: 8px 10px;
  }
}
</style>
