<template>
  <section
    id="study-work-queues"
    class="queues"
    aria-labelledby="study-work-queues-title"
    :aria-busy="loading"
  >
    <h2 id="study-work-queues-title">Your work</h2>

    <p
      v-if="statusLine"
      id="work-queues-status"
      class="queues-status"
      role="status"
    >
      {{ statusLine }}
    </p>

    <div
      v-for="queue in queues"
      :key="queue.definition"
      class="queue"
      :data-queue="queue.code"
    >
      <h3 class="queue-title">
        {{ queue.title }}
        <span class="queue-count">{{ countLabel(queue) }}</span>
      </h3>
      <p v-if="!queue.items.length" class="queue-empty">Nothing waiting.</p>
      <ol v-else class="queue-items">
        <li
          v-for="(item, index) in queue.items"
          :key="item.subject ?? `${queue.code}-hidden-${index}`"
          class="queue-item"
        >
          <span v-if="isMasked(item)" class="queue-subject masked">
            {{ hiddenLabel(item) }}
          </span>
          <router-link
            v-else-if="versionLink(item)"
            class="queue-subject"
            :to="versionLink(item)!"
            >{{ itemLabel(item) }}</router-link
          >
          <span v-else class="queue-subject">{{ itemLabel(item) }}</span>
          <time class="queue-since" :datetime="item.since"
            >waiting since {{ when(item.since) }}</time
          >
        </li>
      </ol>
      <p v-if="queue.truncated" class="queue-more">
        Showing the oldest {{ queue.items.length }}; more are waiting.
      </p>
    </div>

    <p class="queues-footnote">
      Only what you may act on now is listed. Items about things you may not see
      stay in place as hidden.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  isMasked,
  kernelApi,
  type KernelWorkItem,
  type KernelWorkQueue,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";

/**
 * The signed-in person's work queues within the study, on study home (docs/ux/CLINICAL_CANVAS.md PX-15, ADR-2486):
 * what is due that the person may act on now, oldest first, from the kernel. The kernel keeps only items the person's
 * grants permit acting on; an item about something the person may not see keeps its place as hidden (Canvas D1), and
 * a study the person may not see is refused by the kernel, which is shown as it is. A protocol version opens in the
 * Protocol versions section.
 */
const props = defineProps<{ studyCode: string }>();

const ITEMS_PER_QUEUE = 20;

const queues = ref<KernelWorkQueue[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);

const statusLine = computed(() => {
  if (loading.value && !queues.value.length) return "Reading your work...";
  if (error.value) return `The kernel couldn't answer: ${error.value}`;
  if (noStudy.value) {
    return `The kernel has no study ${props.studyCode} that you may see.`;
  }
  if (!loading.value && !queues.value.some((queue) => queue.items.length)) {
    return `Nothing in ${props.studyCode} is waiting for you.`;
  }
  return "";
});

/** `protocol_version` or `NotPermitted` in plain lower-case words: "protocol version", "not permitted". */
function words(name: string): string {
  return name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
}

function countLabel(queue: KernelWorkQueue): string {
  const count = queue.items.length;
  return queue.truncated ? `${count}+` : String(count);
}

function hiddenLabel(item: KernelWorkItem): string {
  return `Hidden ${words(item.subject_kind)} (${words(item.mask ?? "")})`;
}

function itemLabel(item: KernelWorkItem): string {
  if (item.title) return item.title;
  const node = parseNodeRef(item.subject);
  if (!node) return item.subject ?? words(item.subject_kind);
  const version = node.version ? ` version ${node.version}` : "";
  return `${words(node.kind)} ${node.id.slice(-8)}${version}`;
}

function when(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

function versionLink(item: KernelWorkItem): RouteLocationRaw | null {
  const node = parseNodeRef(item.subject);
  if (node?.kind !== "protocol_version" || !item.subject) return null;
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

let latest = 0;

async function read() {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  noStudy.value = false;
  queues.value = [];
  try {
    const studyRef = await findKernelStudyRef(props.studyCode);
    if (request !== latest) return;
    if (!studyRef) {
      noStudy.value = true;
      return;
    }
    const found = await kernelApi.workQueues({
      within: studyRef,
      limit: ITEMS_PER_QUEUE,
    });
    if (request !== latest) return;
    queues.value = found;
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

watch(() => props.studyCode, read, { immediate: true });
</script>

<style scoped>
.queues {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 24px;
}

.queues h2 {
  margin: 0;
  font-size: 1rem;
}

.queues-status,
.queues-footnote,
.queue-empty,
.queue-more {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.queue {
  display: flex;
  flex-direction: column;
  gap: 4px;
}

.queue-title {
  display: flex;
  align-items: baseline;
  gap: 8px;
  margin: 0;
  font-size: 0.95rem;
}

.queue-count {
  font-weight: 400;
  color: var(--neutral-dark);
  font-variant-numeric: tabular-nums;
}

.queue-items {
  display: flex;
  flex-direction: column;
  gap: 4px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;
}

.queue-item {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  padding: 4px 0;
  border-bottom: 1px solid var(--border);
  overflow-wrap: anywhere;
}

.queue-subject.masked {
  font-style: italic;
  color: var(--neutral-dark);
}

.queue-since {
  color: var(--neutral-dark);
}
</style>
