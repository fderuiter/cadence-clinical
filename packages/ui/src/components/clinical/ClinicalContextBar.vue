<template>
  <nav class="clinical-context-bar zoom-path" :aria-label="ariaLabel">
    <ol class="clinical-context-list">
      <li
        v-for="(step, index) in steps"
        :key="step.key"
        class="clinical-context-step"
        :data-context-step="step.key"
      >
        <span v-if="index > 0" class="clinical-context-sep" aria-hidden="true">
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
            focusable="false"
          >
            <path d="m9 18 6-6-6-6" />
          </svg>
        </span>

        <component
          :is="routerLinkComponent"
          v-if="step.to && routerLinkComponent"
          :id="step.id"
          :to="step.to"
          class="clinical-context-link"
          :aria-current="index === steps.length - 1 ? 'page' : undefined"
          @click="handleNavigate(step)"
        >
          <svg
            v-if="step.key === 'studies'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="m12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z"
            />
            <path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65" />
            <path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65" />
          </svg>
          <svg
            v-else-if="step.key === 'study'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="M10 2v7.527a2 2 0 0 1-.211.896L4.72 20.55a1 1 0 0 0 .9 1.45h12.76a1 1 0 0 0 .9-1.45l-5.069-10.127A2 2 0 0 1 14 9.527V2"
            />
            <path d="M8.5 2h7" />
            <path d="M7 16h10" />
          </svg>
          <svg
            v-else-if="step.key === 'site'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path d="M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z" />
            <path d="M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2" />
            <path d="M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2" />
            <path d="M10 6h4" />
            <path d="M10 10h4" />
            <path d="M10 14h4" />
            <path d="M10 18h4" />
          </svg>
          <svg
            v-else-if="step.key === 'participant'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
            <circle cx="9" cy="7" r="4" />
            <polyline points="16 11 18 13 22 9" />
          </svg>
          <template v-if="step.code">
            <span v-if="step.prefix" class="clinical-context-prefix"
              >{{ step.prefix }}&nbsp;</span
            ><span class="clinical-context-code font-mono">{{
              step.code
            }}</span>
          </template>
          <span v-else class="clinical-context-label">{{ step.label }}</span>
        </component>

        <a
          v-else-if="step.to"
          :id="step.id"
          :href="resolveHref(step.to)"
          class="clinical-context-link"
          :aria-current="index === steps.length - 1 ? 'page' : undefined"
          @click="handleNavigate(step)"
        >
          <svg
            v-if="step.key === 'studies'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="m12.83 2.18a2 2 0 0 0-1.66 0L2.6 6.08a1 1 0 0 0 0 1.83l8.58 3.91a2 2 0 0 0 1.66 0l8.58-3.9a1 1 0 0 0 0-1.83Z"
            />
            <path d="m22 17.65-9.17 4.16a2 2 0 0 1-1.66 0L2 17.65" />
            <path d="m22 12.65-9.17 4.16a2 2 0 0 1-1.66 0L2 12.65" />
          </svg>
          <svg
            v-else-if="step.key === 'study'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path
              d="M10 2v7.527a2 2 0 0 1-.211.896L4.72 20.55a1 1 0 0 0 .9 1.45h12.76a1 1 0 0 0 .9-1.45l-5.069-10.127A2 2 0 0 1 14 9.527V2"
            />
            <path d="M8.5 2h7" />
            <path d="M7 16h10" />
          </svg>
          <svg
            v-else-if="step.key === 'site'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path d="M6 22V4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v18Z" />
            <path d="M6 12H4a2 2 0 0 0-2 2v6a2 2 0 0 0 2 2h2" />
            <path d="M18 9h2a2 2 0 0 1 2 2v9a2 2 0 0 1-2 2h-2" />
            <path d="M10 6h4" />
            <path d="M10 10h4" />
            <path d="M10 14h4" />
            <path d="M10 18h4" />
          </svg>
          <svg
            v-else-if="step.key === 'participant'"
            class="clinical-context-icon"
            width="16"
            height="16"
            viewBox="0 0 24 24"
            fill="none"
            stroke="currentColor"
            stroke-width="1.75"
            stroke-linecap="round"
            stroke-linejoin="round"
            aria-hidden="true"
            focusable="false"
          >
            <path d="M16 21v-2a4 4 0 0 0-4-4H6a4 4 0 0 0-4 4v2" />
            <circle cx="9" cy="7" r="4" />
            <polyline points="16 11 18 13 22 9" />
          </svg>
          <template v-if="step.code">
            <span v-if="step.prefix" class="clinical-context-prefix"
              >{{ step.prefix }}&nbsp;</span
            ><span class="clinical-context-code font-mono">{{
              step.code
            }}</span>
          </template>
          <span v-else class="clinical-context-label">{{ step.label }}</span>
        </a>

        <span
          v-else
          :id="step.id"
          class="clinical-context-current"
          aria-current="page"
        >
          {{ step.label }}
        </span>
      </li>
    </ol>
  </nav>
</template>

<script setup>
import { computed, getCurrentInstance } from "vue";

const props = defineProps({
  studiesLabel: {
    type: String,
    default: "Studies",
  },
  studiesTo: {
    type: [String, Object],
    default: "/studies",
  },
  studyCode: {
    type: String,
    default: "",
  },
  studyTo: {
    type: [String, Object],
    default: "",
  },
  siteCode: {
    type: String,
    default: "",
  },
  siteLabelPrefix: {
    type: String,
    default: "Site",
  },
  siteTo: {
    type: [String, Object],
    default: "",
  },
  participantCode: {
    type: String,
    default: "",
  },
  participantTo: {
    type: [String, Object],
    default: "",
  },
  currentLabel: {
    type: String,
    default: "",
  },
  ariaLabel: {
    type: String,
    default: "Zoom path",
  },
});

const emit = defineEmits(["navigate"]);

const instance = getCurrentInstance();

const routerLinkComponent = computed(() => {
  const registered = instance?.appContext?.components;
  return registered?.RouterLink || registered?.["router-link"] || null;
});

function trimText(value) {
  return typeof value === "string" ? value.trim() : "";
}

function resolveHref(target) {
  if (typeof target === "string") {
    return target || "#";
  }
  if (target && typeof target === "object") {
    const basePath = typeof target.path === "string" ? target.path : "/";
    const query = target.query;
    if (query && typeof query === "object") {
      const params = new URLSearchParams();
      for (const [key, val] of Object.entries(query)) {
        if (val !== undefined && val !== null && val !== "") {
          params.set(key, String(val));
        }
      }
      const qs = params.toString();
      return qs ? `${basePath}?${qs}` : basePath;
    }
    return basePath;
  }
  return "#";
}

const steps = computed(() => {
  const items = [];
  const studiesLabel = trimText(props.studiesLabel);
  const studyCode = trimText(props.studyCode);
  const siteCode = trimText(props.siteCode);
  const sitePrefix = trimText(props.siteLabelPrefix);
  const participantCode = trimText(props.participantCode);
  const currentLabel = trimText(props.currentLabel);

  if (studiesLabel) {
    items.push({
      key: "studies",
      id: "context-studies",
      label: studiesLabel,
      code: "",
      prefix: "",
      to: props.studiesTo || "/studies",
    });
  }

  if (studyCode) {
    items.push({
      key: "study",
      id: "context-study",
      label: "",
      code: studyCode,
      prefix: "",
      to: props.studyTo || `/studies/${encodeURIComponent(studyCode)}`,
    });
  }

  if (siteCode) {
    const defaultSiteTo = studyCode
      ? `/studies/${encodeURIComponent(studyCode)}?stage=conduct&section=participants&site=${encodeURIComponent(siteCode)}`
      : `/studies?site=${encodeURIComponent(siteCode)}`;
    items.push({
      key: "site",
      id: "context-site",
      label: "",
      code: siteCode,
      prefix: sitePrefix,
      to: props.siteTo || defaultSiteTo,
    });
  }

  if (participantCode) {
    const siteParam = siteCode ? `&site=${encodeURIComponent(siteCode)}` : "";
    const defaultParticipantTo = studyCode
      ? `/studies/${encodeURIComponent(studyCode)}?stage=conduct${siteParam}&participant=${encodeURIComponent(participantCode)}`
      : `/studies?participant=${encodeURIComponent(participantCode)}`;
    items.push({
      key: "participant",
      id: "context-participant",
      label: "",
      code: participantCode,
      prefix: "",
      to: props.participantTo || defaultParticipantTo,
    });
  }

  if (currentLabel) {
    items.push({
      key: "current",
      id: "context-current",
      label: currentLabel,
      code: "",
      prefix: "",
      to: null,
    });
  }

  return items;
});

function handleNavigate(step) {
  emit("navigate", { step: step.key, to: step.to });
}
</script>

<style scoped>
.clinical-context-bar {
  display: flex;
  align-items: center;
  min-height: 44px;
  padding: 0 var(--spacing-md);
  border-bottom: 1px solid var(--semantic-color-border);
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  overflow-x: auto;
  scrollbar-width: none;
}

.clinical-context-bar::-webkit-scrollbar {
  display: none;
}

.clinical-context-list {
  display: flex;
  flex-wrap: nowrap;
  align-items: center;
  gap: 2px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: var(--font-size-sm);
  line-height: 1.25;
  white-space: nowrap;
}

.clinical-context-step {
  display: inline-flex;
  align-items: center;
  min-width: 0;
}

.clinical-context-sep {
  display: inline-flex;
  align-items: center;
  padding: 0 2px;
  color: var(--semantic-color-text-muted);
  flex-shrink: 0;
}

.clinical-context-link {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  gap: 6px;
  min-height: 44px;
  min-width: 44px;
  padding: 0 var(--spacing-xs);
  border-radius: var(--radius-sm);
  color: var(--semantic-color-text);
  font-weight: 500;
  text-decoration: none;
  transition:
    background-color 0.15s ease,
    color 0.15s ease;
}

.clinical-context-link:hover {
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-primary);
  text-decoration: underline;
}

.clinical-context-icon {
  flex-shrink: 0;
  color: var(--semantic-color-text-muted);
}

.clinical-context-link:hover .clinical-context-icon {
  color: currentColor;
}

.clinical-context-code {
  font-family: var(--semantic-font-mono);
  font-variant-numeric: tabular-nums;
  font-size: var(--font-size-xs);
}

.clinical-context-link[aria-current="page"],
.clinical-context-current {
  font-weight: 600;
  color: var(--semantic-color-text);
}

.clinical-context-current {
  display: inline-flex;
  align-items: center;
  min-height: 44px;
  padding: 0 var(--spacing-xs);
}

@media (min-width: 768px) and (max-width: 1280px) {
  .clinical-context-bar {
    padding: 0 var(--spacing-sm);
  }

  .clinical-context-list {
    gap: 1px;
    font-size: var(--font-size-xs);
  }

  .clinical-context-link {
    padding: 0 6px;
    gap: 5px;
  }

  .clinical-context-current {
    padding: 0 6px;
  }
}
</style>
