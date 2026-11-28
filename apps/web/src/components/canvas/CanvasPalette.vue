<template>
  <Teleport to="body" :disabled="!props.teleport">
    <div
      id="canvas-palette-backdrop"
      class="palette-backdrop"
      role="presentation"
      @click.self="emit('close')"
    >
      <div
        id="canvas-palette"
        ref="dialogRef"
        class="palette"
        :style="paletteMaxHeightStyle"
        role="dialog"
        aria-modal="true"
        :aria-busy="isSearching ? 'true' : 'false'"
        aria-label="Go to or open anything in this study"
      >
        <div class="palette-header">
          <input
            id="canvas-palette-input"
            v-model="query"
            type="text"
            role="combobox"
            aria-autocomplete="list"
            :aria-controls="results.length ? 'canvas-palette-list' : undefined"
            :aria-expanded="results.length > 0"
            :aria-busy="isSearching ? 'true' : 'false'"
            :aria-activedescendant="
              results.length ? `canvas-palette-item-${active}` : undefined
            "
            autocomplete="off"
            :placeholder="
              studyCode
                ? 'Stage, section, participant, workflow or study code'
                : 'Study code or workflow, e.g. CADENCE-101'
            "
            aria-label="Search"
            @keydown="onKeydown"
          />
          <button
            id="canvas-palette-close"
            type="button"
            class="palette-close-btn"
            aria-label="Close command palette"
            @click="emit('close')"
          >
            <ClinicalIcon name="x" :size="18" />
          </button>
        </div>
        <div class="palette-body">
          <ul
            v-if="results.length"
            id="canvas-palette-list"
            class="palette-list"
            role="listbox"
            :aria-busy="isSearching ? 'true' : 'false'"
            aria-label="Results"
          >
            <li
              v-for="(item, index) in results"
              :id="`canvas-palette-item-${index}`"
              :key="item.key"
              class="palette-item"
              :class="{ active: index === active }"
              role="option"
              tabindex="-1"
              :aria-selected="index === active"
              @click="choose(item)"
              @keydown.enter.prevent="choose(item)"
              @keydown.space.prevent="choose(item)"
              @mouseenter="active = index"
              @focus="active = index"
            >
              <span class="palette-group">{{ item.group }}</span>
              <span class="palette-label">{{ item.label }}</span>
              <span v-if="item.detail" class="palette-detail">{{
                item.detail
              }}</span>
            </li>
          </ul>
          <p
            v-else
            id="canvas-palette-empty"
            class="palette-empty"
            role="status"
          >
            {{
              query.trim()
                ? "Nothing matches across studies, lifecycle stages, or authorized workflows."
                : "Type a study code or choose a workflow destination."
            }}
          </p>
          <p
            v-if="kernelNote"
            id="canvas-palette-kernel-note"
            class="palette-footnote"
            role="status"
          >
            {{ kernelNote }}
          </p>
          <p
            v-if="displaySubjectNote"
            id="canvas-palette-subject-note"
            class="palette-footnote"
            role="status"
          >
            {{ displaySubjectNote }}
          </p>
          <p class="palette-footnote">
            Studies are found by code or title among those visible to your
            account. Available actions for a selected participant are under Act
            in the inspector.
          </p>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import { ClinicalIcon, useFocusTrap, useViewportMaxHeight } from "ui";
import { kernelApi, type KernelSearchHit } from "../../api/kernelApi";
import { STAGES, participantStateLabel } from "../../canvas/lifecycle";
import { executionApi, type SubjectSummary } from "../../api/executionApi";
import type {
  PaletteTarget,
  WorkspacePaletteOption,
} from "../../canvas/palette";
import { useAiAssistance } from "../../ai/aiAssistance";
import {
  participantNodeRef,
  useStudyCanvasStore,
} from "../../stores/studyCanvas";
import { isDemoBuild } from "../../utils/demoMode";

/**
 * The unified global ⌘K command palette (docs/ux/CLINICAL_CANVAS.md §2.5):
 * combines kernel study search (ADR-2484, ADR-2540), opening a study by code,
 * lifecycle navigation (study home, stages, sections, loaded and server-searched
 * participants, and Assist Ask when AI assistance is enabled), and authorized UX-1
 * workflow destinations and workspace controls.
 */
interface PaletteItem {
  key: string;
  group: string;
  label: string;
  detail?: string;
  target: PaletteTarget;
}

const props = withDefaults(
  defineProps<{
    studyCode?: string;
    participants?: SubjectSummary[];
    workspaceItems?: WorkspacePaletteOption[];
    teleport?: boolean;
  }>(),
  {
    studyCode: "",
    participants: () => [],
    workspaceItems: () => [],
    teleport: true,
  }
);
const emit = defineEmits<{
  close: [];
  go: [target: PaletteTarget];
}>();

const dialogRef = ref<HTMLElement | null>(null);
const { maxHeightStyle: paletteMaxHeightStyle } = useViewportMaxHeight({
  shortTopOffset: 12,
  bottomMargin: 12,
  minHeight: 160,
});
useFocusTrap(dialogRef, {
  onEscape: () => emit("close"),
  initialFocus: "#canvas-palette-input",
});

const query = ref("");
const active = ref(0);

// Execution accepts any non-blank study code up to 255 characters, compared exactly.
const MAX_STUDY_CODE = 255;
const MAX_RESULTS = 30;
const DEMO_MAX_RESULTS = 80;
const maxResults = computed(() =>
  isDemoBuild() ? DEMO_MAX_RESULTS : MAX_RESULTS
);
const MAX_KERNEL_HITS = 5;
const MIN_SEARCH_LENGTH = 2;
const SEARCH_DELAY_MS = 250;
const MIN_ASK_WORDS = 3;

const { enabled: aiEnabled } = useAiAssistance();

const kernelHits = ref<KernelSearchHit[]>([]);
const kernelNote = ref("");
const subjectNote = ref("");
const remoteParticipants = ref<SubjectSummary[]>([]);
const resolvedParticipant = ref<SubjectSummary | null>(null);
const isDebouncePending = ref(false);
const pendingSearchCount = ref(0);
const activeCanvasStore = computed(() => {
  const activePinia = getActivePinia();
  return activePinia ? useStudyCanvasStore(activePinia) : null;
});
const isSearching = computed(
  () =>
    isDebouncePending.value ||
    pendingSearchCount.value > 0 ||
    Boolean(activeCanvasStore.value?.paletteLoading)
);
const displaySubjectNote = computed(() => {
  if (subjectNote.value) return subjectNote.value;
  const preloadError = activeCanvasStore.value?.paletteError;
  return preloadError
    ? `Participants can't be searched right now: ${preloadError}`
    : "";
});

function isNotFoundStatus(err: unknown): boolean {
  return Boolean(
    err &&
    typeof err === "object" &&
    "status" in err &&
    Number((err as { status?: unknown }).status) === 404
  );
}
let searchTimer: ReturnType<typeof setTimeout> | undefined;
let latestSearch = 0;
let latestParticipantLookup = 0;

async function searchKernel(text: string, study: string, request: number) {
  isDebouncePending.value = false;
  const activePinia = getActivePinia();
  const canvasStore = activePinia ? useStudyCanvasStore(activePinia) : null;
  const shouldQueryRemoteParticipants = Boolean(
    study &&
    typeof executionApi.listSubjects === "function" &&
    (!canvasStore ||
      !canvasStore.paletteLoaded ||
      canvasStore.unfilteredLoadedCount < canvasStore.paletteTotal)
  );
  if (shouldQueryRemoteParticipants) {
    pendingSearchCount.value += 1;
    void executionApi
      .listSubjects({
        studyId: study,
        q: text,
        limit: MAX_KERNEL_HITS,
      })
      .then((page) => {
        if (request !== latestSearch) return;
        remoteParticipants.value = page?.items ?? [];
        subjectNote.value = "";
      })
      .catch((err) => {
        if (request !== latestSearch) return;
        remoteParticipants.value = [];
        if (!isNotFoundStatus(err)) {
          subjectNote.value = `Participants can't be searched right now: ${err instanceof Error ? err.message : String(err)}`;
        }
      })
      .finally(() => {
        pendingSearchCount.value = Math.max(0, pendingSearchCount.value - 1);
      });
  }

  pendingSearchCount.value += 1;
  try {
    const hits = await kernelApi.search({
      text,
      kinds: ["study"],
      limit: MAX_KERNEL_HITS,
    });
    if (request !== latestSearch) return;
    kernelHits.value = hits;
    kernelNote.value = "";
  } catch (e) {
    if (request !== latestSearch) return;
    kernelHits.value = [];
    kernelNote.value = `Studies can't be searched right now: ${e instanceof Error ? e.message : String(e)}`;
  } finally {
    pendingSearchCount.value = Math.max(0, pendingSearchCount.value - 1);
  }
}

watch([query, () => props.studyCode], ([value, study]) => {
  clearTimeout(searchTimer);
  const request = ++latestSearch;
  const lookupRequest = ++latestParticipantLookup;
  const text = value.trim();
  kernelHits.value = [];
  remoteParticipants.value = [];
  resolvedParticipant.value = null;
  subjectNote.value = "";
  if (text.length < MIN_SEARCH_LENGTH || text.length > MAX_STUDY_CODE) {
    isDebouncePending.value = false;
    kernelNote.value = "";
    return;
  }
  isDebouncePending.value = true;
  searchTimer = setTimeout(
    () => void searchKernel(text, study, request),
    SEARCH_DELAY_MS
  );

  const activePinia = getActivePinia();
  const canvasStore = activePinia ? useStudyCanvasStore(activePinia) : null;
  if (
    canvasStore &&
    study &&
    !/\s/.test(text) &&
    text !== study &&
    !props.participants.some(
      (participant) =>
        participant.subject_id.toLowerCase() === text.toLowerCase()
    )
  ) {
    pendingSearchCount.value += 1;
    void canvasStore
      .resolveParticipantByCode(text, study)
      .then((matched) => {
        if (lookupRequest === latestParticipantLookup && matched) {
          resolvedParticipant.value = matched;
        }
      })
      .catch((err) => {
        if (lookupRequest !== latestParticipantLookup) return;
        if (!isNotFoundStatus(err)) {
          subjectNote.value = `Participants can't be searched right now: ${err instanceof Error ? err.message : String(err)}`;
        }
      })
      .finally(() => {
        pendingSearchCount.value = Math.max(0, pendingSearchCount.value - 1);
      });
  }
});

onBeforeUnmount(() => {
  clearTimeout(searchTimer);
  isDebouncePending.value = false;
  latestSearch++;
  latestParticipantLookup++;
});

const allParticipants = computed<SubjectSummary[]>(() => {
  const byId = new Map<string, SubjectSummary>();
  for (const participant of props.participants) {
    byId.set(participant.id, participant);
  }
  for (const candidate of [
    ...remoteParticipants.value,
    ...(resolvedParticipant.value ? [resolvedParticipant.value] : []),
  ]) {
    if (candidate.study_id && candidate.study_id !== props.studyCode) continue;
    if (!byId.has(candidate.id)) {
      byId.set(candidate.id, candidate);
    }
  }
  return Array.from(byId.values());
});

// A study hit opens the study it names by its code; one without a code has nowhere to open.
const studyHits = computed<PaletteItem[]>(() =>
  kernelHits.value
    .filter((hit) => hit.code && hit.code !== props.studyCode)
    .map((hit) => ({
      key: `kernel:${hit.ref}`,
      group: "Study",
      label: hit.code as string,
      detail: hit.title,
      target: { studyCode: hit.code as string },
    }))
);

const items = computed<PaletteItem[]>(() => {
  const list: PaletteItem[] = [];
  const study = props.studyCode;
  if (study) {
    list.push({
      key: "home",
      group: "Study",
      label: `${study} home`,
      target: { studyCode: study },
    });
    for (const stage of STAGES) {
      list.push({
        key: `stage:${stage.id}`,
        group: "Stage",
        label: stage.label,
        detail: stage.scope,
        target: { studyCode: study, stage: stage.id },
      });
      for (const section of stage.sections) {
        list.push({
          key: `section:${section.id}`,
          group: stage.label,
          label: section.label,
          detail: section.description,
          target: { studyCode: study, stage: stage.id, section: section.id },
        });
      }
    }
  }
  for (const wsItem of props.workspaceItems) {
    list.push(wsItem);
  }
  if (study) {
    for (const participant of allParticipants.value) {
      list.push({
        key: participantNodeRef(participant) || `participant:${participant.id}`,
        group: "Participant",
        label: participant.subject_id,
        detail: [
          participantStateLabel(participant.status),
          participant.site_id ? `Site ${participant.site_id}` : "",
        ]
          .filter(Boolean)
          .join(" · "),
        target: {
          studyCode: study,
          stage: "conduct",
          section: "participants",
          participant,
        },
      });
    }
  }
  return list;
});

const results = computed<PaletteItem[]>(() => {
  const text = query.value.trim();
  const needle = text.toLowerCase();
  const matches = needle
    ? items.value.filter((item) =>
        `${item.group} ${item.label} ${item.detail ?? ""}`
          .toLowerCase()
          .includes(needle)
      )
    : items.value;
  const shown = [...matches.slice(0, maxResults.value), ...studyHits.value];
  // A sentence is a question for Assist, which answers by reading or proposing, never by writing (ADR-2635).
  if (
    aiEnabled.value &&
    props.studyCode &&
    text.split(/\s+/).length >= MIN_ASK_WORDS
  ) {
    shown.push({
      key: `ask:${text}`,
      group: "Ask AI",
      label: text,
      detail: "Opens Assist. Nothing changes until a person decides.",
      target: { studyCode: props.studyCode, ask: text },
    });
  }
  const matchesParticipantCode = allParticipants.value.some(
    (participant) => participant.subject_id.toLowerCase() === needle
  );
  if (
    text &&
    !/\s/.test(text) &&
    text.length <= MAX_STUDY_CODE &&
    text !== props.studyCode &&
    !matchesParticipantCode &&
    !studyHits.value.some((item) => item.target.studyCode === text)
  ) {
    shown.push({
      key: `open:${text}`,
      group: "Open study",
      label: text,
      target: { studyCode: text },
    });
  }
  return shown;
});

let lastEvaluatedQuery = "";

watch(results, (nextResults, prevResults) => {
  const normalizedQuery = query.value.trim();
  const queryChanged = normalizedQuery !== lastEvaluatedQuery;
  lastEvaluatedQuery = normalizedQuery;

  if (!queryChanged && prevResults && prevResults.length > 0) {
    const previousKey = prevResults[active.value]?.key;
    if (previousKey) {
      const retainedIndex = nextResults.findIndex(
        (item) => item.key === previousKey
      );
      if (retainedIndex >= 0) {
        active.value = retainedIndex;
        return;
      }
    }
  }
  active.value = 0;
});

function choose(item: PaletteItem) {
  emit("go", item.target);
}

function scrollActiveIntoView() {
  nextTick(() =>
    dialogRef.value
      ?.querySelector<HTMLElement>(`#canvas-palette-item-${active.value}`)
      ?.scrollIntoView?.({ block: "nearest" })
  );
}

function onKeydown(event: KeyboardEvent) {
  const count = results.value.length;
  if (event.key === "Escape") {
    event.preventDefault();
    emit("close");
  } else if (event.key === "ArrowDown" && count) {
    event.preventDefault();
    active.value = (active.value + 1) % count;
    scrollActiveIntoView();
  } else if (event.key === "ArrowUp" && count) {
    event.preventDefault();
    active.value = (active.value - 1 + count) % count;
    scrollActiveIntoView();
  } else if (event.key === "Home" && count) {
    event.preventDefault();
    active.value = 0;
    scrollActiveIntoView();
  } else if (event.key === "End" && count) {
    event.preventDefault();
    active.value = count - 1;
    scrollActiveIntoView();
  } else if (event.key === "Enter" && count) {
    event.preventDefault();
    choose(results.value[active.value]);
  }
}
</script>

<style scoped>
.palette-backdrop {
  position: fixed;
  inset: 0;
  z-index: 9999;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 12vh 16px 16px;
  box-sizing: border-box;
  background: var(--semantic-color-backdrop);
}

.palette {
  display: flex;
  flex-direction: column;
  width: min(640px, 100%);
  max-height: calc(100dvh - 24px);
  box-sizing: border-box;
  background: var(--semantic-color-surface);
  border: 1px solid var(--border);
  border-radius: 10px;
  box-shadow: var(--shadow-xl);
  overflow: hidden;
}

.palette-header {
  display: flex;
  align-items: center;
  gap: 4px;
  padding-right: 6px;
  border-bottom: 1px solid var(--border);
  flex-shrink: 0;
}

.palette-header input {
  flex: 1 1 auto;
  min-width: 0;
  width: 100%;
  box-sizing: border-box;
  padding: 14px 16px;
  border: 0;
  font-size: max(1rem, 16px);
  background: transparent;
  color: inherit;
}

.palette-close-btn {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  box-sizing: border-box;
  min-width: 44px;
  min-height: 44px;
  width: 44px;
  height: 44px;
  padding: 0;
  border: 1px solid transparent;
  border-radius: 6px;
  background: transparent;
  color: var(--neutral-dark);
  cursor: pointer;
}

.palette-close-btn:not(:disabled):hover {
  background: var(--semantic-color-surface-header);
  border-color: var(--semantic-color-border-control);
  color: var(--semantic-color-text);
}

.palette-body {
  display: flex;
  flex-direction: column;
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
  overscroll-behavior: contain;
}

.palette-list {
  list-style: none;
  margin: 0;
  padding: 6px;
  flex: 1 1 auto;
  min-height: 0;
  overflow-y: auto;
}

.palette-item {
  display: grid;
  grid-template-columns: 7rem 1fr;
  gap: 2px 12px;
  padding: 8px 10px;
  border-radius: 6px;
  cursor: pointer;
}

.palette-item.active {
  background: var(--accent-bg);
}

.palette-group {
  grid-row: span 2;
  color: var(--neutral-dark);
  font-size: 0.8rem;
  text-transform: uppercase;
  letter-spacing: 0.04em;
}

.palette-label {
  font-weight: 600;
  overflow-wrap: anywhere;
}

.palette-detail {
  color: var(--neutral-dark);
  font-size: 0.85rem;
  overflow-wrap: anywhere;
}

.palette-empty,
.palette-footnote {
  margin: 0;
  padding: 12px 16px;
  color: var(--neutral-dark);
  font-size: 0.85rem;
}

.palette-footnote {
  border-top: 1px solid var(--border);
}

@media (max-width: 639px) {
  .palette-backdrop {
    padding: 12px;
  }

  .palette-item {
    grid-template-columns: minmax(0, 1fr);
    gap: 2px;
  }

  .palette-group {
    grid-row: auto;
  }
}

@media (max-height: 520px) {
  .palette-backdrop {
    padding: 12px;
  }
}
</style>
