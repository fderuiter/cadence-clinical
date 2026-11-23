<script setup>
import {
  computed,
  getCurrentInstance,
  inject,
  ref,
  useSlots,
  watch,
} from "vue";
import { hasRegisteredUnsavedDrafts } from "../../composables/useSignOutGuard.js";
import ClinicalDrawer from "./ClinicalDrawer.vue";
import ClinicalModal from "./ClinicalModal.vue";

defineOptions({
  inheritAttrs: false,
});

const KIND_LABELS = {
  study: "Study",
  study_site: "Study site",
  site: "Study site",
  participant: "Participant",
  enrollment: "Enrollment",
  person: "Staff member",
  concept: "Concept",
  protocol_version: "Protocol version",
  collection_instance: "Form",
  collection_spec: "Form definition",
  observation: "Observation",
  assertion: "Verification",
  proposal: "Proposal",
  artifact_version: "Document version",
  attestation: "Signature record",
  consent: "Consent record",
  grant: "Role grant",
  role: "Role",
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
  const trimmed = String(raw ?? "").trim();
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
  previewMode: {
    type: String,
    default: "drawer",
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
});

const emit = defineEmits(["select", "preview", "inspect", "navigate"]);

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

const parsedRef = computed(() => parseEntityRef(props.nodeRef));
const effectiveRef = computed(
  () => parsedRef.value?.canonical || String(props.nodeRef ?? "").trim()
);

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
  return (
    props.kind || resolvedMeta.value?.kind || parsedRef.value?.kind || "record"
  );
});

const kindLabel = computed(() => formatKindLabel(effectiveKind.value));

const isMasked = computed(() => {
  if (props.masked) return true;
  return Boolean(resolvedMeta.value?.masked);
});

const maskReasonText = computed(() => {
  if (props.masked && typeof props.masked === "string") {
    return formatMaskReason(props.masked);
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

const baseLabel = computed(() => {
  const explicitLabel = stripTechnicalIds(props.label, "");
  if (explicitLabel) return explicitLabel;

  const metaLabel = stripTechnicalIds(resolvedMeta.value?.label, "");
  if (metaLabel) return metaLabel;

  const cleanCode = effectiveCode.value;
  const cleanTitle = stripTechnicalIds(resolvedMeta.value?.title, "");

  if (cleanCode && cleanTitle && cleanCode !== cleanTitle) {
    return `${cleanCode} · ${cleanTitle}`;
  }
  if (cleanTitle) return cleanTitle;
  if (cleanCode) return cleanCode;

  if (parsedRef.value?.version) {
    return `${kindLabel.value} v${parsedRef.value.version}`;
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

const navigationTarget = computed(() => {
  if (props.previewMode === "inspector" && props.inspectorTo) {
    return props.inspectorTo;
  }
  return props.to || props.inspectorTo || null;
});

const fullViewTarget = computed(() => props.to || props.inspectorTo || null);

const resolvedHref = computed(() => {
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

const tooltipText = computed(() => {
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
  emit("select", effectiveRef.value);

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

  if (props.previewMode === "navigate") {
    await navigateToFullView();
    return;
  }

  if (props.previewMode === "inspector") {
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
    class="entity-link-masked noderef-masked"
    data-masked="true"
  >
    {{ maskedDisplayText }}
  </span>
  <template v-else>
    <a
      v-if="resolvedHref"
      ref="triggerRef"
      v-bind="$attrs"
      :href="resolvedHref"
      class="entity-link noderef-chip node-ref-chip"
      :title="tooltipText"
      @click="handleClick"
    >
      <span class="entity-link-label noderef-chip-label">{{
        displayText
      }}</span>
      <span v-if="srContext" class="sr-only"> ({{ srContext }})</span>
    </a>
    <button
      v-else
      ref="triggerRef"
      v-bind="$attrs"
      type="button"
      class="entity-link entity-link-button noderef-chip node-ref-chip noderef-chip-button"
      :title="tooltipText"
      @click="handleClick"
    >
      <span class="entity-link-label noderef-chip-label">{{
        displayText
      }}</span>
      <span v-if="srContext" class="sr-only"> ({{ srContext }})</span>
    </button>

    <ClinicalDrawer
      v-if="previewMode === 'drawer'"
      v-model="isPreviewOpen"
      :title="baseLabel"
      :subtitle="drawerSubtitle"
      :teleport="teleport"
      :restore-focus="triggerRef"
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
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              stroke-width="2"
              stroke-linecap="round"
              stroke-linejoin="round"
              aria-hidden="true"
              focusable="false"
            >
              <path d="M5 12h14" />
              <path d="m12 5 7 7-7 7" />
            </svg>
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
.entity-link {
  display: inline-flex;
  flex-wrap: wrap;
  align-items: center;
  gap: var(--spacing-2xs);
  min-height: var(--control-target-min);
  max-width: 100%;
  min-width: 0;
  overflow-wrap: anywhere;
  color: var(--semantic-color-primary);
  font-weight: 500;
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

.entity-link:hover {
  color: var(--semantic-color-primary-dark);
}

.entity-link:focus-visible {
  outline: 2px solid var(--semantic-color-accent);
  outline-offset: 2px;
  border-radius: var(--radius-xs);
}

.entity-link-masked {
  color: var(--semantic-color-text-muted);
  font-style: italic;
}

.entity-link-preview-content {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-md);
}

.entity-link-meta {
  display: grid;
  grid-template-columns: minmax(96px, auto) 1fr;
  gap: var(--spacing-xs) var(--spacing-md);
  margin: 0;
  padding: var(--spacing-sm);
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-sm);
  font-size: var(--font-size-sm);
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
  font-family: var(--font-mono);
}

.entity-link-preview-slot {
  display: flex;
  flex-direction: column;
  gap: var(--spacing-sm);
}

.entity-link-footer-actions {
  display: flex;
  justify-content: flex-end;
  align-items: center;
  gap: var(--spacing-xs);
}

.entity-link-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: var(--spacing-2xs);
  min-height: 36px;
  padding: 6px 14px;
  border-radius: var(--radius-sm);
  font-size: var(--font-size-sm);
  font-weight: 600;
  font-family: inherit;
  cursor: pointer;
}

.entity-link-btn-secondary {
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border-control);
}

.entity-link-btn-secondary:hover {
  background: var(--semantic-color-surface-muted);
}

.entity-link-btn-primary {
  background: var(--semantic-color-primary);
  color: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-primary);
}

.entity-link-btn-primary:hover {
  background: var(--semantic-color-primary-dark);
  border-color: var(--semantic-color-primary-dark);
}
</style>
