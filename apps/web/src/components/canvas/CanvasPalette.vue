<template>
  <div class="palette-backdrop" role="presentation" @click.self="emit('close')">
    <div
      id="canvas-palette"
      ref="dialogRef"
      class="palette"
      role="dialog"
      aria-modal="true"
      aria-label="Go to or open anything in this study"
    >
      <input
        id="canvas-palette-input"
        v-model="query"
        type="text"
        role="combobox"
        aria-autocomplete="list"
        aria-controls="canvas-palette-list"
        :aria-expanded="results.length > 0"
        :aria-activedescendant="
          results.length ? `canvas-palette-item-${active}` : undefined
        "
        autocomplete="off"
        :placeholder="
          studyCode
            ? 'Stage, section, participant, workflow or study code'
            : 'Study code or workflow, e.g. CAD-101'
        "
        aria-label="Search"
        @keydown="onKeydown"
      />
      <ul
        v-if="results.length"
        id="canvas-palette-list"
        class="palette-list"
        role="listbox"
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
      <p v-else id="canvas-palette-empty" class="palette-empty" role="status">
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
      <p class="palette-footnote">
        Studies are found by code or title among those visible to your account.
        Available actions for a selected participant are under Act in the
        inspector.
      </p>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, nextTick, onBeforeUnmount, ref, watch } from "vue";
import { getActivePinia } from "pinia";
import { useFocusTrap } from "ui";
import { kernelApi, type KernelSearchHit } from "../../api/kernelApi";
import { STAGES, participantStateLabel } from "../../canvas/lifecycle";
import type { SubjectSummary } from "../../api/executionApi";
import type {
  PaletteTarget,
  WorkspacePaletteOption,
} from "../../canvas/palette";
import { useAiAssistance } from "../../ai/aiAssistance";
import { useStudyCanvasStore } from "../../stores/studyCanvas";

/**
 * The unified global ⌘K command palette (docs/ux/CLINICAL_CANVAS.md §2.5):
 * combines kernel study search (ADR-2484, ADR-2540), opening a study by code,
 * lifecycle navigation (study home, stages, sections, loaded participants, and
 * Assist Ask when AI assistance is enabled), and authorized UX-1 workflow
 * destinations and workspace controls.
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
  }>(),
  {
    studyCode: "",
    participants: () => [],
    workspaceItems: () => [],
  }
);
const emit = defineEmits<{
  close: [];
  go: [target: PaletteTarget];
}>();

const dialogRef = ref<HTMLElement | null>(null);
useFocusTrap(dialogRef, {
  onEscape: () => emit("close"),
  initialFocus: "#canvas-palette-input",
});

const query = ref("");
const active = ref(0);

// Execution accepts any non-blank study code up to 255 characters, compared exactly.
const MAX_STUDY_CODE = 255;
const MAX_RESULTS = 20;
const MAX_KERNEL_HITS = 5;
const MIN_SEARCH_LENGTH = 2;
const SEARCH_DELAY_MS = 250;
const MIN_ASK_WORDS = 3;

const { enabled: aiEnabled } = useAiAssistance();

const kernelHits = ref<KernelSearchHit[]>([]);
const kernelNote = ref("");
const resolvedParticipant = ref<SubjectSummary | null>(null);
let searchTimer: ReturnType<typeof setTimeout> | undefined;
let latestSearch = 0;
let latestParticipantLookup = 0;

async function searchKernel(text: string) {
  const request = ++latestSearch;
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
  }
}

watch([query, () => props.studyCode], ([value, study]) => {
  clearTimeout(searchTimer);
  latestSearch++;
  const lookupRequest = ++latestParticipantLookup;
  const text = value.trim();
  kernelHits.value = [];
  resolvedParticipant.value = null;
  if (text.length < MIN_SEARCH_LENGTH || text.length > MAX_STUDY_CODE) {
    kernelNote.value = "";
    return;
  }
  searchTimer = setTimeout(() => searchKernel(text), SEARCH_DELAY_MS);

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
    void canvasStore.resolveParticipantByCode(text, study).then((matched) => {
      if (lookupRequest === latestParticipantLookup && matched) {
        resolvedParticipant.value = matched;
      }
    });
  }
});

onBeforeUnmount(() => {
  clearTimeout(searchTimer);
  latestParticipantLookup++;
});

const allParticipants = computed<SubjectSummary[]>(() => {
  const extra = resolvedParticipant.value;
  if (
    !extra ||
    (extra.study_id && extra.study_id !== props.studyCode) ||
    props.participants.some((participant) => participant.id === extra.id)
  ) {
    return props.participants;
  }
  return [...props.participants, extra];
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
    for (const participant of allParticipants.value) {
      list.push({
        key: `participant:${participant.id}`,
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
  for (const wsItem of props.workspaceItems) {
    list.push(wsItem);
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
  const shown = [...matches.slice(0, MAX_RESULTS), ...studyHits.value];
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

watch(results, () => {
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
  z-index: 50;
  display: flex;
  justify-content: center;
  align-items: flex-start;
  padding: 12vh 16px 16px;
  background: rgb(15 23 42 / 40%);
}

.palette {
  width: min(640px, 100%);
  background: var(--semantic-color-surface, #fff);
  border: 1px solid var(--border);
  border-radius: 10px;
  box-shadow: 0 20px 50px rgb(15 23 42 / 25%);
  overflow: hidden;
}

.palette input {
  width: 100%;
  box-sizing: border-box;
  padding: 14px 16px;
  border: 0;
  border-bottom: 1px solid var(--border);
  font-size: 1rem;
  background: transparent;
  color: inherit;
}

.palette-list {
  list-style: none;
  margin: 0;
  padding: 6px;
  max-height: 50vh;
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
}

.palette-detail {
  color: var(--neutral-dark);
  font-size: 0.85rem;
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
</style>
