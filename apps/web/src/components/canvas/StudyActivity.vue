<template>
  <section
    id="study-activity"
    class="activity"
    aria-labelledby="study-activity-title"
    :aria-busy="loading"
  >
    <div class="activity-header">
      <h2 id="study-activity-title">
        Recent activity
      </h2>
      <label class="activity-filter">
        <span>Show</span>
        <select
          id="activity-family"
          v-model="family"
          :disabled="!studyRef"
          @change="reload"
        >
          <option value="">Everything</option>
          <option
            v-for="option in FAMILIES"
            :key="option"
            :value="option"
          >
            {{ option }}
          </option>
        </select>
      </label>
    </div>

    <p
      v-if="statusLine"
      id="activity-status"
      class="activity-status"
      role="status"
    >
      {{ statusLine }}
    </p>

    <ol
      v-if="entries.length"
      id="activity-list"
      class="activity-list"
    >
      <li
        v-for="entry in entries"
        :key="entry.event_id"
        class="activity-entry"
        :data-family="entry.family"
      >
        <time
          class="activity-time"
          :datetime="entry.recorded"
        >{{
          when(entry.recorded)
        }}</time>
        <span class="activity-what">{{ words(entry.type) }}</span>
        <span
          v-if="isMasked(entry)"
          class="activity-subject masked"
        >
          {{ hiddenLabel(entry) }}
        </span>
        <router-link
          v-else-if="versionLink(entry)"
          class="activity-subject"
          :to="versionLink(entry)!"
        >
          {{ subjectLabel(entry) }}
        </router-link>
        <span
          v-else-if="entry.subject"
          class="activity-subject"
        >{{
          subjectLabel(entry)
        }}</span>
        <span class="activity-actor">by {{ entry.actor_id }}</span>
      </li>
    </ol>

    <button
      v-if="next && !loading"
      id="btn-activity-older"
      type="button"
      class="btn btn-secondary"
      @click="loadOlder"
    >
      Show older activity
    </button>

    <p class="activity-footnote">
      Entries about things you may not see stay in place as hidden. New activity
      appears a few seconds after it happens.
    </p>
  </section>
</template>

<script setup lang="ts">
import { computed, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import {
  isMasked,
  kernelApi,
  type KernelActivityEntry,
  type KernelEventFamily,
} from "../../api/kernelApi";
import { findKernelStudyRef } from "../../canvas/kernelStudy";
import { parseNodeRef } from "../../canvas/nodeRef";

/**
 * The study's activity stream on study home (docs/ux/CLINICAL_CANVAS.md PX-11, ADR-2485): what happened under the
 * study, newest first, from the kernel. An entry about something the reader may not see keeps its place with its kind
 * and why it is hidden (Canvas D1); a study the reader may not see is refused by the kernel, and the refusal is shown
 * as it is. A protocol version opens in the Protocol versions section.
 */
const props = defineProps<{ studyCode: string }>();

const FAMILIES: KernelEventFamily[] = [
  "Knowledge",
  "Execution",
  "Evidence",
  "Trust",
  "Quality",
  "Intelligence",
];
const PAGE_SIZE = 25;

const studyRef = ref<string | null>(null);
const family = ref<KernelEventFamily | "">("");
const entries = ref<KernelActivityEntry[]>([]);
const next = ref<string | null>(null);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);

const statusLine = computed(() => {
  if (loading.value && !entries.value.length) return "Reading activity...";
  if (error.value) return `The kernel couldn't answer: ${error.value}`;
  if (noStudy.value) {
    return `The kernel has no study ${props.studyCode} that you may see.`;
  }
  if (!loading.value && !entries.value.length) {
    return family.value
      ? `No ${family.value} activity recorded in ${props.studyCode} yet.`
      : `No activity recorded in ${props.studyCode} yet.`;
  }
  return "";
});

/** `ParticipantScreened` or `protocol_version` in plain words: "participant screened", "protocol version". */
function words(name: string): string {
  const spaced = name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function hiddenLabel(entry: KernelActivityEntry): string {
  const kind = words(entry.subject_kind ?? "object").toLowerCase();
  return `Hidden ${kind} (${words(entry.mask ?? "").toLowerCase()})`;
}

function when(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

function subjectLabel(entry: KernelActivityEntry): string {
  const node = parseNodeRef(entry.subject);
  if (!node) return entry.subject ?? "";
  const version = node.version ? ` version ${node.version}` : "";
  return `${words(node.kind).toLowerCase()} ${node.id.slice(-8)}${version}`;
}

function versionLink(entry: KernelActivityEntry): RouteLocationRaw | null {
  const node = parseNodeRef(entry.subject);
  if (node?.kind !== "protocol_version" || !entry.subject) return null;
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      stage: "design",
      section: "protocol-versions",
      node: entry.subject,
    },
  };
}

let latest = 0;

async function read(before: string | null) {
  const request = ++latest;
  loading.value = true;
  error.value = "";
  try {
    if (studyRef.value === null) {
      const found = await findKernelStudyRef(props.studyCode);
      if (request !== latest) return;
      if (!found) {
        noStudy.value = true;
        return;
      }
      studyRef.value = found;
    }
    const page = await kernelApi.activity({
      scope: studyRef.value,
      families: family.value ? [family.value] : undefined,
      before: before ?? undefined,
      limit: PAGE_SIZE,
    });
    if (request !== latest) return;
    entries.value = before ? [...entries.value, ...page.entries] : page.entries;
    next.value = page.next;
  } catch (e) {
    if (request === latest) {
      error.value = e instanceof Error ? e.message : String(e);
    }
  } finally {
    if (request === latest) loading.value = false;
  }
}

function reload() {
  entries.value = [];
  next.value = null;
  void read(null);
}

function loadOlder() {
  void read(next.value);
}

watch(
  () => props.studyCode,
  () => {
    studyRef.value = null;
    noStudy.value = false;
    reload();
  },
  { immediate: true }
);
</script>

<style scoped>
.activity {
  display: flex;
  flex-direction: column;
  gap: 10px;
  margin-top: 24px;
}

.activity-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 12px;
  flex-wrap: wrap;
}

.activity h2 {
  margin: 0;
  font-size: 1rem;
}

.activity-filter {
  display: flex;
  align-items: center;
  gap: 6px;
  font-size: 0.85rem;
}

.activity-status,
.activity-footnote {
  margin: 0;
  font-size: 0.85rem;
  color: var(--neutral-dark);
}

.activity-list {
  display: flex;
  flex-direction: column;
  gap: 6px;
  margin: 0;
  padding: 0;
  list-style: none;
  font-size: 0.9rem;
}

.activity-entry {
  display: flex;
  flex-wrap: wrap;
  gap: 4px 10px;
  padding: 6px 0;
  border-bottom: 1px solid var(--border);
  overflow-wrap: anywhere;
}

.activity-time {
  color: var(--neutral-dark);
  font-variant-numeric: tabular-nums;
}

.activity-what {
  font-weight: 600;
}

.activity-subject.masked {
  font-style: italic;
  color: var(--neutral-dark);
}

.activity-actor {
  color: var(--neutral-dark);
}
</style>
