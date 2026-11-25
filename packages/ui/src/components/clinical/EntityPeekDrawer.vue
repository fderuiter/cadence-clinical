<template>
  <ClinicalDrawer
    :id="drawerId"
    :is-open="isOpen"
    :teleport="teleport"
    size="md"
    panel-class="entity-peek-drawer drawer-mobile-full"
    :title="descriptor.title"
    :subtitle="descriptor.subtitle"
    close-label="Close entity preview drawer"
    @close="$emit('close')"
  >
    <div class="entity-peek-body">
      <div class="entity-peek-badges">
        <span class="entity-peek-kind">{{ descriptor.kindLabel }}</span>
        <span
          v-if="descriptor.statusLabel"
          :class="[
            'entity-peek-status',
            `clinical-badge--${descriptor.statusTone}`,
          ]"
        >
          {{ descriptor.statusLabel }}
        </span>
        <span v-if="descriptor.versionLabel" class="entity-peek-version">
          {{ descriptor.versionLabel }}
        </span>
      </div>

      <div
        v-if="descriptor.studyId || descriptor.siteId || descriptor.subjectId"
        class="entity-peek-scope"
        aria-label="Clinical scope"
      >
        <span v-if="descriptor.studyId" class="entity-peek-scope-pill">
          <span class="scope-key">Study</span>
          <span class="scope-val">{{ descriptor.studyId }}</span>
        </span>
        <span v-if="descriptor.siteId" class="entity-peek-scope-pill">
          <span class="scope-key">Site</span>
          <span class="scope-val">{{ descriptor.siteId }}</span>
        </span>
        <span v-if="descriptor.subjectId" class="entity-peek-scope-pill">
          <span class="scope-key">Participant</span>
          <span class="scope-val">{{ descriptor.subjectId }}</span>
        </span>
      </div>

      <dl class="entity-peek-meta">
        <div v-if="descriptor.artifactLabel" class="entity-peek-meta-row">
          <dt>Artifact Type</dt>
          <dd>{{ descriptor.artifactLabel }}</dd>
        </div>
        <div v-if="descriptor.artifactCode" class="entity-peek-meta-row">
          <dt>DIA Reference Code</dt>
          <dd class="entity-peek-mono">{{ descriptor.artifactCode }}</dd>
        </div>
        <div v-if="descriptor.zoneLabel" class="entity-peek-meta-row">
          <dt>TMF Zone</dt>
          <dd>{{ descriptor.zoneLabel }}</dd>
        </div>
        <div v-if="descriptor.sectionLabel" class="entity-peek-meta-row">
          <dt>TMF Section</dt>
          <dd>{{ descriptor.sectionLabel }}</dd>
        </div>
        <div v-if="descriptor.filename" class="entity-peek-meta-row">
          <dt>Regulated File</dt>
          <dd class="entity-peek-mono">{{ descriptor.filename }}</dd>
        </div>
      </dl>

      <slot :descriptor="descriptor" />

      <details
        v-if="descriptor.supportDetails"
        class="entity-peek-support"
        data-testid="entity-peek-support-details"
      >
        <summary>Support details</summary>
        <p class="entity-peek-support-text">{{ descriptor.supportDetails }}</p>
      </details>
    </div>

    <template #footer>
      <button
        type="button"
        class="btn btn-secondary entity-peek-close-btn"
        @click="$emit('close')"
      >
        Close
      </button>
      <slot name="actions" :descriptor="descriptor" />
      <a
        v-if="descriptor.href"
        :href="descriptor.href"
        class="btn btn-primary entity-peek-open-btn"
        data-testid="entity-peek-open-binder"
        @click="handleOpenBinder"
      >
        <span>{{ openLabel }}</span>
        <svg
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
          <path d="M7 17L17 7" />
          <path d="M7 7h10v10" />
        </svg>
      </a>
    </template>
  </ClinicalDrawer>
</template>

<script setup>
import ClinicalDrawer from "./ClinicalDrawer.vue";

const props = defineProps({
  isOpen: {
    type: Boolean,
    default: false,
  },
  descriptor: {
    type: Object,
    required: true,
  },
  drawerId: {
    type: String,
    default: undefined,
  },
  openLabel: {
    type: String,
    default: "Open in eTMF binder",
  },
  teleport: {
    type: Boolean,
    default: true,
  },
  interceptNavigate: {
    type: Boolean,
    default: false,
  },
});

const emit = defineEmits(["close", "navigate"]);

function handleOpenBinder(event) {
  const payload = {
    href: props.descriptor.href,
    query: props.descriptor.query,
    descriptor: props.descriptor,
    originalEvent: event,
  };
  if (props.interceptNavigate) {
    event.preventDefault();
  }
  emit("navigate", payload);
  emit("close");
}
</script>

<style scoped>
.entity-peek-body {
  display: flex;
  flex-direction: column;
  gap: 1rem;
}

.entity-peek-badges {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  gap: 0.5rem;
}

.entity-peek-kind,
.entity-peek-status,
.entity-peek-version {
  display: inline-flex;
  align-items: center;
  padding: 0.1875rem 0.5625rem;
  border-radius: 9999px;
  font-size: 0.75rem;
  font-weight: 600;
  line-height: 1.3;
  font-variant-numeric: tabular-nums;
}

.entity-peek-kind {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text);
  border: 1px solid var(--semantic-color-border);
}

.entity-peek-version {
  font-family: var(--semantic-font-mono);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-muted);
  border: 1px solid var(--semantic-color-border);
}

.entity-peek-scope {
  display: flex;
  flex-wrap: wrap;
  gap: 0.5rem;
  padding: 0.625rem 0.75rem;
  background: var(--semantic-color-surface-muted);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-md);
}

.entity-peek-scope-pill {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  font-size: 0.75rem;
}

.scope-key {
  color: var(--semantic-color-text-muted);
  font-weight: 600;
}

.scope-val {
  font-family: var(--semantic-font-mono);
  color: var(--semantic-color-text);
  font-weight: 600;
}

.entity-peek-meta {
  margin: 0;
  display: grid;
  grid-template-columns: 1fr;
  gap: 0.625rem;
  padding: 0.875rem;
  background: var(--semantic-color-surface);
  border: 1px solid var(--semantic-color-border);
  border-radius: var(--radius-md);
}

.entity-peek-meta-row {
  display: flex;
  justify-content: space-between;
  align-items: baseline;
  gap: 0.75rem;
  font-size: 0.8125rem;
}

.entity-peek-meta-row dt {
  color: var(--semantic-color-text-muted);
  font-weight: 500;
  flex-shrink: 0;
}

.entity-peek-meta-row dd {
  margin: 0;
  color: var(--semantic-color-text);
  font-weight: 600;
  text-align: right;
  word-break: break-word;
}

.entity-peek-mono {
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
}

.entity-peek-support {
  border-top: 1px solid var(--semantic-color-border);
  padding-top: 0.625rem;
  font-size: 0.75rem;
  color: var(--semantic-color-text-muted);
}

.entity-peek-support summary {
  cursor: pointer;
  font-weight: 600;
  min-height: var(--control-target-min);
  display: inline-flex;
  align-items: center;
}

.entity-peek-support-text {
  margin: 0.375rem 0 0;
  font-family: var(--semantic-font-mono);
  font-size: 0.75rem;
  word-break: break-all;
}

.entity-peek-open-btn,
.entity-peek-close-btn {
  display: inline-flex;
  align-items: center;
  gap: 0.375rem;
  text-decoration: none;
  min-height: 2.25rem;
}

@media (max-width: 768px) {
  :global(.entity-peek-drawer.clinical-drawer-panel) {
    top: auto;
    bottom: 0;
    left: 0;
    right: 0;
    width: 100%;
    max-width: 100%;
    height: auto;
    max-height: 88dvh;
    border-left: none;
    border-top: 1px solid var(--semantic-color-border);
    border-radius: var(--radius-xl) var(--radius-xl) 0 0;
  }
}
</style>
