<template>
  <section
    v-if="hasWorkQueueService"
    id="study-work-queues"
    class="work-queues"
    aria-labelledby="study-work-queues-title"
    :aria-busy="loading"
  >
    <div class="work-queues-header">
      <div>
        <h2 id="study-work-queues-title" tabindex="-1">Work queues</h2>
        <p class="work-queues-subtitle">
          Capability-keyed actionable queues for your authority on
          {{ studyCode }}.
        </p>
      </div>
      <div v-if="queues.length > 0" class="work-queues-controls">
        <span class="work-queues-total" data-testid="work-queues-total">
          {{ totalItems }} actionable {{ totalItems === 1 ? "item" : "items" }}
        </span>
        <button
          v-if="hasEmptyQueues"
          id="btn-toggle-empty-queues"
          type="button"
          class="btn btn-secondary"
          @click="showEmpty = !showEmpty"
        >
          {{ showEmpty ? "Hide empty queues" : "Show all queues" }}
        </button>
      </div>
    </div>

    <div
      v-if="showFilterBar"
      class="work-queues-filters"
      data-testid="work-queues-filters"
      role="group"
      aria-label="Filter work queue items"
    >
      <div v-if="siteOptions.length > 0" class="work-queue-filter-field">
        <label for="work-queues-filter-site">Site</label>
        <select id="work-queues-filter-site" v-model="selectedSite">
          <option value="">All sites</option>
          <option v-for="site in siteOptions" :key="site" :value="site">
            {{ site }}
          </option>
        </select>
      </div>
      <div v-if="participantOptions.length > 0" class="work-queue-filter-field">
        <label for="work-queues-filter-participant">Participant</label>
        <select
          id="work-queues-filter-participant"
          v-model="selectedParticipant"
        >
          <option value="">All participants</option>
          <option
            v-for="subject in participantOptions"
            :key="subject"
            :value="subject"
          >
            {{ subject }}
          </option>
        </select>
      </div>
      <div v-if="formOptions.length > 0" class="work-queue-filter-field">
        <label for="work-queues-filter-form">Form</label>
        <select id="work-queues-filter-form" v-model="selectedForm">
          <option value="">All forms</option>
          <option
            v-for="opt in formOptions"
            :key="opt.value"
            :value="opt.value"
          >
            {{ opt.label }}
          </option>
        </select>
      </div>
      <button
        v-if="hasActiveFilter"
        id="work-queues-filter-clear"
        type="button"
        class="btn btn-secondary"
        @click="clearFilters"
      >
        Clear filters
      </button>
    </div>

    <div
      v-if="error"
      id="work-queues-error"
      class="work-queues-status"
      role="alert"
    >
      <p class="work-queues-status">
        Work queues couldn't be loaded right now.
      </p>
      <SupportDetails :detail="error" />
    </div>
    <p
      v-else-if="statusLine"
      id="work-queues-status"
      class="work-queues-status"
      role="status"
    >
      {{ statusLine }}
    </p>

    <div
      v-if="visibleQueues.length > 0"
      id="work-queues-grid"
      class="work-queues-grid"
    >
      <article
        v-for="queue in visibleQueues"
        :key="queue.code"
        :data-testid="`work-queue-${queue.code}`"
        class="work-queue-card"
      >
        <header class="work-queue-top">
          <div>
            <h3 class="work-queue-title">{{ queue.title }}</h3>
            <code class="work-queue-capability">{{ queue.capability }}</code>
          </div>
          <div class="work-queue-badges">
            <span
              class="work-queue-count"
              :data-testid="`work-queue-count-${queue.code}`"
            >
              {{ queue.items.length }}{{ queue.truncated ? "+" : "" }}
            </span>
            <span
              v-if="queue.truncated"
              class="work-queue-truncated"
              data-testid="work-queue-truncated"
            >
              Truncated (200+)
            </span>
          </div>
        </header>

        <p v-if="queue.items.length === 0" class="work-queue-empty">
          No items waiting for this capability.
        </p>

        <ul v-else class="work-queue-items">
          <li
            v-for="(item, idx) in queue.items"
            :key="item.subject ?? `${queue.code}-masked-${idx}`"
            :data-masked="item.masked ? 'true' : undefined"
            class="work-queue-item"
          >
            <template v-if="item.masked">
              <span
                class="work-item-masked"
                :data-testid="`work-item-masked-${queue.code}-${idx}`"
              >
                Masked ({{ maskWords(item.masked) }})
              </span>
            </template>
            <template v-else>
              <div class="work-item-main">
                <router-link
                  :data-testid="`work-item-link-${queue.code}-${idx}`"
                  class="work-item-link"
                  :to="workItemLocation(item)"
                >
                  {{ item.title || item.subject || "Open item" }}
                </router-link>
                <span v-if="item.subject_kind" class="work-item-kind">
                  {{ item.subject_kind }}
                </span>
              </div>
              <p v-if="item.since" class="work-item-detail">
                Since {{ formatSince(item.since) }}
              </p>
            </template>
          </li>
        </ul>
      </article>
    </div>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  kernelApi,
  type KernelWorkItem,
  type KernelWorkQueue,
} from "../../api/kernelApi";
import { seedKernelStudyRef } from "../../canvas/kernelStudy";
import {
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  tryNodeRef,
} from "../../canvas/nodeRef";
import { maskReasonWords } from "../../canvas/nodeResolver";
import { useScopedNavigation } from "../../composables/useScopedNavigation";
import SupportDetails from "./SupportDetails.vue";

const props = defineProps<{
  studyCode: string;
}>();

const { scopedStudyLocation } = useScopedNavigation(() => props.studyCode);

const hasWorkQueueService = computed(
  () => typeof kernelApi.listWorkQueues === "function"
);

const queues = ref<KernelWorkQueue[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);
const showEmpty = ref(false);
const selectedSite = ref("");
const selectedParticipant = ref("");
const selectedForm = ref("");

function itemSiteCode(item: KernelWorkItem): string {
  return item.target?.site_code?.trim() ?? "";
}

function itemParticipantCode(item: KernelWorkItem): string {
  return item.target?.subject_code?.trim() ?? "";
}

function itemFormOption(
  item: KernelWorkItem
): { value: string; label: string } | null {
  const title = item.target?.form_title?.trim() ?? "";
  const spec = item.target?.collection_spec_id?.trim() ?? "";
  const value = spec || title;
  if (!value) return null;
  return { value, label: title || spec };
}

const siteOptions = computed(() => {
  const seen = new Set<string>();
  for (const queue of queues.value) {
    for (const item of queue.items) {
      if (item.masked) continue;
      const site = itemSiteCode(item);
      if (site) seen.add(site);
    }
  }
  return Array.from(seen).sort((a, b) => a.localeCompare(b));
});

const participantOptions = computed(() => {
  const seen = new Set<string>();
  for (const queue of queues.value) {
    for (const item of queue.items) {
      if (item.masked) continue;
      if (selectedSite.value && itemSiteCode(item) !== selectedSite.value) {
        continue;
      }
      const subject = itemParticipantCode(item);
      if (subject) seen.add(subject);
    }
  }
  return Array.from(seen).sort((a, b) => a.localeCompare(b));
});

const formOptions = computed(() => {
  const seen = new Map<string, string>();
  for (const queue of queues.value) {
    for (const item of queue.items) {
      if (item.masked) continue;
      const opt = itemFormOption(item);
      if (opt && !seen.has(opt.value)) {
        seen.set(opt.value, opt.label);
      }
    }
  }
  return Array.from(seen.entries())
    .map(([value, label]) => ({ value, label }))
    .sort((a, b) => a.label.localeCompare(b.label));
});

const hasActiveFilter = computed(() =>
  Boolean(selectedSite.value || selectedParticipant.value || selectedForm.value)
);

const showFilterBar = computed(
  () =>
    queues.value.length > 0 &&
    (hasActiveFilter.value ||
      siteOptions.value.length > 0 ||
      participantOptions.value.length > 0 ||
      formOptions.value.length > 0)
);

function clearFilters() {
  selectedSite.value = "";
  selectedParticipant.value = "";
  selectedForm.value = "";
}

function matchesItemFilters(item: KernelWorkItem): boolean {
  if (!hasActiveFilter.value) return true;
  if (item.masked) return false;
  if (selectedSite.value && itemSiteCode(item) !== selectedSite.value) {
    return false;
  }
  if (
    selectedParticipant.value &&
    itemParticipantCode(item) !== selectedParticipant.value
  ) {
    return false;
  }
  if (selectedForm.value) {
    const formOpt = itemFormOption(item);
    if (!formOpt || formOpt.value !== selectedForm.value) {
      return false;
    }
  }
  return true;
}

const filteredQueues = computed<KernelWorkQueue[]>(() => {
  if (!hasActiveFilter.value) return queues.value;
  return queues.value.map((queue) => ({
    ...queue,
    items: queue.items.filter(matchesItemFilters),
  }));
});

const totalItems = computed(() =>
  filteredQueues.value.reduce((sum, q) => sum + q.items.length, 0)
);
const hasEmptyQueues = computed(() =>
  filteredQueues.value.some((q) => q.items.length === 0)
);
const visibleQueues = computed(() =>
  showEmpty.value
    ? filteredQueues.value
    : filteredQueues.value.filter((q) => q.items.length > 0)
);

const statusLine = computed(() => {
  if (loading.value) return "Loading capability work queues…";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  if (queues.value.length === 0) {
    return "All capability work queues are clear for your permissions on this study.";
  }
  if (totalItems.value === 0 && hasActiveFilter.value) {
    return "No work queue items match the selected filters.";
  }
  if (totalItems.value === 0) {
    return "All capability work queues are clear for your permissions on this study.";
  }
  return "";
});

function maskWords(mask: string | null | undefined): string {
  return maskReasonWords(mask);
}

function formatSince(value: string): string {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? value
    : date.toLocaleString(undefined, {
        dateStyle: "medium",
        timeStyle: "short",
      });
}

function workItemLocation(item: KernelWorkItem): RouteLocationRaw {
  const target = item.target;
  const targetEnrollmentNode = target?.enrollment_id
    ? tryNodeRef("enrollment", target.enrollment_id)
    : null;
  const targetEnrollmentParticipant = targetEnrollmentNode
    ? formatNodeRef(targetEnrollmentNode)
    : undefined;

  if (target?.proposal_ref) {
    return scopedStudyLocation({ proposal: target.proposal_ref });
  }
  if (target?.protocol_version_ref) {
    return scopedStudyLocation({
      stage: "design",
      section: "protocol-versions",
      node: target.protocol_version_ref,
    });
  }
  if (target?.finding_ref) {
    return scopedStudyLocation({
      stage: "data",
      section: "validation",
    });
  }
  if (target?.form_id) {
    const formRef = collectionInstanceRef(target.form_id);
    if (formRef) {
      return scopedStudyLocation({
        stage: "conduct",
        ...(target.site_code ? { site: target.site_code } : {}),
        form: formRef,
        ...(target.item_id ? { item: target.item_id } : {}),
        ...(targetEnrollmentParticipant
          ? { participant: targetEnrollmentParticipant }
          : {}),
      });
    }
  }
  if (target?.observation_ref) {
    const obsRef = observationNodeRef(target.observation_ref);
    if (obsRef) {
      return scopedStudyLocation({
        stage: "conduct",
        ...(target.site_code ? { site: target.site_code } : {}),
        observation: obsRef,
        ...(targetEnrollmentParticipant
          ? { participant: targetEnrollmentParticipant }
          : {}),
      });
    }
  }
  if (item.subject?.startsWith("proposal:")) {
    return scopedStudyLocation({ proposal: item.subject });
  }
  if (item.subject?.startsWith("protocol_version:")) {
    return scopedStudyLocation({
      stage: "design",
      section: "protocol-versions",
      node: item.subject,
    });
  }
  if (item.subject?.startsWith("validation_finding:")) {
    return scopedStudyLocation({
      stage: "data",
      section: "validation",
    });
  }
  if (item.subject?.startsWith("collection_instance:")) {
    const formRef = collectionInstanceRef(item.subject);
    if (formRef) {
      return scopedStudyLocation({
        stage: "conduct",
        form: formRef,
      });
    }
  }
  if (item.subject?.startsWith("observation:")) {
    const obsRef = observationNodeRef(item.subject);
    if (obsRef) {
      return scopedStudyLocation({
        stage: "conduct",
        observation: obsRef,
      });
    }
  }
  return scopedStudyLocation();
}

let requestSeq = 0;

async function load() {
  if (!hasWorkQueueService.value) return;
  const seq = ++requestSeq;
  const code = props.studyCode.trim();
  loading.value = true;
  error.value = "";
  noStudy.value = false;
  queues.value = [];
  if (!code) {
    noStudy.value = true;
    loading.value = false;
    return;
  }
  try {
    const page = await kernelApi.listWorkQueues({ within: code });
    if (seq === requestSeq) {
      const nextQueues = page?.queues ?? [];
      queues.value = nextQueues;
      for (const q of nextQueues) {
        const studyRefHint = q.items.find((it) => it.target?.study_ref)?.target
          ?.study_ref;
        if (studyRefHint) {
          seedKernelStudyRef(code, studyRefHint);
          break;
        }
      }
    }
  } catch (err) {
    if (seq === requestSeq) {
      if ((err as { status?: number | null } | null)?.status === 404) {
        noStudy.value = true;
      } else {
        error.value = err instanceof Error ? err.message : String(err);
      }
    }
  } finally {
    if (seq === requestSeq) loading.value = false;
  }
}

watch(
  () => props.studyCode,
  () => {
    clearFilters();
    void load();
  },
  { immediate: true }
);
</script>

<style scoped>
.work-queues {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-top: 24px;
}

.work-queues-header {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  justify-content: space-between;
  gap: 8px;
}

.work-queues-header h2 {
  margin: 0;
  font-size: 1rem;
}

.work-queues-subtitle,
.work-queues-status,
.work-queue-empty {
  margin: 2px 0 0;
  font-size: 0.85rem;
  color: var(--semantic-color-text-secondary);
}

.work-queues-controls {
  display: flex;
  align-items: center;
  gap: 8px;
  font-size: 0.82rem;
}

.work-queues-total {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
  font-variant-numeric: tabular-nums;
}

.work-queues-filters {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-end;
  gap: 10px;
}

.work-queue-filter-field {
  display: flex;
  flex-direction: column;
  gap: 4px;
  font-size: 0.8rem;
}

.work-queue-filter-field label {
  font-weight: 600;
  color: var(--semantic-color-text-dark);
}

.work-queue-filter-field select {
  min-height: var(--semantic-control-target-min, 24px);
  padding: 4px 8px;
  border: 1px solid var(--semantic-color-border-control);
  border-radius: 4px;
  background: var(--semantic-color-surface);
  color: var(--semantic-color-text);
  font-family: inherit;
}

@media (pointer: coarse), (max-width: 1024px) {
  .work-queue-filter-field select {
    font-size: 1rem;
  }
}

.work-queues-grid {
  display: grid;
  grid-template-columns: repeat(auto-fit, minmax(min(100%, 280px), 1fr));
  gap: 12px;
}

.work-queue-card {
  display: flex;
  flex-direction: column;
  gap: 10px;
  min-width: 0;
  max-width: 100%;
  box-sizing: border-box;
  padding: 12px 14px;
  border: 1px solid var(--border);
  border-radius: 8px;
  background: var(--semantic-color-surface);
}

.work-queue-top {
  display: flex;
  flex-wrap: wrap;
  align-items: flex-start;
  justify-content: space-between;
  gap: 8px;
  min-width: 0;
}

.work-queue-title {
  margin: 0;
  font-size: 0.92rem;
  overflow-wrap: anywhere;
}

.work-queue-capability {
  display: inline-block;
  margin-top: 2px;
  max-width: 100%;
  overflow-wrap: anywhere;
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.work-queue-badges {
  display: flex;
  align-items: center;
  gap: 6px;
}

.work-queue-count {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  min-width: 24px;
  padding: 2px 8px;
  border-radius: 999px;
  font-size: 0.78rem;
  font-weight: 600;
  font-variant-numeric: tabular-nums;
  background: var(--semantic-color-surface-subtle);
  color: var(--semantic-color-text-dark);
}

.work-queue-truncated {
  padding: 2px 6px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  background: var(--semantic-color-warning-bg-soft);
  color: var(--semantic-color-warning-text-dark);
}

.work-queue-items {
  display: flex;
  flex-direction: column;
  gap: 8px;
  list-style: none;
  margin: 0;
  padding: 0;
}

.work-queue-item {
  display: flex;
  flex-direction: column;
  gap: 2px;
  padding-top: 8px;
  border-top: 1px solid var(--border);
  font-size: 0.85rem;
}

.work-queue-item:first-child {
  padding-top: 0;
  border-top: none;
}

.work-item-main {
  display: flex;
  flex-wrap: wrap;
  align-items: baseline;
  justify-content: space-between;
  gap: 6px;
}

.work-item-link {
  font-weight: 500;
  color: var(--primary);
  text-decoration: underline;
}

.work-item-kind {
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.work-item-detail {
  margin: 0;
  font-size: 0.78rem;
  font-variant-numeric: tabular-nums;
  color: var(--semantic-color-text-secondary);
}

.work-item-masked {
  font-style: italic;
  color: var(--semantic-color-text-secondary);
}
</style>
