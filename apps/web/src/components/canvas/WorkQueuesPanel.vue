<template>
  <section
    v-if="hasWorkQueueService"
    :id="isCrossStudy ? 'cross-study-action-items' : 'study-work-queues'"
    :data-testid="isCrossStudy ? 'my-action-items' : 'study-work-queues'"
    class="work-queues"
    :aria-labelledby="
      isCrossStudy
        ? 'cross-study-action-items-title'
        : 'study-work-queues-title'
    "
    :aria-busy="loading"
  >
    <div class="work-queues-header">
      <div>
        <h2
          :id="
            isCrossStudy
              ? 'cross-study-action-items-title'
              : 'study-work-queues-title'
          "
          tabindex="-1"
        >
          {{ isCrossStudy ? "My Action Items" : "Work queues" }}
        </h2>
        <p class="work-queues-subtitle">
          {{
            isCrossStudy
              ? "Actionable items waiting on your authority across your visible studies."
              : `Capability-keyed actionable queues for your authority on ${normalizedStudyCode}.`
          }}
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
        {{
          isCrossStudy
            ? "Action items couldn't be loaded right now."
            : "Work queues couldn't be loaded right now."
        }}
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
            <p class="work-queue-capability">
              {{ describeCapability(queue.capability) }}
            </p>
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
            <button
              v-if="canExplainQueue(queue)"
              type="button"
              class="btn btn-secondary work-queue-explain-btn"
              :data-testid="`work-queue-explain-btn-${queue.code}`"
              :aria-expanded="Boolean(openExplanations[queue.code])"
              :aria-controls="`work-queue-explanation-${queue.code}`"
              @click="toggleQueueExplanation(queue)"
            >
              {{
                openExplanations[queue.code] ? "Hide explanation" : "Explain"
              }}
            </button>
          </div>
        </header>

        <div
          v-if="openExplanations[queue.code]"
          :id="`work-queue-explanation-${queue.code}`"
          :data-testid="`work-queue-explanation-${queue.code}`"
          class="work-queue-explanation"
          role="region"
          :aria-label="`Authority explanation for ${queue.title}`"
        >
          <p
            v-if="explanationLoading[queue.code]"
            class="work-queue-empty"
            role="status"
          >
            Checking capability rules…
          </p>
          <KernelExplanation
            v-else-if="queueExplanations[queue.code]"
            :explanation="queueExplanations[queue.code]!"
          />
          <p v-else class="work-queue-empty">
            No additional capability conditions were reported for this queue.
          </p>
        </div>

        <p v-if="queue.items.length === 0" class="work-queue-empty">
          No items waiting for your review in this queue.
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
                <EntityLink
                  :data-testid="`work-item-link-${queue.code}-${idx}`"
                  class="work-item-link"
                  :node-ref="item.subject || ''"
                  :label="workItemDisplayLabel(item)"
                  :kind="item.subject_kind || ''"
                  :subtitle="workItemSubtitle(item)"
                  :to="workItemLocation(item) ?? undefined"
                  preview-mode="drawer"
                  :resolve-remotely="!item.title"
                />
                <div class="work-item-tags">
                  <span
                    v-if="isCrossStudy && itemStudyCode(item)"
                    class="work-item-study font-mono"
                    :data-testid="`work-item-study-${queue.code}-${idx}`"
                  >
                    {{ itemStudyCode(item) }}
                  </span>
                  <span v-if="item.subject_kind" class="work-item-kind">
                    {{ formatKindTitle(item.subject_kind) }}
                  </span>
                </div>
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
import { computed, reactive, ref, watch } from "vue";
import type { RouteLocationRaw } from "vue-router";
import { EntityLink } from "ui";
import {
  kernelApi,
  type KernelExplanation as KernelExplanationDto,
  type KernelStudySummary,
  type KernelWorkItem,
  type KernelWorkQueue,
} from "../../api/kernelApi";
import {
  findKernelStudyRef,
  seedKernelStudyRef,
} from "../../canvas/kernelStudy";
import { resolveEntityFullViewLocation } from "../../canvas/entityDestination";
import {
  collectionInstanceRef,
  formatNodeRef,
  observationNodeRef,
  parseNodeRef,
  tryNodeRef,
} from "../../canvas/nodeRef";
import {
  ensureNodesResolved,
  formatCommandTitle,
  formatKindTitle,
  getNodeDisplay,
  maskReasonWords,
  normalizeLookupNodeRef,
  sanitizeDiagnosticSummary,
  seedNodeSummary,
} from "../../canvas/nodeResolver";
import { useScopedNavigation } from "../../composables/useScopedNavigation";
import KernelExplanation from "./KernelExplanation.vue";
import SupportDetails from "./SupportDetails.vue";

const CAPABILITY_LABELS: Record<string, string> = {
  "protocol.approve": "Approve protocol versions in review",
  "protocol_version.approve": "Approve protocol versions in review",
  "proposal.approve": "Review and adjudicate proposed changes",
  "proposal.review": "Review and adjudicate proposed changes",
  "observation.verify": "Verify source clinical observations",
  "collection_instance.sign": "Sign completed clinical forms",
  "collection.sign": "Sign completed clinical forms",
  "form.sign": "Sign completed clinical forms",
  "execution.query.answer": "Answer open data queries",
  "execution.query.manage": "Review answered queries and close",
  "observation.query.answer": "Answer open data queries",
  "observation.query": "Raise and manage clinical data queries",
  "query.answer": "Answer open data queries",
  "query.close": "Review answered queries and close",
  "validation_finding.investigate":
    "Investigate and resolve validation findings",
  "validation_finding.resolve": "Review and resolve validation findings",
  "validation_finding.classify": "Classify and explain validation findings",
};

const props = withDefaults(
  defineProps<{
    studyCode?: string;
    studies?: KernelStudySummary[];
  }>(),
  {
    studyCode: "",
    studies: undefined,
  }
);

const normalizedStudyCode = computed(() => (props.studyCode ?? "").trim());
const isCrossStudy = computed(() => !normalizedStudyCode.value);

const { workspaceScope, scopedStudyLocation } = useScopedNavigation(
  () => normalizedStudyCode.value
);

const hasWorkQueueService = computed(
  () => typeof kernelApi.listWorkQueues === "function"
);
const hasCanIService = computed(() => typeof kernelApi.canI === "function");

const queues = ref<KernelWorkQueue[]>([]);
const resolvedStudyRef = ref<string | null>(null);
const loadedStudies = ref<KernelStudySummary[]>([]);
const loading = ref(false);
const error = ref("");
const noStudy = ref(false);
const showEmpty = ref(false);

const openExplanations = reactive<Record<string, boolean>>({});
const explanationLoading = reactive<Record<string, boolean>>({});
const queueExplanations = reactive<Record<string, KernelExplanationDto | null>>(
  {}
);

const studyCodeByRef = computed<Record<string, string>>(() => {
  const map: Record<string, string> = {};
  for (const s of [...(props.studies ?? []), ...loadedStudies.value]) {
    if (!s?.code || !s?.ref) continue;
    const normalized = normalizeLookupNodeRef(s.ref) ?? s.ref;
    map[normalized] = s.code;
    const parsed = parseNodeRef(normalized);
    if (parsed) {
      map[`${parsed.kind}:${parsed.id}`] = s.code;
    }
  }
  return map;
});

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
  if (loading.value) {
    return isCrossStudy.value
      ? "Loading cross-study action items…"
      : "Loading capability work queues…";
  }
  if (!isCrossStudy.value && noStudy.value) {
    return `Study ${normalizedStudyCode.value} wasn't found or isn't visible to your account.`;
  }
  if (queues.value.length === 0 || totalItems.value === 0) {
    return isCrossStudy.value
      ? "No action items are waiting on your authority across your visible studies."
      : "All capability work queues are clear for your permissions on this study.";
  }
  return "";
});

function describeCapability(capability: string): string {
  const clean = (capability ?? "").trim();
  if (!clean) return "Authorized review";
  if (CAPABILITY_LABELS[clean]) return CAPABILITY_LABELS[clean];
  return formatCommandTitle(clean);
}

function humanizeExplanationText(text: string): string {
  if (!text) return "";
  let cleaned = sanitizeDiagnosticSummary(text);
  cleaned = cleaned.replace(
    /\b([a-z][a-z0-9_]*\.[a-z][a-z0-9_.]*)\b/g,
    (match) =>
      CAPABILITY_LABELS[match]?.toLowerCase() ??
      formatCommandTitle(match).toLowerCase()
  );
  return cleaned;
}

function normalizeExplanation(
  explanation: KernelExplanationDto
): KernelExplanationDto {
  return {
    ...explanation,
    claim: humanizeExplanationText(explanation.claim),
    steps: (explanation.steps ?? []).map((s) => ({
      claim: humanizeExplanationText(s.claim),
      evidence: s.evidence ?? [],
    })),
    conditions: (explanation.conditions ?? []).map((c) => ({
      code: c.code,
      description: humanizeExplanationText(c.description),
      met: c.met,
      evidence: c.evidence ?? [],
    })),
    next_actions: (explanation.next_actions ?? []).map((n) => ({
      description: humanizeExplanationText(n.description),
      capability: n.capability ?? null,
      target: n.target,
    })),
  };
}

function explainTargetForQueue(queue: KernelWorkQueue): string | null {
  for (const item of queue.items) {
    if (!item.masked && item.subject && parseNodeRef(item.subject)) {
      return item.subject;
    }
    if (item.target?.study_ref && parseNodeRef(item.target.study_ref)) {
      return item.target.study_ref;
    }
  }
  if (resolvedStudyRef.value && parseNodeRef(resolvedStudyRef.value)) {
    return resolvedStudyRef.value;
  }
  const fallbackStudy =
    props.studies?.[0]?.ref ?? loadedStudies.value[0]?.ref ?? null;
  return fallbackStudy && parseNodeRef(fallbackStudy) ? fallbackStudy : null;
}

function canExplainQueue(queue: KernelWorkQueue): boolean {
  return (
    hasCanIService.value &&
    (Boolean(explainTargetForQueue(queue)) || !isCrossStudy.value)
  );
}

async function toggleQueueExplanation(queue: KernelWorkQueue) {
  const code = queue.code;
  if (openExplanations[code]) {
    openExplanations[code] = false;
    return;
  }
  openExplanations[code] = true;
  if (queueExplanations[code] !== undefined || explanationLoading[code]) {
    return;
  }
  if (typeof kernelApi.canI !== "function") {
    queueExplanations[code] = null;
    return;
  }
  explanationLoading[code] = true;
  try {
    let target = explainTargetForQueue(queue);
    if (!target && !isCrossStudy.value && normalizedStudyCode.value) {
      target = await findKernelStudyRef(normalizedStudyCode.value);
      if (target) {
        resolvedStudyRef.value = target;
      }
    }
    if (!target) {
      queueExplanations[code] = null;
      return;
    }
    const decision = await kernelApi.canI(queue.capability, target);
    queueExplanations[code] = decision?.explanation
      ? normalizeExplanation(decision.explanation)
      : null;
  } catch {
    queueExplanations[code] = null;
  } finally {
    explanationLoading[code] = false;
  }
}

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

function itemStudyCode(item: KernelWorkItem): string {
  if (normalizedStudyCode.value) return normalizedStudyCode.value;
  const rawStudyRef = item.target?.study_ref;
  if (!rawStudyRef) return "";
  const normalized = normalizeLookupNodeRef(rawStudyRef) ?? rawStudyRef;
  if (studyCodeByRef.value[normalized]) {
    return studyCodeByRef.value[normalized];
  }
  const parsed = parseNodeRef(normalized);
  if (parsed && studyCodeByRef.value[`${parsed.kind}:${parsed.id}`]) {
    return studyCodeByRef.value[`${parsed.kind}:${parsed.id}`];
  }
  const display = getNodeDisplay(normalized);
  return display.code ?? "";
}

function workItemDisplayLabel(item: KernelWorkItem): string {
  if (item.title && item.title.trim()) {
    return item.title.trim();
  }
  if (item.subject) {
    const display = getNodeDisplay(item.subject);
    if (display.label) return display.label;
  }
  if (item.subject_kind) {
    return formatKindTitle(item.subject_kind);
  }
  return "Open item";
}

function activeSiteForStudy(targetStudyCode: string): string {
  if (!targetStudyCode) return "";
  const scope = workspaceScope.value;
  const activeSite = (scope.siteId ?? "").trim();
  if (!activeSite) return "";
  const scopeStudy = (scope.studyCode ?? "").trim();
  if (!scopeStudy || scopeStudy === targetStudyCode) {
    return activeSite;
  }
  return "";
}

function workItemSubtitle(item: KernelWorkItem): string {
  const code = itemStudyCode(item);
  const site = activeSiteForStudy(code);
  if (code && site) return `Study ${code} · Site ${site}`;
  return code ? `Study ${code}` : "";
}

function workItemLocation(item: KernelWorkItem): RouteLocationRaw {
  const code = itemStudyCode(item);
  if (!code) {
    return scopedStudyLocation({ preserveStageSection: false }, "");
  }
  const target = item.target;
  const targetEnrollmentNode = target?.enrollment_id
    ? tryNodeRef("enrollment", target.enrollment_id)
    : null;
  const targetEnrollmentParticipant = targetEnrollmentNode
    ? formatNodeRef(targetEnrollmentNode)
    : undefined;

  if (target?.proposal_ref) {
    return scopedStudyLocation({ proposal: target.proposal_ref }, code);
  }
  if (target?.protocol_version_ref) {
    return scopedStudyLocation(
      {
        stage: "design",
        section: "protocol-versions",
        node: target.protocol_version_ref,
      },
      code
    );
  }
  if (target?.finding_ref) {
    return scopedStudyLocation(
      {
        stage: "data",
        section: "validation",
        node: target.finding_ref,
      },
      code
    );
  }
  if (target?.form_id) {
    const formRef = collectionInstanceRef(target.form_id);
    if (formRef) {
      return scopedStudyLocation(
        {
          stage: "conduct",
          form: formRef,
          ...(targetEnrollmentParticipant
            ? { participant: targetEnrollmentParticipant }
            : {}),
        },
        code
      );
    }
  }
  if (target?.observation_ref) {
    const obsRef = observationNodeRef(target.observation_ref);
    if (obsRef) {
      return scopedStudyLocation(
        {
          stage: "conduct",
          observation: obsRef,
          ...(targetEnrollmentParticipant
            ? { participant: targetEnrollmentParticipant }
            : {}),
        },
        code
      );
    }
  }
  if (item.subject) {
    const subjectLocation = resolveEntityFullViewLocation(item.subject, {
      studyCode: code,
      site: activeSiteForStudy(code) || undefined,
      ...(targetEnrollmentParticipant
        ? { participant: targetEnrollmentParticipant }
        : {}),
      scope: workspaceScope.value,
    });
    if (subjectLocation) {
      return subjectLocation;
    }
  }
  return scopedStudyLocation({ preserveStageSection: false }, code);
}

function resetExplanationState() {
  for (const key of Object.keys(openExplanations)) {
    delete openExplanations[key];
  }
  for (const key of Object.keys(explanationLoading)) {
    delete explanationLoading[key];
  }
  for (const key of Object.keys(queueExplanations)) {
    delete queueExplanations[key];
  }
}

let requestSeq = 0;

async function load() {
  if (!hasWorkQueueService.value) return;
  const seq = ++requestSeq;
  const code = props.studyCode.trim();
  loading.value = true;
  error.value = "";
  noStudy.value = false;
  resolvedStudyRef.value = null;
  queues.value = [];
  resetExplanationState();
  try {
    if (isCrossStudy.value) {
      if (
        props.studies === undefined &&
        loadedStudies.value.length === 0 &&
        typeof kernelApi.listStudies === "function"
      ) {
        try {
          const fetched = await kernelApi.listStudies();
          if (seq === requestSeq && Array.isArray(fetched)) {
            loadedStudies.value = fetched;
            for (const s of fetched) {
              if (s?.ref) {
                seedNodeSummary(s.ref, {
                  kind: "study",
                  code: s.code,
                  title: s.title,
                });
              }
            }
          }
        } catch {
          // Cross-study work queues can still resolve study labels via nodeResolver.
        }
      }
      const page = await kernelApi.listWorkQueues();
      if (seq !== requestSeq) return;
      const nextQueues = page?.queues ?? [];
      queues.value = nextQueues;

      const missingStudyRefs = new Set<string>();
      for (const q of nextQueues) {
        for (const item of q.items) {
          const studyRef = item.target?.study_ref;
          if (studyRef && !studyCodeByRef.value[studyRef]) {
            missingStudyRefs.add(studyRef);
          }
          if (!item.masked && !item.title && item.subject) {
            missingStudyRefs.add(item.subject);
          }
        }
      }
      if (missingStudyRefs.size > 0) {
        ensureNodesResolved(missingStudyRefs);
      }
      return;
    }

    const page = await kernelApi.listWorkQueues({ within: code });
    if (seq === requestSeq) {
      const nextQueues = page?.queues ?? [];
      queues.value = nextQueues;
      for (const q of nextQueues) {
        const studyRefHint = q.items.find((it) => it.target?.study_ref)?.target
          ?.study_ref;
        if (studyRefHint) {
          resolvedStudyRef.value = studyRefHint;
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
  () => props.studies,
  (studiesList) => {
    for (const s of studiesList ?? []) {
      if (s?.ref) {
        seedNodeSummary(s.ref, {
          kind: "study",
          code: s.code,
          title: s.title,
        });
      }
    }
  },
  { immediate: true }
);

watch(normalizedStudyCode, load, { immediate: true });
</script>

<style scoped>
.work-queues {
  display: flex;
  flex-direction: column;
  gap: 12px;
  margin-bottom: 20px;
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
  font-variant-numeric: var(--font-variant-numeric-tabular);
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
  margin: 2px 0 0;
  max-width: 100%;
  overflow-wrap: anywhere;
  font-size: 0.78rem;
  color: var(--semantic-color-text-secondary);
}

.work-queue-badges {
  display: flex;
  flex-wrap: wrap;
  align-items: center;
  justify-content: flex-end;
  gap: 6px;
}

.work-queue-explain-btn {
  padding: 2px 8px;
  min-height: 28px;
  font-size: 0.76rem;
}

.work-queue-explanation {
  padding: 8px 10px;
  border-radius: 6px;
  border: 1px solid var(--border);
  background: var(--semantic-color-surface-muted);
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
  font-variant-numeric: var(--font-variant-numeric-tabular);
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-dark);
}

.work-queue-truncated {
  padding: 2px 6px;
  border-radius: 4px;
  font-size: var(--font-size-2xs);
  font-weight: 600;
  font-variant-numeric: var(--font-variant-numeric-tabular);
  background: var(--semantic-color-warning-bg-light);
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

.work-item-tags {
  display: inline-flex;
  align-items: center;
  gap: 6px;
}

.work-item-study {
  padding: 1px 6px;
  border-radius: 4px;
  font-family: var(--semantic-font-mono);
  font-size: var(--font-size-2xs);
  font-weight: 600;
  background: var(--semantic-color-surface-muted);
  color: var(--semantic-color-text-dark);
}

.work-item-link {
  font-weight: 500;
  color: var(--primary);
  text-decoration: underline;
}

.work-item-kind {
  font-size: var(--font-size-2xs);
  color: var(--semantic-color-text-secondary);
}

.work-item-detail {
  margin: 0;
  font-size: 0.78rem;
  font-variant-numeric: var(--font-variant-numeric-tabular);
  color: var(--semantic-color-text-secondary);
}

.work-item-masked {
  font-style: italic;
  color: var(--semantic-color-text-secondary);
}
</style>
