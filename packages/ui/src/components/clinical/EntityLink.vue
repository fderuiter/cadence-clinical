<template>
  <span v-if="isArtifactMode" class="clinical-entity-link-host">
    <button
      :id="id"
      ref="triggerRef"
      type="button"
      :class="[
        'clinical-entity-link',
        `clinical-entity-link--${variant}`,
        { 'clinical-entity-link-tabular': tabular },
      ]"
      :data-entity-type="resolvedNodeRef.type"
      :data-entity-id="resolvedNodeRef.id"
      aria-haspopup="dialog"
      :aria-expanded="isOpen ? 'true' : 'false'"
      :disabled="disabled"
      @click="handleArtifactClick"
    >
      <svg
        class="clinical-entity-link__icon"
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
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
        <polyline points="14 2 14 8 20 8" />
        <line x1="8" y1="13" x2="16" y2="13" />
        <line x1="8" y1="17" x2="13" y2="17" />
      </svg>
      <span class="clinical-entity-link__label">{{
        descriptor.chipLabel
      }}</span>
      <span
        v-if="showArtifactCode && descriptor.artifactCode"
        class="clinical-entity-link__code"
        >{{ ` (${descriptor.artifactCode})` }}</span
      >
      <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
    </button>

    <EntityPeekDrawer
      :is-open="isOpen"
      :descriptor="descriptor"
      :open-label="openLabel"
      :teleport="teleport"
      :intercept-navigate="hasNavigateListener"
      @close="closeDrawer"
      @navigate="handleNavigate"
    >
      <template v-if="$slots.default" #default="slotProps">
        <slot v-bind="slotProps" />
      </template>
      <template v-if="$slots.actions" #actions="slotProps">
        <slot name="actions" v-bind="slotProps" />
      </template>
    </EntityPeekDrawer>
  </span>
  <span
    v-else-if="isStringNodeRef && parsed.masked"
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
    :id="id"
    ref="triggerRef"
    :href="href"
    :class="[
      'clinical-entity-link',
      isStringNodeRef
        ? 'entity-link entity-link--interactive'
        : 'clinical-entity-link--plain',
      {
        'clinical-entity-link-tabular': tabular,
        'entity-link--compact': isStringNodeRef && compact,
      },
    ]"
    :data-node-ref="isStringNodeRef ? parsed.raw : undefined"
    :data-node-kind="isStringNodeRef ? parsed.kind : undefined"
    :data-entity-type="resolvedNodeRef.type"
    :data-entity-id="resolvedNodeRef.id"
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
    :id="id"
    ref="triggerRef"
    type="button"
    :class="[
      'clinical-entity-link',
      isStringNodeRef
        ? 'entity-link entity-link--interactive'
        : 'clinical-entity-link--plain',
      {
        'clinical-entity-link-tabular': tabular,
        'entity-link--compact': isStringNodeRef && compact,
      },
    ]"
    :data-node-ref="isStringNodeRef ? parsed.raw : undefined"
    :data-node-kind="isStringNodeRef ? parsed.kind : undefined"
    :data-entity-type="resolvedNodeRef.type"
    :data-entity-id="resolvedNodeRef.id"
    :disabled="disabled"
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
import { computed, getCurrentInstance, ref } from "vue";
import { ExternalLink, EyeOff } from "lucide-vue-next";
import EntityPeekDrawer from "./EntityPeekDrawer.vue";
import { resolveEntityLinkDescriptor } from "./entityLinkAdapter.js";
import {
  openEntityPeek,
  parseNodeRef,
} from "../../utils/entityPeekRegistry.js";

const props = defineProps({
  id: {
    type: String,
    default: undefined,
  },
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
  srContext: {
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
  peekDrawer: {
    type: Boolean,
    default: false,
  },
  document: {
    type: Object,
    default: null,
  },
  documentId: {
    type: String,
    default: "",
  },
  artifactCode: {
    type: String,
    default: "",
  },
  artifactName: {
    type: String,
    default: "",
  },
  label: {
    type: String,
    default: "",
  },
  subtitle: {
    type: String,
    default: "",
  },
  filename: {
    type: String,
    default: "",
  },
  status: {
    type: String,
    default: "",
  },
  studyId: {
    type: String,
    default: "",
  },
  siteId: {
    type: String,
    default: "",
  },
  subjectId: {
    type: String,
    default: "",
  },
  versionIndex: {
    type: [Number, String],
    default: null,
  },
  supportDetails: {
    type: String,
    default: "",
  },
  href: {
    type: String,
    default: "",
  },
  tab: {
    type: String,
    default: "",
  },
  openLabel: {
    type: String,
    default: "Open in eTMF binder",
  },
  variant: {
    type: String,
    default: "chip",
  },
  showCode: {
    type: Boolean,
    default: undefined,
  },
  teleport: {
    type: Boolean,
    default: true,
  },
  disabled: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits([
  "peek",
  "select",
  "open-peek",
  "close-peek",
  "navigate",
]);

const isOpen = ref(false);
const triggerRef = ref(null);
const instance = getCurrentInstance();

const isArtifactMode = computed(() =>
  Boolean(
    props.peekDrawer ||
    props.document ||
    props.documentId ||
    props.artifactCode ||
    props.artifactName
  )
);

const hasNavigateListener = computed(() => {
  const vnodeProps = instance?.vnode?.props;
  return Boolean(vnodeProps && typeof vnodeProps.onNavigate === "function");
});

const descriptor = computed(() =>
  resolveEntityLinkDescriptor({
    document: props.document,
    documentId: props.documentId,
    artifactCode: props.artifactCode,
    artifactName: props.artifactName,
    label: props.label,
    subtitle: props.subtitle,
    filename: props.filename,
    status: props.status,
    studyId: props.studyId,
    siteId: props.siteId,
    subjectId: props.subjectId,
    versionIndex: props.versionIndex,
    supportDetails: props.supportDetails,
    href: props.href,
    tab: props.tab,
  })
);

const isStringNodeRef = computed(() => typeof props.nodeRef === "string");

const parsed = computed(() =>
  parseNodeRef(isStringNodeRef.value ? props.nodeRef : "")
);

const resolvedNodeRef = computed(() => {
  if (isArtifactMode.value) {
    return {
      type: descriptor.value.kind,
      id:
        descriptor.value.documentId ||
        descriptor.value.artifactCode ||
        descriptor.value.chipLabel,
      label: descriptor.value.chipLabel,
      studyId: descriptor.value.studyId || null,
      siteId: descriptor.value.siteId || null,
    };
  }
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
  return resolvedNodeRef.value.label || resolvedNodeRef.value.id;
});

const showArtifactCode = computed(() => Boolean(props.showCode));

const showRefCode = computed(
  () =>
    isStringNodeRef.value &&
    (props.showCode ?? true) &&
    Boolean(parsed.value.shortRef) &&
    parsed.value.shortRef !== resolvedLabel.value
);

function buildPeekEventPayload(event) {
  return {
    nodeRef: resolvedNodeRef.value,
    triggerEl: triggerRef.value || event?.currentTarget || null,
    event,
  };
}

function handleClick(event) {
  if (props.disabled) return;
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
  const payload = buildPeekEventPayload(event);
  emit("peek", payload);
  emit("select", payload);
}

function handleArtifactClick(event) {
  if (props.disabled) return;
  isOpen.value = true;
  const payload = buildPeekEventPayload(event);
  emit("peek", payload);
  emit("select", payload);
  emit("open-peek", descriptor.value);
}

function closeDrawer() {
  if (!isOpen.value) return;
  isOpen.value = false;
  emit("close-peek", descriptor.value);
}

function handleNavigate(payload) {
  emit("navigate", payload);
}
</script>

<style scoped>
.clinical-entity-link-host {
  display: inline-flex;
  align-items: center;
  vertical-align: middle;
}

.clinical-entity-link {
  display: inline-flex;
  align-items: center;
  gap: 0.3125rem;
  min-height: var(--control-target-min);
  padding: 0.1875rem 0.5625rem;
  border-radius: var(--radius-full);
  border: 1px solid var(--semantic-color-border-control);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-accent);
  font-family: var(--font-sans);
  font-size: 0.78125rem;
  font-weight: 600;
  line-height: 1.3;
  cursor: pointer;
  transition:
    background-color 0.14s ease,
    border-color 0.14s ease,
    color 0.14s ease;
}

.clinical-entity-link--inline {
  padding: 0.125rem 0.4375rem;
  border-radius: var(--radius-sm);
}

.clinical-entity-link--plain {
  padding: 0.25rem 0.5rem;
  border-radius: var(--radius-sm);
  border-color: transparent;
  background: transparent;
  color: var(--semantic-color-primary);
  font-family: inherit;
  font-size: inherit;
  line-height: 1.35;
  text-align: left;
  text-decoration: underline;
  text-underline-offset: 2px;
}

.clinical-entity-link:hover:not(:disabled) {
  background: var(--semantic-color-accent-bg);
  border-color: var(--semantic-color-accent);
}

.clinical-entity-link--plain:hover:not(:disabled) {
  color: var(--semantic-color-primary-dark);
  background: var(--semantic-color-surface-muted);
  border-color: var(--semantic-color-border);
}

.clinical-entity-link:disabled {
  opacity: 0.6;
  cursor: not-allowed;
}

.clinical-entity-link-tabular {
  font-variant-numeric: tabular-nums;
}

.clinical-entity-link__icon {
  flex-shrink: 0;
}

.clinical-entity-link__code {
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
  font-weight: 500;
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
  font-size: 0.8125rem;
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

.entity-link--interactive:hover:not(:disabled),
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
  font-size: 0.75rem;
  color: var(--text-muted);
}

@media (max-width: 640px) {
  .clinical-entity-link--plain {
    min-height: var(--touch-target-min);
    min-width: var(--touch-target-min);
    padding: 0.5rem 0.625rem;
  }
}
</style>
