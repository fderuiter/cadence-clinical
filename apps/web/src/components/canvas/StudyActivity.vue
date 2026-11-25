<template>
  <section
    id="study-activity"
    class="activity"
    aria-labelledby="study-activity-title"
    :aria-busy="loading"
  >
    <div class="activity-header">
      <h2 id="study-activity-title">Recent activity</h2>
      <label class="activity-filter">
        <span>Show</span>
        <select
          id="activity-family"
          v-model="family"
          :disabled="noStudy || !props.studyCode.trim()"
          @change="reload"
        >
          <option value="">Everything</option>
          <option v-for="option in FAMILIES" :key="option" :value="option">
            {{ option }}
          </option>
        </select>
      </label>
    </div>

    <div v-if="error" id="activity-status" class="activity-status" role="alert">
      <p class="activity-status">
        Recent activity couldn't be loaded right now.
      </p>
      <SupportDetails :detail="error" />
    </div>
    <p
      v-else-if="statusLine"
      id="activity-status"
      class="activity-status"
      role="status"
    >
      {{ statusLine }}
    </p>

    <ol v-if="entries.length" id="activity-list" class="activity-list">
      <li
        v-for="entry in entries"
        :key="entry.event_id"
        class="activity-entry"
        :data-family="entry.family"
      >
        <time class="activity-time" :datetime="entry.recorded">{{
          when(entry.recorded)
        }}</time>
        <span class="activity-what">{{ words(entry.type) }}</span>
        <span v-if="isMasked(entry)" class="activity-subject masked">
          {{ hiddenLabel(entry) }}
        </span>
        <NodeRefChip
          v-else-if="entry.subject"
          class="activity-subject"
          :node-ref="entry.subject"
          :to="subjectLink(entry)"
        />
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
import { parseNodeRef } from "../../canvas/nodeRef";
import {
  ensureNodesResolved,
  formatMaskedNode,
  seedResolvedNodes,
} from "../../canvas/nodeResolver";
import { useScopedNavigation } from "../../composables/useScopedNavigation";
import NodeRefChip from "./NodeRefChip.vue";
import SupportDetails from "./SupportDetails.vue";

/**
 * The study's activity stream on study home (docs/ux/CLINICAL_CANVAS.md PX-11, ADR-2485): what happened under the
 * study, newest first. Restricted entries keep their place as masked placeholders.
 */
const props = defineProps<{ studyCode: string }>();

const { route: currentRoute, scopedStudyLocation } = useScopedNavigation(
  () => props.studyCode
);

const FAMILIES: KernelEventFamily[] = [
  "Knowledge",
  "Execution",
  "Evidence",
  "Trust",
  "Quality",
  "Intelligence",
];
const PAGE_SIZE = 25;

const family = ref<KernelEventFamily | "">("");
const entries = ref<KernelActivityEntry[]>([]);
const next = ref<string | null>(null);
const loading = ref(false);
const error = ref<string | null>(null);
const noStudy = ref(false);

let requestSeq = 0;

async function fetchActivityPage(
  rawCode: string,
  before: string | null
): Promise<void> {
  const seq = ++requestSeq;
  const scope = rawCode.trim();
  error.value = null;
  noStudy.value = false;

  if (!scope) {
    entries.value = [];
    next.value = null;
    noStudy.value = true;
    loading.value = false;
    return;
  }

  loading.value = true;
  try {
    const page = await kernelApi.activity({
      scope,
      families: family.value ? [family.value] : undefined,
      before: before ?? undefined,
      limit: PAGE_SIZE,
      includeNodes: true,
    });
    if (seq !== requestSeq) return;
    if (page?.nodes) {
      seedResolvedNodes(page.nodes);
    }
    const pageEntries = page?.entries ?? [];
    entries.value = before ? [...entries.value, ...pageEntries] : pageEntries;
    next.value = page?.next ?? null;
    ensureNodesResolved(
      entries.value.filter((e) => !isMasked(e)).map((e) => e.subject)
    );
  } catch (err) {
    if (seq !== requestSeq) return;
    if ((err as { status?: number | null } | null)?.status === 404) {
      entries.value = [];
      next.value = null;
      noStudy.value = true;
      return;
    }
    error.value = err instanceof Error ? err.message : String(err);
  } finally {
    if (seq === requestSeq) {
      loading.value = false;
    }
  }
}

watch(
  () => props.studyCode,
  (code) => {
    entries.value = [];
    next.value = null;
    void fetchActivityPage(code ?? "", null);
  },
  { immediate: true }
);

const statusLine = computed(() => {
  if (loading.value && !entries.value.length) return "Reading activity...";
  if (noStudy.value) {
    return `Study ${props.studyCode} wasn't found or isn't visible to your account.`;
  }
  if (!loading.value && !entries.value.length) {
    return family.value
      ? `No ${family.value} activity recorded in ${props.studyCode} yet.`
      : `No activity recorded in ${props.studyCode} yet.`;
  }
  return "";
});

/** `ParticipantScreened` or `protocol_version` in plain words: "Participant screened", "Protocol version". */
function words(name: string): string {
  const spaced = name
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function hiddenLabel(entry: KernelActivityEntry): string {
  return formatMaskedNode(entry.subject_kind ?? "object", entry.mask);
}

function when(iso: string): string {
  return new Date(iso).toLocaleString(undefined, {
    dateStyle: "medium",
    timeStyle: "short",
  });
}

function subjectLink(entry: KernelActivityEntry): RouteLocationRaw | null {
  if (!entry.subject) return null;
  const node = parseNodeRef(entry.subject);
  if (node?.kind === "protocol_version") {
    return scopedStudyLocation({
      stage: "design",
      section: "protocol-versions",
      node: entry.subject,
    });
  }
  const baseLocation = scopedStudyLocation({ node: entry.subject });
  return {
    ...baseLocation,
    query: {
      ...(currentRoute.value?.query ?? {}),
      ...baseLocation.query,
      node: entry.subject,
    },
  };
}

function reload() {
  entries.value = [];
  next.value = null;
  void fetchActivityPage(props.studyCode ?? "", null);
}

function loadOlder() {
  const cursor = next.value;
  if (!cursor) return;
  void fetchActivityPage(props.studyCode ?? "", cursor);
}
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
