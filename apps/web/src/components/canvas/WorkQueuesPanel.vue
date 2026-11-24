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
  observationNodeRef,
} from "../../canvas/nodeRef";
import { maskReasonWords } from "../../canvas/nodeResolver";
import SupportDetails from "./SupportDetails.vue";

const props = defineProps<{
  studyCode: string;
}>();

const hasWorkQueueService = computed(
  () => typeof kernelApi.listWorkQueues === "function"
);

const queues = ref<KernelWorkQueue[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);
const showEmpty = ref(false);

const totalItems = computed(() =>
  queues.value.reduce((sum, q) => sum + q.items.length, 0)
);
const hasEmptyQueues = computed(() =>
  queues.value.some((q) => q.items.length === 0)
);
const visibleQueues = computed(() =>
  showEmpty.value
    ? queues.value
    : queues.value.filter((q) => q.items.length > 0)
);

const statusLine = computed(() => {
  if (loading.value) return "Loading capability work queues…";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  if (queues.value.length === 0 || totalItems.value === 0) {
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
  if (target?.proposal_ref) {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: { proposal: target.proposal_ref },
    };
  }
  if (target?.protocol_version_ref) {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: {
        stage: "design",
        section: "protocol-versions",
        node: target.protocol_version_ref,
      },
    };
  }
  if (target?.finding_ref) {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: {
        stage: "data",
        section: "validation",
      },
    };
  }
  if (target?.form_id) {
    const formRef = collectionInstanceRef(target.form_id);
    if (formRef) {
      return {
        name: "study",
        params: { studyCode: props.studyCode },
        query: {
          stage: "conduct",
          form: formRef,
        },
      };
    }
  }
  if (target?.observation_ref) {
    const obsRef = observationNodeRef(target.observation_ref);
    if (obsRef) {
      return {
        name: "study",
        params: { studyCode: props.studyCode },
        query: { stage: "conduct", observation: obsRef },
      };
    }
  }
  if (item.subject?.startsWith("proposal:")) {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: { proposal: item.subject },
    };
  }
  if (item.subject?.startsWith("protocol_version:")) {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: {
        stage: "design",
        section: "protocol-versions",
        node: item.subject,
      },
    };
  }
  if (item.subject?.startsWith("validation_finding:")) {
    return {
      name: "study",
      params: { studyCode: props.studyCode },
      query: {
        stage: "data",
        section: "validation",
      },
    };
  }
  if (item.subject?.startsWith("collection_instance:")) {
    const formRef = collectionInstanceRef(item.subject);
    if (formRef) {
      return {
        name: "study",
        params: { studyCode: props.studyCode },
        query: { stage: "conduct", form: formRef },
      };
    }
  }
  if (item.subject?.startsWith("observation:")) {
    const obsRef = observationNodeRef(item.subject);
    if (obsRef) {
      return {
        name: "study",
        params: { studyCode: props.studyCode },
        query: { stage: "conduct", observation: obsRef },
      };
    }
  }
  return {
    name: "study",
    params: { studyCode: props.studyCode },
  };
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

watch(() => props.studyCode, load, { immediate: true });
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
  font-size: 0.75rem;
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
  background: var(--semantic-color-surface-subtle);
  color: var(--semantic-color-text-dark);
}

.work-queue-truncated {
  padding: 2px 6px;
  border-radius: 4px;
  font-size: 0.72rem;
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
  font-size: 0.74rem;
  color: var(--semantic-color-text-secondary);
}

.work-item-detail {
  margin: 0;
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
}

.work-item-masked {
  font-style: italic;
  color: var(--semantic-color-text-secondary);
}
</style>
