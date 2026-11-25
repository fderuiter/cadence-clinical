<script setup>
import {
  computed,
  getCurrentInstance,
  inject,
  ref,
  useSlots,
  watch,
} from "vue";
import { ArrowRight, ExternalLink, EyeOff } from "lucide-vue-next";
import { hasRegisteredUnsavedDrafts } from "../../composables/useSignOutGuard.js";
import {
  openEntityPeek,
  parseNodeRef as parsePeekNodeRef,
} from "../../utils/entityPeekRegistry.js";
import ClinicalDrawer from "./ClinicalDrawer.vue";
import ClinicalModal from "./ClinicalModal.vue";

defineOptions({
  inheritAttrs: false,
});

const KIND_LABELS = {
  tenant: "Workspace",
  organization: "Organization",
  study: "Study",
  study_site: "Study site",
  site: "Study site",
  participant: "Participant",
  enrollment: "Enrollment",
  person: "Staff member",
  concept: "Concept",
  concept_mapping: "Concept mapping",
  code_system: "Code system",
  code_system_version: "Code system version",
  standard_version: "Standard version",
  library_item: "Library item",
  study_standards_profile: "Standards profile",
  protocol_version: "Protocol version",
  collection_instance: "Form",
  collection_spec: "Form definition",
  observation: "Observation",
  assertion: "Verification",
  form_query: "Query",
  proposal: "Proposal",
  artifact: "Document",
  artifact_version: "Document version",
  attestation: "Signature record",
  consent: "Consent record",
  grant: "Role grant",
  role: "Role",
  qualification: "Qualification",
  qualification_requirement: "Qualification requirement",
  effectivity: "Effectivity rule",
  tenant_membership: "Workspace membership",
  query: "Query",
  validation_finding: "Validation finding",
  validation_run: "Validation run",
  work_queue_definition: "Work queue",
  protocol_element: "Protocol element",
  form_version_snapshot: "Form snapshot",
  subscription: "Subscription",
  product_entitlement: "Product entitlement",
  task_configuration: "Task configuration",
};

const NODE_REF_PATTERN =
  /^([a-z][a-z0-9_]*):([0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12})(?:@([1-9][0-9]*))?$/i;
const INLINE_NODE_REF_PATTERN =
  /\b([a-z][a-z0-9_]*):[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:@[1-9][0-9]*)?\b/gi;
const BARE_UUID_PATTERN =
  /\b[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}(?:@[1-9][0-9]*)?\b/gi;

function parseEntityRef(raw) {
  if (typeof raw !== "string") return null;
  const trimmed = raw.trim();
  if (!trimmed) return null;
  const match = NODE_REF_PATTERN.exec(trimmed);
  if (!match) return null;
  return {
    kind: match[1].toLowerCase(),
    id: match[2].toLowerCase(),
    version: match[3] ? Number(match[3]) : null,
    canonical: match[3]
      ? `${match[1].toLowerCase()}:${match[2].toLowerCase()}@${match[3]}`
      : `${match[1].toLowerCase()}:${match[2].toLowerCase()}`,
  };
}

function formatKindLabel(kindOrRef) {
  const safe = String(kindOrRef ?? "").trim();
  if (!safe) return "Record";
  const parsed = parseEntityRef(safe);
  const rawKind = (parsed?.kind ?? safe.split(":", 1)[0] ?? "")
    .trim()
    .toLowerCase();
  if (!rawKind) return "Record";
  if (KIND_LABELS[rawKind]) return KIND_LABELS[rawKind];
  const words = rawKind
    .replace(/([a-z0-9])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .trim();
  return words ? words.charAt(0).toUpperCase() + words.slice(1) : "Record";
}

function formatKindInline(kindOrRef) {
  return formatKindLabel(kindOrRef).toLowerCase();
}

function formatMaskReason(mask) {
  if (!mask || mask === true) return "restricted";
  const text = String(mask).trim();
  if (!text) return "restricted";
  if (text === "Blinded") return "blinded";
  if (text === "OutsideScope") return "outside your site";
  if (text === "IdentityProtected") return "identity protected";
  if (text === "NotPermitted") return "not permitted";
  return text
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .toLowerCase();
}

function stripTechnicalIds(text, fallbackKind) {
  const raw = String(text ?? "").trim();
  if (!raw) return "";
  const cleaned = raw
    .replace(INLINE_NODE_REF_PATTERN, (_m, k) => formatKindLabel(k))
    .replace(BARE_UUID_PATTERN, "")
    .replace(/\s{2,}/g, " ")
    .replace(/^[·\-:\s]+|[·\-:\s]+$/g, "")
    .trim();
  return cleaned || (fallbackKind ? formatKindLabel(fallbackKind) : "");
}

const props = defineProps({
  nodeRef: {
    type: [String, Object],
    default: null,
  },
  entityType: {
    type: String,
    default: "",
  },
  entityId: {
    type: String,
    default: "",
  },
  label: {
    type: String,
    default: "",
  },
  kind: {
    type: String,
    default: "",
  },
  code: {
    type: String,
    default: "",
  },
  status: {
    type: String,
    default: "",
  },
  subtitle: {
    type: String,
    default: "",
  },
  includeKind: {
    type: Boolean,
    default: false,
  },
  masked: {
    type: [Boolean, String],
    default: false,
  },
  maskedLabel: {
    type: String,
    default: "",
  },
  to: {
    type: [String, Object],
    default: null,
  },
  inspectorTo: {
    type: [String, Object],
    default: null,
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
  previewMode: {
    type: String,
    default: "",
  },
  fullViewLabel: {
    type: String,
    default: "Open full view",
  },
  srContext: {
    type: String,
    default: "",
  },
  teleport: {
    type: [Boolean, String],
    default: true,
  },
  resolveRemotely: {
    type: Boolean,
    default: true,
  },
  resolveNode: {
    type: Function,
    default: null,
  },
  confirmDiscardDrafts: {
    type: Function,
    default: null,
  },
  compact: {
    type: Boolean,
    default: false,
  },
  showCode: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["peek", "select", "preview", "inspect", "navigate"]);

const slots = useSlots();
const instance = getCurrentInstance();
const injectedResolver = inject("cadenceEntityResolver", null);
const injectedConfirmDrafts = inject("cadenceConfirmUnsavedDrafts", null);

const triggerRef = ref(null);
const isPreviewOpen = ref(false);
const isDiscardModalOpen = ref(false);
let pendingDiscardResolver = null;
const asyncResolved = ref(null);

const router = computed(
  () => instance?.appContext?.config?.globalProperties?.$router ?? null
);

const isObjectNodeRef = computed(() =>
  Boolean(props.nodeRef && typeof props.nodeRef === "object")
);
const isStringNodeRef = computed(() => typeof props.nodeRef === "string");

const parsedKexpRef = computed(() =>
  isStringNodeRef.value ? parsePeekNodeRef(props.nodeRef) : null
);
const parsedRef = computed(() =>
  isStringNodeRef.value ? parseEntityRef(props.nodeRef) : null
);

const effectiveRef = computed(() => {
  if (isStringNodeRef.value) {
    if (parsedKexpRef.value?.masked) return "";
    return parsedRef.value?.canonical || props.nodeRef.trim();
  }
  return "";
});

watch(
  () => [effectiveRef.value, props.resolveRemotely, props.resolveNode],
  ([nextRef, remote]) => {
    asyncResolved.value = null;
    if (!nextRef || !remote) return;
    const customFn =
      typeof props.resolveNode === "function"
        ? props.resolveNode
        : typeof injectedResolver?.resolve === "function"
          ? injectedResolver.resolve
          : null;
    if (typeof injectedResolver?.ensure === "function") {
      injectedResolver.ensure([nextRef]);
    }
    if (customFn) {
      try {
        const result = customFn(nextRef);
        if (result && typeof result.then === "function") {
          result
            .then((resolved) => {
              if (effectiveRef.value === nextRef && resolved) {
                asyncResolved.value = resolved;
              }
            })
            .catch(() => {
              // Keep graceful clinical fallback label on lookup failure.
            });
        } else if (result && typeof result === "object") {
          asyncResolved.value = result;
        }
      } catch {
        // Keep graceful clinical fallback label on synchronous resolver error.
      }
    }
  },
  { immediate: true }
);

const reactiveSummary = computed(() => {
  const refText = effectiveRef.value;
  if (refText && typeof injectedResolver?.getDisplay === "function") {
    return injectedResolver.getDisplay(refText, props.label || null);
  }
  return null;
});

const resolvedMeta = computed(() => {
  return asyncResolved.value || reactiveSummary.value || null;
});

const effectiveKind = computed(() => {
  const objectType = isObjectNodeRef.value ? props.nodeRef.type : "";
  return (
    props.kind ||
    objectType ||
    props.entityType ||
    resolvedMeta.value?.kind ||
    parsedRef.value?.kind ||
    parsedKexpRef.value?.kind ||
    "record"
  );
});

const kindLabel = computed(() => formatKindLabel(effectiveKind.value));

const isMasked = computed(() => {
  if (props.masked) return true;
  if (parsedKexpRef.value?.masked) return true;
  return Boolean(resolvedMeta.value?.masked);
});

const maskReasonText = computed(() => {
  if (props.masked && typeof props.masked === "string") {
    return formatMaskReason(props.masked);
  }
  if (parsedKexpRef.value?.maskReason) {
    return formatMaskReason(parsedKexpRef.value.maskReason);
  }
  if (resolvedMeta.value?.maskReason) {
    return String(resolvedMeta.value.maskReason);
  }
  if (resolvedMeta.value?.mask) {
    return formatMaskReason(resolvedMeta.value.mask);
  }
  return formatMaskReason(props.masked);
});

const maskedDisplayText = computed(() => {
  if (props.maskedLabel && props.maskedLabel.trim()) {
    return props.maskedLabel.trim();
  }
  if (parsedKexpRef.value?.masked) {
    return props.label || parsedKexpRef.value.displayLabel;
  }
  return `Hidden ${formatKindInline(effectiveKind.value)} (${maskReasonText.value})`;
});

const effectiveCode = computed(() => {
  const raw = props.code || resolvedMeta.value?.code || "";
  return stripTechnicalIds(raw, "");
});

const effectiveStatus = computed(() => {
  const raw = props.status || resolvedMeta.value?.status || "";
  if (!raw) return "";
  return String(raw)
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/[._-]+/g, " ")
    .trim();
});

const resolvedObjectNodeRef = computed(() => {
  const base = isObjectNodeRef.value ? props.nodeRef : {};
  const id = String(
    base.id ??
      (props.entityId ||
        parsedRef.value?.id ||
        parsedKexpRef.value?.id ||
        effectiveRef.value ||
        "")
  );
  return {
    type: base.type || props.entityType || effectiveKind.value || "record",
    id,
    label: base.label || props.label || parsedKexpRef.value?.displayLabel || id,
    studyId: base.studyId ?? null,
    siteId: base.siteId ?? null,
  };
});

const baseLabel = computed(() => {
  const explicitLabel = stripTechnicalIds(props.label, "");
  if (explicitLabel) return explicitLabel;

  if (isObjectNodeRef.value) {
    const objectLabel = stripTechnicalIds(props.nodeRef.label, "");
    if (objectLabel) return objectLabel;
    const objectId = stripTechnicalIds(props.nodeRef.id, "");
    if (objectId) return objectId;
  }

  const metaLabel = stripTechnicalIds(resolvedMeta.value?.label, "");
  if (metaLabel) return metaLabel;

  const cleanCode = effectiveCode.value;
  const cleanTitle = stripTechnicalIds(resolvedMeta.value?.title, "");

  if (cleanCode && cleanTitle && cleanCode !== cleanTitle) {
    return `${cleanCode} · ${cleanTitle}`;
  }
  if (cleanTitle) return cleanTitle;
  if (cleanCode) return cleanCode;

  if (props.entityId) {
    const cleanEntityId = stripTechnicalIds(props.entityId, "");
    if (cleanEntityId) return cleanEntityId;
  }

  if (parsedRef.value?.version) {
    return `${kindLabel.value} v${parsedRef.value.version}`;
  }
  if (
    isStringNodeRef.value &&
    parsedKexpRef.value &&
    !parsedRef.value &&
    parsedKexpRef.value.id
  ) {
    const cleanFreeId = stripTechnicalIds(parsedKexpRef.value.id, "");
    if (cleanFreeId) {
      return parsedKexpRef.value.kind !== "record"
        ? `${kindLabel.value} ${cleanFreeId}`
        : cleanFreeId;
    }
  }
  return kindLabel.value;
});

const displayText = computed(() => {
  const label = baseLabel.value;
  if (!props.includeKind) return label;
  if (label.toLowerCase().startsWith(kindLabel.value.toLowerCase())) {
    return label;
  }
  return `${kindLabel.value}: ${label}`;
});

const formattedRefCode = computed(() => {
  if (!props.showCode) return "";
  if (effectiveCode.value && effectiveCode.value !== displayText.value) {
    return effectiveCode.value;
  }
  if (parsedRef.value) {
    const vSuffix = parsedRef.value.version
      ? ` v${parsedRef.value.version}`
      : "";
    return `${kindLabel.value} ${parsedRef.value.id.slice(0, 8)}${vSuffix}`;
  }
  return "";
});

const fullViewTarget = computed(() => props.to || props.inspectorTo || null);

const effectivePreviewMode = computed(() => {
  if (props.previewMode) return props.previewMode;
  if (slots.preview) return "drawer";
  if (isObjectNodeRef.value || (!fullViewTarget.value && !props.href)) {
    return "peek";
  }
  return "drawer";
});

const navigationTarget = computed(() => {
  if (effectivePreviewMode.value === "inspector" && props.inspectorTo) {
    return props.inspectorTo;
  }
  return props.to || props.inspectorTo || null;
});

const resolvedHref = computed(() => {
  if (props.href) return props.href;
  const target = navigationTarget.value;
  if (!target) return "";
  if (typeof target === "string") return target;
  if (router.value && typeof router.value.resolve === "function") {
    try {
      return router.value.resolve(target)?.href || "";
    } catch {
      return "";
    }
  }
  return "";
});

const dataEntityType = computed(() => {
  if (isStringNodeRef.value && !props.entityType && !props.entityId) {
    return undefined;
  }
  return resolvedObjectNodeRef.value.type;
});

const dataEntityId = computed(() => {
  if (isStringNodeRef.value && !props.entityType && !props.entityId) {
    return undefined;
  }
  return resolvedObjectNodeRef.value.id;
});

const tooltipText = computed(() => {
  if (effectivePreviewMode.value === "peek") return undefined;
  return `Inspect ${formatKindInline(effectiveKind.value)} (${baseLabel.value})`;
});

const drawerSubtitle = computed(() => {
  const parts = [kindLabel.value];
  if (props.subtitle && props.subtitle.trim()) {
    parts.push(stripTechnicalIds(props.subtitle, ""));
  }
  return parts.filter(Boolean).join(" · ");
});

const previewSummary = computed(() => ({
  ref: effectiveRef.value,
  kind: effectiveKind.value,
  kindLabel: kindLabel.value,
  label: baseLabel.value,
  code: effectiveCode.value || null,
  status: effectiveStatus.value || null,
  subtitle: props.subtitle || null,
}));

function closePreview() {
  isPreviewOpen.value = false;
}

async function canProceedWithNavigation() {
  const hasDrafts =
    hasRegisteredUnsavedDrafts() ||
    Boolean(
      typeof injectedConfirmDrafts?.hasUnsaved === "function" &&
      injectedConfirmDrafts.hasUnsaved()
    );
  if (!hasDrafts) return true;

  if (typeof props.confirmDiscardDrafts === "function") {
    return Boolean(await props.confirmDiscardDrafts());
  }
  if (typeof injectedConfirmDrafts?.confirm === "function") {
    return Boolean(await injectedConfirmDrafts.confirm());
  }
  if (typeof injectedConfirmDrafts === "function") {
    return Boolean(await injectedConfirmDrafts());
  }
  return new Promise((resolve) => {
    pendingDiscardResolver = resolve;
    isDiscardModalOpen.value = true;
  });
}

function resolveDiscardModal(confirmed) {
  isDiscardModalOpen.value = false;
  const resolver = pendingDiscardResolver;
  pendingDiscardResolver = null;
  if (resolver) {
    resolver(Boolean(confirmed));
  }
}

async function navigateToFullView() {
  const target = fullViewTarget.value;
  if (!target) {
    closePreview();
    return;
  }
  const allowed = await canProceedWithNavigation();
  if (!allowed) return;

  closePreview();
  emit("navigate", target);
  if (router.value && typeof router.value.push === "function") {
    await router.value.push(target);
  } else if (typeof target === "string" && typeof window !== "undefined") {
    window.location.assign(target);
  }
}

async function handleClick(event) {
  if (isMasked.value) return;

  const triggerEl = triggerRef.value || event?.currentTarget || null;
  const peekPayload = isStringNodeRef.value
    ? {
        nodeRef: effectiveRef.value,
        objectNodeRef: resolvedObjectNodeRef.value,
        label: baseLabel.value,
        parsed: parsedKexpRef.value,
        triggerEl,
        event,
        originalEvent: event,
      }
    : {
        nodeRef: resolvedObjectNodeRef.value,
        triggerEl,
        event,
      };

  emit("peek", peekPayload);
  emit(
    "select",
    isStringNodeRef.value
      ? effectiveRef.value
      : effectivePreviewMode.value === "peek"
        ? peekPayload
        : effectiveRef.value
  );

  if (props.href) {
    if (
      props.preventNavigation &&
      event &&
      typeof event.preventDefault === "function"
    ) {
      event.preventDefault();
    }
    if (effectivePreviewMode.value === "peek") {
      if (isStringNodeRef.value && effectiveRef.value) {
        openEntityPeek(effectiveRef.value, {
          label: baseLabel.value,
          triggerEl,
        });
      }
      return;
    }
    if (!props.preventNavigation) {
      return;
    }
  }

  if (
    resolvedHref.value &&
    event &&
    (event.metaKey ||
      event.ctrlKey ||
      event.shiftKey ||
      event.altKey ||
      (typeof event.button === "number" && event.button !== 0))
  ) {
    return;
  }

  if (event && typeof event.preventDefault === "function") {
    event.preventDefault();
  }

  if (effectivePreviewMode.value === "peek") {
    if (isStringNodeRef.value && effectiveRef.value) {
      openEntityPeek(effectiveRef.value, {
        label: baseLabel.value,
        triggerEl,
      });
    }
    return;
  }

  if (effectivePreviewMode.value === "navigate") {
    await navigateToFullView();
    return;
  }

  if (effectivePreviewMode.value === "inspector") {
    emit("inspect", effectiveRef.value);
    const inspectTarget = props.inspectorTo || props.to;
    if (
      inspectTarget &&
      router.value &&
      typeof router.value.push === "function"
    ) {
      await router.value.push(inspectTarget);
    }
    return;
  }

  isPreviewOpen.value = true;
  emit("preview", previewSummary.value);
}
</script>

<template>
  <span
    v-if="isMasked"
    v-bind="$attrs"
    class="entity-link-masked entity-link--masked noderef-masked"
    :class="{ 'entity-link--compact': compact }"
    data-masked="true"
  >
    <EyeOff
      v-if="parsedKexpRef?.masked"
      :size="14"
      :stroke-width="1.75"
      aria-hidden="true"
      class="entity-link__icon"
    />
    <span class="entity-link__label">{{ maskedDisplayText }}</span>
  </span>
  <template v-else>
    <a
      v-if="resolvedHref"
      ref="triggerRef"
      v-bind="$attrs"
      :href="resolvedHref"
      :class="[
        'entity-link',
        'clinical-entity-link',
        'noderef-chip',
        'node-ref-chip',
        {
          'clinical-entity-link-tabular': tabular,
          'clinical-entity-link-peek': effectivePreviewMode === 'peek',
          'entity-link--compact': compact,
        },
      ]"
      :data-entity-type="dataEntityType"
      :data-entity-id="dataEntityId"
      :title="tooltipText"
      @click="handleClick"
    >
      <ExternalLink
        v-if="compact && !$slots.default"
        :size="14"
        :stroke-width="1.75"
        aria-hidden="true"
        class="entity-link__icon"
      />
      <span class="entity-link-label noderef-chip-label">
        <slot>{{ displayText }}</slot>
      </span>
      <span v-if="formattedRefCode" class="entity-link__code">
        {{ formattedRefCode }}
      </span>
      <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
    </a>
    <button
      v-else
      ref="triggerRef"
      v-bind="$attrs"
      type="button"
      :class="[
        'entity-link',
        'clinical-entity-link',
        'entity-link-button',
        'noderef-chip',
        'node-ref-chip',
        'noderef-chip-button',
        {
          'clinical-entity-link-tabular': tabular,
          'clinical-entity-link-peek': effectivePreviewMode === 'peek',
          'entity-link--compact': compact,
        },
      ]"
      :data-entity-type="dataEntityType"
      :data-entity-id="dataEntityId"
      :title="tooltipText"
      @click="handleClick"
    >
      <ExternalLink
        v-if="compact && !$slots.default"
        :size="14"
        :stroke-width="1.75"
        aria-hidden="true"
        class="entity-link__icon"
      />
      <span class="entity-link-label noderef-chip-label">
        <slot>{{ displayText }}</slot>
      </span>
      <span v-if="formattedRefCode" class="entity-link__code">
        {{ formattedRefCode }}
      </span>
      <span v-if="srContext" class="sr-only">: {{ srContext }}</span>
    </button>

    <ClinicalDrawer
      v-if="effectivePreviewMode === 'drawer'"
      v-model="isPreviewOpen"
      :title="baseLabel"
      :subtitle="drawerSubtitle"
      :teleport="teleport"
      :restore-focus="triggerRef"
      mobile-bottom-sheet
      close-label="Close preview"
      size="md"
      data-testid="entity-link-preview-drawer"
      @close="closePreview"
    >
      <div class="entity-link-preview-content">
        <dl class="entity-link-meta">
          <div class="entity-link-meta-row">
            <dt>Record type</dt>
            <dd>{{ kindLabel }}</dd>
          </div>
          <div v-if="effectiveCode" class="entity-link-meta-row">
            <dt>Code</dt>
            <dd class="entity-link-mono">{{ effectiveCode }}</dd>
          </div>
          <div v-if="effectiveStatus" class="entity-link-meta-row">
            <dt>Status</dt>
            <dd>{{ effectiveStatus }}</dd>
          </div>
          <div v-if="subtitle" class="entity-link-meta-row">
            <dt>Context</dt>
            <dd>{{ subtitle }}</dd>
          </div>
        </dl>

        <div v-if="slots.preview" class="entity-link-preview-slot">
          <slot
            name="preview"
            :summary="previewSummary"
            :close="closePreview"
            :open-full-view="navigateToFullView"
          />
        </div>
      </div>

      <template #footer>
        <div class="entity-link-footer-actions">
          <button
            type="button"
            class="entity-link-btn entity-link-btn-secondary"
            @click="closePreview"
          >
            Close
          </button>
          <button
            v-if="fullViewTarget"
            type="button"
            class="entity-link-btn entity-link-btn-primary"
            data-testid="entity-link-open-full-view"
            @click="navigateToFullView"
          >
            <span>{{ fullViewLabel }}</span>
            <ArrowRight
              :size="14"
              :stroke-width="1.75"
              aria-hidden="true"
              class="entity-link__icon"
            />
          </button>
        </div>
      </template>
    </ClinicalDrawer>

    <ClinicalModal
      v-if="isDiscardModalOpen"
      v-model="isDiscardModalOpen"
      title="Unsaved clinical changes"
      :teleport="teleport"
      size="sm"
      data-testid="entity-link-discard-modal"
      @close="resolveDiscardModal(false)"
    >
      <p class="entity-link-discard-copy">
        You have unsaved clinical form changes. Leave without saving?
      </p>
      <template #footer>
        <div class="entity-link-footer-actions">
          <button
            type="button"
            class="entity-link-btn entity-link-btn-secondary"
            @click="resolveDiscardModal(false)"
          >
            Stay here
          </button>
          <button
            type="button"
            class="entity-link-btn entity-link-btn-primary"
            data-testid="entity-link-confirm-discard"
            @click="resolveDiscardModal(true)"
          >
            Leave without saving
          </button>
        </div>
      </template>
    </ClinicalModal>
  </template>
</template>

<style scoped>
.entity-link,
.clinical-entity-link {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-2xs, 4px);
  min-height: var(--semantic-control-target-min, 24px);
  max-width: 100%;
  min-width: 0;
  overflow-wrap: anywhere;
  color: var(--semantic-color-primary);
  font-weight: 500;
  line-height: 1.35;
  text-decoration: underline;
  text-underline-offset: 2px;
  background: none;
  border: 0;
  padding: 2px 0;
  font-family: inherit;
  font-size: inherit;
  cursor: pointer;
  text-align: left;
}

.entity-link:not(:disabled):hover,
.clinical-entity-link:not(:disabled):hover {
  color: var(--semantic-color-primary-dark);
}

.entity-link:focus-visible,
.clinical-entity-link:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: var(--radius-xs, 4px);
}

.entity-link--compact {
  padding: 2px 4px;
  font-size: 0.8125rem;
}

.clinical-entity-link-tabular {
  font-variant-numeric: var(--font-variant-numeric-tabular, tabular-nums);
}

.entity-link__icon {
  flex-shrink: 0;
}

.entity-link__label {
  font-weight: 500;
}

.entity-link__code {
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-muted);
}

.sr-only {
  position: absolute;
  width: 1px;
  height: 1px;
  padding: 0;
  margin: -1px;
  overflow: hidden;
  clip: rect(0, 0, 0, 0);
  white-space: nowrap;
  border: 0;
}

.entity-link-masked {
  display: inline-flex;
  align-items: center;
  gap: var(--spacing-2xs, 4px);
  color: var(--semantic-color-text-muted);
  font-style: italic;
}

.entity-link-preview-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md, 16px);
}

.entity-link-meta {
  display: grid;
  grid-template-columns: minmax(96px, auto) 1fr;
  gap: var(--spacing-xs, 8px) var(--spacing-md, 16px);
  margin: 0;
  padding: var(--spacing-sm, 12px);
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm, 6px);
  font-size: var(--font-size-sm, 0.875rem);
}

.entity-link-meta-row {
  display: contents;
}

.entity-link-meta dt {
  font-weight: 600;
  color: var(--semantic-color-text-muted);
}

.entity-link-meta dd {
  margin: 0;
  color: var(--semantic-color-text);
}

.entity-link-mono {
  font-family: var(--semantic-font-mono);
}

.entity-link-preview-slot {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm, 12px);
}

.entity-link-footer-actions {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: var(--spacing-xs, 8px);
}

.entity-link-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-2xs, 4px);
  min-height: 44px;
  padding: 6px 14px;
  border-radius: var(--radius-sm, 6px);
  font-size: var(--font-size-sm, 0.875rem);
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
}

.entity-link-btn-secondary {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
}

.entity-link-btn-secondary:not(:disabled):hover {
  background: var(--semantic-color-surface-muted);
}

.entity-link-btn-primary {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-text-on-primary);
  border: 1px solid var(--semantic-color-primary);
}

.entity-link-btn-primary:not(:disabled):hover {
  background: var(--semantic-color-primary-dark);
  border-color: var(--semantic-color-primary-dark);
  color: var(--semantic-color-text-on-primary);
}

@media (max-width: 640px) {
  .clinical-entity-link {
    min-height: var(--touch-target-min, 48px);
  }

  .clinical-entity-link-peek {
    min-width: var(--touch-target-min, 48px);
    padding: 8px 10px;
  }
}
</style>
