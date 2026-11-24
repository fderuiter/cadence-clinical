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
          :disabled="!studyRef"
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

    <AuditLogViewer
      v-if="entries.length"
      variant="activity"
      list-id="activity-list"
      hide-header
      :entries="adaptedEntries"
      :fetch-audit-entry="fetchActivityAuditEntry"
    />

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
import { computed, getCurrentInstance, ref } from "vue";
import type { RouteLocationRaw, Router } from "vue-router";
import { AuditLogViewer, adaptKernelActivityEntries } from "ui";
import {
  isMasked,
  kernelApi,
  type KernelActivityEntry,
  type KernelEventFamily,
} from "../../api/kernelApi";
import { useProvideCanvasEntityResolver } from "../../canvas/entityLinkResolver";
import { parseNodeRef, tryNodeRef } from "../../canvas/nodeRef";
import {
  ensureNodesResolved,
  formatMaskedNode,
} from "../../canvas/nodeResolver";
import { useKernelStudyRef } from "../../composables/useKernelStudyRef";
import SupportDetails from "./SupportDetails.vue";

/**
 * The study's activity stream on study home (docs/ux/CLINICAL_CANVAS.md PX-11, ADR-2485): what happened under the
 * study, newest first. Restricted entries keep their place as masked placeholders.
 */
const props = defineProps<{ studyCode: string }>();

useProvideCanvasEntityResolver();

const instance = getCurrentInstance();
const currentRoute = computed(
  () =>
    (
      instance?.appContext.config.globalProperties as {
        $route?: { query?: Record<string, unknown> };
      }
    )?.$route ?? null
);
const currentRouter = computed(
  () =>
    (
      instance?.appContext.config.globalProperties as {
        $router?: Router;
      }
    )?.$router ?? null
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

async function fetchActivityPage(
  scope: string,
  before: string | null,
  isCurrent: () => boolean
) {
  const page = await kernelApi.activity({
    scope,
    families: family.value ? [family.value] : undefined,
    before: before ?? undefined,
    limit: PAGE_SIZE,
  });
  if (!isCurrent()) return;
  entries.value = before ? [...entries.value, ...page.entries] : page.entries;
  next.value = page.next;
  const refsToResolve: Array<string | null | undefined> = [];
  for (const e of entries.value) {
    if (isMasked(e)) continue;
    refsToResolve.push(e.subject, e.audit_entry_ref, e.attestation_ref);
    const personRef = tryNodeRef("person", e.actor_id);
    if (personRef) refsToResolve.push(`person:${personRef.id}`);
  }
  ensureNodesResolved(refsToResolve);
}

const {
  studyRef,
  studyLoading: loading,
  studyError: error,
  noStudy,
  runForCurrentStudy,
} = useKernelStudyRef(() => props.studyCode, {
  onReset: () => {
    entries.value = [];
    next.value = null;
  },
  onResolved: (scope, { isCurrent }) =>
    fetchActivityPage(scope, null, isCurrent),
});

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
  const tail = String(name).split(".").pop() || String(name);
  const spaced = tail
    .replace(/_/g, " ")
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .toLowerCase();
  return spaced.charAt(0).toUpperCase() + spaced.slice(1);
}

function hiddenLabel(entry: KernelActivityEntry): string {
  return formatMaskedNode(entry.subject_kind ?? "object", entry.mask);
}

function subjectLink(entry: KernelActivityEntry): RouteLocationRaw | null {
  if (!entry.subject) return null;
  const node = parseNodeRef(entry.subject);
  if (node?.kind === "protocol_version") {
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
  return {
    name: "study",
    params: { studyCode: props.studyCode },
    query: {
      ...(currentRoute.value?.query ?? {}),
      node: entry.subject,
    },
  };
}

function resolveSubjectHref(entry: KernelActivityEntry): string | null {
  const loc = subjectLink(entry);
  if (!loc || !currentRouter.value?.resolve) return null;
  try {
    return currentRouter.value.resolve(loc).href || null;
  } catch {
    return null;
  }
}

const adaptedEntries = computed(() =>
  adaptKernelActivityEntries(entries.value, {
    formatWhatLabel: words,
    formatMaskedSubjectLabel: hiddenLabel,
    resolveSubjectHref,
  })
);

async function fetchActivityAuditEntry(auditRef: string) {
  if (typeof kernelApi.getAuditEntry !== "function") return null;
  return await kernelApi.getAuditEntry(auditRef);
}

function reload() {
  entries.value = [];
  next.value = null;
  void runForCurrentStudy((scope, isCurrent) =>
    fetchActivityPage(scope, null, isCurrent)
  );
}

function loadOlder() {
  const cursor = next.value;
  void runForCurrentStudy((scope, isCurrent) =>
    fetchActivityPage(scope, cursor, isCurrent)
  );
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
</style>
